using UnityEngine;
using UnityEngine.Rendering;
using System.Collections.Generic;
using System.Linq;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace ShaderGPT {
public partial class TensorContext {
	private bool tracing = false;
	private readonly Dictionary<int, RenderTextureDescriptor> rtDesc = new();
	private readonly Dictionary<int, (int,int,TextureFormat)> texDesc = new();
	private readonly Dictionary<int, float[]> texData = new();
	private readonly List<Material> matList = new();
	private readonly List<(int,int)> matRanges = new();
	private void Tracing_AfterRegister(Texture tex) {
		if(tex is RenderTexture rt)
			rtDesc[tex.GetInstanceID()] = rt.descriptor;
		else if(tex is Texture2D tex2d)
			texDesc[tex.GetInstanceID()] = (tex2d.width, tex2d.height, tex2d.format);
	}
	private void Tracing_BeforeRelease(Texture tex) {
		if(tex is Texture2D tex2d && !tex2d.isReadable)
			texData[tex2d.GetInstanceID()] = ((Tensor)tex).GetData<float>().ToArray();
	}
	private void Tracing_Blit(Tensor t, Material mat) {
		Graphics.Blit(null, (RenderTexture)t, mat, -1);
		matList.Add(mat);
	}

	public TracingScope Tracing() => Application.isPlaying ? new(this)
		// Tracing releases temporary textures before export and relies on deferred Object.Destroy
		// to preserve material texture bindings; edit-mode tracing can export null input textures.
		: throw new System.InvalidOperationException("Tensor tracing and prefab baking require Unity play mode");
	public readonly struct TracingScope : System.IDisposable {
		private readonly TensorContext ctx;
		private readonly int n;
		internal TracingScope(TensorContext ctx) {
			this.ctx = ctx;
			this.n = ctx.matList.Count;
			ctx.tracing = true;
		}
		public void Dispose() {
			ctx.tracing = false;
			ctx.matRanges.Add((n, ctx.matList.Count));
		}
	}
	public Object ExportTracing(string path) {
		var names = new HashSet<string>();
		foreach(var shader in matList.Select(mat => mat.shader).Distinct())
			for(int i=0; i<shader.GetPropertyCount(); i++)
				if(shader.GetPropertyType(i) == ShaderPropertyType.Texture)
					names.Add(shader.GetPropertyName(i));
#if UNITY_EDITOR
		var remap = new Dictionary<int,Texture>();
		var go = new GameObject("trace");
		var prefab = PrefabUtility.SaveAsPrefabAssetAndConnect(go, path, InteractionMode.AutomatedAction);
		// remove old textures and materials
		foreach(var o in AssetDatabase.LoadAllAssetsAtPath(path))
			if(o is Texture || o is Material)
				Object.DestroyImmediate(o, true);
		{
			// NOTE: mipMapBias isn't recorded
			var idx = 0;
			foreach(var pair in rtDesc) {
				var tex = new RenderTexture(pair.Value){name = $"rt{idx++}"};
				AssetDatabase.AddObjectToAsset(tex, path);
				remap[pair.Key] = tex;
			}
			idx = 0;
			foreach(var pair in texDesc) {
				var (width, height, format) = pair.Value;
				var tex = new Texture2D(width, height, textureFormat:format, mipChain:false, linear:true){name = $"tex{idx++}"};
				if(texData.TryGetValue(pair.Key, out var data)) {
					tex.SetPixelData(data, 0, 0);
					// makeNoLongerReadable causes error in SaveAssets if "load texture data on demand" is enabled
					tex.Apply(updateMipmaps:false, makeNoLongerReadable:false);
				}
				AssetDatabase.AddObjectToAsset(tex, path);
				remap[pair.Key] = tex;
			}
			idx = 0;
			foreach(var mat in matList) {
				foreach(var name in names) {
					if(!mat.HasProperty(name))
						continue;
					var tex = mat.GetTexture(name);
					if(object.ReferenceEquals(tex, null))
						continue;
					var id = tex.GetInstanceID();
					if(remap.TryGetValue(id, out var value))
						mat.SetTexture(name, value);
					else if(!AssetDatabase.Contains(tex))
						Debug.LogWarning($"Material {mat.name} has property {name} bound to non-traced non-asset texture {tex.name}");
				}
				mat.name = $"mat{idx++}";
				AssetDatabase.AddObjectToAsset(mat, path);
			}
		}
		foreach(var (start,stop) in matRanges) {
			var arr = new Material[stop-start];
			for(int j=start; j<stop; j++)
				arr[j-start] = matList[j];
			var child = new GameObject($"{start}~{stop}", typeof(MeshRenderer));
			child.transform.parent = go.transform;
			child.GetComponent<MeshRenderer>().sharedMaterials = arr;
		}
		PrefabUtility.ApplyPrefabInstance(go, InteractionMode.AutomatedAction);
		Object.Destroy(go);
		AssetDatabase.SaveAssets();
		return prefab;
#else
		return null;
#endif
	}
}
}