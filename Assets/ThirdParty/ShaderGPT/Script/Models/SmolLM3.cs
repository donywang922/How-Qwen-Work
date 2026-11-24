using System.Diagnostics.CodeAnalysis;

namespace ShaderGPT.Models {
[System.Serializable]
public class SmolLM3Config : LlamaConfig {
	public int[] no_rope_layers;
	public string[] layer_types;
}
[RegisterModel(typeof(SmolLM3Config), "smollm3")]
[ParityTolerance(nameof(CausalLMOutput.hidden_states), 1e-5f)] // smollm3-tiny-random
[ParityTolerance(nameof(CausalLMOutput.logits), 1e-5f)]
public class SmolLM3 : Llama {
	public SmolLM3(Functional fn, SmolLM3Config config): base(fn, config) {}
	public new SmolLM3Config config => (SmolLM3Config)base.config;

	Tensor SmolLM3Attention(string path, Tensor hidden_states, TensorView input_ids, Tensor position_embeddings, Cache past_key_values, Tensor residual, LinearProlog prolog=default) {
		var use_rope = config.no_rope_layers[LayerIdx(path)] != 0;
		var is_sliding = config.layer_types[LayerIdx(path)] == "sliding_attention";
		return BaseAttention(path, hidden_states, input_ids, use_rope?position_embeddings:null, past_key_values, residual, prolog:prolog,
			sliding_window:is_sliding?config.sliding_window:null);
	}
	protected override Tensor LlamaAttention(string path, Tensor hidden_states, TensorView input_ids, Tensor position_embeddings, Cache past_key_values, Tensor residual, LinearProlog prolog=default)
		=> SmolLM3Attention(path, hidden_states, input_ids, position_embeddings, past_key_values, residual, prolog);
	[ExcludeFromCodeCoverage]
	public Tensor SmolLM3Model(string path, TensorView input_ids, Cache past_key_values)
		=> LlamaModel(path, input_ids, past_key_values);
	[ExcludeFromCodeCoverage]
	public CausalLMOutput SmolLM3ForCausalLM(string path, TensorView input_ids, Cache past_key_values)
		=> LlamaForCausalLM(path, input_ids, past_key_values);
}
}