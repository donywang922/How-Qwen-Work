using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Rendering;

namespace ShaderGPT.Backends {
public partial class ShaderOps {
	public enum FusionKeyword {
		None = 0,
		CLAMP, COS, EXP, LOG, POW, ROUND, SIN, WHERE,
		GELU, GELU_TANH, RELU, SIGMOID, SILU, SOFTPLUS, TANH,
		RAND_NORMAL,
		GROUPNORM, SOFTMAX,
		FROM_IMAGE, LSTM_CELL, ROTARY,
		NARROW, STRIDED,
	}

	public Tensor Fusion(TensorView input, FusionKeyword func=0, float scale=1, float bias=0,
			TensorView other=default, Vector2Int? groups=null, (Vector4,Vector4,Vector4)? stride=null,
			TensorView mul=default, TensorView add=default, float cmul=1f, float cadd=0f,
			Window? window=null, TensorView @out=default) {
		var output = @out ? @out : ctx.GPUTensor(input.size0, input.size1, dtype:input.dtype);
		Assert.IsTrue(!mul || (output.size0%mul.size0 == 0 && output.size1%mul.size1 == 0), "mul size mismatch");
		Assert.IsTrue(!add || (output.size0%add.size0 == 0 && output.size1%add.size1 == 0), "add size mismatch");
		Assert.IsTrue(groups == null || (output.size0%groups.Value.x == 0 && output.size1%groups.Value.y == 0), "group size mismatch");
		var mat = Material("Fusion");
		SetTensor(mat, "O", output);
		if(input)
			SetTensor(mat, "X", input);
		if(other)
			SetTensor(mat, "Y", other);
		mat.SetVector("DimHead", new Vector2((output.size0/groups?.x)??1, output.size1/(groups?.y??1)));
		if(window.HasValue)
			SetWindow(mat, "Win", window.Value);
		if(mul)
			SetTensor(mat, "Mul", mul);
		if(add && add != @out)
			SetTensor(mat, "Add", add);
		mat.SetVector("VecMul", cmul * Vector4.one);
		mat.SetVector("VecAdd", cadd * Vector4.one);
		mat.SetFloat("Scale", scale);
		mat.SetFloat("Bias",  bias);
		if(func == FusionKeyword.STRIDED) {
			Assert.IsTrue(other == default);
			mat.SetVector("DimY",    stride.Value.Item1);
			mat.SetVector("Stride0", stride.Value.Item2);
			mat.SetVector("Stride1", stride.Value.Item3);
		}
		if(func != default)
			mat.EnableKeyword(func.ToString());
		if(add && add == @out)
			mat.SetInt("DstBlend", (int)BlendMode.One);
		ctx.Blit(output.tensor, mat);
		return output.tensor;
	}

	public Tensor Narrow(TensorView input, Window window, TensorView @out, int groups=1, PaddingMode padding_mode=0) {
		Assert.IsTrue(input.size1%groups == 0 && @out.size1%groups == 0, "group size mismatch");
		return Fusion(input, @out:@out, func:FusionKeyword.NARROW, groups:new(@out.size0, groups), bias:(int)padding_mode,
			window:new Window(window.start, window.stop, window.scale, window.index, float.NegativeInfinity)); // ignore 2nd range
	}

	public Tensor Strided(TensorView input, Vector3Int size0, Vector3Int size1, Vector4 stride0, Vector4 stride1,
			PaddingMode padding_mode=0, TensorView mul=default, TensorView add=default, TensorView @out=default) {
		return Fusion(input, func:FusionKeyword.STRIDED, stride:(new(size0.y, size0.z, size1.y, size1.z), stride0, stride1), bias:(int)padding_mode,
			mul:mul, add:add, @out:@out?@out:ctx.GPUTensor(size0.x*size0.y*size0.z, (size1.x*size1.y*size1.z+3)/4, dtype:input.dtype),
			window:@out?null:new(0, size1.x*size1.y*size1.z)); // zero padding if prod(size1)%4 != 0
	}

	public Tensor Rotary(TensorView input, TensorView rotary, int groups=1) {
		Assert.IsTrue(input.size1%groups == 0, "group size mismatch");
		return Fusion(input, func:FusionKeyword.ROTARY, other:rotary, groups:new(input.size0,groups));
	}
}
}