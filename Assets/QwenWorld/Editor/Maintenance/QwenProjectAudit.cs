using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using VRC.SDKBase;

namespace QwenWorld.Editor
{
    public static class QwenProjectAudit
    {
        public static readonly string Output = QwenProject.DataPath("validation/refactor");
        [MenuItem("Qwen World/Maintenance/Audit project dependencies")]
        public static void ValidateBatch()
        {
            try
            {
                Directory.CreateDirectory(Output);
                QwenModelPipeline.Compile();
                var authored = Directory.GetFiles(QwenProject.Root + "/Scenes", "*.unity", SearchOption.AllDirectories);
                Require(authored.Length == 1 && authored[0].Replace('\\', '/') == QwenProject.MainScene, "Expected one authored scene.");
                Require(AssetDatabase.AssetPathToGUID(QwenProject.MainScene) == "45a918a17ba5668498ffa51b17eac902", "Main scene GUID changed.");
                Require(Directory.GetDirectories(QwenProject.Root + "/Models").Length == 1, "Retired model directory remains.");
                var scene = EditorSceneManager.OpenScene(QwenProject.MainScene, OpenSceneMode.Single);
                var controller = QwenProject.Components<QwenWorldController>(scene).Single();
                var runner = controller.runner;
                var display = QwenProject.Components<QwenComputeDisplay>(scene).Single();
                Require(controller.supportsInference && runner.supportsInference && runner.steps.Length == 849 && runner.buffers.Length == 89, "Inference graph changed.");
                Require(runner.outputUniforms != null && runner.tokenUniforms != null && !runner.outputUniforms.enabled && !runner.tokenUniforms.enabled, "Runtime uniform bindings missing or visible.");
                Require(display.resourceCount == 573 && display.readOffsets.Length == 850 && display.writeResources.Length == 849 && display.readResources.Length == 2266, "Display graph changed.");
                Require(QwenProject.Components<QwenSocialUI>(scene).Single().panels.Length == 80, "Chat pool changed.");
                Require(display.coreRenderer.GetComponent<MeshFilter>().sharedMesh.uv.Select(uv => Mathf.RoundToInt(uv.x)).Distinct().Count() == 24, "Core no longer has 24 layers.");
                ValidateSceneObjects(scene);
                ValidateGallery(scene);
                var pcIds = Ids(scene);
                var dependencies = new HashSet<string>(AssetDatabase.GetDependencies(QwenProject.MainScene, true));
                Require(dependencies.Any(p => p.StartsWith(QwenProject.ModelFolder + "/", StringComparison.Ordinal)), "Main model dependencies missing.");
                Require(!dependencies.Any(p => p.Contains("CEJK131k") || p.Contains("SelectedTensor") || p.Contains("Snapshot.mat")), "Legacy dependency remains.");
                var textures = dependencies.SelectMany(AssetDatabase.LoadAllAssetsAtPath).OfType<Texture>().Distinct().ToArray();
                var budget = new ResourceBudget{passesPerToken = runner.steps.Length, mipLevelsIncluded = true, texturePayloadBytes = textures.Where(t => !(t is RenderTexture)).Sum(TextureBytes), renderTexturePayloadBytes = runner.buffers.Distinct().Sum(t => TextureBytes(t))};
                File.WriteAllText(QwenProject.DataPath("validation/resource-budget.json"), JsonUtility.ToJson(budget, true));
                int shaders = 0;
                foreach (string path in Directory.GetFiles(QwenProject.Root + "/Shaders", "*.shader", SearchOption.AllDirectories))
                {
                    var shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
                    Require(shader != null && shader.isSupported && !ShaderUtil.ShaderHasError(shader), "Shader invalid: " + path);
                    shaders++;
                }

                string viewer = QwenPlatformBuild.CreateViewerScene();
                var viewerScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
                ValidateSceneObjects(viewerScene);
                Require(pcIds.SequenceEqual(Ids(viewerScene)), "Platform network IDs differ.");
                var viewerController = QwenProject.Components<QwenWorldController>(viewerScene).Single();
                Require(!viewerController.supportsInference && !viewerController.runner.supportsInference, "Viewer inference enabled.");
                Require(QwenProject.Components<QwenComputeDisplay>(viewerScene).Single().flowRenderer == null, "Viewer flow renderer remains.");
                var assets = AssetDatabase.GetAllAssetPaths().Where(p => p.StartsWith(QwenProject.Root + "/", StringComparison.Ordinal) && File.Exists(p)).ToArray();
                var baseline = File.Exists(Output + "/baseline.json") ? JsonUtility.FromJson<Baseline>(File.ReadAllText(Output + "/baseline.json")) : null;
                if (baseline != null)
                {
                    var modelBefore = baseline.assets.Where(a => a.path.StartsWith(QwenProject.ModelFolder + "/", StringComparison.Ordinal)).ToArray();
                    Require(modelBefore.All(a => File.Exists(a.path) && new FileInfo(a.path).Length == a.bytes), "Full model source size changed.");
                }

                EditorSceneManager.OpenScene(QwenProject.MainScene, OpenSceneMode.Single);
                EditorBuildSettings.scenes = new[]{new EditorBuildSettingsScene(QwenProject.MainScene, true)};
                var report = new AuditReport{passed = true, validatedAtUtc = DateTime.UtcNow.ToString("o"), mainScene = QwenProject.MainScene, authoredScenes = authored.Length, passes = 849, buffers = 89, panels = 573, customShaders = shaders, networkIds = pcIds, viewerModelDependencies = 0, mainSceneGuidPreserved = true, modelSourceSizesPreserved = baseline != null, customAssets = assets.Length, customAssetBytes = assets.Sum(p => new FileInfo(p).Length), generatedBuildBytes = assets.Where(p => p.StartsWith(QwenProject.Root + "/Generated/Build/", StringComparison.Ordinal)).Sum(p => new FileInfo(p).Length), baselineCustomAssetBytes = baseline?.customAssetBytes ?? 0};
                File.WriteAllText(Output + "/final-audit.json", JsonUtility.ToJson(report, true));
                if (File.Exists(Output + "/audit-failure.txt")) File.Delete(Output + "/audit-failure.txt");
                Debug.Log("QWEN_PROJECT_AUDIT_PASSED assets=" + report.customAssets + " bytes=" + report.customAssetBytes);
                if (Application.isBatchMode)
                    EditorApplication.Exit(0);
            }
            catch (Exception error)
            {
                File.WriteAllText(Output + "/audit-failure.txt", error.ToString());
                Debug.LogException(error);
                if (Application.isBatchMode)
                    EditorApplication.Exit(1);
                else
                    throw;
            }
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }

