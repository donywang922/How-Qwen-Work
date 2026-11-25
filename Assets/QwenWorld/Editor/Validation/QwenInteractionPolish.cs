using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using VRC.Udon;

namespace QwenWorld.Editor
{
    /// <summary>In-place UI/interaction update; preserves furniture and model layout.</summary>
    public static class QwenInteractionPolish
    {
        [Serializable] class Report
        {
            public bool passed, advertisementOnly, noInteractHighlight, keyboardEases, panelsAnimate, furniturePreserved;
            public Vector2 inputMetres, smallestChatMetres;
            public float firstChatHeight, keyboardFirstFrameDegrees, keyboardAfterSecondDegrees;
            public string scope;
        }

        [MenuItem("Qwen World/Scene/Apply interaction and conversation polish")]
        public static void ApplyBatch()
        {
            try
            {
                if(EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
                if(!Application.isBatchMode&&!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
                string output=QwenProject.DataPath("validation/interaction-polish"); Directory.CreateDirectory(output);
                string backup=QwenProject.DataPath("archive/interaction-polish/QwenLaboratory-before.unity");
                Directory.CreateDirectory(Path.GetDirectoryName(backup));
                if(!File.Exists(backup)) File.Copy(QwenProject.MainScene,backup);
                QwenModelPipeline.Compile();
                var scene=EditorSceneManager.OpenScene(QwenProject.MainScene,OpenSceneMode.Single);
                var social=QwenProject.Components<QwenSocialUI>(scene).Single();
                var box=QwenProject.Components<QwenLightbox>(scene).Single();
                Vector3 furniturePosition=box.transform.position; Quaternion furnitureRotation=box.transform.rotation;
                QwenConsoleVisuals.ConfigureConversationLayout();
                QwenSpawnLoungeBuilder.ConfigureAdvertisementTargets(box);
                QwenProject.SaveUdonFields(scene); QwenProject.ResetSdkBuildCache(scene);
                EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();

                var input=(RectTransform)social.inputRoot.transform;
                Vector2 inputSize=Vector2.Scale(input.sizeDelta,new Vector2(input.lossyScale.x,input.lossyScale.y));
                var narrow=social.panels.Cast<RectTransform>().OrderBy(p=>p.sizeDelta.x*p.lossyScale.x).First();
                Vector2 chatSize=Vector2.Scale(narrow.sizeDelta,new Vector2(narrow.lossyScale.x,narrow.lossyScale.y));
                Require(inputSize.x<chatSize.x&&inputSize.y<chatSize.y,"Input is not smaller than chat.");
                Require(Mathf.Abs(social.slotPositions[0].y-1.56f)<.001f,"Chat height was not lowered.");
                Require(box.GetComponents<Collider>().Length==0,"Furniture-wide interaction collider remains.");
                var hitAreas=box.GetComponentsInChildren<Button>(true).Where(b=>b.name.StartsWith("Advertisement click ",StringComparison.Ordinal)).ToArray();
                Require(hitAreas.Length==2,"Expected front/back advertisement hit regions.");
                foreach(var button in hitAreas)
                {
                    Require(button.GetComponent<Image>().color.a==0&&button.transition==Selectable.Transition.None,"Advert hit region can highlight.");
                    Require(button.GetComponent<RectTransform>().rect.width==1070,"Advert hit region covers frame or pens.");
                    Require(button.onClick.GetPersistentMethodName(0)=="SendCustomEvent","Advert click is not wired.");
                    Require(button.onClick.GetPersistentTarget(0)==box.GetComponent<UdonBehaviour>(),"Advert click targets the wrong Udon object.");
                }
                ValidateInvisibleHit(hitAreas[0]);
                Require(typeof(QwenLightbox).GetMethod("Interact").DeclaringType!=typeof(QwenLightbox),"Lightbox still exposes Interact highlight.");
                float first,settled; ValidateMotion(social,out first,out settled);
                ValidateReveal(social,output);
                var report=new Report{passed=true,advertisementOnly=true,noInteractHighlight=true,keyboardEases=true,panelsAnimate=true,
                    furniturePreserved=box.transform.position==furniturePosition&&box.transform.rotation==furnitureRotation,
                    inputMetres=inputSize,smallestChatMetres=chatSize,firstChatHeight=social.slotPositions[0].y,
                    keyboardFirstFrameDegrees=first,keyboardAfterSecondDegrees=settled,
                    scope="Udon compilation; in-place scene geometry; transparent advertisement UI raycast; production keyboard damping and panel animation called on editor proxies. No model inference, world build or native VR/multiplayer test."};
                Require(report.furniturePreserved,"Furniture was moved.");
                File.WriteAllText(output+"/report.json",JsonUtility.ToJson(report,true)+"\n");
                Debug.Log("QWEN_INTERACTION_POLISH_READY");
                if(Application.isBatchMode) EditorApplication.Exit(0);
            }
            catch(Exception error)
            {
                Debug.LogException(error);
                if(Application.isBatchMode) EditorApplication.Exit(1); else throw;
            }
        }

        static void ValidateInvisibleHit(Button button)
        {
            var root=button.GetComponent<RectTransform>();
            var go=new GameObject("Temporary advertisement hit camera"); var camera=go.AddComponent<Camera>(); camera.enabled=false;
            camera.transform.position=root.position-root.forward*1.5f; camera.transform.LookAt(root.position);
            var canvas=button.GetComponent<Canvas>(); var previous=canvas.worldCamera; canvas.worldCamera=camera;
            var target=new RenderTexture(256,256,24); target.Create(); camera.targetTexture=target;
            try
            {
                Canvas.ForceUpdateCanvases();
                camera.Render();
                var pointer=new PointerEventData(EventSystem.current){position=camera.WorldToScreenPoint(root.position)};
                var hits=new System.Collections.Generic.List<RaycastResult>(); button.GetComponent<GraphicRaycaster>().Raycast(pointer,hits);
                var image=button.GetComponent<Image>();
                Require(hits.Any(hit=>hit.gameObject==button.gameObject),"Transparent advertisement cannot be clicked: depth="+image.depth+", cull="+image.canvasRenderer.cull+", raycast="+image.raycastTarget);
            }
            finally { canvas.worldCamera=previous; camera.targetTexture=null; target.Release(); UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(go); }
        }

        static void ValidateMotion(QwenSocialUI social,out float first,out float settled)
        {
            const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
            var angle=typeof(QwenSocialUI).GetField("keyboardAngle",flags);
            var velocity=typeof(QwenSocialUI).GetField("keyboardAngularVelocity",flags);
            var follow=typeof(QwenSocialUI).GetMethod("FollowKeyboard",flags);
            Quaternion before=social.keyboardPivot.rotation;
            angle.SetValue(social,0f); velocity.SetValue(social,0f);
            follow.Invoke(social,new object[]{90f,1f/90}); first=(float)angle.GetValue(social);
            Require(first>0&&first<5,"Keyboard snaps instead of easing in.");
            for(int i=1;i<90;i++) follow.Invoke(social,new object[]{90f,1f/90});
            settled=(float)angle.GetValue(social); Require(Mathf.Abs(settled-90)<.5f,"Keyboard damping is too slow.");
            angle.SetValue(social,359f); velocity.SetValue(social,0f);
            for(int i=0;i<12;i++) follow.Invoke(social,new object[]{1f,1f/90});
            Require(Mathf.Abs(Mathf.DeltaAngle(359,(float)angle.GetValue(social)))<2.01f,"Keyboard took the long path around wrap.");
            social.keyboardPivot.rotation=before;
        }

        static void ValidateReveal(QwenSocialUI social,string output)
        {
            const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
            var reveals=new float[social.panels.Length]; var open=new bool[social.panels.Length];
            var scales=social.panels.Select(p=>p.localScale).ToArray();
            typeof(QwenSocialUI).GetField("panelReveal",flags).SetValue(social,reveals);
            typeof(QwenSocialUI).GetField("panelOpen",flags).SetValue(social,open);
            typeof(QwenSocialUI).GetField("panelBaseScale",flags).SetValue(social,scales);
            var animate=typeof(QwenSocialUI).GetMethod("AnimatePanels",flags);
            var start=typeof(QwenSocialUI).GetMethod("OpenPanel",flags);
            int slot=8; var panel=social.panels[slot]; panel.gameObject.SetActive(false);
            start.Invoke(social,new object[]{slot});
            Require(social.panelFade[slot].alpha==0&&panel.localScale.x<scales[slot].x,"Chat flashes open at full size.");
            animate.Invoke(social,new object[]{.15f});
            Require(social.panelFade[slot].alpha>.2f&&social.panelFade[slot].alpha<.8f,"Chat transition has no midpoint.");
            animate.Invoke(social,new object[]{.2f});
            Require(Mathf.Abs(social.panelFade[slot].alpha-1)<.001f&&Vector3.Distance(panel.position,social.slotPositions[slot])<.001f,"Chat did not finish unfolding.");
            bool inputWas=social.inputRoot.activeSelf;
            social.inputRoot.SetActive(true); social.panelCaption[slot].text="共享对话 / Shared chat";
            social.panelText[slot].text="你：请简单介绍一下自己。\n\n千问：你好！我是千问。\n\n中文 English 日本語 한국어";
            QwenPreview.Capture(output+"/conversation.png",new Vector3(0,1.6f,-2.25f),new Vector3(0,1.42f,-.8f),55,1400,1100);
            open[slot]=false; animate.Invoke(social,new object[]{.35f});
            Require(!panel.gameObject.activeSelf,"Chat did not close after fading out.");
            panel.localScale=scales[slot]; panel.position=social.slotPositions[slot]; social.inputRoot.SetActive(inputWas);
        }

        static void Require(bool condition,string message) { if(!condition) throw new InvalidOperationException(message); }
    }
}
