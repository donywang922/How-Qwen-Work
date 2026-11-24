using System;
using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace QwenWorld.Editor
{
    public static class QwenLogoRelief
    {
        public static readonly string Output = QwenProject.DataPath("validation/floor-logo");
        const string TexturePath = QwenSceneInstaller.Folder + "/QwenFloorRelief.png";
        public static void Configure(Material material)
        {
            var importer = AssetImporter.GetAtPath(TexturePath) as TextureImporter;
            if (importer == null)
                throw new FileNotFoundException("Generate the icon relief with Tools/Qwen35/make_floor_logo.py first.");
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = false;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = false;
            importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Trilinear;
            importer.anisoLevel = 8;
            importer.SetPlatformTextureSettings(new TextureImporterPlatformSettings{name = "Standalone", overridden = true, maxTextureSize = 1024, format = TextureImporterFormat.BC7, compressionQuality = 100});
            importer.SetPlatformTextureSettings(new TextureImporterPlatformSettings{name = "Android", overridden = true, maxTextureSize = 512, format = TextureImporterFormat.ASTC_4x4, compressionQuality = 100});
            importer.SaveAndReimport();
            material.SetTexture("_LogoRelief", AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath));
            material.SetFloat("_LogoSize", 7.2f);
            material.SetVector("_LogoCentre", Vector4.zero);
            material.SetFloat("_LogoStrength", 1);
            material.SetFloat("_LogoNormalStrength", .20f);
            material.SetFloat("_LogoSmoothness", .72f);
            EditorUtility.SetDirty(material);
        }
    }
}
