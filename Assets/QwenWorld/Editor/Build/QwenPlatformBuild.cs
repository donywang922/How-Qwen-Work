using System;
using System.IO;
using System.Linq;
using ShaderGPT.Udon;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRC.SDKBase;
using VRC.SDKBase.Network;

namespace QwenWorld.Editor
{
    /// <summary>One authored world; Android is a deterministic build-time projection.</summary>
    public sealed class QwenPlatformBuild : IProcessSceneWithReport
    {
        public int callbackOrder => -10000;
        public void OnProcessScene(Scene scene, BuildReport report)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || Application.isPlaying || EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
                return;
            if (QwenProject.Components<QwenWorldController>(scene).Length == 0)
                return;
            StripInference(scene);
            Debug.Log("QWEN_ANDROID_BUILD_SCENE_STRIPPED " + scene.path);
        }

        public static string CreateViewerScene()
        {
            QwenProject.EnsureAssetFolder(Path.GetDirectoryName(QwenProject.ViewerScene));
            if (File.Exists(QwenProject.ViewerScene))
            {
                if (SceneManager.GetActiveScene().path != QwenProject.MainScene)
                    EditorSceneManager.OpenScene(QwenProject.MainScene, OpenSceneMode.Single);
                if (!AssetDatabase.DeleteAsset(QwenProject.ViewerScene))
                    throw new IOException("Could not replace generated viewer scene.");
            }

            if (!AssetDatabase.CopyAsset(QwenProject.MainScene, QwenProject.ViewerScene))
                throw new IOException("Could not create Android build scene from the main scene.");
            var scene = EditorSceneManager.OpenScene(QwenProject.ViewerScene, OpenSceneMode.Single);
            StripInference(scene);
            CopyViewerLighting();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            ValidateViewerDependencies(QwenProject.ViewerScene);
            return QwenProject.ViewerScene;
        }

        public static void StripInference(Scene scene)
        {
            var controller = QwenProject.Components<QwenWorldController>(scene).Single();
            var runner = controller.runner;
            controller.supportsInference = false;
            runner.supportsInference = false;
            runner.steps = null;
            runner.buffers = null;
            runner.stepLayers = null;
            runner.controlMaterial = null;
            runner.clearMaterial = null;
            runner.outputUniforms = null;
            runner.tokenUniforms = null;
            var tokenizer = controller.tokenizer;
            tokenizer.tokenizerJson = null;
            tokenizer.enabled = false;
            if (controller.promptInput != null)
                controller.promptInput.interactable = false;
            if (controller.sendButton != null)
                controller.sendButton.interactable = false;
            foreach (var menu in QwenProject.Components<QwenLocalMenu>(scene))
                menu.textureRoots = Array.Empty<GameObject>();
            foreach (var display in QwenProject.Components<QwenComputeDisplay>(scene))
            {
                if (display.flowRenderer != null)
                    UnityEngine.Object.DestroyImmediate(display.flowRenderer.gameObject);
                display.flowRenderer = null;
                display.resourceCount = 0;
                display.readOffsets = null;
                display.readResources = null;
                display.writeResources = null;
            }

            foreach (var go in scene.GetRootGameObjects())
                if (go.name == "PC MODEL DATA - excluded from Android" || go.name == QwenTextureGalleryBuilder.RootName)
                    UnityEngine.Object.DestroyImmediate(go);
            QwenProject.SaveUdonFields(scene);
            // Preserve the same network objects/IDs as the PC scene, including tokenizer/engine placeholders.
            var descriptor = QwenProject.Components<VRC_SceneDescriptor>(scene).Single();
            // The SDK caches dynamic material/prefab dependencies on the descriptor.
            // Rebuild these from the stripped scene instead of carrying the PC cache.
            QwenProject.ResetSdkBuildCache(scene);
            NetworkIDAssignment.ConfigureNetworkIDs(descriptor, out var errors, NetworkIDAssignment.SetError.IncompatibleTypes);
            if (errors.Count != 0)
                throw new BuildFailedException("Android network IDs are inconsistent: " + errors.Count);
        }

        public static void ValidateViewerDependencies(string scenePath)
        {
            var models = AssetDatabase.GetDependencies(scenePath, true).Where(p => p.StartsWith(QwenProject.Root + "/Models/", StringComparison.Ordinal)).ToArray();
            if (models.Length != 0)
            {
                var causes = AssetDatabase.GetDependencies(scenePath, false).Where(p => p != scenePath).Select(p => new
                {
                path = p, count = AssetDatabase.GetDependencies(p, true).Count(d => d.StartsWith(QwenProject.Root + "/Models/", StringComparison.Ordinal))}).Where(p => p.count > 0).Select(p => p.path + ": " + p.count);
                string diagnostic = QwenProject.DataPath("validation/refactor/viewer-dependency-causes.txt");
                Directory.CreateDirectory(Path.GetDirectoryName(diagnostic));
                File.WriteAllLines(diagnostic, causes);
                throw new BuildFailedException("Android scene retained " + models.Length + " model dependencies. See viewer-dependency-causes.txt.");
            }
        }

        private static void CopyViewerLighting()
        {
            CopyLightingForScene(QwenProject.ViewerScene,QwenProject.Root + "/Generated/Build/ViewerLighting.asset");
        }

        public static void CopyLightingForScene(string targetScene,string path)
        {
            var original = Lightmapping.lightingDataAsset;
            if (original == null)
                return;
            string source = AssetDatabase.GetAssetPath(original);
            if (File.Exists(path))
                AssetDatabase.DeleteAsset(path);
            if (!AssetDatabase.CopyAsset(source, path))
                throw new IOException("Could not copy viewer lighting metadata.");
            var copy = AssetDatabase.LoadAssetAtPath<LightingDataAsset>(path);
            var serialized = new SerializedObject(copy);
            var scene = serialized.FindProperty("m_Scene");
            if (scene == null)
                throw new InvalidOperationException("Lighting metadata has no scene binding.");
            scene.objectReferenceValue = AssetDatabase.LoadAssetAtPath<SceneAsset>(targetScene);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            Lightmapping.lightingDataAsset = copy;
            EditorUtility.SetDirty(copy);
        }
    }
}
