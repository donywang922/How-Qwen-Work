namespace ShaderGPT.Models {
[System.Serializable]
public class FalconH1Config : LlamaConfig {
	public float embedding_multiplier;
	public float lm_head_multiplier;
	public float[] mlp_multipliers;
	public float key_multiplier;
	public float attention_in_multiplier;
	public float attention_out_multiplier;
	public float[] ssm_multipliers;
	public float ssm_in_multiplier;
	public float ssm_out_multiplier;
	public int mamba_d_ssm;
	public int mamba_n_heads;
	public int mamba_n_groups;
	public int mamba_d_state;
	public int mamba_expand;
	public bool mamba_norm_before_gate;
	public bool mamba_rms_norm;
}
[RegisterModel(typeof(FalconH1Config), "falcon_h1")]
[ParityTolerance(nameof(CausalLMOutput.hidden_states), 2e-3f)] // Falcon-H1-Tiny-90M-Instruct
[ParityTolerance(nameof(CausalLMOutput.logits), 3e-5f)]
public class FalconH1 : MambaBase {
	public FalconH1(Functional fn, FalconH1Config config): base(fn, config) {}
	public new FalconH1Config config => (FalconH1Config)base.config;

	Tensor FalconH1Attention(string path, Tensor hidden_states, TensorView input_ids, Tensor position_embeddings, Cache past_key_values, Tensor residual) {
		hidden_states = BaseAttention(path, hidden_states, input_ids, position_embeddings, past_key_values, residual:null,
			scaling:config.key_multiplier/System.MathF.Sqrt(config.head_dim));
		return ~ctx|fn.Add(residual, ~hidden_states, config.attention_out_multiplier);
	}
	Tensor FalconH1Mixer(string path, ref Tensor hidden_states, TensorView input_ids, Cache cache_params, Tensor residual) {
		// adapted from GraniteMoeHybridMambaLayer
		var intermediate_size = config.mamba_d_ssm > 0 ? config.mamba_d_ssm : config.mamba_expand * config.hidden_size;
		var grouped_state_size = config.mamba_n_groups * config.mamba_d_state;
		hidden_states = ~ctx|nn.Linear($"{path}.in_proj", ~hidden_states);
		var (gate, hidden_states_B_C, dt) = Mul(ref hidden_states, config.ssm_multipliers,
			intermediate_size/4, intermediate_size/4, grouped_state_size/4, grouped_state_size/4, config.mamba_n_heads/4)
			.Split((intermediate_size/4, (intermediate_size + 2*grouped_state_size)/4, config.mamba_n_heads/4), axis:1);
		var conv_out = CausalConv1d($"{path}.conv1d", hidden_states_B_C, input_ids, cache_params);
		dt = fn.Act2Fn("softplus", dt); // interleave to hide latency
		var (hidden_part, B, C) = ~ctx|fn.Act2Fn(config.hidden_act, ~conv_out)
			.Split((intermediate_size/4, grouped_state_size/4, grouped_state_size/4), axis:1);

		hidden_states = ~ctx|SelectiveStateUpdate($"{path}.ssm", ~hidden_part, input_ids, cache_params, config.mamba_d_state,
			~dt, state_dict[$"{path}.A.weight"], B, C, state_dict[$"{path}.D.weight"]);
		if(config.mamba_rms_norm && config.mamba_norm_before_gate)
			hidden_states = ~ctx|nn.LayerNorm($"{path}.norm", ~hidden_states, eps:config.rms_norm_eps, rms:true, groups:config.mamba_n_groups);
		hidden_states = ~ctx|fn.Act2Fn("silu", ~gate, mul:~hidden_states); // FalconH1RMSNormGated
		if(config.mamba_rms_norm && !config.mamba_norm_before_gate)
			hidden_states = ~ctx|nn.LayerNorm($"{path}.norm", ~hidden_states, eps:config.rms_norm_eps, rms:true, groups:config.mamba_n_groups);
		hidden_states = ~ctx|nn.Linear($"{path}.out_proj", ~hidden_states, cmul:config.ssm_out_multiplier, add:residual);
		return hidden_states;
	}
	Tensor FalconH1MLP(string path, Tensor hidden_states, Tensor residual, LinearProlog prolog) {
		var (gate, up) = nn.Linear(($"{path}.gate_proj", $"{path}.up_proj"), hidden_states, prolog:prolog);
		gate = fn.Mul(gate, config.mlp_multipliers[0]);
		hidden_states = ~ctx|nn.Linear($"{path}.down_proj", ~gate, prolog:(config.hidden_act, ~up),
			cmul:config.mlp_multipliers[1], add:residual);
		return hidden_states;
	}
	void FalconH1DecoderLayer(string path, ref Tensor hidden_states, TensorView input_ids, Tensor position_embeddings, Cache past_key_values) {
		var norm_states = nn.LayerNorm($"{path}.input_layernorm", hidden_states, config.rms_norm_eps, rms:true);
		var mamba_input = fn.Mul(norm_states, config.ssm_in_multiplier);
		var attn_input = Mul(ref norm_states, new[]{config.attention_in_multiplier}, norm_states.size1);
		hidden_states = ~ctx|FalconH1Mixer($"{path}.mamba", ref mamba_input, input_ids, past_key_values, residual:~hidden_states);
		hidden_states = ~ctx|FalconH1Attention($"{path}.self_attn", ~attn_input, input_ids, position_embeddings, past_key_values, residual:~hidden_states);
		var mlp_prolog = nn.LayerNormProlog($"{path}.pre_ff_layernorm", config.rms_norm_eps, rms:true);
		hidden_states = ~ctx|FalconH1MLP($"{path}.feed_forward", hidden_states, residual:~hidden_states, prolog:mlp_prolog);
	}
	protected override void LlamaDecoderLayer(string path, ref Tensor hidden_states, TensorView input_ids, Tensor position_embeddings, Cache past_key_values)
		=> FalconH1DecoderLayer(path, ref hidden_states, input_ids, position_embeddings, past_key_values);
	protected override string LlamaModel_norm => "final_layernorm";
	public Tensor FalconH1Model(string path, TensorView input_ids, Cache past_key_values, Tensor input_embeds=default) {
		var path_ = path == null ? "" : path + ".";
		return base.LlamaModel(path, input_ids, past_key_values, input_embeds ? input_embeds :
			~ctx|fn.Mul(~nn.Embedding($"{path_}embed_tokens", (input_ids, 0)), config.embedding_multiplier));
	}
	public override Tensor LlamaModel(string path, TensorView input_ids, Cache past_key_values, Tensor input_embeds=default)
		=> FalconH1Model(path, input_ids, past_key_values, input_embeds);
	public CausalLMOutput FalconH1ForCausalLM(string path, TensorView input_ids, Cache past_key_values) {
		var output = LlamaForCausalLM(path, input_ids, past_key_values);
		output.logits = ~ctx|fn.Mul(~output.logits, config.lm_head_multiplier);
		return output;
	}
	public override CausalLMOutput ForCausalLM(TensorView input_ids, Cache past_key_values)
		=> FalconH1ForCausalLM(null, input_ids, past_key_values);

	private Tensor Mul(ref Tensor input, float[] other, params int[] sizes) {
		if(System.Array.TrueForAll(other, x => x == 1))
			return input;
		var output = fn.EmptyLike(input);
		for(int i=0, j=0; i<sizes.Length; j+=sizes[i++])
			fn.Mul(input[.., j..(j+sizes[i])], other[i], @out:output[.., j..(j+sizes[i])]);
		ctx.Release(input);
		return output;
	}
}
}