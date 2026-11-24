using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Experimental.Rendering;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace ShaderGPT {
public partial class TensorContext {
	// tensor manager
	public float? defaultValue;
	const HideFlags newHideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
	public Tensor GPUTensor(int size0, int size1, GraphicsFormat? dtype=null, bool persistent=false,
		int lod=0, [CallerMemberName] string callerMemberName=null) {
		var desc = GPUTensorDescriptor(size0, size1, dtype:dtype, lod:lod);
		// NOTE: don't use temp RT for persistent, as previous ops may touch it when traced graph is replayed
		var tex = persistent ? new RenderTexture(desc){hideFlags=newHideFlags} : RenderTexture.GetTemporary(desc);
		tex.name = callerMemberName;
		if(defaultValue is float v) {
			Graphics.SetRenderTarget(tex);
			GL.Clear(true, true, new(v,v,v,v));
		}
		return Register(tex, size0);
	}
	public Tensor CPUTensor(int size0, int size1, GraphicsFormat? dtype=null) {
		var (width, height, textureFormat) = CPUTensorDescriptor(size0, size1, dtype:dtype);
		var tex = new Texture2D(width, height, textureFormat, mipChain:false, linear:true);
		return Register(tex, size0);
	}
	private readonly HashSet<Texture> textures = new();
	private Tensor Register(Texture tex, int size0) {
		var ok = textures.Add(tex);
		Assert.IsTrue(ok);
		((Tensor)tex).size0 = size0;
		if(tracing)
			Tracing_AfterRegister(tex);
		return tex;
	}
	public void Release(Tensor t) => Release((Texture)t);
	private void Release(Texture tex) {
		if(ReferenceEquals(tex, null))
			return;
		if(tracing)
			Tracing_BeforeRelease(tex);
		var ok = textures.Remove(tex);
		Assert.IsTrue(ok);
		if(tex is RenderTexture rt && rt.hideFlags != newHideFlags) {
			rt.mipMapBias = 0; // reset parameters for temporary texture
			RenderTexture.ReleaseTemporary(rt);
		} else
			Destroy(tex);
	}
	private static void Destroy(Object o) {
		if(Application.isPlaying)
			Object.Destroy(o);
		else
			Object.DestroyImmediate(o);
	}

	// operator manager
	public Material Operator(Shader shader) {
		return new Material(shader);
	}
	public void Blit(Tensor t, Material mat) {
		if(tracing) {
			Tracing_Blit(t, mat);
			return;
		}
		Graphics.Blit(null, (RenderTexture)t, mat, -1);
		Destroy(mat);
	}

	// tensor scope
	[System.ThreadStatic]
	private static Stack<TensorContext> scopeStack;
	private readonly Stack<Tensor> releaseStack = new();
	internal static void ScopeRelease(Tensor t) => scopeStack.Peek().releaseStack.Push(t);
	public static TensorScope operator ~([NotNull] TensorContext t) => new(t);
	public readonly struct TensorScope : System.IDisposable {
		private readonly int n;
		internal TensorScope(TensorContext ctx) {
			(scopeStack ??= new()).Push(ctx);
			n = ctx.releaseStack.Count;
		}
		public void Dispose()  {
			var ctx = scopeStack.Pop();
			while(ctx.releaseStack.Count > n)
				ctx.Release(ctx.releaseStack.Pop());
		}
		public static Tensor operator | (TensorScope self, Tensor x) {
			self.Dispose();
			return x;
		}
		public static TensorView operator | (TensorScope self, TensorView x) {
			self.Dispose();
			return x;
		}
		public static (TensorView,TensorView) operator | (TensorScope self, (TensorView,TensorView) x) {
			self.Dispose();
			return x;
		}
		public static (TensorView,TensorView,TensorView) operator | (TensorScope self, (TensorView,TensorView,TensorView) x) {
			self.Dispose();
			return x;
		}
	}

	// tensor descriptor
	public GraphicsFormat defaultDType = GraphicsFormat.R32G32B32A32_SFloat;
	private (int,int,TextureFormat) CPUTensorDescriptor(int size0, int size1, GraphicsFormat? dtype=null) {
		return (size1, size0, GraphicsFormatUtility.GetTextureFormat(dtype??defaultDType));
	}
	private RenderTextureDescriptor GPUTensorDescriptor(int size0, int size1, GraphicsFormat? dtype=null, int lod=0) {
		const int AUTO_TILE = 0; // inconsistent test result: disable for now
		var log0 = Mathf.CeilToInt(Mathf.Log(size0, 2));
		var log1 = Mathf.CeilToInt(Mathf.Log(size1, 2));
		var ctz1 = Mathf.RoundToInt(Mathf.Log(size1 & -size1, 2));
		// max(0, lod+log1-14) <= tile <= min(ctz1, 14-lod-log0)
		// log0+lod+tile >= AUTO_TILE if possible
		var lod_ub = Mathf.Min(14-log0, ctz1+14-log1, (14-log0+14-log1)>>1);
		Assert.IsTrue(0 <= lod_ub, $"can't allocate a wide tensor of exact size ({size0},{size1})");
		lod = Mathf.Clamp(lod, 0, lod_ub);
		var tile = Mathf.Clamp(AUTO_TILE-log0-lod, Mathf.Max(0, lod+log1-14), Mathf.Min(ctz1, 14-lod-log0));
		var desc = new RenderTextureDescriptor((size1>>tile)<<lod, (size0<<tile)<<lod, dtype??defaultDType, 0){
			mipCount = 1+lod, useMipMap = lod>0,
			autoGenerateMips = lod>1, // skip mip 1 generation
		};
		var msize = GraphicsFormatUtility.ComputeMipmapSize(desc.width, desc.height, desc.graphicsFormat);
		if(desc.width > 16384 || desc.height > 16384 || msize >= SystemInfo.graphicsMemorySize*131072) // throw exception to avoid gpu crash
			throw new System.ArgumentException($"RenderTexture is too large ({desc.width},{desc.height})");
		return desc;
	}
	public (int,int) ChooseLodAndInst(int size0, int size1, int workSize, int? forceLod=null, int? forceInst=null) {
		var bestCost = Mathf.Infinity;
		var bestPair = (0, 1);
		for(int lod=forceLod??0; lod<=(forceLod??1); lod++)
		for(int inst=forceInst??1; inst<=(forceInst??32); inst++) {
			if(lod == 0 && inst > 1)
				continue;
			var (H, W) = (size0<<lod, size1<<lod);
			if(W > 16384)
				(H, W) = (H*2, W/2);
			// heuristic cost model. not proportional to actual time
			var cost = Mathf.Max(6000, Mathf.Ceil(H/2f) * Mathf.Ceil(W/2f) * inst);
			cost *= Mathf.Max(8, (float)workSize / (inst<<lod<<lod));
			if(cost < bestCost) {
				bestCost = cost;
				bestPair = (lod, inst);
			}
		}
		return bestPair;
	}

	// utilities
	public int TensorCount() {
		return textures.Count;
	}
	public string[] TensorNames() {
		return new List<Texture>(textures).ConvertAll(tex => tex == null ? "<null>" : $"{tex.name} ({tex.width}x{tex.height})").ToArray();
	}
	public void ReleaseAll() {
		var textureList = new List<Texture>(textures);
		foreach(var tex in textureList)
			Release(tex);
	}
	public void Log(TensorView input, bool full=false, string prefix=null) {
		Debug.Log(TensorFormatter.Format(input.GetData<float>(), input.size0, input.size1*4, full, prefix));
	}
	public void AssertEqual(TensorView input, [NotNull] float[] value, float atol=0f, bool trim=false) {
		var nrow = input.size0;
		var ncol = input.size1*4;
		var data = input.GetData<float>();
		var errorL1 = 0f;
		var errorL2 = 0f;
		var errorLi = 0f;
		var errorIdx = -1;
		var count = value.Length;
		var rowSize = value.Length / nrow;
		for(int i=0; i<count; i++) {
			var j = i;
			if(trim)
				j += i/rowSize * (ncol-rowSize);
			var error = Mathf.Abs(data[j] - value[i]);
			errorL1 += error;
			errorL2 += error*error;
			errorLi = System.MathF.Max(errorLi, error); // Mathf.Max doesn't handle NaN correctly
			if(error == errorLi)
				errorIdx = i;
		}
		errorL1 = errorL1/count;
		errorL2 = Mathf.Sqrt(errorL2/count);
		if(Mathf.Abs(errorLi) <= atol) {
			Debug.Log($"AssertEqual passed: L1={errorL1}, L2={errorL2}, Li={errorLi} @ {errorIdx}");
		} else {
			Debug.LogError(TensorFormatter.Format(data, nrow, ncol,
				prefix:$"AssertEqual failed: L1={errorL1}, L2={errorL2}, Li={errorLi} @ {errorIdx}\n"));
		}
	}
}
}