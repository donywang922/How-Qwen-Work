using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace QwenWorld.Editor
{
    public static class QwenGalleryBuilder
    {
        static readonly string ReportFolder = QwenProject.DataPath("validation/gallery");
        [MenuItem("Qwen World/Environment/Install texture gallery in PC scenes")]
        public static void Install()
        {
            QwenSceneInstaller.Install();
        }

        public static void InstallBatch()
        {
            QwenSceneInstaller.InstallBatch();
        }

        internal static GameObject Add(QwenTextureRunner runner, string modelFolder)
        {
            var scene = SceneManager.GetActiveScene();
            var previous = scene.GetRootGameObjects().FirstOrDefault(go => go.name == QwenTextureGalleryBuilder.RootName);
            var floor = scene.GetRootGameObjects().Single(go => go.name == "Floor").GetComponent<Renderer>();
            string output = $"{QwenModelPipeline.Root}/Generated/Gallery/{Path.GetFileName(modelFolder)}";
            var root = new GameObject(QwenTextureGalleryBuilder.RootName);
            QwenPanelPlacement.Reset();
            var archive = QwenTextureGalleryBuilder.AddStatic(root.transform, runner.steps, modelFolder, output);
            if (archive.sources.Length != 484 || runner.buffers.Length != 89)
                throw new InvalidOperationException($"Expected 484 static and 89 live textures, got {archive.sources.Length} and {runner.buffers.Length}.");
            QwenTextureGalleryBuilder.AddLive(root.transform, runner.buffers, output);
            QwenGalleryLayout.Align(root.transform, Path.GetFileName(modelFolder));
            var platform = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            platform.name = "Gallery walkable floor";
            platform.transform.SetParent(root.transform, false);
            platform.transform.localPosition = new Vector3(0, -.08f, 0);
            float diameter = (QwenTextureGalleryBuilder.StaticRadius + QwenTextureGalleryBuilder.StaticRadiusVariation + .7f) * 2;
            platform.transform.localScale = new Vector3(diameter, .04f, diameter);
            platform.GetComponent<MeshRenderer>().sharedMaterial = floor.sharedMaterial;
            UnityEngine.Object.DestroyImmediate(platform.GetComponent<CapsuleCollider>());
            platform.AddComponent<MeshCollider>().sharedMesh = platform.GetComponent<MeshFilter>().sharedMesh;
            if (previous != null)
                UnityEngine.Object.DestroyImmediate(previous);
            return root;
        }
    }
}
