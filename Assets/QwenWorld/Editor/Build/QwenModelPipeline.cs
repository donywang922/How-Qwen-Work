using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ShaderGPT;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using VRC.Udon;

namespace QwenWorld.Editor
{
    [InitializeOnLoad]
    public static class QwenModelPipeline
    {
        public const string Root = "Assets/QwenWorld";
        public static string ModelName => QwenProject.ModelName;
        public static string ModelFolder => QwenProject.ModelFolder;
        public static string PrefabPath => ModelFolder + "/" + ModelName + ".prefab";
        public static string PcScene => QwenProject.MainScene;
        public const string AndroidScene = QwenProject.ViewerScene;
        public static string Results => QwenProject.DataPath("validation");
        static double phaseStart;
        static string fatal;
        static int lastCount;
        static List<int> actualTokens = new List<int>();
        static UdonBehaviour runningEngine, runningController;
        [Serializable]
        class WorldCase
        {
            public string lang, question, answer;
            public int[] input_ids, generated_ids;
        }

        [Serializable]
        class WorldCases
        {
            public WorldCase[] cases;
        }

        [Serializable]
        class CaseResult
        {
            public string lang, response;
            public bool tokensMatch;
            public int[] tokens;
            public double seconds;
        }

        [Serializable]
        class WorldResult
        {
            public bool passed, controlsPassed;
            public CaseResult[] cases;
        }

        static WorldCases worldCases;
        static List<CaseResult> caseResults = new List<CaseResult>();
        static int caseIndex, controlFrames, controlPosition;
        static bool controlsPassed;
        static QwenModelPipeline()
        {
            EditorApplication.update += Tick;
            Application.logMessageReceived += ObserveLog;
        }

        static void ObserveLog(string message, string stack, LogType type)
        {
            if (SessionState.GetString("QwenPhase", "") != "" && (type == LogType.Exception || type == LogType.Error || type == LogType.Assert))
                if (!message.Contains("Failed to get user") && !message.Contains("Failed to fetch remote config"))
                    fatal = message + "\n" + stack;
        }

        [MenuItem("Qwen World/1 Compile Udon")]
        public static void Compile()
        {
            EnsurePrograms();
            UdonSharp.Compiler.UdonSharpCompilerV1.CompileSync(new UdonSharp.Compiler.UdonSharpCompileOptions{IsEditorBuild = true});
            if (UdonSharpProgramAsset.AnyUdonSharpScriptHasError())
                throw new InvalidOperationException("UdonSharp compilation reported errors; refusing stale programs.");
            AssetDatabase.SaveAssets();
            foreach (string name in new[]{"QwenTextureRunner", "QwenWorldController", "QwenLocalMenu", "QwenExhibitState", "QwenSocialUI", "QwenKeyboard", "QwenComputeDisplay", "QwenObservationPlatform", "QwenLocalization", "QwenLightbox", "QwenBenchSeat"})
            {
                var program = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(Root + "/Runtime/" + name + ".asset");
                if (program.GetRealProgram() == null)
                    throw new InvalidOperationException("Udon program did not compile: " + name);
            }

            Debug.Log("QWEN_UDON_COMPILE_FINISHED");
        }

        public static void EnsurePrograms()
        {
            foreach (string name in new[]{"QwenTextureRunner", "QwenWorldController", "QwenLocalMenu", "QwenExhibitState", "QwenSocialUI", "QwenKeyboard", "QwenComputeDisplay", "QwenObservationPlatform", "QwenLocalization", "QwenLightbox", "QwenBenchSeat"})
            {
                string path = Root + "/Runtime/" + name + ".asset";
                if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(path) != null)
                    continue;
                var asset = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
                asset.sourceCsScript = AssetDatabase.LoadAssetAtPath<MonoScript>(Root + "/Runtime/" + name + ".cs");
                asset.ScriptVersion = UdonSharpProgramVersion.CurrentVersion;
                AssetDatabase.CreateAsset(asset, path);
            }

            AssetDatabase.SaveAssets();
        }

