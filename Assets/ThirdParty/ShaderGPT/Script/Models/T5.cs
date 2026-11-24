using System.Text.RegularExpressions;

namespace ShaderGPT.Models {
[System.Serializable]
public class T5Config : PretrainedConfig {
	public int d_model;
	public int num_layers;
	public int num_heads;
	public int relative_attention_max_distance;
	public float layer_norm_epsilon;
	public string dense_act_fn;
	public bool is_gated_act;
	public bool scale_decoder_outputs;

	public int hidden_size => d_model;
	public int num_attention_heads => num_heads;
	public int num_hidden_layers => num_layers;
	public string hidden_act => dense_act_fn;
	public float rms_norm_eps => layer_norm_epsilon;
}
[RegisterModel(typeof(T5Config), "t5")]
[ParityTolerance(nameof(Seq2SeqLMOutput.encoder_hidden_states), 1e-5f)] // flan-t5-small
[ParityTolerance(nameof(CausalLMOutput.hidden_states), 3e-5f)]
[ParityTolerance(nameof(CausalLMOutput.logits), 7e-5f)]
public class T5 : ModelForSeq2SeqLM {
	public T5(Functional fn, T5Config config): base(fn, config) {}
	public new T5Config config => (T5Config)base.config;

	Tensor T5Attention(string path, Tensor hidden_states, TensorView input_ids, Cache past_key_values,
			Tensor key_value_states=default, bool encoder=false, bool cross=false, Tensor residual=default, LinearProlog prolog=default) {
		using var _ = ~ctx;
		TensorView q=default, k=default, v=default;
		if(cross) {
			if(key_value_states)
				(k,v) = nn.Linear(($"{path}.k", $"{path}.v"), key_value_states);
			if(hidden_states)
				q = ~nn.Linear($"{path}.q", hidden_states, prolog:prolog);
		} else
			(q,k,v) = nn.Linear(($"{path}.q", $"{path}.k", $"{path}.v"), hidden_states, prolog:prolog);
		var keys   = past_key_values?.CrossUpdate($"{path}.k", k,  (input_ids, 1), cross, !(cross?hidden_states:encoder)) ?? k;
		var values = past_key_values?.CrossUpdate($"{path}.v", ~v, (input_ids, 1), cross, !(cross?hidden_states:encoder)) ?? ~v;
		if(!hidden_states)
			return hidden_states;

		var window_size = config.relative_attention_max_distance;
		var position_bias = cross ? null : fn.Narrow(
			state_dict[Regex.Replace($"{path}.relative_attention_bias.weight.T", @"[.]\d+[.]", ".0.")],
			window:new(window_size, window_size+keys.size0, -1, (input_ids, 1)),
			groups:config.num_attention_heads, size0:q.size0, padding_mode:PaddingMode.Replicate);
		hidden_states = ~ctx|fn.ScaledDotProductAttention(q, keys, values,
			heads:config.num_attention_heads, scale:1f, attn_mask:position_bias?(~position_bias):null,
			window:!encoder&&!cross ? new(1-int.MaxValue, 1, 1, (input_ids, 1)) : new(-int.MaxValue, 0, 1, (input_ids, 2)));
		hidden_states = ~ctx|nn.Linear($"{path}.o", ~hidden_states, add:residual);
		return hidden_states;
	}
	Tensor T5DenseActDense(string path, Tensor hidden_states, Tensor residual, LinearProlog prolog) {
		var (gate,up) = config.is_gated_act
			? nn.Linear(($"{path}.wi_0", $"{path}.wi_1"), hidden_states, prolog:prolog)
			: (nn.Linear($"{path}.wi", hidden_states, prolog:prolog), default);
		hidden_states = ~ctx|nn.Linear($"{path}.wo", ~gate, prolog:(config.hidden_act, up), add:residual);
		return hidden_states;
	}
	void T5Block(string path, ref Tensor hidden_states, TensorView input_ids, Cache past_key_values, Tensor encoder_hidden_states=default, bool is_decoder=true) {
		if(is_decoder && !hidden_states) {
			T5Attention($"{path}.layer.1.EncDecAttention", hidden_states, input_ids, past_key_values, encoder_hidden_states, cross:true);
			return; // when hidden_states is null, only compute cross attn kv
		}
		var attn_prolog = nn.LayerNormProlog($"{path}.layer.0.layer_norm", config.rms_norm_eps, rms:true);
		hidden_states = ~ctx|T5Attention($"{path}.layer.0.SelfAttention", hidden_states, input_ids, past_key_values,
			encoder:!is_decoder, residual:~hidden_states, prolog:attn_prolog);
		if(is_decoder) {
			attn_prolog = nn.LayerNormProlog($"{path}.layer.1.layer_norm", config.rms_norm_eps, rms:true);
			hidden_states = ~ctx|T5Attention($"{path}.layer.1.EncDecAttention", hidden_states, input_ids, past_key_values,
				encoder_hidden_states, cross:true, residual:~hidden_states, prolog:attn_prolog);
		}
		var mlp_prolog = nn.LayerNormProlog($"{path}.layer.{(is_decoder?2:1)}.layer_norm", config.rms_norm_eps, rms:true);
		hidden_states = ~ctx|T5DenseActDense($"{path}.layer.{(is_decoder?2:1)}.DenseReluDense", hidden_states, residual:~hidden_states, prolog:mlp_prolog);
	}
	Tensor T5Stack(string path, TensorView input_ids, Cache past_key_values, Tensor encoder_hidden_states=default, bool is_decoder=true) {
		var hidden_states = !input_ids ? null : nn.Embedding($"{path}.embed_tokens", (input_ids, 0), fallback:"shared");
		for(int i=0; i<config.num_hidden_layers; i++)
			T5Block($"{path}.block.{i}", ref hidden_states, input_ids, past_key_values, encoder_hidden_states, is_decoder);
		hidden_states = !input_ids ? null : ~ctx|nn.LayerNorm($"{path}.final_layer_norm", ~hidden_states, config.rms_norm_eps, rms:true);
		return hidden_states;
	}
	public Seq2SeqLMOutput T5ForConditionalGeneration(string path, TensorView input_ids, TensorView decoder_input_ids, Cache past_key_values, Tensor encoder_hidden_states=default) {
		var path_ = path == null ? "" : path + ".";
		if(!encoder_hidden_states && input_ids)
			encoder_hidden_states = T5Stack($"{path_}encoder", input_ids, past_key_values, is_decoder:false);
		var decoder_hidden_states = T5Stack($"{path_}decoder", decoder_input_ids, past_key_values, encoder_hidden_states:encoder_hidden_states);
		var logits = !decoder_input_ids ? null : ~ctx|nn.Linear($"{path_}lm_head", decoder_hidden_states, fallback:$"{path_}shared",
			cmul:config.scale_decoder_outputs ? 1f/System.MathF.Sqrt(config.hidden_size) : 1f);
		return new Seq2SeqLMOutput{logits=logits, decoder_hidden_states=decoder_hidden_states,
			encoder_hidden_states=encoder_hidden_states};
	}
	public override Seq2SeqLMOutput ForSeq2SeqLM(TensorView input_ids, TensorView decoder_input_ids, Cache past_key_values, Tensor encoder_hidden_states=default)
		=> T5ForConditionalGeneration(null, input_ids, decoder_input_ids, past_key_values, encoder_hidden_states);
}
}