        private static string[] Ids(UnityEngine.SceneManagement.Scene scene) => QwenProject.Components<VRC_SceneDescriptor>(scene).Single().NetworkIDCollection.Select(x => x.ID + ":" + x.gameObject.name).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        private static void ValidateSceneObjects(UnityEngine.SceneManagement.Scene scene)
        {
            foreach (var root in scene.GetRootGameObjects())
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    Require(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject) == 0, "Missing script: " + t.name);
            foreach (var renderer in QwenProject.Components<MeshRenderer>(scene))
            {
                Require(renderer.sharedMaterials.All(m => m != null && m.shader != null && !ShaderUtil.ShaderHasError(m.shader)), "Invalid material: " + renderer.name);
                var mesh = renderer.GetComponent<MeshFilter>();
                Require(mesh == null || mesh.sharedMesh != null, "Missing mesh: " + renderer.name);
            }
        }

        private static void ValidateGallery(UnityEngine.SceneManagement.Scene scene)
        {
            var root = scene.GetRootGameObjects().Single(o => o.name == QwenTextureGalleryBuilder.RootName).transform;
            var outer = root.Find("Outer ring - static textures").GetComponentsInChildren<MeshRenderer>(true).Where(r => Regex.IsMatch(r.name, @"^\d{3} ")).OrderBy(r => r.name, StringComparer.Ordinal).ToArray();
            var inner = root.Find("Inner ring - live CRT").GetComponentsInChildren<MeshRenderer>(true).OrderBy(r => r.name, StringComparer.Ordinal).ToArray();
            Require(outer.Length == 484 && inner.Length == 89, "Gallery panel count changed.");
            QwenPanelPlacement.Reset();
            foreach (var set in new[]{outer, inner})
                for (int i = 0; i < set.Length; i++)
                {
                    var panel = set[i];
                    var size = panel.GetComponent<MeshFilter>().sharedMesh.bounds.size;
                    QwenPanelPlacement.Register(panel.transform.position, panel.transform.rotation, new Vector2(size.x, size.y), set == outer, i);
                }

            QwenPanelPlacement.ValidateAndReport(QwenProject.ModelName);
        }

