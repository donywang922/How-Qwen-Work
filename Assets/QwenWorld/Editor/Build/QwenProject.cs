using System;
using System.IO;
using System.Linq;
using UdonSharp;
using UdonSharpEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace QwenWorld.Editor
{
    /// <summary>Canonical paths and scene-scoped editor operations.</summary>
    public static class QwenProject
    {
        public const string Root = "Assets/QwenWorld";
        public const string MainScene = Root + "/Scenes/QwenLaboratory.unity";
        public const string ViewerScene = Root + "/Generated/Build/QwenLaboratory-Android.unity";
        public const string NativeTestScene = Root + "/Generated/Build/QwenLaboratory-SDK-Validation.unity";
        public const string ModelName = "Qwen35-Full-Q4";
        public const string ModelFolder = Root + "/Models/" + ModelName;
        [Serializable] class LocalPaths { public string dataRoot; }
        public static string DataRoot
        {
            get
            {
                string project = Directory.GetParent(Application.dataPath).FullName;
                string configured = Environment.GetEnvironmentVariable("QWEN_DATA_ROOT");
                string local = Path.Combine(project,".qwen-local.json");
                if (string.IsNullOrWhiteSpace(configured) && File.Exists(local))
                    configured = JsonUtility.FromJson<LocalPaths>(File.ReadAllText(local)).dataRoot;
                if (string.IsNullOrWhiteSpace(configured)) configured = ".qwen-work";
                return Path.GetFullPath(Path.IsPathRooted(configured) ? configured : Path.Combine(project,configured)).Replace('\\','/').TrimEnd('/');
            }
        }
        public static string DataPath(string relative) => DataRoot + "/" + relative.TrimStart('/');
        public static T[] Components<T>(Scene scene)
            where T : Component => scene.GetRootGameObjects().SelectMany(o => o.GetComponentsInChildren<T>(true)).ToArray();
        public static void SaveUdonFields(Scene scene)
        {
            foreach (var proxy in Components<UdonSharpBehaviour>(scene))
            {
                UdonSharpEditorUtility.CopyProxyToUdon(proxy, ProxySerializationPolicy.All);
                var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(proxy);
                string[] obsolete = proxy is QwenTextureRunner ? new[]{"stepLabels", "snapshotMaterial", "snapshot", "heatmapMaterial", "tensorLabel", "progressLabel", "selectedPass"} : proxy is QwenWorldController ? new[]{"modelText", "transcriptText", "modelDescription", "systemPrompt"} : proxy is QwenLocalization ? new[]{"replyLanguage", "replyLabel", "replyButton"} : proxy is QwenLocalMenu ? new[]{"mountOnArm", "mountButton", "mountLabel"} : proxy is QwenExhibitState ? new[]{"waitingPlayers"} : Array.Empty<string>();
                foreach (string symbol in obsolete)
                    backing.publicVariables.RemoveVariable(symbol);
                UnityEditor.EditorUtility.SetDirty(backing);
            }
        }

        public static void EnsureAssetFolder(string path)
        {
            Directory.CreateDirectory(path);
            UnityEditor.AssetDatabase.Refresh(UnityEditor.ImportAssetOptions.ForceSynchronousImport);
        }

        public static void ResetSdkBuildCache(Scene scene)
        {
            foreach (var descriptor in Components<VRC.SDKBase.VRC_SceneDescriptor>(scene))
            {
                var serialized = new UnityEditor.SerializedObject(descriptor);
                foreach (string name in new[]{"DynamicMaterials", "DynamicPrefabs"})
                {
                    var property = serialized.FindProperty(name);
                    if (property != null && property.isArray)
                        property.arraySize = 0;
                }

                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }
    }
}
