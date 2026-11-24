using UnityEngine;

namespace ShaderGPT.Models {
public partial class Vits {
	Tensor VitsAttention(string path, Tensor hidden_states, Window padding_mask, TensorView input_ids, Tensor residual) {
		var window_size = config.window_size;
		var size0 = hidden_states.size0;
		var (q,k,v) = nn.Linear(($"{path}.q_proj", $"{path}.k_proj", $"{path}.v_proj"), hidden_states);

		var norm_factor = 1f / Mathf.Sqrt(config.hidden_size / config.num_attention_heads);
		var attn_weights = fn.Linear(q, k, heads:config.num_attention_heads, cmul:norm_factor);
		if(window_size > 0) {
			var relative_logits = fn.Linear(q, state_dict[$"{path}.emb_rel_k.weight"], heads:config.num_attention_heads, weightHeads:1, cmul:norm_factor);
			var rel_pos_bias = ~ctx|fn.Narrow(~relative_logits,
				window:new(window_size, window_size+size0, -1, (input_ids, 1)), groups:config.num_attention_heads);
			Debug.Assert(rel_pos_bias.size1 == attn_weights.size1);
			// NOTE: rel_pos_bias will be truncated at padding_mask later in attn_probs
			_ = ~ctx|fn.Add_(attn_weights, ~rel_pos_bias);
		}
		var attn_probs = ~ctx|fn.Softmax(~attn_weights, window:padding_mask, groups:config.num_attention_heads);

		hidden_states = ~ctx|fn.Linear(attn_probs, ~v, weightT:true, heads:config.num_attention_heads);
		if(window_size > 0) {
			var relative_weights = ~ctx|fn.Narrow(~attn_probs,
				window:new(-window_size, window_size+1, 1, (input_ids, 1)), groups:config.num_attention_heads);
			Debug.Assert(relative_weights.size1 == (window_size/2+1)*config.num_attention_heads);
			var rel_pos_bias = ~ctx|fn.Linear(~relative_weights, state_dict[$"{path}.emb_rel_v.weight"], weightT:true, heads:config.num_attention_heads, weightHeads:1);
			_ = ~ctx|fn.Add_(hidden_states, ~rel_pos_bias);
		} else
			ctx.Release(attn_probs);

		hidden_states = ~ctx|nn.Linear($"{path}.out_proj", ~hidden_states, add:residual);
		return hidden_states;
	}
	Tensor VitsFeedForward(string path, Tensor hidden_states, Window padding_mask, Tensor residual) {
		var size0 = hidden_states.size0;
		hidden_states = fn.Transpose(hidden_states);
		hidden_states = ~ctx|fn.Mul(~hidden_states, 1f, window:padding_mask); // truncate input
		hidden_states = ~ctx|nn.Conv1d($"{path}.conv_1", ~hidden_states, config.ffn_kernel_size);
		hidden_states = ~ctx|fn.Act2Fn(config.hidden_act, ~hidden_states, window:padding_mask);
		hidden_states = ~ctx|nn.Conv1d($"{path}.conv_2", ~hidden_states, config.ffn_kernel_size);
		hidden_states = ~ctx|fn.Transpose(~hidden_states, size0:size0);
		hidden_states = ~ctx|fn.Add(~hidden_states, residual);
		return hidden_states;
	}
	void VitsEncoderLayer(string path, ref Tensor hidden_states, Window padding_mask, TensorView input_ids) {
		hidden_states = ~ctx|VitsAttention($"{path}.attention", hidden_states, padding_mask, input_ids, residual:~hidden_states);
		hidden_states = ~ctx|nn.LayerNorm($"{path}.layer_norm", ~hidden_states, config.layer_norm_eps);
		hidden_states = ~ctx|VitsFeedForward($"{path}.feed_forward", hidden_states, padding_mask, residual:~hidden_states);
		hidden_states = ~ctx|nn.LayerNorm($"{path}.final_layer_norm", ~hidden_states, config.layer_norm_eps);
	}
	(Tensor, Tensor) VitsTextEncoder(string path, TensorView input_ids, Window padding_mask) {
		var inputs_embeds = nn.Embedding($"{path}.embed_tokens", (input_ids, 0));
		var hidden_states = ~ctx|fn.Mul(~inputs_embeds, Mathf.Sqrt(config.hidden_size));
		for(int i=0; i<config.num_hidden_layers; i++)
			VitsEncoderLayer($"{path}.encoder.layers.{i}", ref hidden_states, padding_mask, input_ids);
		var stats = nn.Linear($"{path}.project", hidden_states);
		return (hidden_states, stats);
	}
}
}