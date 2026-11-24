using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Experimental.Rendering;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace ShaderGPT {
public readonly struct Window {
	// intersection of [start,stop)+scale*index and [start2,stop2)+scale2*index2
	private readonly (float o0, float o1, float s, (TensorView,int) i) data;
	private readonly (float o0, float o1, float s, (TensorView,int) i) data2;
	public Window(float start, float stop, float scale=0, (TensorView,int) index=default,
				float start2=0, float stop2=float.PositiveInfinity, float scale2=0, (TensorView,int) index2=default) {
		data  = (start,  stop,  scale,  index);
		data2 = (start2, stop2, scale2, index2);
	}
	public float start => data.o0;
	public float stop  => data.o1;
	public float scale => data.s;
	public (TensorView,int) index => data.i;
	public float start2 => data2.o0;
	public float stop2  => data2.o1;
	public float scale2 => data2.s;
	public (TensorView,int) index2 => data2.i;
	public Window Scale(float mul) => new(
		mul*start,  mul*stop,  mul*scale,  index,
		mul*start2, mul*stop2, mul*scale2, index2);
}
}

namespace ShaderGPT.Backends {
public partial class ShaderOps {
	public TensorContext ctx;
	public Dictionary<string, Shader> kernels = new();

	private int maxBatchSize = 65536;
	public System.IDisposable SetBatchSize(int maxBatchSize) => new BatchSizeScope(this, maxBatchSize);
	private readonly struct BatchSizeScope : System.IDisposable {
		private readonly ShaderOps ops;
		private readonly int maxBatchSize;
		public BatchSizeScope(ShaderOps ops, int maxBatchSize) {
			this.ops = ops;
			this.maxBatchSize = ops.maxBatchSize;
			ops.maxBatchSize = maxBatchSize;
		}
		public void Dispose() {
			ops.maxBatchSize = maxBatchSize;
		}
	}

	internal Material Material(string kernel) => ctx.Operator(kernels[kernel]);
	internal void SetTensor(Material mat, string name, TensorView view) {
		Assert.IsTrue(view);
		mat.SetTexture($"Tex{name}", (Texture)view.tensor);
		mat.SetVector($"Dim{name}", new Vector4(view.size0, view.size1, view.tensor.tile1, view.lod));
		mat.SetTextureOffset($"Tex{name}", new Vector2(view.offset0, view.offset1));
		if(name == "O" || name == "Clear")
			mat.SetTextureScale($"Tex{name}", new Vector2(view.tensor.size0, view.tensor.size1)); // vertClip
	}
	private void SetTensorQuant(Material mat, string name, Tensor tex, Tensor weight) {
		SetTensor(mat, name, tex);
		mat.EnableKeyword(GraphicsFormatUtility.IsUNormFormat(tex.dtype) ? nameof(CommonKeyword.QUANT_E8)
			: weight.dtype == GraphicsFormat.R8G8B8_UNorm ? nameof(CommonKeyword.QUANT_E6M11_Z14_W6666)
			: nameof(CommonKeyword.QUANT_E6M11_Z14));
	}
	private void SetWindow(Material mat, string name, Window window) {
		mat.SetVector(name, new Vector4(window.start, window.stop, window.scale, window.index.Item2));
		mat.SetVector(name+"2", new Vector4(window.start2, window.stop2, window.scale2, window.index2.Item2));
		if(window.index.Item1)
			SetTensor(mat, name, window.index.Item1);
	}

	private enum CommonKeyword {
		AXIS_1,
		TRANSPOSE_W,
		QUANT_E6M11_Z14, QUANT_E6M11_Z14_W6666, QUANT_E8,
	}
}
}