using System.Diagnostics.CodeAnalysis;

namespace ShaderGPT.Models {
[System.Serializable]
public class GPTNeoXConfig : LlamaConfig {
	protected override void FromPretrained(string json) {
		base.FromPretrained(json);
		num_key_value_heads = num_attention_heads;
	}
}
[RegisterModel(typeof(GPTNeoXConfig), "gpt_neox")]
[ParityTolerance(nameof(CausalLMOutput.hidden_states), 8e-3f)] // pythia-70m-deduped
[ParityTolerance(nameof(CausalLMOutput.logits), 6e-3f)]
public class GPTNeoX : Llama {
	public GPTNeoX(Functional fn, GPTNeoXConfig config): base(fn, config) {}
	public new GPTNeoXConfig config => (GPTNeoXConfig)base.config;

	Tensor GPTNeoXAttention(string path, Tensor hidden_states, TensorView input_ids, Tensor position_embeddings, Cache past_key_values, Tensor residual) {
		return BaseAttention(path, hidden_states, input_ids, position_embeddings, past_key_values, residual,
			qkv_proj:"query_key_value", stable:true, o_proj:"dense");
	}
	Tensor GPTNeoXMLP(string path, ref Tensor hidden_states, Tensor residual) {
		hidden_states = ~ctx|nn.Linear($"{path}.dense_h_to_4h", ~hidden_states);
		hidden_states = ~ctx|nn.Linear($"{path}.dense_4h_to_h", ~hidden_states, prolog:config.hidden_act, add:residual);
		return hidden_states;
	}
	void GPTNeoXLayer(string path, ref Tensor hidden_states, TensorView input_ids, Tensor position_embeddings, Cache past_key_values) {
		var attn_states = nn.LayerNorm($"{path}.input_layernorm", hidden_states, config.layer_norm_eps);
		var mlp_states = nn.LayerNorm($"{path}.post_attention_layernorm", hidden_states, config.layer_norm_eps);
		hidden_states = ~ctx|GPTNeoXAttention($"{path}.attention", ~attn_states, input_ids, position_embeddings, past_key_values, residual:~hidden_states);
		hidden_states = ~ctx|GPTNeoXMLP($"{path}.mlp", ref mlp_states, residual:~hidden_states);
	}
	protected override void LlamaDecoderLayer(string path, ref Tensor hidden_states, TensorView input_ids, Tensor position_embeddings, Cache past_key_values)
		=> GPTNeoXLayer(path, ref hidden_states, input_ids, position_embeddings, past_key_values);
	protected override string LlamaModel_embed_tokens => "embed_in";
	protected override string LlamaModel_norm => "final_layer_norm";
	[ExcludeFromCodeCoverage]
	public Tensor GPTNeoXModel(string path, TensorView input_ids, Cache past_key_values)
		=> LlamaModel(path, input_ids, past_key_values);
	protected override string LlamaForCausalLM_model => "gpt_neox";
	[ExcludeFromCodeCoverage]
	public CausalLMOutput GPTNeoXForCausalLM(string path, TensorView input_ids, Cache past_key_values)
		=> LlamaForCausalLM(path, input_ids, past_key_values);
}
}