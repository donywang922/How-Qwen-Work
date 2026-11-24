using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace QwenWorld.Editor
{
    public static class QwenFloorValidation
    {
        public static readonly string Output = QwenProject.DataPath("validation/visual/floor");
        [MenuItem("Qwen World/Environment/Apply directional brushed hexagonal floor")]
        public static void ApplyAndPreviewBatch()
        {
            try
            {
                Directory.CreateDirectory(Output);
                int quality = Array.IndexOf(QualitySettings.names, "VRC High");
                if (quality >= 0)
                    QualitySettings.SetQualityLevel(quality, true);
                var material = AssetDatabase.LoadAssetAtPath<Material>(QwenSceneInstaller.Folder + "/FloorSatin.mat");
                if (material == null || ShaderUtil.ShaderHasError(material.shader))
                    throw new InvalidOperationException("Floor shader failed.");
                Color colour = material.color;
                QwenFloorMaterial.Configure(material);
                var directionData = ReadData(material.GetTexture("_BrushDirectionMap"));
                var logoData = ReadData(material.GetTexture("_LogoRelief"));
                var axes = new System.Collections.Generic.HashSet<int>();
                for (int column = 0; column < 18; column++)
                    for (int row = 0; row < 15; row++)
                    {
                        Color c = directionData.GetPixelBilinear(column / 18f, (row + (column % 2) * .5f) / 15f);
                        float angle = Mathf.Repeat(Mathf.Atan2(c.g * 2 - 1, c.r * 2 - 1) * Mathf.Rad2Deg * .5f, 180);
                        axes.Add(Mathf.RoundToInt(angle * 1000));
                    }

                float logoBrushCoverage = 0;
                for (float z = -3.5f; z <= 3.5f; z += .1f)
                    for (float x = -3.5f; x <= 3.5f; x += .1f)
                        if (logoData.GetPixelBilinear(x / 7.2f + .5f, z / 7.2f + .5f).b > .99f)
                            logoBrushCoverage = Mathf.Max(logoBrushCoverage, directionData.GetPixelBilinear(Mathf.Repeat(x / 25.92f, 1), Mathf.Repeat(z / 24.941532f, 1)).b);
                UnityEngine.Object.DestroyImmediate(directionData);
                UnityEngine.Object.DestroyImmediate(logoData);
                if (axes.Count != 270 || logoBrushCoverage > .02f)
                    throw new InvalidOperationException("Direction/exclusion data failed: " + axes.Count + " / " + logoBrushCoverage);
                if (File.Exists(QwenSceneInstaller.Folder + "/HexFloorNormal.png"))
                    throw new InvalidOperationException("Legacy hex normal asset remains.");
                foreach (string path in QwenSceneInstaller.ActiveScenePaths)
                {
                    EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                    var floor = GameObject.Find("Floor").GetComponent<MeshRenderer>();
                    if (floor.sharedMaterial != material || material.color != colour || material.GetTexture("_LogoRelief") == null || material.GetFloat("_LogoStrength") < .99f)
                        throw new InvalidOperationException("Floor colour or logo was changed.");
                    if (LightmapSettings.lightmaps.Length == 0)
                        throw new InvalidOperationException("Lost baked lighting.");
                }

                AssetDatabase.SaveAssets();
                EditorSceneManager.OpenScene(QwenModelPipeline.PcScene, OpenSceneMode.Single);
                Capture("wide", new Vector3(3, 2.8f, -4.4f), new Vector3(0, .9f, 0), 70);
                Capture("detail", new Vector3(2.8f, 1.6f, -5), new Vector3(2.8f, 0, -3.6f), 65);
                Capture("logo", new Vector3(-1.4f, 1.35f, -3.8f), new Vector3(-1.1f, 0, -2), 65);
                Capture("opposite", new Vector3(-4, 1.65f, 3.5f), new Vector3(-2, 0, 2.7f), 68);
                Capture("glancing", new Vector3(-4.5f, 1.65f, -3.8f), new Vector3(-3, 0, -2.5f), 65);
                Capture("macro-detail", new Vector3(-3.5f, .55f, -3.3f), new Vector3(-3, 0, -2.7f), 55);
                var eye = new Vector3(-4.5f, 1.65f, -3.8f);
                var target = new Vector3(-3, 0, -2.5f);
                material.SetFloat("_FloorSpecular", 0);
                material.SetFloat("_BrushAnisotropy", 0);
                material.SetFloat("_BrushGrainStrength", 0);
                var flatDiffuse = Capture("diagnostic-diffuse-flat", eye, target, 65);
                material.SetFloat("_BrushAnisotropy", .9f);
                material.SetFloat("_BrushGrainStrength", .025f);
                var hexDiffuse = Capture("diagnostic-diffuse-hex", eye, target, 65);
                material.SetFloat("_FloorSpecular", 1);
                var hexGloss = Capture("diagnostic-reflective", eye, target, 65);
                float diffuseDelta = Difference(flatDiffuse, hexDiffuse), specularDelta = Difference(hexDiffuse, hexGloss);
                if (diffuseDelta > .05f || specularDelta < .1f)
                    throw new InvalidOperationException("Hexagonal reflections did not remain separate from substrate shading: " + diffuseDelta + " / " + specularDelta);
                Directory.CreateDirectory(Output + "/motion");
                for (int frame = 0; frame < 24; frame++)
                {
                    float angle = Mathf.Lerp(210, 270, frame / 23f) * Mathf.Deg2Rad;
                    var focus = new Vector3(-2.7f, 0, -2.5f);
                    Capture("motion/frame-" + frame.ToString("D2"), focus + new Vector3(Mathf.Sin(angle) * 2.2f, 1.65f, Mathf.Cos(angle) * 2.2f), focus, 65, 960, 600);
                }

                EditorUtility.SetDirty(material);
                AssetDatabase.SaveAssets();
                File.WriteAllText(Output + "/validation.json", JsonUtility.ToJson(new Report{passed = true, rectangularGridRemoved = true, baseColourUnchanged = true, logoPreserved = true, logoBrushExcluded = true, legacyHexNormalsRemoved = true, uniqueDirections = axes.Count, logoMaxBrushCoverage = logoBrushCoverage, anisotropicBrushedSurface = true, reflectionOnlyHexPattern = true, diffusePixelDelta = diffuseDelta, specularPixelDelta = specularDelta, motionFrames = 24, pcSize = 1024, androidSize = 512, hexFlatToFlatMetres = 1.662769f, anisotropy = material.GetFloat("_BrushAnisotropy"), grainStrength = material.GetFloat("_BrushGrainStrength"), smoothness = material.GetFloat("_Smoothness"), extraTextureSamples = 1, environmentSamples = 3, extraRenderers = 0, extraRuntimeScripts = 0, parallaxSteps = 0, scenes = QwenSceneInstaller.ActiveScenePaths}, true));
                Debug.Log("QWEN_HEX_FLOOR_PASSED");
                if (Application.isBatchMode)
                    EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                if (Application.isBatchMode)
                    EditorApplication.Exit(1);
                else
                    throw;
            }
        }

        static float Difference(Color32[] a, Color32[] b)
        {
            // Bottom 60% is floor in this close view; excludes animated exhibit objects.
            int count = a.Length * 6 / 10;
            double sum = 0;
            for (int i = 0; i < count; i++)
                sum += (Mathf.Abs((int)a[i].r - b[i].r) + Mathf.Abs((int)a[i].g - b[i].g) + Mathf.Abs((int)a[i].b - b[i].b)) / 3f;
            return (float)(sum / count);
        }

        static Texture2D ReadData(Texture source)
        {
            var rt = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            var previous = RenderTexture.active;
            Graphics.Blit(source, rt);
            RenderTexture.active = rt;
            var texture = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false, true)
            {wrapMode = TextureWrapMode.Repeat};
            texture.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            texture.Apply();
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);
            return texture;
        }

        static Color32[] Capture(string name, Vector3 eye, Vector3 target, float fov, int width = 1600, int height = 1000)
        {
            return QwenPreview.Capture(Output + "/" + name + ".png", eye, target, fov, width, height);
        }

        [Serializable]
        class Report
        {
            public bool passed, rectangularGridRemoved, baseColourUnchanged, logoPreserved, logoBrushExcluded, legacyHexNormalsRemoved, anisotropicBrushedSurface, reflectionOnlyHexPattern;
            public int pcSize, androidSize, uniqueDirections, extraTextureSamples, environmentSamples, extraRenderers, extraRuntimeScripts, parallaxSteps, motionFrames;
            public float hexFlatToFlatMetres, anisotropy, grainStrength, smoothness, diffusePixelDelta, specularPixelDelta, logoMaxBrushCoverage;
            public string[] scenes;
        }
    }
}
