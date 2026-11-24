using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace QwenWorld.Editor
{
    public static class QwenProjectMaintenance
    {
        [MenuItem("Qwen World/Maintenance/Archive and remove retired assets")]
        public static void CleanupBatch()
        {
            try
            {
                QwenModelPipeline.Compile();
                var scene = EditorSceneManager.OpenScene(QwenProject.MainScene, OpenSceneMode.Single);
                QwenProject.ResetSdkBuildCache(scene);
                QwenProject.SaveUdonFields(scene);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                AssetDatabase.SaveAssets();
                var required = new HashSet<string>(AssetDatabase.GetDependencies(QwenProject.MainScene, true));
                var retired = new[]{QwenProject.Root + "/Models/Qwen35-CEJK131k-Q4", QwenProject.Root + "/Environment/WhiteExhibit/Compact", QwenProject.Root + "/Generated/Gallery/Qwen35-CEJK131k-Q4", QwenProject.Root + "/Scenes/QwenLaboratory-Compact-PC.unity", QwenProject.Root + "/Scenes/QwenLaboratory-Android.unity", QwenProject.Root + "/Scenes/QwenLaboratory-Android", QwenProject.Root + "/Tests/QwenLaboratory-SDK-Validation.unity"};
                string archive = QwenProject.DataPath("archive/cleanup/" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
                var removed = new List<Entry>();
                foreach (string path in retired.Where(Exists))
                    Remove(path, required, archive, removed);
                foreach (string path in AssetDatabase.GetAllAssetPaths().Where(p => p.StartsWith(QwenProject.Root + "/", StringComparison.Ordinal) && File.Exists(p)).ToArray())
                {
                    if (required.Contains(path) || Protected(path))
                        continue;
                    Remove(path, required, archive, removed);
                }

                // The imported VRC template is not a production scene or an authoring source.
                foreach (string path in new[]{"Assets/Scenes/VRCDefaultWorldScene.unity", "Assets/Scenes/VRCDefaultWorldScene"}.Where(Exists))
                    Remove(path, required, archive, removed);
                foreach (string path in Directory.GetDirectories(QwenProject.Root, "*", SearchOption.AllDirectories).OrderByDescending(p => p.Length))
                    if (!Directory.EnumerateFileSystemEntries(path).Any())
                        Remove(path.Replace('\\', '/'), required, archive, removed);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                Directory.CreateDirectory(QwenProject.DataPath("validation/refactor"));
                string reportPath = QwenProject.DataPath("validation/refactor/cleanup.json");
                var history = File.Exists(reportPath) ? JsonUtility.FromJson<Report>(File.ReadAllText(reportPath)) : null;
                var all = history?.removed?.ToList() ?? new List<Entry>();
                foreach (var entry in all)
                    if (string.IsNullOrEmpty(entry.archive))
                        entry.archive = history.archive;
                all.AddRange(removed);
                File.WriteAllText(reportPath, JsonUtility.ToJson(new Report{passed = true, archive = archive, removed = all.ToArray(), removedBytes = all.Sum(e => e.bytes), retainedRuntimeDependencies = required.Count}, true));
                Debug.Log("QWEN_CLEANUP_PASSED assets=" + removed.Count + " bytes=" + removed.Sum(e => e.bytes));
                if (Application.isBatchMode)
                    EditorApplication.Exit(0);
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

        private static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);
        private static bool Protected(string path)
        {
            string[] directories = {"/Editor/", "/Runtime/", "/Models/" + QwenProject.ModelName + "/", "/Fonts/", "/Licenses/", "/Environment/ImportedOrbitalRing/", "/Environment/QwenGirl/", "/Generated/Build/"};
            if (directories.Any(d => path.StartsWith(QwenProject.Root + d, StringComparison.Ordinal)))
                return true;
            if (path.StartsWith(QwenProject.Root + "/Shaders/", StringComparison.Ordinal))
                return !new[]{"OrbitalStructure.shader", "TensorHeatmap.shader", "TensorSnapshot.shader"}.Contains(Path.GetFileName(path));
            if (path.StartsWith(QwenProject.Root + "/Tests/", StringComparison.Ordinal))
                return path.EndsWith(".cs") || path.EndsWith(".asset");
            return path.EndsWith("SkySources.txt", StringComparison.Ordinal);
        }

        private static void Remove(string path, HashSet<string> required, string archive, List<Entry> removed)
        {
            string full = Path.GetFullPath(path);
            string permitted = Path.GetFullPath(QwenProject.Root) + Path.DirectorySeparatorChar;
            bool template = full == Path.GetFullPath("Assets/Scenes/VRCDefaultWorldScene") || full == Path.GetFullPath("Assets/Scenes/VRCDefaultWorldScene.unity");
            if (!full.StartsWith(permitted, StringComparison.OrdinalIgnoreCase) && !template)
                throw new InvalidOperationException("Cleanup outside Qwen assets: " + path);
            if (required.Any(p => p == path || p.StartsWith(path + "/", StringComparison.Ordinal)))
                throw new InvalidOperationException("Retired asset remains in use: " + path);
            var files = Directory.Exists(path) ? Directory.GetFiles(path, "*", SearchOption.AllDirectories).ToList() : new List<string>{path};
            if (File.Exists(path + ".meta"))
                files.Add(path + ".meta");
            long bytes = 0;
            foreach (string file in files)
            {
                string destination = Path.Combine(archive, file);
                Directory.CreateDirectory(Path.GetDirectoryName(destination));
                File.Copy(file, destination, true);
                bytes += new FileInfo(file).Length;
            }

            if (!AssetDatabase.DeleteAsset(path))
                throw new IOException("Could not remove " + path);
            removed.Add(new Entry{path = path, bytes = bytes, archive = archive});
        }

        [Serializable]
        private class Entry
        {
            public string path, archive;
            public long bytes;
        }

        [Serializable]
        private class Report
        {
            public bool passed;
            public string archive;
            public long removedBytes;
            public int retainedRuntimeDependencies;
            public Entry[] removed;
        }
    }
}
