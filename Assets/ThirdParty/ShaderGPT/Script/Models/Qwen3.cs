using System.Diagnostics.CodeAnalysis;

namespace ShaderGPT.Models {
[System.Serializable]
public class Qwen3Config : LlamaConfig {
	public string[] layer_types;
}
[RegisterModel(typeof(Qwen3Config), "qwen3")]
[ParityTolerance(nameof(CausalLMOutput.hidden_states), 2e-4f)] // Qwen3-0.6B-Q8_0
[ParityTolerance(nameof(CausalLMOutput.logits), 4e-5f)]
public class Qwen3 : Llama {
	public Qwen3(Functional fn, Qwen3Config config): base(fn, config) {}
	public new Qwen3Config config => (Qwen3Config)base.config;

	Tensor Qwen3Attention(string path, Tensor hidden_states, TensorView input_ids, Tensor position_embeddings, Cache past_key_values, Tensor residual, LinearProlog prolog=default) {
		var is_sliding = config.layer_types[LayerIdx(path)] == "sliding_attention";
		return BaseAttention(path, hidden_states, input_ids, position_embeddings, past_key_values, residual, prolog:prolog,
			q_norm:"q_norm", k_norm:"k_norm", sliding_window:is_sliding?config.sliding_window:null);
	}
	protected override Tensor LlamaAttention(string path, Tensor hidden_states, TensorView input_ids, Tensor position_embeddings, Cache past_key_values, Tensor residual, LinearProlog prolog=default)
		=> Qwen3Attention(path, hidden_states, input_ids, position_embeddings, past_key_values, residual, prolog);
	[ExcludeFromCodeCoverage]
	public Tensor Qwen3Model(string path, TensorView input_ids, Cache past_key_values)
		=> LlamaModel(path, input_ids, past_key_values);
	[ExcludeFromCodeCoverage]
	public CausalLMOutput Qwen3ForCausalLM(string path, TensorView input_ids, Cache past_key_values)
		=> LlamaForCausalLM(path, input_ids, past_key_values);
}
}