using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VRC.SDK3.ClientSim;
using VRC.SDKBase;
using VRC.Udon;
using VRC.Udon.Common;

namespace QwenWorld.Editor
{
    [InitializeOnLoad]
    public static class QwenPlatformInteractionFix
    {
        const string Key="QwenPlatformInteractionFixPhase";
        static double since;
        static QwenLocalMenu menu;
        static QwenObservationPlatform platform;
        static UdonBehaviour menuUdon,platformUdon;
        static float baseY,maxJump,maxMenuError,menuOffset;
        static Quaternion menuRotation;
        static Report report;
        static QwenJumpProbe probe;
        [Serializable] class Report
        {
            public bool passed, menuFollowsLift, groundJump, raisedJump, movingJump, previewDecayPreserved;
            public float jumpImpulse,groundJumpMetres,raisedJumpMetres,movingJumpGapMetres,menuVerticalError;
            public string scope;
        }
        static string Output => QwenProject.DataPath("validation/platform-interaction-fix");
        static QwenPlatformInteractionFix() { EditorApplication.update+=Tick; }

        [MenuItem("Qwen World/Validation/Fix platform menu, step decay and jumping")]
        public static void ApplyBatch()
        {
            try
            {
                if(EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
                if(!Application.isBatchMode&&!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
                Directory.CreateDirectory(Output);
                QwenModelPipeline.Compile();
                var scene=EditorSceneManager.OpenScene(QwenProject.MainScene,OpenSceneMode.Single);
                QwenProject.Components<QwenWorldController>(scene).Single().jumpImpulse=3;
                QwenProject.SaveUdonFields(scene); EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
                ValidateFlowDecay();
                Next("enter");
            }
            catch(Exception error) { Fail(error); }
        }

        static void ValidateFlowDecay()
        {
            const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
            var display=UnityEngine.Object.FindObjectOfType<QwenComputeDisplay>();
            int first=-1,oldEvent=-1;
            for(int pass=0;pass<display.writeResources.Length-2;pass++)
            {
                var old=Events(display,pass); var next=Events(display,pass+1);
                var unique=old.Except(next).ToArray();
                if(unique.Length>0) { first=pass; oldEvent=unique[0]; break; }
            }
            Require(first>=0,"No adjacent operators available for decay check.");
            var renderers=new[]{display.coreRenderer,display.barsRenderer,display.flowRenderer};
            var original=renderers.Select(r=>r.sharedMaterial).ToArray();
            display.Start();
            var preview=typeof(QwenComputeDisplay).GetMethod("PreviewPausedPass",flags);
            preview.Invoke(display,new object[]{first});
            display.RecordStep(first); preview.Invoke(display,new object[]{first+1});
            var texture=(Texture2D)typeof(QwenComputeDisplay).GetField("eventsTexture",flags).GetValue(display);
            Color faded=texture.GetPixel(oldEvent,0);
            Require(faded.g==0&&faded.r>-100&&faded.a==1,"Executed operator was cleared instead of released into decay.");
            foreach(int id in Events(display,first+1)) Require(texture.GetPixel(id,0).g==1,"Next paused operator is not fully previewed.");
            display.RecordStep(first+1); preview.Invoke(display,new object[]{first+2});
            Require(texture.GetPixel(oldEvent,0).r>-100,"A later pause cleared earlier decay history.");
            display.ResetSequence();
            for(int i=0;i<renderers.Length;i++)
            {
                var temporary=renderers[i].sharedMaterial; renderers[i].sharedMaterial=original[i];
                if(temporary!=original[i]) UnityEngine.Object.DestroyImmediate(temporary);
            }
            UnityEngine.Object.DestroyImmediate(texture);
            File.WriteAllText(Output+"/flow-decay.json","{\"passed\":true,\"oldEventReleased\":true,\"nextPreviewPinned\":true,\"historySurvivesAnotherPause\":true,\"fullModelInference\":false}\n");
        }

        static int[] Events(QwenComputeDisplay display,int pass)
        {
            return display.readResources.Skip(display.readOffsets[pass]).Take(display.readOffsets[pass+1]-display.readOffsets[pass]).Select(id=>id*2)
                .Concat(display.writeResources[pass]>=0?new[]{display.writeResources[pass]*2+1}:Array.Empty<int>()).Distinct().ToArray();
        }
        static void Next(string phase) { SessionState.SetString(Key,phase); since=EditorApplication.timeSinceStartup; }
        static void Require(bool value,string reason) { if(!value) throw new InvalidOperationException(reason); }
        static float Height() => (float)platformUdon.GetProgramVariable("currentHeight");

        static void Tick()
        {
            string phase=SessionState.GetString(Key,""); if(phase=="") return;
            try
            {
                double now=EditorApplication.timeSinceStartup;
                if(phase=="enter")
                {
                    if(EditorApplication.isCompiling||EditorApplication.isUpdating||Environment.StackTrace.Contains("VRC.Editor.EnvConfig")) { since=now; return; }
                    if(now-since<3) return;
                    Next("ready"); EditorApplication.isPlaying=true; return;
                }
                if(phase=="exit")
                {
                    if(EditorApplication.isPlayingOrWillChangePlaymode) return;
                    SessionState.SetString(Key,"");
                    if(Application.isBatchMode) EditorApplication.Exit(0);
                    return;
                }
                if(!EditorApplication.isPlaying) return;
                var player=Networking.LocalPlayer;
                if(phase=="ready")
                {
                    if(!Utilities.IsValid(player)) { if(now-since>40) throw new TimeoutException("ClientSim local player did not start."); return; }
                    menu=UnityEngine.Object.FindObjectOfType<QwenLocalMenu>(); platform=UnityEngine.Object.FindObjectOfType<QwenObservationPlatform>();
                    menuUdon=UdonSharpEditorUtility.GetBackingUdonBehaviour(menu); platformUdon=UdonSharpEditorUtility.GetBackingUdonBehaviour(platform);
                    if(!(bool)platformUdon.GetProgramVariable("initialized")) return;
                    var runner=UdonSharpEditorUtility.GetBackingUdonBehaviour(menu.runner);
                    Require(!(bool)runner.GetProgramVariable("running"),"This validation must not run inference.");
                    report=new Report{previewDecayPreserved=true,jumpImpulse=player.GetJumpImpulse(),scope="ClientSim real Udon for menu following, ground/platform jumps and ascent input latch; editor production flow event state for pause/step decay. No inference, model dispatch, world build or multi-client/native VR test."};
                    Require(report.jumpImpulse>0,"Local player still has zero jump impulse.");
                    player.TeleportTo(new Vector3(3.5f,.03f,0),Quaternion.identity); Next("ground-ready"); return;
                }
                if(phase=="ground-ready")
                {
                    if(now-since<.4||!player.IsPlayerGrounded()) { if(now-since>5) throw new TimeoutException("Player did not stand on white floor."); return; }
                    baseY=player.GetPosition().y; maxJump=0; Jump(); Next("ground-jump"); return;
                }
                if(phase=="ground-jump")
                {
                    maxJump=Mathf.Max(maxJump,probe.maximum);
                    if(now-since<.8||!player.IsPlayerGrounded()) { if(now-since>4) throw new TimeoutException("Ground jump did not land."); return; }
                    Require(maxJump>.2f,"Player could not jump on white floor."); report.groundJump=true; report.groundJumpMetres=maxJump;
                    menuUdon.SendCustomEvent("OpenMenu"); menuOffset=menu.menuRoot.transform.position.y-player.GetPosition().y; menuRotation=menu.menuRoot.transform.rotation;
                    platform.heightSlider.value=1.5f; maxMenuError=0; Next("raise"); return;
                }
                if(phase=="raise")
                {
                    if(now-since>.1) maxMenuError=Mathf.Max(maxMenuError,Mathf.Abs(menu.menuRoot.transform.position.y-player.GetPosition().y-menuOffset));
                    Require(Quaternion.Angle(menu.menuRoot.transform.rotation,menuRotation)<.01f,"Menu rotated while dragging.");
                    if(Height()<1.49f||player.GetPosition().y<1.4f) { if(now-since>5) throw new TimeoutException("Platform did not carry player."); return; }
                    Require(maxMenuError<.1f,"Desktop menu stayed behind during platform movement."); report.menuFollowsLift=true; report.menuVerticalError=maxMenuError;
                    Next("raised-ready"); return;
                }
                if(phase=="raised-ready")
                {
                    if(now-since<.35||!player.IsPlayerGrounded()) { if(now-since>4) throw new TimeoutException("Player did not stand on raised platform."); return; }
                    baseY=player.GetPosition().y; maxJump=0; Jump(); Next("raised-jump"); return;
                }
                if(phase=="raised-jump")
                {
                    maxJump=Mathf.Max(maxJump,probe.maximum);
                    if(now-since<.8||!player.IsPlayerGrounded()) { if(now-since>4) throw new TimeoutException("Raised jump did not land."); return; }
                    Require(maxJump>.2f,"Player could not jump on acrylic platform."); report.raisedJump=true; report.raisedJumpMetres=maxJump;
                    platform.heightSlider.value=2.5f; Jump(); probe.relativeDeck=platform.deck; maxJump=0; Next("moving-jump"); return;
                }
                if(phase=="moving-jump")
                {
                    maxJump=Mathf.Max(maxJump,probe.maximum);
                    if(now-since<.45) return;
                    Require(maxJump>.035f,"Platform carry cancelled jump input during ascent."); report.movingJump=true; report.movingJumpGapMetres=maxJump;
                    report.passed=true; File.WriteAllText(Output+"/report.json",JsonUtility.ToJson(report,true)+"\n");
                    Debug.Log("QWEN_PLATFORM_INTERACTION_FIX_PASSED"); Next("exit"); EditorApplication.isPlaying=false;
                }
            }
            catch(Exception error) { Fail(error); }
        }

        static void Jump()
        {
            platformUdon.RunInputEvent(UdonManager.UDON_INPUT_JUMP,new UdonInputEventArgs(true,HandType.LEFT));
            Require((bool)platformUdon.GetProgramVariable("jumping"),"Jump suppression latch did not receive input.");
            var local=Resources.FindObjectsOfTypeAll<ClientSimPlayer>().First(p=>p!=null&&p.Player==Networking.LocalPlayer);
            var controller=(ClientSimPlayerController)typeof(ClientSimPlayer).GetField("playerController",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(local);
            var dispatcher=(IClientSimEventDispatcher)typeof(ClientSimPlayerController).GetField("_eventDispatcher",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(controller);
            dispatcher.SendEvent(new ClientSimMenuStateChangedEvent{isMenuOpen=false});
            if(probe==null) probe=new GameObject("Temporary fixed-step jump probe").AddComponent<QwenJumpProbe>();
            probe.baseline=Networking.LocalPlayer.GetPosition().y; probe.maximum=0; probe.relativeDeck=null;
            typeof(ClientSimPlayerController).GetMethod("JumpInput",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(controller,new object[]{true,HandType.LEFT});
            Require((bool)typeof(ClientSimPlayerController).GetField("_jump",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(controller),"ClientSim jump input was not accepted.");
        }
        static void Fail(Exception error)
        {
            SessionState.SetString(Key,""); Directory.CreateDirectory(Output); File.WriteAllText(Output+"/failure.txt",error.ToString()); Debug.LogException(error);
            if(EditorApplication.isPlaying) EditorApplication.isPlaying=false;
            if(Application.isBatchMode) EditorApplication.Exit(1);
        }
    }

    [DefaultExecutionOrder(32000)]
    public class QwenJumpProbe : MonoBehaviour
    {
        public float baseline,maximum;
        public Transform relativeDeck;
        void FixedUpdate()
        {
            var player=Networking.LocalPlayer;
            if(Utilities.IsValid(player)) maximum=Mathf.Max(maximum,player.GetPosition().y-(relativeDeck!=null?relativeDeck.position.y:baseline));
        }
    }
}
