#if UNITY_EDITOR
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEditor;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace ShaderGPT {
public class ModelImporter : AssetPostprocessor {
	[MenuItem("Assets/ShaderGPT/ImportModel", false, 100)]
	static void ImportModel() {
		foreach(var o in Selection.objects) {
			var path = AssetDatabase.GetAssetPath(o);
			if(Directory.Exists(path))
				EditorGUIUtility.PingObject(ImportModel(path));
		}
	}
	[MenuItem("Assets/ShaderGPT/ProfileModel", false, 101)]
	static void ProfileModel() {
		foreach(var o in Selection.objects) {
			var path = AssetDatabase.GetAssetPath(o);
			if(Directory.Exists(path))
				ProfileModel(path);
		}
	}
	[MenuItem("Assets/ShaderGPT/ProfilePrefab", false, 102)]
	static void ProfilePrefab() {
		foreach(var o in Selection.objects) {
			var path = AssetDatabase.GetAssetPath(o);
			if(Path.GetExtension(path) == ".prefab")
				ProfilePrefab(path);
		}
	}
	[MenuItem("Assets/ShaderGPT/UseFloat32", false, 200)]
	static void UseFloat32() {
		foreach(var o in Selection.objects) {
			var path = AssetDatabase.GetAssetPath(o);
			if(Directory.Exists(path))
				SetModelCompression(path, ModelAssetUtility.DataType.Float32);
			else if(o is Texture2D)
				ModelAssetUtility.SetTextureFormat(path, TextureImporterFormat.RGBAFloat);
		}
	}
	[MenuItem("Assets/ShaderGPT/UseFloat16", false, 201)]
	static void UseFloat16() {
		foreach(var o in Selection.objects) {
			var path = AssetDatabase.GetAssetPath(o);
			if(Directory.Exists(path))
				SetModelCompression(path, ModelAssetUtility.DataType.Float16);
			else if(o is Texture2D)
				ModelAssetUtility.SetTextureFormat(path, TextureImporterFormat.RGBAHalf);
		}
	}
	[MenuItem("Assets/ShaderGPT/UseUnorm8", false, 202)]
	static void UseUnorm8() {
		foreach(var o in Selection.objects) {
			var path = AssetDatabase.GetAssetPath(o);
			if(Directory.Exists(path))
				SetModelCompression(path, ModelAssetUtility.DataType.Unorm8);
			else if(o is Texture2D)
				ModelAssetUtility.SetTextureFormat(path, TextureImporterFormat.RGBA32);
		}
	}
	[MenuItem("Assets/ShaderGPT/UseUnorm4", false, 203)]
	static void UseUnorm4() {
		foreach(var o in Selection.objects) {
			var path = AssetDatabase.GetAssetPath(o);
			if(Directory.Exists(path))
				SetModelCompression(path, ModelAssetUtility.DataType.Unorm4);
			else if(o is Texture2D)
				ModelAssetUtility.SetTextureFormat(path, TextureImporterFormat.ARGB16);
		}
	}
	[MenuItem("Assets/ShaderGPT/UseUnorm4ForAllQuantized", false, 204)]
	static void UseUnorm4ForAllQuantized() {
		foreach(var o in Selection.objects) {
			var path = AssetDatabase.GetAssetPath(o);
			if(Directory.Exists(path))
				SetModelCompression(path, ModelAssetUtility.DataType.Unorm4, force:true);
			else if(o is Texture2D)
				ModelAssetUtility.SetTextureFormat(path, TextureImporterFormat.ARGB16);
		}
	}
	void OnPreprocessAsset() {
		if(assetImporter.importSettingsMissing) {
			var importer = assetImporter as TextureImporter;
			if(importer != null) {
				var path = AssetDatabase.GetAssetPath(importer);
				var configPath = Path.Join(Path.GetDirectoryName(Path.GetDirectoryName(path)), "config.json");
				if(File.Exists(configPath) && ModelAssetUtility.CheckTextureExtension(path))
					ModelAssetUtility.FixTextureImport(importer);
			}
		}
	}

	static MonoBehaviour ImportModel(string folder) {
		var assets = ModelAssetUtility.LoadModelAssets(folder);
		var model_type = assets.model_type;
		Debug.Log($"{folder} : {model_type}");

		if(AutoModel.Contains<ModelForTextToWaveform>(model_type)) {
			var go = new GameObject(Path.GetFileName(folder), typeof(BasicTTS));
			var pipeline = go.GetComponent<BasicTTS>();
			pipeline.shaders = assets.shaders;
			pipeline.textures = assets.textures;
			pipeline.configJson = assets.configJson;
			pipeline.tokenizerJson = assets.tokenizerJson;
			pipeline.testcaseJson = assets.testcaseJson;
			return pipeline;
		} else if(AutoModel.Contains<ModelForSpeechSeq2Seq>(model_type)) {
			var go = new GameObject(Path.GetFileName(folder), typeof(BasicSTT));
			var pipeline = go.GetComponent<BasicSTT>();
			pipeline.shaders = assets.shaders;
			pipeline.textures = assets.textures;
			pipeline.configJson = assets.configJson;
			pipeline.tokenizerJson = assets.tokenizerJson;
			pipeline.testcaseJson = assets.testcaseJson;
			pipeline.processorJson = assets.processorJson;
			return pipeline;
		} else if(AutoModel.Contains<ModelForImageTextToText>(model_type)) {
			var go = new GameObject(Path.GetFileName(folder), typeof(BasicVLM));
			var pipeline = go.GetComponent<BasicVLM>();
			pipeline.shaders = assets.shaders;
			pipeline.textures = assets.textures;
			pipeline.configJson = assets.configJson;
			pipeline.tokenizerJson = assets.tokenizerJson;
			pipeline.testcaseJson = assets.testcaseJson;
			pipeline.processorJson = assets.processorJson;
			return pipeline;
		} else {
			var go = new GameObject(Path.GetFileName(folder), typeof(BasicLM));
			var pipeline = go.GetComponent<BasicLM>();
			pipeline.shaders = assets.shaders;
			pipeline.textures = assets.textures;
			pipeline.configJson = assets.configJson;
			pipeline.tokenizerJson = assets.tokenizerJson;
			pipeline.testcaseJson = assets.testcaseJson;
			if(assets.max_position_embeddings != 0) {
				pipeline.generationConfig ??= new();
				pipeline.generationConfig.max_length = Mathf.Min(assets.max_position_embeddings, pipeline.generationConfig.max_length);
			}
			return pipeline;
		}
	}
	static void SetModelCompression(string folder, ModelAssetUtility.DataType dtype, bool force=false) {
		var texturePaths = ModelAssetUtility.GetTexturePaths(folder);
		AssetDatabase.StartAssetEditing();
		try {
			foreach(var path in texturePaths)
				ModelAssetUtility.SetTextureFormat(path, ModelAssetUtility.CalcCompressionFormat(path, dtype, force:force));
		} finally {
			AssetDatabase.StopAssetEditing();
		}
	}
	static void ProfileTexture(IEnumerable<(string, Texture)> namedTextures) {
		var groupSizes = new Dictionary<(string, TextureFormat), Vector3>();
		foreach(var (name, tex) in namedTextures) {
			var group = string.Join(".", name.Split(".").Reverse().TakeWhile(x => !x.All(char.IsDigit)).Reverse());
			var size = (long)GraphicsFormatUtility.ComputeMipmapSize(tex.width, tex.height, tex.graphicsFormat);
			var count = tex.width * tex.height * (tex.graphicsFormat == GraphicsFormat.R8G8B8_UNorm ? 4 :
				GraphicsFormatUtility.GetComponentCount(tex.graphicsFormat));
			if(Regex.IsMatch(name, @"\.q[1-8](?:\.|$)"))
				count = 0;
			var key = (group, GraphicsFormatUtility.GetTextureFormat(tex.graphicsFormat));
			groupSizes.TryGetValue(key, out var groupSize);
			groupSizes[key] = groupSize + new Vector3(size, count, 1);
		}
		var totalSizes = groupSizes.Values.Aggregate(Vector3.zero, (a, b) => a + b);
		const float MB = 1e6f;
		var sb = new System.Text.StringBuilder();
		sb.AppendLine($"TOTAL: {totalSizes.x/MB:0.0} MB / {totalSizes.y/MB:0.0}M params / {totalSizes.z} tex");
		var remSize = totalSizes.x;
		foreach(var pair in groupSizes.OrderBy(p => -p.Value.x))
			if(remSize > totalSizes.x/100) {
				sb.AppendLine($"[{pair.Key.Item2}]\t{pair.Key.Item1}\t{pair.Value.x/MB:0.0} MB / {pair.Value.y/MB:0.0}M params / {pair.Value.z} tex");
				remSize -= pair.Value.x;
			}
		Debug.Log(sb.ToString());
	}
	static void ProfileModel(string folder) {
		ProfileTexture(ModelAssetUtility.GetTexturePaths(folder).Select(
			path => (Path.GetFileNameWithoutExtension(path), AssetDatabase.LoadAssetAtPath<Texture>(path))));
	}
	static void ProfilePrefab(string path) {
		var assets = AssetDatabase.LoadAllAssetsAtPath(path);
		ProfileTexture(assets.OfType<Texture>().Select(
			tex => (string.Format("{0}x{1}", tex.width, tex.height), tex)));
	}
}
}
#endif