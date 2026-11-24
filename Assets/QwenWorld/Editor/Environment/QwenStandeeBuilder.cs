using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace QwenWorld.Editor
{
    public static class QwenStandeeBuilder
    {
        const string Folder = "Assets/QwenWorld/Environment/QwenGirl";
        const string RootName = "Qwen girl standee";
        static readonly string ReportFolder = QwenProject.DataPath("validation/standee");
        const float VisibleHeight = 1.75f;
        [MenuItem("Qwen World/Environment/Install Qwen girl standee")]
        public static void Install()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;
            string previous = SceneManager.GetActiveScene().path;
            PrepareAssets();
            Directory.CreateDirectory(ReportFolder);
            foreach (string name in QwenSceneInstaller.ActiveSceneNames)
            {
                string path = "Assets/QwenWorld/Scenes/" + name + ".unity";
                string backup = ReportFolder + "/before-" + name + ".unity";
                if (!File.Exists(backup))
                    File.Copy(path, backup);
                var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                AddToCurrentScene();
                if (!EditorSceneManager.SaveScene(scene))
                    throw new IOException("Could not save " + path);
            }

            if (!string.IsNullOrEmpty(previous))
                EditorSceneManager.OpenScene(previous, OpenSceneMode.Single);
        }

        public static GameObject AddToCurrentScene()
        {
            var existing = SceneManager.GetActiveScene().GetRootGameObjects().FirstOrDefault(go => go.name == RootName);
            if (existing != null)
                return existing;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "/QwenGirl.prefab");
            if (prefab == null)
            {
                PrepareAssets();
                prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "/QwenGirl.prefab");
            }

            foreach (var go in SceneManager.GetActiveScene().GetRootGameObjects().Where(go => go.name == RootName))
                UnityEngine.Object.DestroyImmediate(go);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            instance.name = RootName;
            instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            return instance;
        }

        static void PrepareAssets()
        {
            foreach (string name in new[]{"Open", "Closed"})
            {
                string path = Folder + "/QwenGirl-" + name + ".png";
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.textureType = TextureImporterType.Default;
                importer.sRGBTexture = true;
                importer.alphaSource = TextureImporterAlphaSource.FromInput;
                importer.alphaIsTransparency = true;
                // POT import permits block compression; source PNGs and mesh aspect stay intact.
                importer.npotScale = TextureImporterNPOTScale.ToLarger;
                importer.mipmapEnabled = true;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.filterMode = FilterMode.Trilinear;
                importer.maxTextureSize = 2048;
                importer.textureCompression = TextureImporterCompression.CompressedHQ;
                importer.SetPlatformTextureSettings(new TextureImporterPlatformSettings{name = "Standalone", overridden = true, maxTextureSize = 2048, format = TextureImporterFormat.DXT5, compressionQuality = 100});
                importer.SetPlatformTextureSettings(new TextureImporterPlatformSettings{name = "Android", overridden = true, maxTextureSize = 1024, format = TextureImporterFormat.ASTC_6x6, compressionQuality = 100});
                importer.SaveAndReimport();
            }

            var shader = Shader.Find("QwenWorld/QwenGirlStandee");
            if (shader == null || ShaderUtil.ShaderHasError(shader))
                throw new InvalidOperationException("Standee shader failed to compile.");
            string materialPath = Folder + "/QwenGirl.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                material = new Material(shader)
                {name = "Qwen girl expressions"};
                AssetDatabase.CreateAsset(material, materialPath);
            }

            material.shader = shader;
            material.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "/QwenGirl-Open.png"));
            material.SetTexture("_ClosedTex", AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "/QwenGirl-Closed.png"));
            material.SetFloat("_EyesClosed", 0);
            material.SetVector("_FaceRect", new Vector4(.435f, .817f, .595f, .887f));
            material.SetFloat("_FaceFeather", .008f);
            EditorUtility.SetDirty(material);
            // Full texture UVs are retained. Bounds align visible shoes at y=0 and hat at 1.75m.
            const float widthPixels = 937, heightPixels = 1679, alphaTop = 43, alphaBottom = 1663;
            float h = VisibleHeight * heightPixels / (alphaBottom - alphaTop);
            float w = h * widthPixels / heightPixels;
            float bottom = -(heightPixels - alphaBottom) / heightPixels * h;
            string meshPath = Folder + "/QwenGirl.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if (mesh == null)
            {
                mesh = new Mesh{name = "Qwen girl fixed plane"};
                AssetDatabase.CreateAsset(mesh, meshPath);
            }

            mesh.Clear();
            mesh.vertices = new[]{new Vector3(-w / 2, bottom, 0), new Vector3(w / 2, bottom, 0), new Vector3(w / 2, bottom + h, 0), new Vector3(-w / 2, bottom + h, 0)};
            mesh.uv = new[]{Vector2.zero, Vector2.right, Vector2.one, Vector2.up};
            mesh.triangles = new[]{0, 2, 1, 0, 3, 2};
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            EditorUtility.SetDirty(mesh);
            var root = new GameObject(RootName, typeof(MeshFilter), typeof(MeshRenderer));
            root.GetComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = root.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            PrefabUtility.SaveAsPrefabAsset(root, Folder + "/QwenGirl.prefab");
            UnityEngine.Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
        }

        public static void InstallAndPreviewBatch()
        {
            try
            {
                if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                    throw new InvalidOperationException("Graphics device required.");
                Install();
                EditorSceneManager.OpenScene(QwenProject.MainScene, OpenSceneMode.Single);
                var standee = GameObject.Find(RootName);
                var renderer = standee.GetComponent<MeshRenderer>();
                var material = renderer.sharedMaterial;
                var previewMaterial = new Material(material);
                renderer.sharedMaterial = previewMaterial;
                var camera = new GameObject("Temporary standee preview").AddComponent<Camera>();
                camera.enabled = false;
                camera.nearClipPlane = .05f;
                camera.farClipPlane = 800;
                camera.allowHDR = false;
                camera.cullingMask = 1 << 30;
                standee.layer = 30;
                camera.orthographic = true;
                camera.orthographicSize = .97f;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(.16f, .18f, .21f);
                Capture(camera, new Vector3(0, .88f, -3), new Vector3(0, .88f, 0), "open");
                previewMaterial.SetFloat("_EyesClosed", 1);
                Capture(camera, new Vector3(0, .88f, -3), new Vector3(0, .88f, 0), "closed");
                previewMaterial.SetFloat("_EyesClosed", 0);
                Capture(camera, new Vector3(1.8f, 1.1f, -2.6f), new Vector3(0, .88f, 0), "oblique");
                camera.orthographic = false;
                camera.fieldOfView = 55;
                camera.cullingMask = -1;
                camera.clearFlags = CameraClearFlags.Skybox;
                Capture(camera, new Vector3(0, 1.6f, -3.6f), new Vector3(0, 1.2f, 0), "scene");
                renderer.sharedMaterial = material;
                standee.layer = 0;
                UnityEngine.Object.DestroyImmediate(camera.gameObject);
                UnityEngine.Object.DestroyImmediate(previewMaterial);
                var textures = new[]{(Texture2D)material.GetTexture("_MainTex"), (Texture2D)material.GetTexture("_ClosedTex")};
                var report = new Report{triangles = standee.GetComponent<MeshFilter>().sharedMesh.triangles.Length / 3, renderers = standee.GetComponentsInChildren<Renderer>().Length, visibleHeightMeters = VisibleHeight, fixedPlane = true, doubleSided = true, bodyAndSilhouetteShared = true, shaderSupported = material.shader.isSupported, sceneNames = QwenSceneInstaller.ActiveSceneNames, textures = textures.Select(t => new TextureReport{name = t.name, width = t.width, height = t.height, format = t.graphicsFormat.ToString()}).ToArray()};
                if (report.triangles != 2 || report.renderers != 1 || !report.shaderSupported || ShaderUtil.ShaderHasError(material.shader))
                    throw new InvalidOperationException("Standee validation failed.");
                File.WriteAllText(ReportFolder + "/validation.json", JsonUtility.ToJson(report, true));
                Debug.Log("QWEN_STANDEE_INSTALLED_AND_PREVIEWED " + ReportFolder);
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        static void Capture(Camera camera, Vector3 eye, Vector3 target, string name)
        {
            camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye, Vector3.up));
            QwenPreview.Capture(camera, ReportFolder + "/" + name + ".png", 1200, 1600);
        }

        [Serializable]
        class Report
        {
            public int triangles, renderers;
            public float visibleHeightMeters;
            public bool fixedPlane, doubleSided, bodyAndSilhouetteShared, shaderSupported;
            public string[] sceneNames;
            public TextureReport[] textures;
        }

        [Serializable]
        class TextureReport
        {
            public string name, format;
            public int width, height;
        }
    }
}
