using System.Diagnostics.CodeAnalysis;

namespace ShaderGPT.Models {
[System.Serializable]
public class GPTNeoConfig : PretrainedConfig {
	public int hidden_size;
	public int num_layers;
	public int num_heads;
	public int window_size;
	public string activation_function;
	public float layer_norm_epsilon;
	public string[] attention_layers;

	public int num_attention_heads => num_heads;
	public int num_hidden_layers => num_layers;
	public string hidden_act => activation_function;
	public float layer_norm_eps => layer_norm_epsilon;
}
[RegisterModel(typeof(GPTNeoConfig), "gpt_neo")]
[ParityTolerance(nameof(CausalLMOutput.hidden_states), 2e-4f)] // gpt-neo-125m
[ParityTolerance(nameof(CausalLMOutput.logits), 2e-4f)]
public class GPTNeo : ModelForCausalLM {
	public GPTNeo(Functional fn, GPTNeoConfig config): base(fn, config) {}
	public new GPTNeoConfig config => (GPTNeoConfig)base.config;

	Tensor GPTNeoSelfAttention(string path, ref Tensor hidden_states, TensorView input_ids, [NotNull] Cache past_key_values, Tensor residual) {
		var (q,k,v) = ~ctx|nn.Linear(($"{path}.q_proj", $"{path}.k_proj", $"{path}.v_proj"), ~hidden_states);
		var keys   = past_key_values.Update($"{path}.k", k, (input_ids, 1));
		var values = past_key_values.Update($"{path}.v", v, (input_ids, 1));

		var sliding_window = config.attention_layers[LayerIdx(path)] == "local" ? config.window_size : int.MaxValue;
		hidden_states = ~ctx|fn.ScaledDotProductAttention(~q, keys, values,
			heads:config.num_attention_heads, scale:1f,
			window:new(1-sliding_window, 1, 1, (input_ids, 1)));
		hidden_states = ~ctx|nn.Linear($"{path}.out_proj", ~hidden_states, add:residual);
		return hidden_states;
	}
	protected Tensor GPTNeoMLP(string path, ref Tensor hidden_states, Tensor residual) {
		hidden_states = ~ctx|nn.Linear($"{path}.c_fc", ~hidden_states);
		hidden_states = ~ctx|nn.Linear($"{path}.c_proj", ~hidden_states, prolog:config.hidden_act, add:residual);
		return hidden_states;
	}
	protected virtual void GPTNeoBlock(string path, ref Tensor hidden_states, TensorView input_ids, [NotNull] Cache past_key_values) {
		var attn_states = nn.LayerNorm($"{path}.ln_1", hidden_states, config.layer_norm_eps);
		hidden_states = ~ctx|GPTNeoSelfAttention($"{path}.attn.attention", ref attn_states, input_ids, past_key_values, residual:~hidden_states);
		var mlp_states = nn.LayerNorm($"{path}.ln_2", hidden_states, config.layer_norm_eps);
		hidden_states = ~ctx|GPTNeoMLP($"{path}.mlp", ref mlp_states, residual:~hidden_states);
	}
	public Tensor GPTNeoModel(string path, TensorView input_ids, Cache past_key_values) {
		var path_ = path == null ? "" : path + ".";
		var inputs_embeds   = nn.Embedding($"{path_}wte", (input_ids, 0));
		var position_embeds = nn.Embedding($"{path_}wpe", (input_ids, 1));
		var hidden_states   = ~ctx|fn.Add(~inputs_embeds, ~position_embeds);
		for(int i=0; i<config.num_hidden_layers; i++)
			GPTNeoBlock($"{path_}h.{i}", ref hidden_states, input_ids, past_key_values);
		hidden_states = ~ctx|nn.LayerNorm($"{path_}ln_f", ~hidden_states, config.layer_norm_eps);
		return hidden_states;
	}
	public CausalLMOutput GPTNeoForCausalLM(string path, TensorView input_ids, Cache past_key_values) {
		var path_ = path == null ? "" : path + ".";
		var hidden_states = GPTNeoModel($"{path_}transformer", input_ids, past_key_values);
		var logits = nn.Linear($"{path_}lm_head", hidden_states, fallback:$"{path_}transformer.wte");
		return new CausalLMOutput{logits=logits, hidden_states=hidden_states};
	}
	public override CausalLMOutput ForCausalLM(TensorView input_ids, Cache past_key_values)
		=> GPTNeoForCausalLM(null, input_ids, past_key_values);
}
}