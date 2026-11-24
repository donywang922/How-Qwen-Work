using System.Diagnostics.CodeAnalysis;

namespace ShaderGPT.Models {
public class VitsDecoder : PretrainedModel {
	public int upsample_rate = 1;
	public VitsDecoder(Functional fn, [NotNull] VitsConfig config): base(fn, config) {
		foreach(var x in config.upsample_rates)
			upsample_rate *= x;
	}
	public new VitsConfig config => (VitsConfig)base.config;

	int num_kernels => config.resblock_kernel_sizes.Length;
	int num_dilations => config.resblock_dilation_sizes.Length / config.resblock_kernel_sizes.Length;
	int num_upsamples => config.upsample_rates.Length;

	Tensor HifiGanResidualBlock(string path, Tensor hidden_states, Window padding_mask, int block_id) {
		var kernel_size = config.resblock_kernel_sizes[block_id];
		for(int i=0; i<num_dilations; i++) {
			var dilation = config.resblock_dilation_sizes[block_id*num_dilations+i];
			var residual = hidden_states;
			hidden_states = fn.LeakyRelu(hidden_states, config.leaky_relu_slope);
			hidden_states = ~ctx|nn.Conv1d($"{path}.convs1.{i}", ~hidden_states, kernel_size, dilation:dilation);
			hidden_states = ~ctx|fn.LeakyRelu(~hidden_states, config.leaky_relu_slope, window:padding_mask);
			hidden_states = ~ctx|nn.Conv1d($"{path}.convs2.{i}", ~hidden_states, kernel_size);
			hidden_states = ~ctx|fn.Add(residual, ~hidden_states, 1f, window:padding_mask);
			if(i>0)
				ctx.Release(residual);
		}
		return hidden_states;
	}
	public Tensor VitsHifiGan(string path, TensorView spectrogram, Window padding_mask, TensorView global_conditioning) {
		var hidden_states = fn.Mul(spectrogram, 1f, window:padding_mask); // truncate input
		hidden_states = ~ctx|Conv1d_Add($"{path}.conv_pre", ~hidden_states, 7,
			add:global_conditioning?(~nn.Linear($"{path}.cond", global_conditioning)):default);
		hidden_states = ~ctx|fn.Mul(~hidden_states, 1f, window:padding_mask);
		for(int i=0; i<num_upsamples; i++) {
			hidden_states = ~ctx|fn.LeakyRelu(~hidden_states, config.leaky_relu_slope);
			hidden_states = ~ctx|nn.ConvTranspose1d($"{path}.upsampler.{i}", ~hidden_states,
				config.upsample_kernel_sizes[i], stride:config.upsample_rates[i]);
			padding_mask = padding_mask.Scale(config.upsample_rates[i]);
			hidden_states = ~ctx|fn.Mul(~hidden_states, 1f, window:padding_mask);

			var res_state = default(Tensor);
			for(int j=0; j<num_kernels; j++) {
				var x = HifiGanResidualBlock($"{path}.resblocks.{i*num_kernels+j}", hidden_states, padding_mask, block_id:j);
				res_state = j == 0 ? x : ~ctx|fn.Add(~res_state, ~x);
			}
			ctx.Release(hidden_states);
			hidden_states = ~ctx|fn.Mul(~res_state, 1f/num_kernels);
		}
		hidden_states = ~ctx|fn.LeakyRelu(~hidden_states);
		hidden_states = ~ctx|nn.Conv1d($"{path}.conv_post", ~hidden_states, 7);
		var waveform = ~ctx|fn.Tanh(~hidden_states, window:padding_mask);
		return waveform;
	}

	Tensor Conv1d_Add(string path, TensorView input, int kernel_size, int stride=1, int dilation=1, TensorView add=default) {
		var bias = state_dict[$"{path}.bias"];
		return ~ctx|fn.Conv1d(input, state_dict[$"{path}.weight"],
			~fn.Add(bias, add), kernel_size, stride:stride, dilation:dilation);
	}
}
}