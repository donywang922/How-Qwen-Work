using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Rendering;

namespace ShaderGPT.Backends {
public partial class ShaderOps {
	internal static int WideSize1(int size1) {
		// extend size1 to satisfy wide tensor alignment
		var lvl = Mathf.Max(0, Mathf.CeilToInt(Mathf.Log(size1, 2)-14));
		return (size1 + (1<<lvl)-1) &~ ((1<<lvl)-1);
	}

	public enum ConvKeyword {
		CONV1D, CONVTRANSPOSE1D,
		K1_S1, K3_S1, K5_S1, K7_S1, K9_S1, K11_S1,
		K4_S2, K8_S4, K12_S6, K16_S8, K20_S10, K24_S12,
		K20_S5,
	}

	public Tensor Conv1d(TensorView input, TensorView weight, TensorView bias, int kernel_size,
			int stride=1, int padding=0, int groups=1, Tensor quantizer=default) {
		var wdim = (kernel_size+3)/4;
		Assert.IsTrue(input.size0%groups == 0, "input size mismatch");
		Assert.IsTrue(weight.size1 == (kernel_size==1 ? (input.size0/groups+3)/4 : input.size0/groups*wdim), "weight size mismatch");
		Assert.IsTrue(!bias || (bias.size0 == 1 && bias.size1 == (weight.size0+3)/4), "bias size mismatch");
		Assert.IsFalse(kernel_size == 1 && padding != 0, "nonzero padding for kernel_size=1");
		var (lod, inst) = ctx.ChooseLodAndInst(weight.size0, WideSize1((input.size1+stride-1)/stride), input.size0/groups*wdim);
		var output = ctx.GPUTensor(weight.size0, WideSize1((input.size1+stride-1)/stride), dtype:input.dtype, lod:lod);
		if(inst > 1)
			Fusion(null, cmul:0, @out:output);
		for(int step=Mathf.Max(1,maxBatchSize), start=0; start<output.size1; start+=step) {
			var stop = Mathf.Min(start+step, output.size1);
			var mat = Material("Conv");
			mat.EnableKeyword(nameof(ConvKeyword.CONV1D));
			mat.EnableKeyword(System.Enum.Parse<ConvKeyword>($"K{kernel_size}_S{stride}").ToString());
			mat.SetVector("DimHead", new Vector3(groups, 1, kernel_size == 1 ? 0 : start*4*stride-padding));
			SetTensor(mat, "O", output[.., start..stop]);
			SetTensor(mat, "X", kernel_size == 1 ? input[.., start..stop] : input);
			SetTensor(mat, "W", weight);
			if(quantizer)
				SetTensorQuant(mat, "Q", quantizer, weight.tensor);
			if(bias)
				SetTensor(mat, "B", bias);
			if(inst > 1) {
				mat.SetInt("DstBlend", (int)BlendMode.One);
				mat.SetInt("InstCount", inst);
			}
			ctx.Blit(output, mat);
		}
		return output;
	}

	public Tensor ConvTranspose1d(TensorView input, TensorView weight, TensorView bias, int kernel_size,
			int stride=1, int padding=0, int groups=1, Tensor quantizer=default, int output_padding=0) {
		var wdim = (kernel_size+3)/4;
		var ocnt = kernel_size==1 ? weight.size1*4 : weight.size1/wdim;
		Assert.IsTrue(input.size0%groups == 0, "input size mismatch");
		Assert.IsTrue(weight.size0 == input.size0 && weight.size1%wdim == 0, "weight size mismatch");
		Assert.IsTrue(!bias || (bias.size0 == 1 && bias.size1 == (ocnt*groups+3)/4), "bias size mismatch");
		Assert.IsFalse(kernel_size == 1 && padding != 0, "nonzero padding for kernel_size=1");
		var (lod, inst) = ctx.ChooseLodAndInst(ocnt*groups, WideSize1(input.size1*stride+(output_padding+3)/4), input.size0/groups*wdim);
		var output = ctx.GPUTensor(ocnt*groups, WideSize1(input.size1*stride+(output_padding+3)/4), dtype:input.dtype, lod:lod);
		if(inst > 1)
			Fusion(null, cmul:0, @out:output);
		for(int step=Mathf.Max(1,maxBatchSize), start=0; start<output.size1; start+=step) {
			var stop = Mathf.Min(start+step, output.size1);
			var mat = Material("Conv");
			mat.EnableKeyword(nameof(ConvKeyword.CONVTRANSPOSE1D));
			mat.EnableKeyword(System.Enum.Parse<ConvKeyword>($"K{kernel_size}_S{stride}").ToString());
			mat.SetVector("DimHead", new Vector3(groups, 1, kernel_size == 1 ? 0 : start*4+padding));
			SetTensor(mat, "O", output[.., start..stop]);
			SetTensor(mat, "X", kernel_size == 1 ? input[.., start..stop] : input);
			SetTensor(mat, "W", weight);
			if(quantizer)
				SetTensorQuant(mat, "Q", quantizer, weight.tensor);
			if(bias)
				SetTensor(mat, "B", bias);
			if(inst > 1) {
				mat.SetInt("DstBlend", (int)BlendMode.One);
				mat.SetInt("InstCount", inst);
			}
			ctx.Blit(output, mat);
		}
		return output;
	}
}
}