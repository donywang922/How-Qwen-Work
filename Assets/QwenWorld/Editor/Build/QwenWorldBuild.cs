using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VRC.Core;
using VRC.SDK3.Editor;
using VRC.SDKBase;
using VRC.SDKBase.Editor;
using VRC.SDKBase.Network;

namespace QwenWorld.Editor
{
    [InitializeOnLoad]
    public static class QwenWorldBuild
    {
        private const string PendingKey = "QwenWorld.BuildTarget";
        private static double readySince;
        private static bool building;
        static QwenWorldBuild()
        {
            EditorApplication.update += Tick;
        }

        [MenuItem("Qwen World/Build/PC world")]
        public static void BuildPC()
        {
            Queue("PC", false, false);
        }

        [MenuItem("Qwen World/Build/Android viewer")]
        public static void BuildAndroid()
        {
            Queue("Android", false, false);
        }

        // Exercises the same process-scene callback used by a manual SDK build of the main scene.
        public static void ValidateAndroidFromMainBatch()
        {
            Queue("Android", false, false);
            SessionState.SetBool(PendingKey + ".Direct", true);
        }

        [MenuItem("Qwen World/Validation/Build iOS viewer from main (no upload)")]
        public static void ValidateIOSFromMainBatch()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Queue("iOS", false, false);
            SessionState.SetBool(PendingKey + ".Direct", true);
        }

        public static void BuildNativeTest()
        {
            Queue("PC", true, false);
        }

        [MenuItem("Qwen World/Validation/Build and run VRChat client test")]
        public static void BuildAndTest()
        {
            Queue("PC", true, true);
        }

        private static void Queue(string target, bool nativeTest, bool launch)
        {
            SessionState.SetString(PendingKey, target);
            SessionState.SetBool(PendingKey + ".Native", nativeTest);
            SessionState.SetBool(PendingKey + ".Launch", launch);
            SessionState.SetBool(PendingKey + ".Direct", false);
            readySince = 0;
        }

        private static void Tick()
        {
            string target = SessionState.GetString(PendingKey, "");
            if (building || target.Length == 0)
                return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || Environment.StackTrace.Contains("VRC.Editor.EnvConfig"))
            {
                readySince = 0;
                return;
            }

