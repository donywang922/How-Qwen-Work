using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace QwenWorld.Editor
{
    [InitializeOnLoad]
    public static class QwenLighting
    {
        public static readonly string Output = QwenProject.DataPath("validation/blue-violet-look");
        const string Folder = QwenSceneInstaller.Folder;
        const string RootName = "Baked warm centre and Earth fill";
        static Action completed;
        static int sceneIndex;
        static double started;
        static bool baking;
        static readonly List<BakeReport> reports = new List<BakeReport>();
        static QwenLighting()
        {
            EditorApplication.update += Tick;
        }

        public static void ConfigureCurrentScene()
        {
            var old = GameObject.Find(RootName);
            if (old != null)
                UnityEngine.Object.DestroyImmediate(old);
            var root = new GameObject(RootName);
            var anchor = new GameObject("Local environment anchor").transform;
            anchor.SetParent(root.transform);
            anchor.position = new Vector3(0, 1.8f, 0);
            var floor = GameObject.Find("Floor").GetComponent<MeshRenderer>();
            var floorMat = floor.sharedMaterial;
            floorMat.SetColor("_Color", new Color(.83f, .81f, .77f));
            floorMat.SetFloat("_Smoothness", .64f);
            EditorUtility.SetDirty(floorMat);
            var white = AssetDatabase.LoadAssetAtPath<Material>(Folder + "/White.mat");
            white.color = new Color(.86f, .88f, .92f);
            white.SetFloat("_Glossiness", .64f);
            white.SetFloat("_Metallic", .025f);
            EditorUtility.SetDirty(white);
            var table = GameObject.Find("Floating circular desk");
            var desk = new QwenExhibitGeometry();
            desk.BeveledRing(QwenConsoleVisuals.InnerRadius, QwenConsoleVisuals.OuterRadius, .025f, .008f, new Vector3(0, .94f, 0));
            var deskMesh = desk.Save(Folder + "/Desk.asset", "White lacquered desk with bevels");
            table.GetComponent<MeshFilter>().sharedMesh = deskMesh;
            var collider = table.GetComponent<MeshCollider>();
            collider.sharedMesh = null;
            collider.sharedMesh = deskMesh;
            string floorMeshPath = Folder + "/FloorBaked.asset";
            var floorMesh = AssetDatabase.LoadAssetAtPath<Mesh>(floorMeshPath);
            if (floorMesh == null)
            {
                floorMesh = UnityEngine.Object.Instantiate(floor.GetComponent<MeshFilter>().sharedMesh);
                floorMesh.name = "Unwrapped walkable floor";
                AssetDatabase.CreateAsset(floorMesh, floorMeshPath);
                Unwrap(floorMesh);
            }

            floor.GetComponent<MeshFilter>().sharedMesh = floorMesh;
            floor.GetComponent<MeshCollider>().sharedMesh = floorMesh;
            StaticSurface(floor);
            StaticSurface(table.GetComponent<MeshRenderer>());
            var keyboard = GameObject.Find("Shared white keyboard");
            QwenConsoleVisuals.ConfigureKeyboard(UnityEngine.Object.FindObjectOfType<QwenSocialUI>(), white);
            foreach (var renderer in new[]{floor, table.GetComponent<MeshRenderer>(), keyboard.GetComponent<MeshRenderer>()})
            {
                renderer.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
                renderer.lightProbeUsage = LightProbeUsage.BlendProbes;
                renderer.probeAnchor = anchor;
            }

            var bandGeo = new QwenExhibitGeometry();
            bandGeo.BeveledRing(QwenConsoleVisuals.OuterRadius - .13f, QwenConsoleVisuals.OuterRadius - .10f, .008f, .004f, new Vector3(0, .909f, 0));
            var bandMat = QwenGraphDisplayBuilder.Material(Folder + "/VioletLight.mat", "Standard");
            bandMat.color = new Color(.48f, .34f, .75f);
            bandMat.SetFloat("_Glossiness", .65f);
            bandMat.SetColor("_EmissionColor", new Color(.48f, .20f, 1.0f) * 1.4f);
            bandMat.EnableKeyword("_EMISSION");
            bandMat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            EditorUtility.SetDirty(bandMat);
            var band = new GameObject("Recessed violet desk light", typeof(MeshFilter), typeof(MeshRenderer));
            band.transform.SetParent(root.transform);
            var bandMesh = bandGeo.Save(Folder + "/DeskLight.asset", "Recessed violet light channel");
            band.GetComponent<MeshFilter>().sharedMesh = bandMesh;
            band.GetComponent<MeshRenderer>().sharedMaterial = bandMat;
            GameObjectUtility.SetStaticEditorFlags(band, StaticEditorFlags.ReflectionProbeStatic);
            band.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            foreach (var light in UnityEngine.Object.FindObjectsOfType<Light>())
                if (light.type == LightType.Directional)
                {
                    light.color = new Color(1, .93f, .84f);
                    light.intensity = .78f;
                    light.lightmapBakeType = LightmapBakeType.Mixed;
                    light.shadowStrength = .62f;
                    light.bounceIntensity = 1;
                }

            var centre = AddLight(root.transform, "Warm white centre", new Vector3(0, 4.8f, 0), new Color(1, .88f, .73f), 6.5f, 7);
            centre.type = LightType.Spot;
            centre.spotAngle = 108;
            centre.innerSpotAngle = 68;
            centre.transform.rotation = Quaternion.Euler(90, 0, 0);
            AddLight(root.transform, "Earth blue reflection", new Vector3(7, 2.3f, 3), new Color(.20f, .48f, 1), 4.8f, 14);
            AddLight(root.transform, "Soft violet balance", new Vector3(-6, 2.8f, 4), new Color(.48f, .34f, 1), 1.4f, 13);
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI / 4;
                AddLight(root.transform, "Baked desk glow " + i, new Vector3(Mathf.Sin(a) * QwenConsoleVisuals.KeyboardRadius, .88f, Mathf.Cos(a) * QwenConsoleVisuals.KeyboardRadius), new Color(.62f, .42f, 1), .30f, 1.8f);
            }

            var probes = new GameObject("Avatar and keyboard light probes", typeof(LightProbeGroup)).GetComponent<LightProbeGroup>();
            probes.transform.SetParent(root.transform);
            var positions = new List<Vector3>();
            foreach (float radius in new[]{1.1f, 3.4f, 7.8f})
                foreach (float y in new[]{.25f, 1.4f, 2.7f})
                    for (int i = 0; i < 16; i++)
                    {
                        float a = i * Mathf.PI / 8;
                        positions.Add(new Vector3(Mathf.Sin(a) * radius, y, Mathf.Cos(a) * radius));
                    }

            probes.probePositions = positions.ToArray();
            var probe = new GameObject("Static local environment reflection", typeof(ReflectionProbe)).GetComponent<ReflectionProbe>();
            probe.transform.SetParent(root.transform);
            probe.transform.position = new Vector3(0, 1.8f, 0);
            probe.mode = ReflectionProbeMode.Baked;
            probe.resolution = 256;
            probe.hdr = true;
            probe.size = new Vector3(26, 10, 26);
            probe.boxProjection = true;
            probe.nearClipPlane = .1f;
            probe.farClipPlane = 400;
            probe.clearFlags = ReflectionProbeClearFlags.Skybox;
            probe.intensity = .85f;
            var renderers = UnityEngine.Object.FindObjectsOfType<MeshRenderer>(true);
            foreach (var r in renderers)
                if (r.sharedMaterial != null)
                {
                    string shader = r.sharedMaterial.shader.name;
                    if (shader == "QwenWorld/WhiteLayerCore" || shader == "QwenWorld/WhiteMotionBars" || shader == "QwenWorld/OrbitalRingPBR")
                    {
                        if (shader == "QwenWorld/WhiteMotionBars")
                        {
                            r.sharedMaterial.SetColor("_GlowColor", new Color(1.15f, 1.15f, 1.15f, 1));
                            EditorUtility.SetDirty(r.sharedMaterial);
                            r.shadowCastingMode = ShadowCastingMode.Off;
                            r.receiveShadows = false;
                            r.lightProbeUsage = LightProbeUsage.Off;
                            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
                        }
                        else
                        {
                            SetTheme(r.sharedMaterial, shader.Contains("Orbital"));
                            r.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
                            r.probeAnchor = anchor;
                        }

                        if (shader.Contains("Orbital"))
                            GameObjectUtility.SetStaticEditorFlags(r.gameObject, GameObjectUtility.GetStaticEditorFlags(r.gameObject) | StaticEditorFlags.ReflectionProbeStatic);
                    }
                }

            var bars = GameObject.Find("Six white moving bars");
            if (bars != null)
            {
                var geometry = new QwenExhibitGeometry();
                geometry.Bars();
                bars.GetComponent<MeshFilter>().sharedMesh = geometry.Save(Folder + "/Bars.asset", "Six independently lit moving bars", new Bounds(new Vector3(0, 1, 0), new Vector3(2.5f, 6, 2.5f)));
            }

            foreach (var image in UnityEngine.Object.FindObjectsOfType<Image>(true))
                if (image.name == "White panel")
                    image.color = new Color(.83f, .89f, .96f, .94f);
            var social = UnityEngine.Object.FindObjectOfType<QwenSocialUI>();
            if (social != null)
            {
                foreach (var text in social.panelText)
                    text.color = new Color(.055f, .10f, .19f);
                foreach (var text in social.panelCaption)
                    text.color = new Color(.37f, .27f, .64f);
            }

            QwenConsoleVisuals.ConfigureScreens();
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(.22f, .29f, .42f);
            RenderSettings.ambientEquatorColor = new Color(.14f, .19f, .30f);
            RenderSettings.ambientGroundColor = new Color(.085f, .085f, .16f);
            RenderSettings.reflectionIntensity = .8f;
            var settings = AssetDatabase.LoadAssetAtPath<LightingSettings>(Folder + "/ExhibitLighting.lighting");
            if (settings == null)
            {
                settings = new LightingSettings{name = "White and blue violet exhibit"};
                AssetDatabase.CreateAsset(settings, Folder + "/ExhibitLighting.lighting");
            }

            settings.bakedGI = true;
            settings.realtimeGI = false;
            settings.autoGenerate = false;
            settings.lightmapper = LightingSettings.Lightmapper.ProgressiveCPU;
            settings.mixedBakeMode = MixedLightingMode.Shadowmask;
            settings.directionalityMode = LightmapsMode.CombinedDirectional;
            settings.lightmapResolution = 20;
            settings.lightmapMaxSize = 1024;
            settings.directSampleCount = 32;
            settings.indirectSampleCount = 128;
            settings.environmentSampleCount = 64;
            settings.maxBounces = 2;
            settings.ao = true;
            settings.aoMaxDistance = .35f;
            Lightmapping.lightingSettings = settings;
            EditorUtility.SetDirty(settings);
        }

        static void Unwrap(Mesh mesh)
        {
            var p = new UnwrapParam();
            UnwrapParam.SetDefaults(out p);
            p.packMargin = .01f;
            p.hardAngle = 88;
            Unwrapping.GenerateSecondaryUVSet(mesh, p);
            EditorUtility.SetDirty(mesh);
        }

        static void StaticSurface(MeshRenderer r)
        {
            GameObjectUtility.SetStaticEditorFlags(r.gameObject, StaticEditorFlags.ContributeGI | StaticEditorFlags.ReflectionProbeStatic);
            r.receiveGI = ReceiveGI.Lightmaps;
        }

        static Light AddLight(Transform root, string name, Vector3 position, Color color, float intensity, float range)
        {
            var light = new GameObject(name, typeof(Light)).GetComponent<Light>();
            light.transform.SetParent(root);
            light.transform.position = position;
            light.type = LightType.Point;
            light.color = color;
            light.intensity = intensity;
            light.range = range;
            light.lightmapBakeType = LightmapBakeType.Baked;
            light.shadows = LightShadows.Soft;
            return light;
        }

        static void SetTheme(Material mat, bool distant)
        {
            mat.SetColor("_ThemeWarm", new Color(1.4f, 1.25f, 1.1f));
            mat.SetColor("_ThemeCool", new Color(.07f, .16f, .30f));
            mat.SetColor("_ThemeAmbient", new Color(.30f, .34f, .42f));
            mat.SetColor("_ThemeLed", new Color(.26f, .46f, .85f));
            mat.SetFloat("_ThemeReflection", distant ? .35f : .8f);
            if (distant)
                mat.SetFloat("_EmissionStrength", .75f);
            EditorUtility.SetDirty(mat);
        }

        [MenuItem("Qwen World/Environment/Apply and bake white blue violet look")]
        public static void ApplyAndBakeBatch()
        {
            QwenModelPipeline.Compile();
            BeginBake(() =>
            {
                Debug.Log("QWEN_BLUE_VIOLET_COMPLETE");
                if (Application.isBatchMode)
                    EditorApplication.Exit(0);
            });
        }

        public static void BeginBake(Action onComplete)
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;
            Directory.CreateDirectory(Output);
            int quality = Array.IndexOf(QualitySettings.names, "VRC High");
            if (quality >= 0)
                QualitySettings.SetQualityLevel(quality, true);
            reports.Clear();
            completed = onComplete;
            sceneIndex = 0;
            NextScene();
        }

        static void NextScene()
        {
            try
            {
                if (sceneIndex == QwenSceneInstaller.ActiveSceneNames.Length)
                {
                    File.WriteAllText(Output + "/validation.json", JsonUtility.ToJson(new Reports{passed = true, scenes = reports.ToArray()}, true));
                    EditorSceneManager.OpenScene(QwenSceneInstaller.ActiveScenePaths[0], OpenSceneMode.Single);
                    completed?.Invoke();
                    return;
                }

                var scene = EditorSceneManager.OpenScene(QwenSceneInstaller.ActiveScenePaths[sceneIndex], OpenSceneMode.Single);
                if (sceneIndex == 0 && !File.Exists(Output + "/before-wide.png"))
                    Capture("before-wide", new Vector3(4.5f, 3.1f, -6.5f), new Vector3(0, 1.8f, 0), 65);
                QwenSurfaceMaterials.ApplyToCurrentScene();
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                AssetDatabase.SaveAssets();
                started = EditorApplication.timeSinceStartup;
                baking = true;
                if (!Lightmapping.BakeAsync())
                    throw new InvalidOperationException("Light bake did not start");
                Debug.Log("QWEN_BLUE_VIOLET_BAKE_START " + scene.name);
            }
            catch (Exception e)
            {
                Fail(e);
            }
        }

        static void Tick()
        {
            if (!baking)
                return;
            if (EditorApplication.timeSinceStartup - started > 1200)
            {
                Lightmapping.Cancel();
                Fail(new TimeoutException("Lighting bake exceeded 20 minutes"));
                return;
            }

            if (Lightmapping.isRunning || EditorApplication.timeSinceStartup - started < 3)
                return;
            baking = false;
            try
            {
                if (LightmapSettings.lightmaps.Length == 0 || LightmapSettings.lightmaps[0].lightmapColor == null)
                    throw new InvalidOperationException("No baked lightmaps were generated");
                var probe = UnityEngine.Object.FindObjectOfType<ReflectionProbe>();
                var hidden = UnityEngine.Object.FindObjectsOfType<Canvas>(true).Where(c => c.enabled).ToArray();
                foreach (var c in hidden)
                    c.enabled = false;
                string cubePath = Folder + "/ThemeReflection.exr";
                if (!Lightmapping.BakeReflectionProbe(probe, cubePath))
                    throw new InvalidOperationException("Reflection baking failed");
                foreach (var c in hidden)
                    c.enabled = true;
                AssetDatabase.ImportAsset(cubePath, ImportAssetOptions.ForceSynchronousImport);
                probe.mode = ReflectionProbeMode.Custom;
                probe.customBakedTexture = AssetDatabase.LoadAssetAtPath<Cubemap>(cubePath);
                if (probe.customBakedTexture == null)
                    throw new InvalidOperationException("Baked reflection cubemap missing");
                var scene = SceneManager.GetActiveScene();
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                AssetDatabase.SaveAssets();
                var report = new BakeReport{scene = scene.name, lightmaps = LightmapSettings.lightmaps.Length, lightProbes = LightmapSettings.lightProbes != null ? LightmapSettings.lightProbes.count : 0, bakedLights = UnityEngine.Object.FindObjectsOfType<Light>().Count(l => l.lightmapBakeType == LightmapBakeType.Baked), realtimeLights = UnityEngine.Object.FindObjectsOfType<Light>().Count(l => l.lightmapBakeType != LightmapBakeType.Baked), reflectionSize = probe.resolution, seconds = (float)(EditorApplication.timeSinceStartup - started)};
                if (report.lightProbes < 100 || report.bakedLights < 10 || report.realtimeLights != 1)
                    throw new InvalidOperationException("Incomplete lighting setup");
                reports.Add(report);
                Capture(sceneIndex == 0 ? "after-wide" : "android-viewer", new Vector3(4.5f, 3.1f, -6.5f), new Vector3(0, 1.8f, 0), 65);
                if (sceneIndex == 0)
                {
                    Capture("after-centre", new Vector3(1.85f, 1.8f, -2.85f), new Vector3(0, 1.35f, 0), 65);
                    Capture("after-keyboard", new Vector3(.50f, 1.48f, -1.97f), new Vector3(0, .975f, -QwenConsoleVisuals.KeyboardRadius), 65);
                    QwenConsoleVisuals.CaptureScreenPreviews(Output);
                }

                Debug.Log("QWEN_BLUE_VIOLET_BAKE_PASS " + scene.name + " maps=" + report.lightmaps + " probes=" + report.lightProbes);
                sceneIndex++;
                EditorApplication.delayCall += NextScene;
            }
            catch (Exception e)
            {
                Fail(e);
            }
        }

        static void Fail(Exception e)
        {
            baking = false;
            Directory.CreateDirectory(Output);
            File.WriteAllText(Output + "/failure.txt", e.ToString());
            Debug.LogException(e);
            if (Application.isBatchMode)
                EditorApplication.Exit(1);
        }

        static void Capture(string name, Vector3 eye, Vector3 target, float fov)
        {
            QwenPreview.Capture(Output + "/" + name + ".png", eye, target, fov);
        }

        [Serializable]
        class Reports
        {
            public bool passed;
            public BakeReport[] scenes;
        }

        [Serializable]
        class BakeReport
        {
            public string scene;
            public int lightmaps, lightProbes, bakedLights, realtimeLights, reflectionSize;
            public float seconds;
        }
    }
}
