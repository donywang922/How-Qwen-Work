using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace QwenWorld.Editor
{
    // Applies these presentation changes without reinstalling the world or rebaking light.
    public static class QwenPresentationUpdate
    {
        public static void ApplyBatch()
        {
            try
            {
                QwenModelPipeline.Compile();
                var scene = EditorSceneManager.OpenScene(QwenProject.MainScene,OpenSceneMode.Single);
                var ring = GameObject.Find("Orbital ring - imported Blender distant asset").GetComponent<MeshFilter>().sharedMesh;
                var rebuilt = QwenOrbitalRingMesh.Build();
                ring.Clear(); ring.indexFormat = rebuilt.indexFormat;
                ring.vertices = rebuilt.vertices; ring.normals = rebuilt.normals; ring.uv = rebuilt.uv;
                ring.triangles = rebuilt.triangles; ring.tangents = rebuilt.tangents; ring.bounds = rebuilt.bounds;
                UnityEngine.Object.DestroyImmediate(rebuilt);
                EditorUtility.SetDirty(ring);
                Vector3 axis = QwenOrbitalRingMesh.EarthDirection.normalized;
                float outerRadius = ring.vertices.Max(v => (v - Vector3.Dot(v,axis) * axis).magnitude);
                Require(Mathf.Abs(outerRadius - QwenWalkwaysBuilder.OuterRadius) < .01f,"Ring outer edge does not match ground");
                QwenConsoleVisuals.ConfigureProgressScreens();
                QwenProject.SaveUdonFields(scene);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                AssetDatabase.SaveAssets();
                string output = QwenProject.DataPath("validation/presentation-update");
                Directory.CreateDirectory(output);
                var gallery = GameObject.Find(QwenTextureGalleryBuilder.RootName);
                bool wasGallery = gallery.activeSelf;
                try
                {
                    gallery.SetActive(false);
                    QwenPreview.Capture(output + "/ring-and-ground.png",-axis * 54,Vector3.zero,55,1600,1100);
                    CheckProgressAndCapture(output);
                }
                finally { gallery.SetActive(wasGallery); }
                File.WriteAllText(output + "/validation.json",JsonUtility.ToJson(new Report
                {
                    passed = true, ringOuterRadius = outerRadius, groundRadius = QwenWalkwaysBuilder.OuterRadius,
                    validatedAtUtc = DateTime.UtcNow.ToString("o"),
                    scope = "Udon compilation, ring geometry, local/remote progress formatting and UI text fit. No full inference, live networking, native client or package build was run."
                },true));
                Debug.Log("QWEN_PRESENTATION_UPDATE_PASSED outerRadius=" + outerRadius);
                if (Application.isBatchMode) EditorApplication.Exit(0);
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                if (Application.isBatchMode) EditorApplication.Exit(1);
                else throw;
            }
        }

        static void CheckProgressAndCapture(string output)
        {
            var controller = UnityEngine.Object.FindObjectOfType<QwenWorldController>();
            var social = UnityEngine.Object.FindObjectOfType<QwenSocialUI>();
            var menu = UnityEngine.Object.FindObjectOfType<QwenLocalMenu>();
            var runner = controller.runner;
            var state = controller.exhibit;
            var shared = typeof(QwenWorldController).GetField("sharedState",BindingFlags.NonPublic | BindingFlags.Instance);
            var prompt = typeof(QwenTextureRunner).GetField("promptTokens",BindingFlags.NonPublic | BindingFlags.Instance);
            int oldState = (int)shared.GetValue(controller), oldPosition = runner.position, oldPass = runner.passCursor, oldTokens = runner.generatedCount;
            object oldPrompt = prompt.GetValue(runner);
            bool oldPaused = runner.paused, oldInference = controller.supportsInference;
            var oldMenuPosition = menu.menuRoot.transform.position;
            var oldMenuRotation = menu.menuRoot.transform.rotation;
            bool oldMenuActive = menu.menuRoot.activeSelf, oldInputActive = social.inputRoot.activeSelf;
            int oldDone = state.progressPromptDone, oldTotal = state.progressPromptTotal, oldOutputs = state.progressOutputTokens,
                oldOp = state.progressOperator, oldOps = state.progressOperatorTotal, oldLayer = state.progressLayer;
            try
            {
                shared.SetValue(controller,1); prompt.SetValue(runner,new int[100]);
                runner.position = 25; runner.passCursor = 300; runner.generatedCount = 0;
                string prefill = controller.GetProgressText();
                Require(prefill.Contains("25/100") && prefill.Contains("300/849"),"Prefill progress is incorrect");
                runner.position = 100; runner.generatedCount = 16; runner.paused = true;
                string paused = controller.GetProgressText();
                Require(paused.Contains("已暂停") && paused.Contains("16 token"),"Paused decode progress is incorrect");
                controller.supportsInference = false;
                state.progressPromptDone = state.progressPromptTotal = 100; state.progressOutputTokens = 16;
                state.progressOperator = 300; state.progressOperatorTotal = 849; state.progressLayer = runner.GetProgressLayer();
                state.computationPaused = true;
                Require(controller.GetProgressText() == paused,"Viewer progress differs from executor progress");
                state.computationPaused = false; runner.paused = false; controller.supportsInference = oldInference;
                string text = controller.GetProgressText();
                controller.statusText.text = menu.stateLabel.text = text;
                Canvas.ForceUpdateCanvases();
                Require(controller.statusText.preferredHeight <= controller.statusText.rectTransform.rect.height,
                    "Input progress text overflows");
                Require(menu.stateLabel.preferredHeight <= menu.stateLabel.rectTransform.rect.height,"Menu progress text overflows");
                menu.menuRoot.SetActive(true);
                menu.menuRoot.transform.SetPositionAndRotation(new Vector3(0,1.65f,-1.1f),Quaternion.identity);
                var corners = new Vector3[4];
                menu.menuRoot.transform.Find("White panel").GetComponent<RectTransform>().GetWorldCorners(corners);
                Vector3 center = (corners[0] + corners[2]) * .5f;
                QwenPreview.Capture(output + "/menu-progress.png",center + Vector3.back * .98f,center,42,1100,1100);
                menu.menuRoot.SetActive(false);
                social.inputRoot.SetActive(true);
                social.inputRoot.GetComponent<RectTransform>().GetWorldCorners(corners);
                center = (corners[0] + corners[2]) * .5f;
                QwenPreview.Capture(output + "/input-progress.png",center - social.inputRoot.transform.forward * 1.15f,center,40,1300,1000);
                Require(social.panelProgress.Length == 80,"Read-only progress footers missing");
                foreach (var footer in social.panelProgress)
                {
                    footer.text = text.Substring(0,text.IndexOf("\n"));
                    Require(footer.preferredHeight <= footer.rectTransform.rect.height,"Chat progress footer overflows");
                    footer.text = "就绪 · 等待输入";
                }
            }
            finally
            {
                shared.SetValue(controller,oldState); prompt.SetValue(runner,oldPrompt);
                runner.position = oldPosition; runner.passCursor = oldPass; runner.generatedCount = oldTokens; runner.paused = oldPaused;
                controller.supportsInference = oldInference; state.computationPaused = false;
                state.progressPromptDone = oldDone; state.progressPromptTotal = oldTotal; state.progressOutputTokens = oldOutputs;
                state.progressOperator = oldOp; state.progressOperatorTotal = oldOps; state.progressLayer = oldLayer;
                menu.menuRoot.transform.SetPositionAndRotation(oldMenuPosition,oldMenuRotation);
                menu.menuRoot.SetActive(oldMenuActive); social.inputRoot.SetActive(oldInputActive);
                controller.statusText.text = menu.stateLabel.text = "就绪 · 等待输入";
            }
        }

        static void Require(bool ok,string message) { if (!ok) throw new InvalidOperationException(message); }
        [Serializable] class Report
        {
            public bool passed;
            public float ringOuterRadius,groundRadius;
            public string validatedAtUtc,scope;
        }
    }
}