        [MenuItem("Qwen World/2 Bake and validate Q4 model")]
        public static void BakeAndValidate()
        {
            Directory.CreateDirectory(Results);
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                throw new InvalidOperationException("Real graphics required; do not use -nographics.");
            if (!File.Exists(ModelFolder + "/config.json"))
                throw new FileNotFoundException("Copy the Q4 export into " + ModelFolder);
            SessionState.SetString("QwenPhase", "prepare");
            fatal = null;
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            foreach (string path in ModelAssetUtility.GetTexturePaths(ModelFolder))
            {
                ModelAssetUtility.SetTextureFormat(path, ModelAssetUtility.CalcCompressionFormat(path, ModelAssetUtility.DataType.Float32));
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.filterMode = FilterMode.Point;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.isReadable = false;
                importer.SaveAndReimport();
            }

            ValidateGpu();
            if (!string.IsNullOrEmpty(fatal))
                throw new InvalidOperationException(fatal);
            var assets = ModelAssetUtility.LoadModelAssets(ModelFolder);
            Directory.CreateDirectory(Root + "/Generated");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var go = new GameObject("Bake Qwen3.5 text graph");
            go.SetActive(false);
            var basic = go.AddComponent<BasicLM>();
            basic.shaders = assets.shaders;
            basic.textures = assets.textures;
            basic.configJson = assets.configJson;
            basic.tokenizerJson = assets.tokenizerJson;
            basic.task = BasicLM.Task.Bake;
            basic.generationConfig = new GenerationConfig{max_length = 512, do_sample = false, repetition_penalty = 1};
            go.SetActive(true);
            if (AssetDatabase.LoadMainAssetAtPath(PrefabPath) != null)
                AssetDatabase.DeleteAsset(PrefabPath);
            EditorSceneManager.SaveScene(scene, Root + "/Generated/Bake.unity");
            SessionState.SetString("QwenPhase", "bake-enter");
            phaseStart = EditorApplication.timeSinceStartup;
        }

        public static void BakeFullAndValidate()
        {
            BakeAndValidate();
        }

        public static void ResumeWorldValidation()
        {
            fatal = null;
            Directory.CreateDirectory(Results);
            if (File.Exists(Results + "/failure.txt"))
                File.Delete(Results + "/failure.txt");
            SessionState.SetString("QwenPhase", "world");
        }

        public static void ValidateExistingWorld()
        {
            fatal = null;
            Directory.CreateDirectory(Results);
            if (File.Exists(Results + "/failure.txt")) File.Delete(Results + "/failure.txt");
            EditorSceneManager.OpenScene(PcScene, OpenSceneMode.Single);
            SessionState.SetString("QwenPhase", "smoke-wait");
            phaseStart = EditorApplication.timeSinceStartup;
        }

        public static void ValidateGpuAndWorldBatch()
        {
            try
            {
                Compile();
                ValidateGpu();
                ValidateExistingWorld();
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                if (Application.isBatchMode)
                    EditorApplication.Exit(1);
                else
                    throw;
            }
        }

        public static void ValidateAndroidViewer()
        {
            fatal = null;
            Directory.CreateDirectory(Results);
            QwenPlatformBuild.CreateViewerScene();
            SessionState.SetBool("QwenViewerTest", true);
            SessionState.SetString("QwenPhase", "smoke-wait");
            phaseStart = EditorApplication.timeSinceStartup;
        }

        [Serializable]
        class Reference
        {
            public int[] input_ids;
            public int expected_next_token;
        }

        [Serializable]
        class Testcase
        {
            public int[] input_ids;
            public float[] hidden_states;
            public float[] logits;
        }

        [Serializable]
        class GpuResult
        {
            public bool passed;
            public int predicted, expected;
            public float maxLogitError, rmsLogitError;
            public string device;
        }

