using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace QwenWorld.Editor
{
    public static class QwenSurfaceMaterials
    {
        const string Folder = QwenSceneInstaller.Folder;
        public static void ApplyToCurrentScene()
        {
            var shader = Shader.Find("QwenWorld/WhiteSatinFloor");
            if (shader == null || ShaderUtil.ShaderHasError(shader))
                throw new InvalidOperationException("White floor shader failed compilation.");
            var detail = AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "/SurfaceGrain.asset");
            if (detail == null)
            {
                const int size = 256;
                detail = new Texture2D(size, size, TextureFormat.RGBA32, true, true)
                {name = "Linear satin surface grain", filterMode = FilterMode.Trilinear, wrapMode = TextureWrapMode.Repeat, anisoLevel = 4};
                var random = new System.Random(61073);
                var pixels = new Color32[size * size];
                for (int i = 0; i < pixels.Length; i++)
                    pixels[i] = new Color32((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256), 255);
                detail.SetPixels32(pixels);
                detail.Apply(true, true);
                AssetDatabase.CreateAsset(detail, Folder + "/SurfaceGrain.asset");
            }

            var floorMaterial = QwenGraphDisplayBuilder.Material(Folder + "/FloorSatin.mat", shader.name);
            floorMaterial.SetColor("_Color", new Color(.78f, .79f, .80f));
            floorMaterial.SetTexture("_DetailTex", detail);
            floorMaterial.SetFloat("_Smoothness", .64f);
            QwenFloorMaterial.Configure(floorMaterial);
            EditorUtility.SetDirty(floorMaterial);
            QwenLogoRelief.Configure(floorMaterial);
            var floor = GameObject.Find("Floor").GetComponent<Renderer>();
            floor.sharedMaterial = floorMaterial;
            floor.receiveShadows = true;
            floor.shadowCastingMode = ShadowCastingMode.Off;
            var white = AssetDatabase.LoadAssetAtPath<Material>(Folder + "/White.mat");
            white.color = new Color(.82f, .83f, .84f);
            white.SetFloat("_Metallic", 0);
            white.SetFloat("_Glossiness", .36f);
            white.DisableKeyword("_EMISSION");
            white.SetColor("_EmissionColor", Color.black);
            EditorUtility.SetDirty(white);
            foreach (var renderer in UnityEngine.Object.FindObjectsOfType<MeshRenderer>(true))
            {
                if (renderer.name == "Floating circular desk" || renderer.name == "Shared white keyboard")
                {
                    renderer.shadowCastingMode = ShadowCastingMode.On;
                    renderer.receiveShadows = true;
                }

                if (renderer.sharedMaterial != null && renderer.sharedMaterial.shader.name == "QwenWorld/QwenGirlStandee")
                    renderer.shadowCastingMode = ShadowCastingMode.TwoSided;
            }

            // Explicit ambient avoids inheriting a stale sky-baked ambient probe.
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(.30f, .32f, .35f);
            RenderSettings.ambientEquatorColor = new Color(.20f, .22f, .25f);
            RenderSettings.ambientGroundColor = new Color(.11f, .12f, .14f);
            RenderSettings.ambientIntensity = 1;
            RenderSettings.reflectionIntensity = .45f;
            foreach (var light in UnityEngine.Object.FindObjectsOfType<Light>())
                if (light.type == LightType.Directional)
                {
                    light.color = new Color(1, .98f, .95f);
                    light.intensity = 1.10f;
                    light.transform.rotation = Quaternion.Euler(48, -135, 0);
                    light.shadows = LightShadows.Soft;
                    light.shadowStrength = .72f;
                    light.shadowBias = .025f;
                    light.shadowNormalBias = .15f;
                    light.shadowNearPlane = .15f;
                    light.shadowCustomResolution = 2048;
                }

            QwenLighting.ConfigureCurrentScene();
        }

        [MenuItem("Qwen World/Environment/Apply satin floor and soft shadows")]
        public static void ApplyAllAndPreviewBatch()
        {
            QwenLighting.ApplyAndBakeBatch();
        }
    }
}
