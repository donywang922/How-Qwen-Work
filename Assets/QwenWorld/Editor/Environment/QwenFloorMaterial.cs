using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace QwenWorld.Editor
{
    public static class QwenFloorMaterial
    {
        private const string TexturePath = QwenSceneInstaller.Folder + "/HexBrushDirections.png";
        public static void Configure(Material material)
        {
            var importer = AssetImporter.GetAtPath(TexturePath) as TextureImporter;
            if (importer == null)
                throw new FileNotFoundException("Run Tools/Qwen35/make_hex_brush.py first.");
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = false;
            importer.alphaIsTransparency = false;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.filterMode = FilterMode.Trilinear;
            importer.anisoLevel = 8;
            // Keep the exact RG axis codes; lossy block compression can merge close directions.
            importer.SetPlatformTextureSettings(new TextureImporterPlatformSettings{name = "Standalone", overridden = true, maxTextureSize = 1024, format = TextureImporterFormat.RGBA32});
            importer.SetPlatformTextureSettings(new TextureImporterPlatformSettings{name = "Android", overridden = true, maxTextureSize = 512, format = TextureImporterFormat.RGBA32});
            importer.SaveAndReimport();
            material.SetTexture("_BrushDirectionMap", AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath));
            material.SetVector("_HexWorldSize", new Vector4(25.92f, 24.941532f, 0, 0));
            material.SetFloat("_BrushAnisotropy", .9f);
            material.SetFloat("_BrushReflectance", .06f);
            material.SetFloat("_BrushGrainStrength", .025f);
            material.SetVector("_BrushGrainScale", new Vector4(1.2f, .035f, 0, 0));
            material.SetFloat("_Smoothness", .64f);
            material.SetFloat("_LogoSmoothness", .72f);
            material.SetFloat("_FloorSpecular", 1);
            var serialized = new SerializedObject(material);
            foreach (string list in new[]{"m_TexEnvs", "m_Floats"})
            {
                var entries = serialized.FindProperty("m_SavedProperties." + list);
                for (int i = entries.arraySize - 1; i >= 0; i--)
                {
                    string key = entries.GetArrayElementAtIndex(i).FindPropertyRelative("first").stringValue;
                    if (new[]{"_HexNormalMap", "_HexNormalStrength", "_PanelSize", "_SeamWidth", "_NormalStrength", "_DetailScale"}.Contains(key))
                        entries.DeleteArrayElementAtIndex(i);
                }
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(material);
        }
    }
}