        public static void ValidateGpu()
        {
            var assets = ModelAssetUtility.LoadModelAssets(ModelFolder);
            var testcase = JsonUtility.FromJson<Testcase>(File.ReadAllText(ModelFolder + "/testcase.json"));
            var reference = JsonUtility.FromJson<Reference>(File.ReadAllText(ModelFolder + "/reference.json"));
            var ctx = new TensorContext();
            var fn = new Functional{ctx = ctx, kernels = assets.shaders.ToDictionary(x => x.name.Split('/')[1], x => x)};
            var model = AutoModel.FromPretrained<ModelForCausalLM>(fn, assets.configJson, assets.textures);
            var cache = new ShaderGPT.Cache(fn, 512);
            model.generation_config = new GenerationConfig{max_length = 512};
            float[] logits = null;
            try
            {
                for (int pos = 0; pos < reference.input_ids.Length; pos++)
                {
                    Tensor input = ctx.CPUTensor(1, 1);
                    input.SetData(new float[]{reference.input_ids[pos], pos, 0, 0});
                    var result = model.ForCausalLM(input, cache);
                    ctx.Release(input);
                    if (pos == reference.input_ids.Length - 1)
                        logits = result.logits.GetData<float>().ToArray();
                    ctx.Release(result.hidden_states);
                    ctx.Release(result.logits);
                }

                double squares = 0;
                float max = 0;
                int predicted = 0;
                for (int i = 0; i < testcase.logits.Length; i++)
                {
                    if (float.IsNaN(logits[i]) || float.IsInfinity(logits[i]))
                        throw new InvalidOperationException("Nonfinite GPU logits at " + i);
                    float delta = Mathf.Abs(logits[i] - testcase.logits[i]);
                    max = Mathf.Max(max, delta);
                    squares += delta * delta;
                    if (logits[i] > logits[predicted])
                        predicted = i;
                }

                var report = new GpuResult{passed = max < .05f && predicted == reference.expected_next_token, predicted = predicted, expected = reference.expected_next_token, maxLogitError = max, rmsLogitError = (float)Math.Sqrt(squares / testcase.logits.Length), device = SystemInfo.graphicsDeviceName};
                Directory.CreateDirectory(Results);
                File.WriteAllText(Results + "/gpu-parity.json", JsonUtility.ToJson(report, true));
                Debug.Log("QWEN_GPU_PARITY " + JsonUtility.ToJson(report));
                if (!report.passed)
                    throw new InvalidOperationException("GPU parity failed: " + Results + "/gpu-parity.json");
            }
            finally
            {
                cache.Reset();
                ctx.ReleaseAll();
                RenderTexture.active = null;
            }
        }