            if (readySince == 0)
                readySince = EditorApplication.timeSinceStartup;
            if (EditorApplication.timeSinceStartup - readySince < 5)
                return;
            SessionState.SetString(PendingKey, "");
            building = true;
            BuildAsync(target, SessionState.GetBool(PendingKey + ".Native", false), SessionState.GetBool(PendingKey + ".Launch", false), SessionState.GetBool(PendingKey + ".Direct", false));
        }

        private static async void BuildAsync(string target, bool nativeTest, bool launch, bool direct)
        {
            try
            {
                string failure = QwenModelPipeline.Results + "/bundle-failure.txt";
                if (File.Exists(failure)) File.Delete(failure);
                var expectedTarget = target == "iOS" ? BuildTarget.iOS : target == "Android" ? BuildTarget.Android : BuildTarget.StandaloneWindows64;
                bool viewer = QwenPlatformBuild.IsViewerTarget(expectedTarget);
                if (EditorUserBuildSettings.activeBuildTarget != expectedTarget)
                    throw new InvalidOperationException("Start Unity with -buildTarget " + (viewer ? target : "Win64"));
                QwenModelPipeline.Compile();
                string scenePath = viewer && !direct ? QwenPlatformBuild.CreateViewerScene() : nativeTest ? QwenNativeTestScene.Create() : QwenProject.MainScene;
                var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
                if (scene.path != scenePath)
                    scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                QwenProject.ResetSdkBuildCache(scene);
                QwenProject.SaveUdonFields(scene);
                UpdateLayers.SetupEditorLayers();
                UpdateLayers.SetupCollisionLayerMatrix();
                var descriptor = QwenProject.Components<VRC_SceneDescriptor>(scene).Single();
                if (descriptor.GetComponent<PipelineManager>() == null)
                    descriptor.gameObject.AddComponent<PipelineManager>();
                NetworkIDAssignment.ConfigureNetworkIDs(descriptor, out var errors, NetworkIDAssignment.SetError.IncompatibleTypes);
                if (errors.Count != 0)
                    throw new InvalidOperationException("Network ID errors: " + errors.Count);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                AssetDatabase.SaveAssets();
                if (viewer && !direct)
                    QwenPlatformBuild.ValidateViewerDependencies(scenePath);
                Directory.CreateDirectory(QwenModelPipeline.Results);
                if (!nativeTest && !direct)
                    File.WriteAllText(QwenModelPipeline.Results + "/network-" + (viewer ? target.ToLowerInvariant() : "pc") + ".json", "[" + string.Join(",", descriptor.NetworkIDCollection.Select(x => "{\"id\":" + x.ID + ",\"object\":\"" + x.gameObject.name + "\"}")) + "]");
                var panel = EditorWindow.GetWindow<VRCSdkControlPanel>();
                var builder = new HeadlessWorldBuilder(panel);
                // Public SDK validation/build pipeline. This code never invokes an upload API.
                string builtPath = await builder.Build();
                if (string.IsNullOrEmpty(builtPath) || !File.Exists(builtPath))
                    throw new IOException("SDK did not return a world bundle.");
                string destination = QwenProject.DataPath("builds/QwenLaboratory-" + (nativeTest ? "SDK-Validation-" : "") + target + ".vrcw");
                Directory.CreateDirectory(Path.GetDirectoryName(destination));
                File.Copy(builtPath, destination, true);
                long bytes = new FileInfo(destination).Length;
                if (bytes >= (viewer ? 100_000_000L : 1_000_000_000L))
                    throw new InvalidOperationException("World bundle exceeds its platform budget.");
                if (!nativeTest && !direct)
                    File.WriteAllText(QwenModelPipeline.Results + "/bundle-" + target + ".json", JsonUtility.ToJson(new BundleReport{platform = target, path = destination, bytes = bytes}, true));
                Debug.Log("QWEN_BUNDLE_READY " + target + " " + bytes + " bytes " + destination);
                if (direct)
                {
                    var source = EditorSceneManager.OpenScene(QwenProject.MainScene, OpenSceneMode.Single);
                    if (!QwenProject.Components<QwenWorldController>(source).Single().supportsInference)
                        throw new InvalidOperationException("Mobile build altered the authored PC scene.");
                    File.WriteAllText(QwenProject.DataPath("validation/refactor/direct-" + target.ToLowerInvariant() + ".json"), JsonUtility.ToJson(new BundleReport{platform = target + "-from-main", path = destination, bytes = bytes}, true));
                    Debug.Log("QWEN_DIRECT_" + target.ToUpperInvariant() + "_BUILD_PASSED");
                }

                if (launch)
                {
                    await RestoreSdkSession();
                    VRCSettings.NumClients = 1;
                    VRCSettings.ForceNoVR = true;
                    await builder.TestLastBuild();
                }

                if (Application.isBatchMode)
                    EditorApplication.Exit(0);
            }
            catch (Exception error)
            {
                Directory.CreateDirectory(QwenModelPipeline.Results);
                File.WriteAllText(QwenModelPipeline.Results + "/bundle-failure.txt", error.ToString());
                Debug.LogError("QWEN_BUNDLE_FAILED " + error);
                if (Application.isBatchMode)
                    EditorApplication.Exit(1);
            }
            finally
            {
                building = false;
            }
        }

        private static async Task RestoreSdkSession()
        {
            if (APIUser.IsLoggedIn) return;
            // The normal SDK panel does this from OnGUI; batch mode has no GUI frame.
            VRCSdkControlPanel.RefreshApiUrlSetting();
            if (!ApiCredentials.Load())
                throw new InvalidOperationException("Sign in through VRChat SDK before launching a client test.");
            var restored = new TaskCompletionSource<bool>();
            APIUser.InitialFetchCurrentUser(_ => restored.TrySetResult(true), _ => restored.TrySetResult(false));
            await Task.WhenAny(restored.Task, Task.Delay(30000));
            if (!restored.Task.IsCompleted || !restored.Task.Result || !APIUser.IsLoggedIn)
                throw new InvalidOperationException("Saved SDK session could not be restored; sign in through VRChat SDK.");
        }

        [MenuItem("Qwen World/Open main scene")]
        public static void OpenMainScene()
        {
            EditorSceneManager.OpenScene(QwenProject.MainScene, OpenSceneMode.Single);
        }

        public static void RestoreMainScene()
        {
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneWindows64)
                throw new InvalidOperationException("Restore with -buildTarget Win64.");
            int quality = Array.IndexOf(QualitySettings.names, "VRC High");
            if (quality >= 0)
                QualitySettings.SetQualityLevel(quality, true);
            OpenMainScene();
            EditorBuildSettings.scenes = new[]{new EditorBuildSettingsScene(QwenProject.MainScene, true)};
            Debug.Log("QWEN_WORKSPACE_RESTORED Win64 " + QwenProject.MainScene);
            QwenSceneInstaller.DiagnoseShadersBatch();
        }

        private sealed class HeadlessWorldBuilder : VRCSdkControlPanelWorldBuilder
        {
            public HeadlessWorldBuilder(VRCSdkControlPanel panel)
            {
                RegisterBuilder(panel);
                FindScenes();
            }
        }

        [Serializable]
        private class BundleReport
        {
            public string platform, path;
            public long bytes;
            public bool uploaded;
        }
    }
}
