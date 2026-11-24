#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace ShaderGPT {
public sealed class ModelAssetBundle {
	public string folder;
	public TextAsset configJson;
	public TextAsset tokenizerJson;
	public TextAsset testcaseJson;
	public TextAsset processorJson;
	public Texture[] textures;
	public Shader[] shaders;
	public string model_type;
	public int max_position_embeddings;
}

public static class ModelAssetUtility {
	public static ModelAssetBundle LoadModelAssets(string folder, bool prepare=true) {
		if(prepare)
			PrepareModelFolder(folder);

		var configJson = AssetDatabase.LoadAssetAtPath<TextAsset>(Path.Join(folder, "config.json"));
		if(configJson == null)
			throw new FileNotFoundException($"Missing config.json in {folder}");

		var tokenizerJson = AssetDatabase.LoadAssetAtPath<TextAsset>(Path.Join(folder, "tokenizer.json"));
		var testcaseJson = AssetDatabase.LoadAssetAtPath<TextAsset>(Path.Join(folder, "testcase.json"));
		var processorJson = AssetDatabase.LoadAssetAtPath<TextAsset>(Path.Join(folder, "processor_config.json"));
		var config = JsonUtility.FromJson<Config>(configJson.text);
		return new ModelAssetBundle {
			folder = folder,
			configJson = configJson,
			tokenizerJson = tokenizerJson,
			testcaseJson = testcaseJson,
			processorJson = processorJson,
			textures = GetTexturePaths(folder).Select(path => AssetDatabase.LoadAssetAtPath<Texture>(path)).ToArray(),
			shaders = GetShaders(),
			model_type = config.model_type,
			max_position_embeddings = config.max_position_embeddings,
		};
	}

	public static void PrepareModelFolder(string folder) {
		var texturePaths = GetTexturePaths(folder);
		if(texturePaths.Length == 0)
			return;

		AssetDatabase.StartAssetEditing();
		try {
			foreach(var path in texturePaths)
				FixTextureImport((TextureImporter)AssetImporter.GetAtPath(path), reimport:true);
		} finally {
			AssetDatabase.StopAssetEditing();
		}
	}

	public static string[] GetTexturePaths(string folder) {
		return AssetDatabase.FindAssets("t:Texture", new string[]{folder}).Select(
			guid => AssetDatabase.GUIDToAssetPath(guid)).Where(
			path => CheckTextureExtension(path)).ToArray();
	}

	public static Shader[] GetShaders() {
		return ShaderUtil.GetAllShaderInfo().Where(info => info.name.StartsWith("GPT/", System.StringComparison.Ordinal)).Select(
			info => Shader.Find(info.name)).ToArray();
	}

	public static void FixTextureImport(TextureImporter importer, bool reimport=false) {
		if(importer == null)
			return;
		if(importer.sRGBTexture == false && importer.npotScale == TextureImporterNPOTScale.None && importer.maxTextureSize == 16384)
			return;

		importer.sRGBTexture = false;
		importer.npotScale = TextureImporterNPOTScale.None;
		importer.mipmapEnabled = false;

		var path = AssetDatabase.GetAssetPath(importer);
		var fmt32 = CalcCompressionFormat(path, DataType.Float32);
		var fmt16 = CalcCompressionFormat(path, DataType.Float16);
		var settings = importer.GetDefaultPlatformTextureSettings();
		settings.maxTextureSize = 16384;
		if(settings.format != fmt32 && settings.format != fmt16)
			settings.format = fmt32;
		importer.SetPlatformTextureSettings(settings);
		if(reimport)
			importer.SaveAndReimport();
	}

	public static bool CheckTextureExtension(string path) {
		var ext = Path.GetExtension(path);
		return ext == ".png" || ext == ".exr";
	}

	public static TextureImporterFormat CalcCompressionFormat(string path, DataType dtype, bool force=false) {
		if(Regex.IsMatch(path, @"\.q[1-8]\.(idx\.)?png$"))
			return TextureImporterFormat.RGBA32;
		if(Regex.IsMatch(path, @"\.q[1-8]\.(idx\.)?exr$"))
			return TextureImporterFormat.RGBAFloat;
		for(int i=1; i<=8; i++)
			if(File.Exists(Path.ChangeExtension(path, $".q{i}.png")) || File.Exists(Path.ChangeExtension(path, $".q{i}.exr"))) {
				if(Path.GetExtension(path) == ".png")
					return i <= 4 ? TextureImporterFormat.ARGB16 : i <= 6 ? TextureImporterFormat.RGB24 : TextureImporterFormat.RGBA32;
				if(dtype == DataType.Unorm4)
					return i <= 4 || force ? TextureImporterFormat.ARGB16 : TextureImporterFormat.RGBA32;
				if(dtype == DataType.Unorm8)
					return TextureImporterFormat.RGBA32;
				break;
			}
		if(Path.GetExtension(path) == ".png")
			return TextureImporterFormat.RGBA32;
		return dtype == DataType.Float16 ? TextureImporterFormat.RGBAHalf : TextureImporterFormat.RGBAFloat;
	}

	public static void SetTextureFormat(string path, TextureImporterFormat format) {
		var importer = (TextureImporter)AssetImporter.GetAtPath(path);
		var settings = importer.GetDefaultPlatformTextureSettings();
		var standalone = importer.GetPlatformTextureSettings("Standalone");
		if(settings.format == format && !(format == TextureImporterFormat.RGBAFloat && standalone.overridden))
			return;

		settings.format = format;
		importer.SetPlatformTextureSettings(settings);
		if(format == TextureImporterFormat.RGBAFloat) {
			standalone.overridden = false;
			importer.SetPlatformTextureSettings(standalone);
		}
		importer.SaveAndReimport();
	}

#pragma warning disable CA1720
	public enum DataType {
		Float32,
		Float16,
		Unorm8,
		Unorm4,
	}
#pragma warning restore CA1720
#pragma warning disable CS0649, CA1812, CA1852
	[System.Serializable]
	class Config {
		public string model_type;
		public int max_position_embeddings;
	}
#pragma warning restore CS0649, CA1812, CA1852
}
}
#endif