        static void Tick()
        {
            string phase = SessionState.GetString("QwenPhase", "");
            if (phase == "")
                return;
            try
            {
                if (!string.IsNullOrEmpty(fatal))
                    throw new InvalidOperationException(fatal);
                if (phase == "bake-enter" || phase == "smoke-wait")
                {
                    // EnvConfig can reenter EditorApplication.update during asset imports.
                    // Starting play from that nested tick leaves XR loader setup half complete.
                    if (Environment.StackTrace.Contains("VRC.Editor.EnvConfig"))
                    {
                        phaseStart = EditorApplication.timeSinceStartup;
                        return;
                    }

                    if (EditorApplication.isCompiling || EditorApplication.isUpdating)
                    {
                        phaseStart = EditorApplication.timeSinceStartup;
                        return;
                    }

                    if (phaseStart == 0)
                        phaseStart = EditorApplication.timeSinceStartup;
                    if (EditorApplication.timeSinceStartup - phaseStart < 5)
                        return;
                    SessionState.SetString("QwenPhase", phase == "bake-enter" ? "bake" : "smoke-enter");
                    phaseStart = EditorApplication.timeSinceStartup;
                    EditorApplication.isPlaying = true;
                    return;
                }

                if (phase == "bake" && EditorApplication.isPlaying && File.Exists(PrefabPath))
                {
                    SessionState.SetString("QwenPhase", "world");
                    EditorApplication.isPlaying = false;
                    return;
                }

                if (phase == "world" && !EditorApplication.isPlaying && !EditorApplication.isPlayingOrWillChangePlaymode)
                {
                    QwenWorldBootstrap.Build();
                    SessionState.SetString("QwenPhase", "smoke-wait");
                    phaseStart = EditorApplication.timeSinceStartup;
                    return;
                }

                if (phase == "smoke-enter" && EditorApplication.isPlaying)
                {
                    var controller = UnityEngine.Object.FindObjectOfType<QwenWorldController>();
                    if (controller == null)
                        return;
                    if (SessionState.GetBool("QwenViewerTest", false))
                    {
                        if (VRC.SDKBase.Networking.LocalPlayer == null || controller.supportsInference)
                            return;
                        runningController = UdonSharpEditorUtility.GetBackingUdonBehaviour(controller);
                        runningEngine = UdonSharpEditorUtility.GetBackingUdonBehaviour(controller.runner);
                        var viewerSteps = runningEngine.GetProgramVariable("steps") as Material[];
                        if ((bool)runningEngine.GetProgramVariable("supportsInference") || (viewerSteps != null && viewerSteps.Length > 0) || controller.sendButton.interactable)
                            throw new Exception("Viewer unexpectedly enables inference");
                        string sample = "中文共享回答。 English shared answer. 日本語の回答。 한국어 답변.";
                        runningController.SetProgramVariable("sharedSpeaker", "PC test source");
                        runningController.SetProgramVariable("sharedPrompt", "四语言同步显示测试");
                        runningController.SetProgramVariable("sharedState", 1);
                        runningController.SetProgramVariable("sharedResponse", sample);
                        runningController.RunProgram("_onDeserialization");
                        if (!((string)runningController.GetProgramVariable("readableTranscript")).Contains(sample))
                            throw new Exception("Viewer deserialization display failed");
                        CaptureScene(Results + "/android-viewer-preview.png");
                        File.WriteAllText(Results + "/android-viewer.json", "{\"passed\":true,\"scope\":\"ClientSim synthetic deserialization; not a multi-client network test\",\"modelDependencies\":0}");
                        SessionState.SetBool("QwenViewerTest", false);
                        SessionState.SetString("QwenPhase", "done");
                        EditorApplication.isPlaying = false;
                        return;
                    }

                    // ClientSim initializes Udon after entering play, not on the first editor tick.
                    var tokenizer = UdonSharpEditorUtility.GetBackingUdonBehaviour(controller.tokenizer);
                    if (VRC.SDKBase.Networking.LocalPlayer == null || (int)tokenizer.GetProgramVariable("vocab_size") <= 0)
                        return;
                    runningController = UdonSharpEditorUtility.GetBackingUdonBehaviour(controller);
                    runningEngine = UdonSharpEditorUtility.GetBackingUdonBehaviour(controller.runner);
                    worldCases = new WorldCases{cases = QwenWorldReference.Load().Select(c => new WorldCase
                    {lang=c.lang,question=c.question,answer=c.answer,input_ids=c.input_ids,generated_ids=c.generated_ids}).ToArray()};
                    caseIndex = 0;
                    caseResults.Clear();
                    controlsPassed = false;
                    BeginCase();
                    runningEngine.SendCustomEvent("PauseOrResume");
                    controlFrames = 0;
                    controlPosition = 0;
                    SessionState.SetString("QwenPhase", "control-pause");
                    return;
                }

                if (phase == "control-pause")
                {
                    if (++controlFrames < 5)
                        return;
                    if (!(bool)runningEngine.GetProgramVariable("paused") || (int)runningEngine.GetProgramVariable("passCursor") != 0)
                        throw new Exception("Pause failed");
                    runningEngine.SendCustomEvent("StepOperator");
                    controlFrames = 0;
                    SessionState.SetString("QwenPhase", "control-op");
                    return;
                }

                if (phase == "control-op")
                {
                    if (!(bool)runningEngine.GetProgramVariable("paused"))
                        return;
                    if ((int)runningEngine.GetProgramVariable("passCursor") != 1)
                        throw new Exception("Single operator failed");
                    runningEngine.SendCustomEvent("StepLayer");
                    SessionState.SetString("QwenPhase", "control-layer");
                    return;
                }

                if (phase == "control-layer")
                {
                    if (!(bool)runningEngine.GetProgramVariable("paused"))
                        return;
                    if ((int)runningEngine.GetProgramVariable("passCursor") <= 1)
                        throw new Exception("Single layer failed");
                    controlPosition = (int)runningEngine.GetProgramVariable("position");
                    runningEngine.SendCustomEvent("StepToken");
                    SessionState.SetString("QwenPhase", "control-token");
                    return;
                }

                if (phase == "control-token")
                {
                    if (!(bool)runningEngine.GetProgramVariable("paused"))
                        return;
                    if ((int)runningEngine.GetProgramVariable("position") != controlPosition + 1 || (int)runningEngine.GetProgramVariable("passCursor") != 0)
                        throw new Exception("Single token failed");
                    controlsPassed = true;
                    runningEngine.SendCustomEvent("PauseOrResume");
                    SessionState.SetString("QwenPhase", "smoke-run");
                    return;
                }

                if (phase == "smoke-run" && EditorApplication.isPlaying)
                {
                    int count = (int)runningEngine.GetProgramVariable("generatedCount");
                    if (count != lastCount)
                    {
                        lastCount = count;
                        actualTokens.Add((int)runningEngine.GetProgramVariable("outputToken"));
                        Debug.Log("QWEN_UDON_TOKEN " + count + ": " + actualTokens.Last());
                    }

                    bool running = (bool)runningEngine.GetProgramVariable("running");
                    if ((!running && count > 0) || count >= 24)
                    {
                        runningController.SendCustomEvent("StopGeneration");
                        File.WriteAllText(Results + "/udon-tokens.json", "[" + string.Join(",", actualTokens) + "]");
                        var controller = UnityEngine.Object.FindObjectOfType<QwenWorldController>();
                        string response = (string)runningController.GetProgramVariable("readableTranscript");
                        File.WriteAllText(Results + "/udon-response.txt", response);
                        bool matches = actualTokens.SequenceEqual(worldCases.cases[caseIndex].generated_ids);
                        caseResults.Add(new CaseResult{lang = worldCases.cases[caseIndex].lang, tokensMatch = matches, tokens = actualTokens.ToArray(), response = response, seconds = EditorApplication.timeSinceStartup - phaseStart});
                        if (!matches)
                            throw new Exception("Udon token mismatch: " + worldCases.cases[caseIndex].lang);
                        if (++caseIndex < worldCases.cases.Length)
                        {
                            BeginCase();
                            return;
                        }

                        CaptureScene(Results + "/world-preview.png");
                        File.WriteAllText(Results + "/world-smoke.json", JsonUtility.ToJson(new WorldResult{passed = true, controlsPassed = controlsPassed, cases = caseResults.ToArray()}, true));
                        runningController.SendCustomEvent("ResetConversation");
                        if ((bool)runningEngine.GetProgramVariable("running"))
                            throw new Exception("Reset failed to stop runner");
                        SessionState.SetString("QwenPhase", "done");
                        EditorApplication.isPlaying = false;
                    }
                    else if (EditorApplication.timeSinceStartup - phaseStart > 600)
                        throw new TimeoutException("Udon smoke test timed out");
                }

                if (phase == "done" && !EditorApplication.isPlaying && !EditorApplication.isPlayingOrWillChangePlaymode)
                {
                    SessionState.SetString("QwenPhase", "");
                    Debug.Log("QWEN_IMPLEMENTATION_VALIDATION_COMPLETE");
                    if (Application.isBatchMode)
                        EditorApplication.Exit(0);
                }
            }
            catch (Exception error)
            {
                SessionState.SetString("QwenPhase", "");
                Directory.CreateDirectory(Results);
                File.WriteAllText(Results + "/failure.txt", error.ToString());
                Debug.LogException(error);
                if (EditorApplication.isPlaying)
                    EditorApplication.isPlaying = false;
                if (Application.isBatchMode)
                    EditorApplication.Exit(1);
            }
        }

