using System.Diagnostics.CodeAnalysis;

namespace ShaderGPT.Models {
[System.Serializable]
public class PhiConfig : LlamaConfig {}
[RegisterModel(typeof(PhiConfig), "phi")]
[ParityTolerance(nameof(CausalLMOutput.hidden_states), 1e-5f)] // phi-2-tiny-random
[ParityTolerance(nameof(CausalLMOutput.logits), 1e-5f)]
public class Phi : Llama {
	public Phi(Functional fn, PhiConfig config): base(fn, config) {}
	public new PhiConfig config => (PhiConfig)base.config;

	Tensor PhiAttention(string path, Tensor hidden_states, TensorView input_ids, Tensor position_embeddings, Cache past_key_values, Tensor residual) {
		return BaseAttention(path, hidden_states, input_ids, position_embeddings, past_key_values, residual, stable:true, o_proj:"dense");
	}
	Tensor PhiMLP(string path, ref Tensor hidden_states, Tensor residual) {
		hidden_states = ~ctx|nn.Linear($"{path}.fc1", ~hidden_states);
		hidden_states = ~ctx|nn.Linear($"{path}.fc2", ~hidden_states, prolog:config.hidden_act, add:residual);
		return hidden_states;
	}
	void PhiDecoderLayer(string path, ref Tensor hidden_states, TensorView input_ids, Tensor position_embeddings, Cache past_key_values) {
		var attn_states = nn.LayerNorm($"{path}.input_layernorm", hidden_states, config.layer_norm_eps);
		hidden_states = ~ctx|PhiAttention($"{path}.self_attn", attn_states, input_ids, position_embeddings, past_key_values, residual:~hidden_states);
		hidden_states = ~ctx|PhiMLP($"{path}.mlp", ref attn_states, residual:~hidden_states);
	}
	protected override void LlamaDecoderLayer(string path, ref Tensor hidden_states, TensorView input_ids, Tensor position_embeddings, Cache past_key_values)
		=> PhiDecoderLayer(path, ref hidden_states, input_ids, position_embeddings, past_key_values);
	protected override string LlamaModel_norm => "final_layernorm";
	[ExcludeFromCodeCoverage]
	public Tensor PhiModel(string path, TensorView input_ids, Cache past_key_values)
		=> LlamaModel(path, input_ids, past_key_values);
	[ExcludeFromCodeCoverage]
	public CausalLMOutput PhiForCausalLM(string path, TensorView input_ids, Cache past_key_values)
		=> LlamaForCausalLM(path, input_ids, past_key_values);
}
}