        private static long TextureBytes(Texture texture)
        {
            var format = texture.graphicsFormat;
            int blockWidth = (int)GraphicsFormatUtility.GetBlockWidth(format), blockHeight = (int)GraphicsFormatUtility.GetBlockHeight(format);
            if (blockWidth == 0 || blockHeight == 0)
                return 0;
            int levels = texture is Texture2D t ? t.mipmapCount : texture is Cubemap c ? c.mipmapCount : 1;
            long bytes = 0;
            for (int level = 0; level < levels; level++)
                bytes += (long)((Math.Max(1, texture.width >> level) + blockWidth - 1) / blockWidth) * ((Math.Max(1, texture.height >> level) + blockHeight - 1) / blockHeight) * GraphicsFormatUtility.GetBlockSize(format);
            return bytes * (texture is Cubemap ? 6 : 1);
        }

        [Serializable]
        private class AuditReport
        {
            public bool passed, mainSceneGuidPreserved, modelSourceSizesPreserved;
            public string validatedAtUtc, mainScene;
            public string[] networkIds;
            public int authoredScenes, passes, buffers, panels, customShaders, customAssets, viewerModelDependencies;
            public long customAssetBytes, baselineCustomAssetBytes, generatedBuildBytes;
        }

        [Serializable]
        private class ResourceBudget
        {
            public long texturePayloadBytes, renderTexturePayloadBytes;
            public int passesPerToken;
            public bool mipLevelsIncluded;
            public string scope = "Referenced texture payloads and distinct inference buffers; excludes driver overhead, depth buffers, runtime copies and render targets created during play.";
        }

        public static void CaptureBaselineBatch()
        {
            Directory.CreateDirectory(Output);
            string main = QwenProject.MainScene;
            var dependencies = new HashSet<string>(AssetDatabase.GetDependencies(main, true));
            var assets = AssetDatabase.GetAllAssetPaths().Where(p => p.StartsWith("Assets/QwenWorld/", StringComparison.Ordinal) && File.Exists(p)).Select(p => new AssetEntry{path = p, bytes = new FileInfo(p).Length, runtimeDependency = dependencies.Contains(p)}).ToArray();
            EditorSceneManager.OpenScene(main, OpenSceneMode.Single);
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            var runner = UnityEngine.Object.FindObjectOfType<QwenTextureRunner>();
            var report = new Baseline{capturedAtUtc = DateTime.UtcNow.ToString("o"), mainScene = main, assets = assets, mainDependencies = dependencies.OrderBy(p => p).ToArray(), roots = scene.GetRootGameObjects().Select(o => o.name).ToArray(), passes = runner.steps.Length, buffers = runner.buffers.Length, customAssetBytes = assets.Sum(a => a.bytes), customAssetCount = assets.Length, sourceFiles = Directory.GetFiles("Assets/QwenWorld", "*.cs", SearchOption.AllDirectories).Length};
            File.WriteAllText(Output + "/baseline.json", JsonUtility.ToJson(report, true));
            Debug.Log("QWEN_REFACTOR_BASELINE_READY assets=" + report.customAssetCount + " bytes=" + report.customAssetBytes);
            if (Application.isBatchMode)
                EditorApplication.Exit(0);
        }

        [Serializable]
        public class AssetEntry
        {
            public string path;
            public long bytes;
            public bool runtimeDependency;
        }

        [Serializable]
        class Baseline
        {
            public string capturedAtUtc, mainScene;
            public string[] mainDependencies, roots;
            public AssetEntry[] assets;
            public int passes, buffers, customAssetCount, sourceFiles;
            public long customAssetBytes;
        }
    }
}
