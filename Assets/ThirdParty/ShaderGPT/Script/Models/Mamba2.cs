using System.Diagnostics.CodeAnalysis;

namespace ShaderGPT.Models {
[System.Serializable]
public class Mamba2Config : LlamaConfig {
	public int num_heads;
	public int state_size;
	public int expand;
	public int n_groups;
	public float layer_norm_epsilon;
}
[RegisterModel(typeof(Mamba2Config), "mamba2")]
[ParityTolerance(nameof(CausalLMOutput.hidden_states), 8e-5f)] // mamba2-130m-hf
[ParityTolerance(nameof(CausalLMOutput.logits), 3e-4f)]
public class Mamba2 : MambaBase {
	public Mamba2(Functional fn, Mamba2Config config): base(fn, config) {}
	public new Mamba2Config config => (Mamba2Config)base.config;

	Tensor Mamba2Mixer(string path, Tensor hidden_states, TensorView input_ids, Cache cache_params, Tensor residual, LinearProlog prolog) {
		var intermediate_size = config.expand * config.hidden_size;
		var grouped_state_size = config.n_groups * config.state_size;
		var (gate, hidden_states_B_C, dt) = nn.Linear($"{path}.in_proj", hidden_states, prolog:prolog)
			.Split((intermediate_size/4, (intermediate_size + 2*grouped_state_size)/4, config.num_heads/4), axis:1);
		var conv_out = CausalConv1d($"{path}.conv1d", hidden_states_B_C, input_ids, cache_params);
		dt = fn.Act2Fn("softplus", dt); // interleave to hide latency
		var (hidden_part, B, C) = ~ctx|fn.Act2Fn(config.hidden_act, ~conv_out)
			.Split((intermediate_size/4, grouped_state_size/4, grouped_state_size/4), axis:1);

		hidden_states = ~ctx|SelectiveStateUpdate($"{path}.ssm", ~hidden_part, input_ids, cache_params, config.state_size,
			~dt, state_dict[$"{path}.A.weight"], B, C, state_dict[$"{path}.D.weight"]);
		hidden_states = ~ctx|fn.Act2Fn("silu", ~gate, mul:~hidden_states); // MambaRMSNormGated
		prolog = nn.LayerNormProlog($"{path}.norm", config.layer_norm_epsilon, rms:true);
		hidden_states = ~ctx|nn.Linear($"{path}.out_proj", ~hidden_states, prolog:prolog, add:residual);
		return hidden_states;
	}
	void Mamba2Block(string path, ref Tensor hidden_states, TensorView input_ids, Cache cache_params) {
		var prolog = nn.LayerNormProlog($"{path}.norm", config.layer_norm_epsilon, rms:true);
		hidden_states = ~ctx|Mamba2Mixer($"{path}.mixer", hidden_states, input_ids, cache_params, residual:~hidden_states, prolog:prolog);
	}
	protected override void LlamaDecoderLayer(string path, ref Tensor hidden_states, TensorView input_ids, Tensor position_embeddings, Cache past_key_values)
		=> Mamba2Block(path, ref hidden_states, input_ids, past_key_values);
	protected override string LlamaModel_embed_tokens => "embeddings";
	protected override string LlamaModel_rotary_emb => null;
	protected override string LlamaModel_norm => "norm_f";
	[ExcludeFromCodeCoverage]
	public Tensor Mamba2Model(string path, TensorView input_ids, Cache cache_params)
		=> LlamaModel(path, input_ids, cache_params);
	protected override string LlamaForCausalLM_model => "backbone";
	[ExcludeFromCodeCoverage]
	public CausalLMOutput Mamba2ForCausalLM(string path, TensorView input_ids, Cache cache_params)
		=> LlamaForCausalLM(path, input_ids, cache_params);
}
}