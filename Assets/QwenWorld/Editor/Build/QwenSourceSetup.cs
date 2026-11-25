using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VRC.Core;

namespace QwenWorld.Editor
{
    public static class QwenSourceSetup
    {
        [Serializable] class LocalConfig { public string dataRoot,unityPath,worldBlueprintId; }
        [Serializable] class Payloads { public string modelFolder; public Payload[] payloads; }
        [Serializable] class Payload { public string path,meta; }
        [Serializable] class Report { public bool passed; public int payloads,dependencies; public string scope; }

        [MenuItem("Qwen World/Setup/Initialize source checkout")]
        public static void InitializeBatch()
        {
            try
            {
                string project = Directory.GetParent(Application.dataPath).FullName;
                var manifest = JsonUtility.FromJson<Payloads>(File.ReadAllText(Path.Combine(project,"Tools/Qwen35/model-payloads.lock.json")));
                foreach (var item in manifest.payloads)
                {
                    string path = manifest.modelFolder + "/" + item.path;
                    if (!File.Exists(path)) throw new FileNotFoundException("Run Tools/Qwen35/Initialize-Qwen.ps1 before importing the scene: " + path);
                    string guid = Regex.Match(item.meta,@"(?m)^guid: ([0-9a-f]+)").Groups[1].Value;
                    if (AssetDatabase.AssetPathToGUID(path) != guid)
                        throw new InvalidOperationException("Model import GUID differs; rerun Initialize-Qwen.ps1: " + path);
                }
                QwenModelPipeline.Compile();
                var scene = EditorSceneManager.OpenScene(QwenProject.MainScene,OpenSceneMode.Single);
                var pipeline = QwenProject.Components<PipelineManager>(scene).SingleOrDefault();
                if (pipeline != null && !string.IsNullOrEmpty(pipeline.blueprintId))
                {
                    string localPath = Path.Combine(project,".qwen-local.json");
                    var config = File.Exists(localPath) ? JsonUtility.FromJson<LocalConfig>(File.ReadAllText(localPath)) : new LocalConfig();
                    config.worldBlueprintId = pipeline.blueprintId;
                    File.WriteAllText(localPath,JsonUtility.ToJson(config,true) + "\n");
                    pipeline.blueprintId = "";
                    EditorUtility.SetDirty(pipeline);
                    Debug.Log("Preserved published-world binding in ignored local configuration; public scene is unbound.");
                }
                QwenProject.ResetSdkBuildCache(scene);
                QwenProject.SaveUdonFields(scene);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                AssetDatabase.SaveAssets();
                var dependencies = AssetDatabase.GetDependencies(QwenProject.MainScene,true);
                var missingSource = dependencies.Where(p => p.StartsWith("Assets/UdonSharp/",StringComparison.Ordinal)
                    || p.StartsWith("Assets/Scenes/",StringComparison.Ordinal)).ToArray();
                if (missingSource.Length > 0) throw new InvalidOperationException("Excluded source dependency: " + string.Join(", ",missingSource));
                if (AssetDatabase.AssetPathToGUID(QwenProject.MainScene) != "45a918a17ba5668498ffa51b17eac902")
                    throw new InvalidOperationException("Authored scene GUID changed");
                // A maintainer's existing ID is tested only in an ignored scene copy.
                string localConfig = Path.Combine(project,".qwen-local.json");
                var savedConfig = File.Exists(localConfig) ? JsonUtility.FromJson<LocalConfig>(File.ReadAllText(localConfig)) : new LocalConfig();
                if (Application.isBatchMode && !string.IsNullOrEmpty(savedConfig.worldBlueprintId))
                {
                    OpenLocalPublishCopy();
                    var localScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
                    if (QwenProject.Components<PipelineManager>(localScene).Single().blueprintId != savedConfig.worldBlueprintId)
                        throw new InvalidOperationException("Local publish copy lost its saved world binding");
                    EditorSceneManager.OpenScene(QwenProject.MainScene,OpenSceneMode.Single);
                    if (QwenProject.Components<PipelineManager>(UnityEngine.SceneManagement.SceneManager.GetActiveScene()).Any(p => !string.IsNullOrEmpty(p.blueprintId)))
                        throw new InvalidOperationException("Local publishing modified the public scene binding");
                }
                string output = QwenProject.DataPath("validation/source-import.json");
                Directory.CreateDirectory(Path.GetDirectoryName(output));
                File.WriteAllText(output,JsonUtility.ToJson(new Report
                {
                    passed=true,payloads=manifest.payloads.Length,dependencies=dependencies.Length,
                    scope="Restored model GUIDs, Udon compilation, authored scene references and source exclusions. No inference, native client or bundle build."
                },true));
                Debug.Log("QWEN_SOURCE_CHECKOUT_READY payloads=" + manifest.payloads.Length);
                if (Application.isBatchMode) EditorApplication.Exit(0);
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                if (Application.isBatchMode) EditorApplication.Exit(1);
                else throw;
            }
        }

        [MenuItem("Qwen World/Build/Open local publish copy")]
        public static void OpenLocalPublishCopy()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first");
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            string project = Directory.GetParent(Application.dataPath).FullName;
            string configPath = Path.Combine(project,".qwen-local.json");
            var config = File.Exists(configPath) ? JsonUtility.FromJson<LocalConfig>(File.ReadAllText(configPath)) : new LocalConfig();
            if (string.IsNullOrEmpty(config.worldBlueprintId))
                throw new InvalidOperationException("Set worldBlueprintId in ignored .qwen-local.json to update an existing world. The source scene is intentionally unbound.");
            string source = QwenPlatformBuild.IsViewerTarget(EditorUserBuildSettings.activeBuildTarget) ? QwenPlatformBuild.CreateViewerScene() : QwenProject.MainScene;
            const string target = QwenProject.Root + "/Generated/Build/QwenLaboratory-LocalPublish.unity";
            EditorSceneManager.OpenScene(QwenProject.MainScene,OpenSceneMode.Single);
            QwenProject.EnsureAssetFolder(QwenProject.Root + "/Generated/Build");
            File.Copy(source,target,true);
            AssetDatabase.ImportAsset(target);
            var scene = EditorSceneManager.OpenScene(target,OpenSceneMode.Single);
            var descriptor = QwenProject.Components<VRC.SDKBase.VRC_SceneDescriptor>(scene).Single();
            var pipeline = descriptor.GetComponent<PipelineManager>() ?? descriptor.gameObject.AddComponent<PipelineManager>();
            QwenPlatformBuild.CopyLightingForScene(target,QwenProject.Root + "/Generated/Build/LocalPublishLighting.asset");
            pipeline.blueprintId = config.worldBlueprintId;
            EditorUtility.SetDirty(pipeline);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("Opened ignored local publish copy. Continue using the SDK panel; no upload has been performed.");
        }
    }
}
