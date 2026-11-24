using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace QwenWorld.Editor
{
    /// <summary>Static orbital background. No runtime scripts, cameras, CRTs or lights.</summary>
    public static class QwenSkyBuilder
    {
        const string Folder = "Assets/QwenWorld/Environment";
        const string RootName = "Qwen orbital sky environment";
        const string PcScene = QwenProject.MainScene;
        static string[] Scenes => QwenSceneInstaller.ActiveScenePaths;
        static readonly string Output = QwenProject.DataPath("validation/sky");
        static Material sky, structure;
        static Mesh bodyMesh;
        [MenuItem("Qwen World/Environment/Apply orbital sky to all production scenes")]
        public static void ApplyAll()
        {
            ApplyAllCore(true);
        }

        public static void RefreshOuterRing()
        {
            ApplyAllCore(false);
        }

        static void ApplyAllCore(bool refreshCredits)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play Mode before editing sky.");
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;
            string original = SceneManager.GetActiveScene().path;
            EnsureAssets();
            Directory.CreateDirectory(Output);
            var reports = new List<SceneReport>();
            foreach (string path in Scenes)
            {
                if (!File.Exists(path))
                    continue;
                // Keep the exact pre-sky scene for a reversible first installation.
                string backup = Output + "/before-" + Path.GetFileName(path);
                if (!File.Exists(backup))
                    File.Copy(path, backup);
                var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                ApplyToCurrentScene(refreshCredits);
                EditorSceneManager.SaveScene(scene);
                reports.Add(Validate(scene));
            }

            AssetDatabase.SaveAssets();
            File.WriteAllText(Output + "/sky-validation.json", JsonUtility.ToJson(new Report{scenes = reports.ToArray(), bodyTriangles = bodyMesh.triangles.Length / 3, lightTriangles = 0, backgroundWidth = 4096, backgroundHeight = 2048}, true));
            EditorSceneManager.OpenScene(string.IsNullOrEmpty(original) ? PcScene : original, OpenSceneMode.Single);
            Debug.Log("QWEN_ORBITAL_SKY_FINISHED " + Output + "/sky-validation.json");
        }

        public static void ApplyToCurrentScene(bool refreshCredits = true)
        {
            if (sky == null)
                EnsureAssets();
            var scene = SceneManager.GetActiveScene();
            foreach (var root in scene.GetRootGameObjects())
                if (root.name == RootName)
                    UnityEngine.Object.DestroyImmediate(root);
            RenderSettings.skybox = sky;
            var environment = new GameObject(RootName);
            AddMesh(environment, "Orbital ring - imported Blender distant asset", bodyMesh, structure);
            foreach (var camera in scene.GetRootGameObjects().SelectMany(go => go.GetComponentsInChildren<Camera>(true)))
                if (camera.CompareTag("EditorOnly"))
                    camera.clearFlags = CameraClearFlags.Skybox;
            var footer = scene.GetRootGameObjects().SelectMany(go => go.GetComponentsInChildren<UnityEngine.UI.Text>(true)).FirstOrDefault(label => label.name == "Footer");
            if (footer != null && refreshCredits)
            {
                int marker = footer.text.IndexOf("\nSky:", StringComparison.Ordinal);
                string original = marker >= 0 ? footer.text.Substring(0, marker) : footer.text;
                footer.text = original + "\nSky: NASA/GSFC SVS; ESA/Gaia/DPAC | Earth: NASA Earth Observatory, R. Stoeckli; Suomi NPP/VIIRS. Artistic composite.";
                footer.fontSize = 15;
                footer.rectTransform.sizeDelta = new Vector2(footer.rectTransform.sizeDelta.x, 45);
                EditorUtility.SetDirty(footer);
            }

            EditorSceneManager.MarkSceneDirty(scene);
        }

        static void EnsureAssets()
        {
            Directory.CreateDirectory(Folder);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            string path = Folder + "/OrbitalSky.png";
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
                throw new FileNotFoundException("Run Tools/Qwen35/make_orbital_sky.py first", path);
            importer.textureType = TextureImporterType.Default;
            importer.textureShape = TextureImporterShape.Texture2D;
            importer.sRGBTexture = true;
            importer.isReadable = false;
            importer.mipmapEnabled = true;
            importer.wrapModeU = TextureWrapMode.Repeat;
            importer.wrapModeV = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Trilinear;
            importer.anisoLevel = 1;
            importer.maxTextureSize = 4096;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.SetPlatformTextureSettings(new TextureImporterPlatformSettings{name = "Standalone", overridden = true, maxTextureSize = 4096, format = TextureImporterFormat.BC7, compressionQuality = 100});
            importer.SetPlatformTextureSettings(new TextureImporterPlatformSettings{name = "Android", overridden = true, maxTextureSize = 2048, format = TextureImporterFormat.ASTC_6x6, compressionQuality = 100});
            importer.SaveAndReimport();
            sky = GetMaterial("OrbitalSky", "Skybox/Panoramic");
            sky.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(path));
            sky.SetColor("_Tint", new Color(.5f, .5f, .5f, 1));
            sky.SetFloat("_Exposure", 1);
            sky.SetFloat("_Mapping", 1);
            sky.SetFloat("_ImageType", 0);
            sky.SetFloat("_Rotation", 0);
            QwenOrbitalRingMesh.ConfigureImport();
            structure = GetMaterial("OrbitalStructure", "QwenWorld/OrbitalRingPBR");
            QwenOrbitalRingMesh.ConfigureMaterial(structure);
            bodyMesh = SaveMesh("OrbitalRing", QwenOrbitalRingMesh.Build());
            foreach (var material in new[]{sky, structure})
            {
                EditorUtility.SetDirty(material);
                if (ShaderUtil.ShaderHasError(material.shader))
                    throw new InvalidOperationException("Shader failed: " + material.shader.name);
            }

            AssetDatabase.SaveAssets();
        }

        static Material GetMaterial(string name, string shaderName)
        {
            string path = Folder + "/" + name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            var shader = Shader.Find(shaderName);
            if (shader == null)
                throw new InvalidOperationException("Missing shader: " + shaderName);
            if (mat == null)
            {
                mat = new Material(shader)
                {name = name};
                AssetDatabase.CreateAsset(mat, path);
            }
            else
                mat.shader = shader;
            return mat;
        }

        static Mesh SaveMesh(string name, Mesh mesh)
        {
            mesh.name = name;
            string path = Folder + "/" + name + ".asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(mesh, path);
                return mesh;
            }

            // Re-upload the actual vertex/index buffers as well as serialized bounds.
            // CopySerialized alone can leave a loaded mesh's GPU buffer stale.
            existing.Clear();
            existing.indexFormat = mesh.indexFormat;
            existing.vertices = mesh.vertices;
            existing.normals = mesh.normals;
            existing.colors = mesh.colors;
            existing.uv = mesh.uv;
            existing.uv2 = mesh.uv2;
            existing.triangles = mesh.triangles;
            existing.tangents = mesh.tangents;
            existing.bounds = mesh.bounds;
            UnityEngine.Object.DestroyImmediate(mesh);
            EditorUtility.SetDirty(existing);
            return existing;
        }

        static void AddMesh(GameObject root, string name, Mesh mesh, Material material)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(root.transform, false);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.allowOcclusionWhenDynamic = false;
        }

        [Serializable]
        class SceneReport
        {
            public string scene;
            public bool skyAssigned, noRuntimeBehaviours, noColliders, previewUsesSky;
            public int renderers;
        }

        [Serializable]
        class Report
        {
            public int bodyTriangles, lightTriangles, backgroundWidth, backgroundHeight;
            public SceneReport[] scenes;
        }

        static SceneReport Validate(Scene scene)
        {
            var root = scene.GetRootGameObjects().Single(go => go.name == RootName);
            var cameras = scene.GetRootGameObjects().SelectMany(go => go.GetComponentsInChildren<Camera>(true)).Where(camera => camera.CompareTag("EditorOnly")).ToArray();
            var report = new SceneReport{scene = scene.path, skyAssigned = RenderSettings.skybox == sky, noRuntimeBehaviours = root.GetComponentsInChildren<MonoBehaviour>(true).Length == 0, noColliders = root.GetComponentsInChildren<Collider>(true).Length == 0, previewUsesSky = cameras.All(camera => camera.clearFlags == CameraClearFlags.Skybox), renderers = root.GetComponentsInChildren<Renderer>().Length};
            if (!report.skyAssigned || !report.noRuntimeBehaviours || !report.noColliders || !report.previewUsesSky || report.renderers != 1)
                throw new InvalidOperationException("Sky scene validation failed: " + scene.path);
            return report;
        }

        [Serializable]
        class AndroidReport
        {
            public bool passed;
            public int width, height;
            public string graphicsFormat;
            public long texturePayloadBytes, bundleBytes;
        }

        public static void ValidateAndroidAssets()
        {
            try
            {
                if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
                    throw new InvalidOperationException("Launch Unity with -buildTarget Android.");
                EnsureAssets();
                string output = Output + "/android-assets";
                Directory.CreateDirectory(output);
                var manifest = BuildPipeline.BuildAssetBundles(output, new[]{new AssetBundleBuild{assetBundleName = "qwen-orbital-sky", assetNames = new[]{Folder + "/OrbitalSky.mat", Folder + "/OrbitalStructure.mat", Folder + "/OrbitalRing.asset"}}}, BuildAssetBundleOptions.ForceRebuildAssetBundle | BuildAssetBundleOptions.ChunkBasedCompression, BuildTarget.Android);
                if (manifest == null)
                    throw new InvalidOperationException("Android sky asset bundle failed.");
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "/OrbitalSky.png");
                long bytes = 0;
                for (int i = 0; i < texture.mipmapCount; i++)
                    bytes += UnityEngine.Experimental.Rendering.GraphicsFormatUtility.ComputeMipmapSize(Mathf.Max(1, texture.width >> i), Mathf.Max(1, texture.height >> i), texture.graphicsFormat);
                var report = new AndroidReport{passed = true, width = texture.width, height = texture.height, graphicsFormat = texture.graphicsFormat.ToString(), texturePayloadBytes = bytes, bundleBytes = new FileInfo(output + "/qwen-orbital-sky").Length};
                if (texture.width > 2048)
                    throw new InvalidOperationException("Android sky texture exceeds intended size.");
                File.WriteAllText(Output + "/android-sky-validation.json", JsonUtility.ToJson(report, true));
                Debug.Log("QWEN_ANDROID_SKY_FINISHED");
                if (Application.isBatchMode)
                    EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                if (Application.isBatchMode)
                    EditorApplication.Exit(1);
                else
                    throw;
            }
        }

        // Real GPU previews without entering Play Mode or running inference.
        [MenuItem("Qwen World/Environment/Render orbital sky previews")]
        public static void BuildAndPreview()
        {
            try
            {
                ApplyAll();
                Directory.CreateDirectory(Output);
                var scene = EditorSceneManager.OpenScene(PcScene, OpenSceneMode.Single);
                var go = new GameObject("Temporary sky preview");
                var camera = go.AddComponent<Camera>();
                camera.clearFlags = CameraClearFlags.Skybox;
                camera.farClipPlane = 600;
                camera.nearClipPlane = .1f;
                camera.fieldOfView = 75;
                camera.allowHDR = false;
                camera.allowMSAA = false;
                Capture(camera, new Vector3(0, 2, -5), Quaternion.Euler(-22, 0, 0), "sky-platform.png");
                // Sky-only composition, with ring, from platform origin.
                camera.cullingMask = 1 << 30;
                foreach (var renderer in GameObject.Find(RootName).GetComponentsInChildren<Renderer>())
                    renderer.gameObject.layer = 30;
                camera.fieldOfView = 95;
                Capture(camera, new Vector3(0, 2, -1), Quaternion.Euler(-14, 12, 0), "sky-detail.png");
                camera.fieldOfView = 75;
                Capture(camera, new Vector3(0, 2, -1), Quaternion.Euler(-52, 0, 0), "sky-overhead.png");
                foreach (var renderer in GameObject.Find(RootName).GetComponentsInChildren<Renderer>())
                    renderer.gameObject.layer = 0;
                UnityEngine.Object.DestroyImmediate(go);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                Debug.Log("QWEN_SKY_PREVIEWS_FINISHED");
                if (Application.isBatchMode)
                    EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                if (Application.isBatchMode)
                    EditorApplication.Exit(1);
                else
                    throw;
            }
        }

        static void Capture(Camera camera, Vector3 position, Quaternion rotation, string filename)
        {
            camera.transform.SetPositionAndRotation(position, rotation);
            QwenPreview.Capture(camera, Output + "/" + filename, 1600, 900);
        }
    }
}