        static void BeginCase()
        {
            if (VRC.SDKBase.Networking.LocalPlayer != null)
                VRC.SDKBase.Networking.LocalPlayer.TeleportTo(new Vector3(0, .03f, -2.6f), Quaternion.identity);
            var controller = UnityEngine.Object.FindObjectOfType<QwenWorldController>();
            if (controller.exhibit != null)
                UdonSharpEditorUtility.GetBackingUdonBehaviour(controller.exhibit).SendCustomEvent("ClaimKeyboardLocal");
            runningController.SendCustomEvent("ResetConversation");
            controller.promptInput.text = worldCases.cases[caseIndex].question;
            lastCount = 0;
            actualTokens.Clear();
            runningController.SendCustomEvent("SendPrompt");
            var ids = (int[])runningEngine.GetProgramVariable("promptTokens");
            if (ids == null || !ids.SequenceEqual(worldCases.cases[caseIndex].input_ids))
                throw new Exception("Udon tokenizer parity failed: " + worldCases.cases[caseIndex].lang);
            phaseStart = EditorApplication.timeSinceStartup;
            SessionState.SetString("QwenPhase", "smoke-run");
        }

        static void CaptureScene(string path)
        {
            QwenPreview.Capture(path, new Vector3(3, 2.8f, -4.4f), new Vector3(0, .9f, 0), 70);
        }
    }
}
