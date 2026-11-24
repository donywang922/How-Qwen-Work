using UnityEngine;

namespace ShaderGPT.Models {
public partial class Vits {
	void VitsWaveNet(string path, ref Tensor inputs, Window padding_mask, TensorView global_conditioning) {
		using var _ = ~ctx;
		var num_layers = config.prior_encoder_num_wavenet_layers;
		var outputs = fn.ZerosLike(inputs);
		if(global_conditioning)
			global_conditioning = ~nn.Linear($"{path}.cond_layer", global_conditioning);
		for(int i=0; i<num_layers; i++) {
			var hidden_states = Conv1d_Add($"{path}.in_layers.{i}", inputs, config.wavenet_kernel_size,
				dilation:(int)Mathf.Pow(config.wavenet_dilation_rate, i),
				add:global_conditioning?global_conditioning.Split(config.hidden_size/2, i, axis:1):default);

			var (half0, half1) = hidden_states.Chunk2(axis:0);
			var acts = ~ctx|fn.Mul(~fn.Tanh(half0), ~fn.Sigmoid(~half1), window:padding_mask);

			var res_skip_acts = ~ctx|nn.Conv1d($"{path}.res_skip_layers.{i}", ~acts, 1);
			res_skip_acts = ~ctx|fn.Mul(~res_skip_acts, 1f, window:padding_mask);
			if(i < num_layers-1) {
				(half0, half1) = res_skip_acts.Chunk2(axis:0);
				inputs  = ~ctx|fn.Add(~inputs, half0);
				outputs = ~ctx|fn.Add(~outputs, ~half1);
			} else
				outputs = ~ctx|fn.Add(~outputs, ~res_skip_acts);
		}
		inputs = (~inputs, outputs).Item2;
	}
	void VitsResidualCouplingLayer(string path, ref Tensor inputs, Window padding_mask, TensorView global_conditioning, bool reverse) {
		var (half0, half1) = inputs.Chunk2(axis:0);
		var hidden_states = nn.Conv1d($"{path}.conv_pre", half0, 1);
		hidden_states = ~ctx|fn.Mul(~hidden_states, 1f, window:padding_mask);
		VitsWaveNet($"{path}.wavenet", ref hidden_states, padding_mask, global_conditioning);
		var mean = ~ctx|nn.Conv1d($"{path}.conv_post", ~hidden_states, 1);
		_ = ~ctx|fn.Add_(half1, ~mean, reverse?-1:+1, window:padding_mask);
	}
	void VitsResidualCouplingBlock(string path, ref Tensor inputs, Window padding_mask, TensorView global_conditioning, bool reverse) {
		inputs = ~ctx|fn.Mul(~inputs, 1f, window:padding_mask); // truncate input
		for(int i=0; i<config.prior_encoder_num_flows; i++) {
			inputs = !reverse ? inputs : ~ctx|fn.Flip(~inputs, axis:0);
			VitsResidualCouplingLayer($"{path}.flows.{(reverse?config.prior_encoder_num_flows-1-i:i)}", ref inputs, padding_mask, global_conditioning, reverse);
			inputs =  reverse ? inputs : ~ctx|fn.Flip(~inputs, axis:0);
		}
	}

	Tensor Conv1d_Add(string path, TensorView input, int kernel_size, int stride=1, int dilation=1, TensorView add=default) {
		var bias = state_dict[$"{path}.bias"];
		return ~ctx|fn.Conv1d(input, state_dict[$"{path}.weight"],
			~fn.Add(bias, add), kernel_size, stride:stride, dilation:dilation);
	}
}
}