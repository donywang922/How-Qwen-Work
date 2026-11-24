using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Rendering;

namespace ShaderGPT.Backends {
public partial class ShaderOps {
	public enum LinearKeyword {
		None = 0,
		LINEAR_CONV, LINEAR_GROUPNORM, LINEAR_SOFTMAX,
		LINEAR_MUL, LINEAR_GELU_MUL, LINEAR_SILU_MUL,
	}

	public Tensor Linear(TensorView input, TensorView weight, TensorView bias=default, TensorView @out=default,
			bool weightT=false, int heads=1, int? weightHeads=null, TensorView clear=default, bool accum=false,
			LinearKeyword func=default, TensorView other=default, TensorView reduce=default, float eps=0,
			float cmul=1f, TensorView add=default, Window? window=null, Tensor quantizer=default, Tensor permuter=default) {
		Assert.IsTrue(!clear || (clear.tensor == @out.tensor && clear.size0 == @out.size0 && accum), "clear size mismatch");
		Assert.IsTrue(!other || (func == LinearKeyword.LINEAR_GROUPNORM
			? input.size0%other.size0 == 0 && input.size1%other.size1 == 0
			: input.size0 == other.size0 && input.size1 == other.size1), "other size mismatch");
		Assert.IsTrue(func == LinearKeyword.LINEAR_GROUPNORM || func == LinearKeyword.LINEAR_SOFTMAX ?
			reduce.size0 == input.size0 && reduce.size1 == heads : !reduce, "reduce size mismatch");
		var wheads = weightHeads ?? heads;
		using var _ = ~ctx;
		if(permuter) {
			Assert.IsTrue(func != LinearKeyword.LINEAR_CONV && heads == wheads && !(reduce && heads != 1), "not implemented");
			input = ~Gather(input, (permuter, 0), axis:1);
			if(func != default && other)
				other = ~Gather(other, (permuter, 0), axis:1);
		}
		var idim = !weightT ? weight.size1/wheads : (weight.size0+3)/4; // head is always in axis 1
		var odim =  weightT ? weight.size1/wheads : (weight.size0+3)/4;
		int iheads = heads, oheads = heads;
		if(func == LinearKeyword.LINEAR_CONV) {
			odim  = iheads = weight.size1/wheads;
			idim  = !weightT ? wheads : weight.size0;
			oheads = weightT ? wheads : weight.size0;
		}
		Assert.IsTrue(input.size1 == iheads*idim && weight.size1%wheads == 0, "head size mismatch");
		Assert.IsTrue(!bias || (bias.size0 == 1 && bias.size1 == oheads*odim), "bias size mismatch");
		var (lod, inst) = ctx.ChooseLodAndInst(input.size0, oheads*odim, idim, forceLod:@out ? @out.lod : null);
		var output = @out ? @out : ctx.GPUTensor(input.size0, oheads*odim, dtype:input.dtype, lod:lod);
		if(func == default && !weightT && (window?.scale??0f) != 0f)
			inst = 1; // save bandwidth on sparse output in SDPA
		if(!accum && inst > 1)
			(clear, accum) = (output, true);
		for(int step=Mathf.Max(1,maxBatchSize), start=0; start<input.size0; start+=step) {
			var stop = Mathf.Min(start+step, input.size0);
			var mat = Material(clear ? "LinearClear" : "Linear");
			if(clear)
				SetTensor(mat, "Clear", clear[start..stop, ..]);
			if(func != default) {
				mat.EnableKeyword(func.ToString());
				if(other)
					SetTensor(mat, "Y", other.size0 == 1 ? other : other[start..stop, ..]);
				if(reduce)
					SetTensor(mat, "Reduce", reduce[start..stop, ..]);
				mat.SetFloat("Bias", eps);
			}
			mat.SetVector("DimHead", new(idim, odim, heads/wheads, 0));
			SetTensor(mat, "O", output[start..stop, ..]);
			SetTensor(mat, "X", input[start..stop, ..]);
			SetTensor(mat, "W", weight);
			if(quantizer)
				SetTensorQuant(mat, "Q", quantizer, weight.tensor);
			if(weightT)
				mat.EnableKeyword(nameof(CommonKeyword.TRANSPOSE_W));
			if(bias)
				SetTensor(mat, "B", bias);
			if(window.HasValue)
				SetWindow(mat, "Win", window.Value);
			if(add)
				SetTensor(mat, "Add", add[start..stop, ..]);
			mat.SetVector("VecMul", cmul * Vector4.one);
			if(accum) {
				mat.SetInt("DstBlend", (int)BlendMode.One);
				mat.SetInt("InstCount", inst);
			}
			ctx.Blit(output.tensor, mat);
		}
		return output.tensor;
	}
}
}