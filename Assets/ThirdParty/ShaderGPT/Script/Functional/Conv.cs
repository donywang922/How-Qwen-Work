using UnityEngine;
using UnityEngine.Assertions;
using System.Collections.Generic;
using ShaderGPT.Backends;

namespace ShaderGPT {
public enum PaddingMode {
	Constant = 0,
	Replicate = 1,
	Reflect = 2,
	Circular = 3,
}
public partial class Functional {
	private int WideSize1(int size1) => ShaderOps.WideSize1(size1);
	public Tensor UpsampleNearest(TensorView input, int scale_factor)
		=> ops.Strided(input,
			new(input.size0, 1, 1), new(input.size1*4, 1, scale_factor),
			new(1,           0, 0), new(1,             0, 0));
	public Tensor DownsampleNearest(TensorView input, int scale_factor)
		=> ops.Strided(input,
			new(input.size0, 1, 1), new(input.size1/scale_factor*4, 1, 1),
			new(1,           0, 0), new(scale_factor,               0, 0));

	private Tensor Conv1dGeneric(TensorView input, TensorView weight, TensorView bias=default, int kernel_size=1,
			int stride=1, int dilation=1, int groups=1, int padding=0, PaddingMode padding_mode=0, Tensor quantizer=default) {
		var wdim = (kernel_size+3)/4;
		var isize0 = input.size0;
		var dsize1 = (input.size1+stride-1)/stride;
		var unfold = kernel_size == 1
			? ops.Strided(input,
				new(isize0, 1, 1), new(WideSize1(dsize1)*4, 1, 1),
				new(1,      0, 0), new(stride,              0, 0, -padding), padding_mode:padding_mode)
			: ops.Strided(input,
				new(groups, isize0/groups,    wdim*4), new(WideSize1(dsize1)*4, 1, 1),
				new(isize0/groups,      1, -dilation), new(stride,              0, 0, -padding), padding_mode:padding_mode);
		return ~ctx|ops.Conv1d(~unfold, weight, bias, kernel_size:1, groups:groups, quantizer:quantizer);
	}
	public Tensor Conv1d(TensorView input, TensorView weight, TensorView bias=default, int kernel_size=1,
			int stride=1, int dilation=1, int groups=1, int? padding=null, PaddingMode padding_mode=0) {
		padding ??= ((kernel_size-1)*dilation+1-stride)/2; // "same" padding
		quantizers.TryGetValue(weight.tensor, out var quantizer);
		Assert.IsTrue(!permuters.ContainsKey(weight.tensor), "permuter not supported");
		for(int orig = kernel_size; !System.Enum.TryParse<ShaderOps.ConvKeyword>($"K{kernel_size}_S{stride}", out _);)
			if(kernel_size > 1 && kernel_size%4 != 0)
				kernel_size += 1; // we assume kernel is aligned in weight
			else {
				if(orig <= 16)
					Debug.LogWarning($"Conv1d(kernel_size:{orig}, stride:{stride}) falls back to generic implementation");
				return Conv1dGeneric(input, weight, bias, orig, stride, dilation:dilation, groups:groups, padding:padding.Value, padding_mode:padding_mode, quantizer:quantizer);
			}
		if((kernel_size == 1 || dilation == 1) && padding_mode == 0)
			return ops.Conv1d(input, weight, bias, kernel_size, stride, padding:padding.Value, groups:groups, quantizer:quantizer);

		var osize0 = weight.size0;
		var dsize1 = ((input.size1-1)/(stride*dilation)+1)*stride + (kernel_size-stride+3)/4;
		var output = default(Tensor);
		for(int i=0; i<dilation; i++) {
			// process each slice separately to save vram
			var dilated = ops.Strided(input,
				new(input.size0, 1, 1), new(WideSize1(dsize1)*4, 1, 1),
				new(1,           0, 0), new(dilation,            0, 0, stride*i-padding.Value), padding_mode:padding_mode);
			dilated = ~ctx|ops.Conv1d(~dilated, weight, bias, kernel_size, stride, groups:groups, quantizer:quantizer);
			output = ~ctx|ops.Strided(~dilated,
				new(-1, 1, 1),            new(-1, 1, dilation),
				new(1,  0, 0, -i*osize0), new(1,  0, -osize0), add:i==0?default:output,
				@out:i==0?ctx.GPUTensor(osize0, WideSize1((input.size1+stride-1)/stride), dtype:input.dtype):output);
		}
		return output;
	}

