using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace ShaderGPT.Backends {
public partial class ShaderOps {
	public Tensor Gather(TensorView input, (TensorView tex, int chan) index, int axis=0, TensorView @out=default,
			bool inputT=false, GraphicsFormat? dtype=null, int bias=0, int? logPeek=null, Tensor quantizer=default, Tensor permuter=default) {
		using var _ = ~ctx;
		if(permuter && axis == 1) {
			index = (~Gather(~Fusion(permuter, cadd:1), index, axis:1, bias:bias, logPeek:logPeek), 1);
			(bias, logPeek) = (-1, null); // permute OOB index to -1 with cadd
		}
		var size0 = axis != 1 ? index.tex.size0 : (inputT ? input.size1*4 : input.size0);
		var size1 = axis == 1 ? index.tex.size1 : (inputT ? (input.size0+3)/4 : input.size1);
		var output = @out ? @out : ctx.GPUTensor(size0, size1, dtype:dtype ?? (quantizer ? ctx.defaultDType : input.dtype));
		var mat = Material("Gather");
		if(axis == 1)
			mat.EnableKeyword(nameof(CommonKeyword.AXIS_1));
		SetTensor(mat, "O", output);
		SetTensor(mat, "W", input);
		if(quantizer)
			SetTensorQuant(mat, "Q", quantizer, input.tensor);
		if(inputT)
			mat.EnableKeyword(nameof(CommonKeyword.TRANSPOSE_W));
		SetTensor(mat, "X", index.tex);
		mat.SetInt("ChanX", index.chan);
		mat.SetInt("Bias", bias);
		if(logPeek.HasValue)
			mat.SetInt("LogPeek", logPeek.Value);
		ctx.Blit(output.tensor, mat);
		if(permuter && axis == 0)
			return @out ? Fusion(~Gather(output, (permuter, 1), axis:1), @out:output) : Gather(~output, (permuter, 1), axis:1);
		return output.tensor;
	}

	public Tensor Scatter(Tensor output, (TensorView tex, int chan) index, TensorView source, int axis=0,
			float value=0, int bias=0, int? logPeek=null, bool add=false) {
		const int batchSize = 2*32*8; // must match Scatter.shader
		Assert.IsTrue(!source || (axis == 1 ? index.tex.size1 == source.size1 : index.tex.size0 == source.size0), "source size mismatch");
		var count = axis == 1 ? index.tex.size1*4 : index.tex.size0;
		for(int mask=axis==1&&!add?1:15; mask<16; mask<<=1)
		for(int off=0; off<count; off+=batchSize) {
			var mat = Material("Scatter");
			if(axis == 1)
				mat.EnableKeyword(nameof(CommonKeyword.AXIS_1));
			SetTensor(mat, "O", output);
			if(source)
				SetTensor(mat, "Y", source.Split(batchSize/(axis==1?4:1), off/batchSize, axis:axis));
			else
				mat.SetVector("VecY", value * Vector4.one);
			var intex = index.tex;
			if(intex)
				SetTensor(mat, "X", intex.Split(batchSize/(axis==1?4:1), off/batchSize, axis:axis));
			mat.SetInt("ChanX", index.chan);
			mat.SetInt("Bias", bias);
			if(logPeek.HasValue)
				mat.SetInt("LogPeek", logPeek.Value);
			mat.SetInt("MaskO", mask);
			if(add)
				mat.SetInt("DstBlend", (int)BlendMode.One);
			ctx.Blit(output, mat);
		}
		return output;
	}
}
}