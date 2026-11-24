using System.Diagnostics.CodeAnalysis;

namespace ShaderGPT.Models {
[System.Serializable]
public class GPT2Config : GPTNeoConfig {
	public int n_embd;
	public int n_layer;
	public int n_head;
	protected override void FromPretrained(string json) {
		base.FromPretrained(json);
		hidden_size = n_embd;
		num_heads = n_head;
		num_layers = n_layer;
	}
}
[RegisterModel(typeof(GPT2Config), "gpt2")]
[ParityTolerance(nameof(CausalLMOutput.hidden_states), 1e-4f)] // distilgpt2-Q8_0
[ParityTolerance(nameof(CausalLMOutput.logits), 2e-4f)]
public class GPT2 : GPTNeo {
	public GPT2(Functional fn, GPT2Config config): base(fn, config) {}
	public new GPT2Config config => (GPT2Config)base.config;

	Tensor GPT2Attention(string path, ref Tensor hidden_states, TensorView input_ids, Cache past_key_values, Tensor residual) {
		var (q,k,v) = ~ctx|nn.Linear($"{path}.c_attn", ~hidden_states).Chunk3(axis:1);
		var keys   = past_key_values.Update($"{path}.k", k, (input_ids, 1));
		var values = past_key_values.Update($"{path}.v", v, (input_ids, 1));

		var head_dim = config.hidden_size / config.num_attention_heads;
		hidden_states = ~ctx|fn.ScaledDotProductAttention(~q, keys, values,
			heads:config.num_attention_heads, scale:1f/System.MathF.Sqrt(head_dim),
			window:new(int.MinValue, 1, 1, (input_ids, 1)));
		hidden_states = ~ctx|nn.Linear($"{path}.c_proj", ~hidden_states, add:residual);
		return hidden_states;
	}
	void GPT2Block(string path, ref Tensor hidden_states, TensorView input_ids, Cache past_key_values) {
		var attn_states = nn.LayerNorm($"{path}.ln_1", hidden_states, config.layer_norm_eps);
		hidden_states = ~ctx|GPT2Attention($"{path}.attn", ref attn_states, input_ids, past_key_values, residual:~hidden_states);
		var mlp_states = nn.LayerNorm($"{path}.ln_2", hidden_states, config.layer_norm_eps);
		hidden_states = ~ctx|GPTNeoMLP($"{path}.mlp", ref mlp_states, residual:~hidden_states);
	}
	protected override void GPTNeoBlock(string path, ref Tensor hidden_states, TensorView input_ids, Cache past_key_values)
		=> GPT2Block(path, ref hidden_states, input_ids, past_key_values);
	[ExcludeFromCodeCoverage]
	public Tensor GPT2Model(string path, TensorView input_ids, Cache past_key_values)
		=> GPTNeoModel(path, input_ids, past_key_values);
	[ExcludeFromCodeCoverage]
	public CausalLMOutput GPT2LMHeadModel(TensorView input_ids, Cache past_key_values)
		=> GPTNeoForCausalLM(null, input_ids, past_key_values);
}
}