	public Tensor ConvTranspose1d(TensorView input, TensorView weight, TensorView bias=default, int kernel_size=1, int stride=1, int groups=1, int? padding=null) {
		padding ??= (kernel_size-stride)/2; // "same" padding
		quantizers.TryGetValue(weight.tensor, out var quantizer);
		Assert.IsTrue(!permuters.ContainsKey(weight.tensor), "permuter not supported");
		for(int orig = kernel_size; !System.Enum.TryParse<ShaderOps.ConvKeyword>($"K{kernel_size}_S{stride}", out _);)
			if(kernel_size > 1 && kernel_size%4 != 0)
				kernel_size += 1; // we assume kernel is aligned in weight
			else {
				if(orig <= 16)
					Debug.LogWarning($"ConvTranspose1d(kernel_size:{orig}, stride:{stride}) falls back to generic implementation");
				return ConvTranspose1dGeneric(input, weight, bias, orig, stride, groups:groups, padding:padding.Value, quantizer:quantizer);
			}
		return ops.ConvTranspose1d(input, weight, bias, kernel_size, stride, padding:padding.Value, groups:groups, quantizer:quantizer);
	}
	private Tensor ConvTranspose1dGeneric(TensorView input, TensorView weight, TensorView bias=default, int kernel_size=1,
			int stride=1, int groups=1, int padding=0, Tensor quantizer=default) {
		int phases;
		for(phases=stride; phases>0; phases--)
			if(stride%phases == 0 && kernel_size%phases == 0
				&& System.Enum.TryParse<ShaderOps.ConvKeyword>($"K{kernel_size/phases}_S{stride/phases}", out _))
					break;
		if(phases <= 0 || quantizer)
			throw new System.ArgumentException(
				$"ConvTranspose1d(kernel_size:{kernel_size}, stride:{stride}) not supported");

		var wdim = (kernel_size+3)/4;
		var ocnt = kernel_size == 1 ? weight.size1*4 : weight.size1/wdim;
		var weight2 = ops.Strided(weight,
			new(weight.size0, 1, 1), new(phases, ocnt, kernel_size == phases ? 1 : (kernel_size/phases+3)/4*4),
			new(1, 0, 0), new(1, kernel_size == 1 ? 1 : wdim*4, phases));
		var bias2 = !bias ? default : ops.Strided(bias,
			new(bias.size0, kernel_size == phases ? groups : 1, 1), new(kernel_size == phases ? 1 : groups, phases, ocnt),
			new(1, -ocnt, 0), new(ocnt, 0, 1));
		if(bias && kernel_size == phases)
			bias2 = ~ctx|ops.Strided(~bias2,
				new(bias.size0, 1, 1), new(1, groups, (phases*ocnt+3)/4*4),
				new(groups, 0, 0), new(1, -1, 1));
		var phased = ~ctx|ops.ConvTranspose1d(input, ~weight2, ~bias2, kernel_size/phases,
			stride/phases, padding:padding/phases, groups:groups, output_padding:padding%phases == 0 ? 0 : 1);
		var output = ~ctx|ops.Strided(~phased,
			new(groups, ocnt, 1), new(phased.size1*4, phases, 1),
			new(kernel_size == phases ? (phases*ocnt+3)/4*4 : phases*ocnt, 1, 0), new(1, -ocnt, 0));
		return padding%phases == 0 ? output : ~ctx|ops.Strided(~output,
			new(output.size0, 1, 1), new(input.size1*4*stride, 1, 1),
			new(1, 0, 0), new(1, 0, 0, padding%phases));
	}

	public (TensorView real, TensorView imag) STFT(TensorView input, int n_fft, int hop_length, FFTWindow window, int? padding=null) {
		Assert.IsTrue(n_fft%4 == 0, "n_fft must be multiple of 4");
		padding ??= n_fft/2; // "center" padding
		var kernel = ctx.CPUTensor(n_fft+2, n_fft/4);
		kernel.SetData(STFTKernel(window, n_fft));
		var output = ~ctx|Conv1d(input, ~kernel, kernel_size:n_fft, stride:hop_length, padding:padding.Value, padding_mode:PaddingMode.Reflect);
		return output.Chunk2(axis:0);
	}
	public Tensor ISTFT((TensorView real, TensorView imag) input, int n_fft, int hop_length, FFTWindow window, int? padding=null) {
		Assert.IsTrue(n_fft%4 == 0, "n_fft must be multiple of 4");
		padding ??= n_fft/2; // "center" padding
		var kernel_weight = ctx.CPUTensor(n_fft+3, n_fft/4);
		kernel_weight.SetData(STFTKernel(window, n_fft, inverse:true));
		var (kernel, weight) = kernel_weight.Split((n_fft+2, 1), axis:0);
		var norm = ~ctx|ConvTranspose1d(~FullLike(input.real[..1, ..], 1), weight, kernel_size:n_fft, stride:hop_length, padding:padding.Value);
		var reim = Concat((input.real, input.imag), axis:0);
		var output = ~ctx|ConvTranspose1d(~reim, ~kernel, kernel_size:n_fft, stride:hop_length, padding:padding.Value);
		output = ~ctx|Div(~output, ~norm);
		return output;
	}
	static float[] STFTKernel(FFTWindow window, int n_fft, bool inverse=false) {
		var coef = FFTWindowCoefficient[window];
		var half = n_fft/2;
		var weight = new float[(n_fft+(inverse?3:2))*n_fft];
		for(int w=0; w<=half; w++)
		for(int k=0; k<n_fft; k++) {
			var theta = -2*System.Math.PI*k/n_fft;
			var v = coef.x + coef.y * System.Math.Cos(theta) + coef.z * System.Math.Cos(2*theta);
			weight[(w+half+1)*n_fft+k] = (float)(System.Math.Sin(theta*w) * (!inverse ? v : v/half));
			weight[w*n_fft+k] = (float)(System.Math.Cos(theta*w) * (!inverse ? v : w==0 || w==half ? v/n_fft : v/half));
			if(inverse && w == 0)
				weight[(n_fft+2)*n_fft+k] = (float)(v*v); // squared window for istft normalization
		}
		return weight;
	}
	public static readonly Dictionary<FFTWindow, Vector3> FFTWindowCoefficient = new() {
		{ FFTWindow.Hamming,  new Vector3(0.54f, -0.46f, 0f) },
		{ FFTWindow.Hanning,  new Vector3(0.5f, -0.5f, 0f) },
		{ FFTWindow.Blackman, new Vector3(0.42f, -0.5f, 0.08f) },
	};
}
}