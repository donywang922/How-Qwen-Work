using System.Diagnostics.CodeAnalysis;

namespace ShaderGPT.Models {
[System.Serializable]
public class Phi3Config : LlamaConfig {}
[RegisterModel(typeof(Phi3Config), "phi3")]
[ParityTolerance(nameof(CausalLMOutput.hidden_states), 1e-5f)] // phi-4-tiny-random
[ParityTolerance(nameof(CausalLMOutput.logits), 1e-5f)]
public class Phi3 : Llama {
	public Phi3(Functional fn, Phi3Config config): base(fn, config) {}
	public new Phi3Config config => (Phi3Config)base.config;

	Tensor Phi3Attention(string path, Tensor hidden_states, TensorView input_ids, Tensor position_embeddings, Cache past_key_values, Tensor residual, LinearProlog prolog=default) {
		return BaseAttention(path, hidden_states, input_ids, position_embeddings, past_key_values, residual, prolog:prolog,
			qkv_proj:"qkv_proj", sliding_window:config.sliding_window>0?config.sliding_window:null, stable:true);
	}
	protected override Tensor LlamaAttention(string path, Tensor hidden_states, TensorView input_ids, Tensor position_embeddings, Cache past_key_values, Tensor residual, LinearProlog prolog=default)
		=> Phi3Attention(path, hidden_states, input_ids, position_embeddings, past_key_values, residual, prolog);
	Tensor Phi3MLP(string path, Tensor hidden_states, Tensor residual, LinearProlog prolog=default) {
		var (gate,up) = nn.Linear($"{path}.gate_up_proj", hidden_states, prolog:prolog).Chunk2(axis:1);
		hidden_states = ~ctx|nn.Linear($"{path}.down_proj", ~gate, prolog:(config.hidden_act, up), add:residual);
		return hidden_states;
	}
	protected override Tensor LlamaMLP(string path, Tensor hidden_states, Tensor residual, LinearProlog prolog=default)
		=> Phi3MLP(path, hidden_states, residual, prolog);
	[ExcludeFromCodeCoverage]
	public Tensor Phi3Model(string path, TensorView input_ids, Cache past_key_values)
		=> LlamaModel(path, input_ids, past_key_values);
	[ExcludeFromCodeCoverage]
	public CausalLMOutput Phi3ForCausalLM(string path, TensorView input_ids, Cache past_key_values)
		=> LlamaForCausalLM(path, input_ids, past_key_values);
}
}