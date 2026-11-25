using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UdonSharpEditor;
using VRC.SDKBase;
using VRC.Udon;

namespace QwenWorld.Editor
{
    // Exercises the production Udon paths with synthetic run state, without dispatching model operators.
    [InitializeOnLoad]
    public static class QwenCoreSyncValidation
    {
        const string Key = "QwenCoreSyncValidation";
        static double since;
        static string Output => QwenProject.DataPath("validation/core-sync");
        static QwenCoreSyncValidation() { EditorApplication.update += Tick; }

        public static void ValidateBatch()
        {
            QwenModelPipeline.Compile();
            var scene = EditorSceneManager.OpenScene(QwenProject.MainScene, OpenSceneMode.Single);
            // This fixture never tokenizes or generates. Keep all tokenizer components asleep.
            foreach (var tokenizer in QwenProject.Components<ShaderGPT.Udon.GPTTokenizer>(scene))
            {
                tokenizer.enabled = false; Backing(tokenizer).enabled = false;
            }
            if (File.Exists(Output + "/failure.txt")) File.Delete(Output + "/failure.txt");
            SessionState.SetString(Key, "enter"); since = EditorApplication.timeSinceStartup;
        }

        static void Tick()
        {
            string phase = SessionState.GetString(Key, "");
            if (phase.Length == 0) return;
            try
            {
                if (phase == "exit")
                {
                    if (EditorApplication.isPlayingOrWillChangePlaymode) return;
                    SessionState.SetString(Key, "");
                    Debug.Log("QWEN_CORE_SYNC_PASSED");
                    if (Application.isBatchMode) EditorApplication.Exit(0);
                    return;
                }
                if (phase == "enter")
                {
                    if (EditorApplication.isCompiling || EditorApplication.isUpdating) { since = EditorApplication.timeSinceStartup; return; }
                    if (EditorApplication.timeSinceStartup - since < 3) return;
                    SessionState.SetString(Key, "ready"); since = EditorApplication.timeSinceStartup;
                    EditorApplication.isPlaying = true; return;
                }
                if (EditorApplication.timeSinceStartup - since > 60) throw new TimeoutException("ClientSim did not initialize the sync fixture");
                if (!EditorApplication.isPlaying || !Utilities.IsValid(Networking.LocalPlayer)) return;
                var controller = UnityEngine.Object.FindObjectOfType<QwenWorldController>();
                if (controller == null) return;
                var visualProxy = UnityEngine.Object.FindObjectOfType<QwenComputeDisplay>();
                var visual = Backing(visualProxy); var world = Backing(controller);
                var engine = Backing(controller.runner); var state = Backing(controller.exhibit);
                if (!(bool)visual.GetProgramVariable("initialized")) return;
                Require(!(bool)engine.GetProgramVariable("running"), "Validation must not dispatch inference");
                // Freeze automatic updates; invoke each real Udon event explicitly.
                visual.enabled = world.enabled = engine.enabled = state.enabled = false;
                state.SetProgramVariable("localMenu", null); state.SetProgramVariable("social", null);
                state.SetProgramVariable("texturesShown", false);
                Core(state, 1 << 9, true, false);
                Require((int)state.GetProgramVariable("layerMask") == 0 && (bool)state.GetProgramVariable("computing"), "Hidden state included actual layers");
                state.SetProgramVariable("dirty", false);
                for (int i = 0; i < 24; i++) Core(state, 1 << i, true, false);
                Require(!(bool)state.GetProgramVariable("dirty"), "Hidden layer pulses dirtied the shared snapshot");
                Progress(state);
                Require((int)state.GetProgramVariable("progressOperator") == 0, "Hidden progress RPC was accepted");
                engine.SetProgramVariable("running", true); engine.SetProgramVariable("paused", false);
                visual.RunProgram("_postLateUpdate"); // Initial busy transition.
                state.SetProgramVariable("dirty", false);
                visual.SetProgramVariable("nextPublish", -100f); visual.SetProgramVariable("nextHeartbeat", 0f);
                for (int i = 0; i < 24; i++) visual.RunProgram("_postLateUpdate");
                Require((float)visual.GetProgramVariable("nextHeartbeat") == 0f, "Hidden core still publishes heartbeat or layer changes");
                world.SetProgramVariable("nextProgressSync", -100f);
                world.SetProgramVariable("progressWasVisible", false);
                world.RunProgram("_update");
                Require((float)world.GetProgramVariable("nextProgressSync") == -100f, "Hidden controller publishes progress");
                var coreMaterial = (Material)visual.GetProgramVariable("coreMaterial");
                visual.SetProgramVariable("hiddenAnimationClock", 0f); visual.RunProgram("_postLateUpdate");
                float first = coreMaterial.GetFloat("_LayerMask");
                visual.SetProgramVariable("hiddenAnimationClock", .6f); visual.RunProgram("_postLateUpdate");
                Require(first != coreMaterial.GetFloat("_LayerMask"), "Local illustration did not advance");
                engine.SetProgramVariable("paused", true); visual.RunProgram("_postLateUpdate");
                float held = (float)visual.GetProgramVariable("hiddenAnimationClock");
                visual.RunProgram("_postLateUpdate");
                Require((float)visual.GetProgramVariable("hiddenAnimationClock") == held, "Paused illustration moved");
                Require((bool)state.GetProgramVariable("computationPaused"), "Pause transition was not published");
                // Showing textures must force an immediate exact snapshot despite the rate limit.
                state.SetProgramVariable("texturesShown", true);
                engine.SetProgramVariable("stepLayers", new[]{13}); engine.SetProgramVariable("passCursor", 0);
                visual.SetProgramVariable("nextPublish", Time.unscaledTime + 100);
                visual.RunProgram("_postLateUpdate");
                Require((int)state.GetProgramVariable("layerMask") == 1 << 13, "Showing textures did not restore exact layer preview");
                Require(coreMaterial.GetFloat("_LayerMask") == 1 << 13, "Visible core uses illustration instead of actual layers");
                engine.SetProgramVariable("promptTokens", new int[7]); engine.SetProgramVariable("position", 3);
                engine.SetProgramVariable("generatedCount", 2);
                world.SetProgramVariable("nextProgressSync", Time.unscaledTime + 100);
                world.RunProgram("_update");
                Require((int)state.GetProgramVariable("progressPromptDone") == 3, "Visible progress was not immediately refreshed");
                Progress(state);
                Require((int)state.GetProgramVariable("progressOperator") == 42, "Visible progress RPC was dropped");
                var visibility = state.GetNetworkCallingMetadata("_SetVisibility");
                state.RunEventAdvanced<bool>("_SetVisibility", mangleParameterNames:false, canRunBeforeStart:true, (visibility.Parameters[0].Name, false));
                Require((int)state.GetProgramVariable("layerMask") == 0 && (int)state.GetProgramVariable("progressPromptTotal") == 0, "Hiding retained stale detail");
                // The viewer text must describe state, never pretend its animation is real progress.
                world.SetProgramVariable("supportsInference", false); world.SetProgramVariable("sharedState", 1);
                world.SendCustomEvent("RefreshLocalizedText");
                Require(controller.statusText.text == controller.localization.T(QwenLocalization.PAUSED_STATE), "Hidden viewer shows stale detailed progress");
                engine.SetProgramVariable("running", false); engine.SetProgramVariable("paused", false);
                visual.RunProgram("_postLateUpdate");
                Require(!(bool)state.GetProgramVariable("computing") && coreMaterial.GetFloat("_LayerMask") == 0, "Stop left the illustration active");
                Directory.CreateDirectory(Output);
                File.WriteAllText(Output + "/report.json", "{\"passed\":true,\"hiddenLayerUpdatesSuppressed\":true,\"hiddenProgressSuppressed\":true,\"hiddenHeartbeatSuppressed\":true,\"localAnimation\":true,\"pauseAndStop\":true,\"visibleExactLayerAndProgress\":true,\"modeTransitionSnapshot\":true,\"viewerStateText\":true,\"scope\":\"Production Udon in ClientSim with synthetic run state. No model dispatch, world build or real multi-client bandwidth measurement.\"}\n");
                SessionState.SetString(Key, "exit"); EditorApplication.isPlaying = false;
            }
            catch (Exception error)
            {
                SessionState.SetString(Key, ""); Directory.CreateDirectory(Output);
                File.WriteAllText(Output + "/failure.txt", error.ToString()); Debug.LogException(error);
                if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
                if (Application.isBatchMode) EditorApplication.Exit(1);
            }
        }

        static UdonBehaviour Backing(UdonSharp.UdonSharpBehaviour proxy) => UdonSharpEditorUtility.GetBackingUdonBehaviour(proxy);
        static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        static void Core(UdonBehaviour state, int mask, bool busy, bool paused)
        {
            var m = state.GetNetworkCallingMetadata("_Core");
            state.RunEventAdvanced<int,bool,bool>("_Core", mangleParameterNames:false, canRunBeforeStart:true,
                (m.Parameters[0].Name,mask), (m.Parameters[1].Name,busy), (m.Parameters[2].Name,paused));
        }
        static void Progress(UdonBehaviour state)
        {
            var m = state.GetNetworkCallingMetadata("_Progress");
            state.RunEventAdvanced<int,int,int,int,int,int>("_Progress", mangleParameterNames:false, canRunBeforeStart:true,
                (m.Parameters[0].Name,3), (m.Parameters[1].Name,7), (m.Parameters[2].Name,2),
                (m.Parameters[3].Name,42), (m.Parameters[4].Name,849), (m.Parameters[5].Name,14));
        }
    }
}
