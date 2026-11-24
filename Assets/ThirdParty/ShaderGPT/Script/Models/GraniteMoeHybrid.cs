namespace ShaderGPT.Models {
[System.Serializable]
public class GraniteMoeHybridConfig : LlamaConfig {
	public float embedding_multiplier;
	public float logits_scaling;
	public float residual_multiplier;
	public float attention_multiplier;
	public string position_embedding_type;
	public string[] layer_types;
	public int mamba_n_heads;
	public int mamba_n_groups;
	public int mamba_d_state;
	public int mamba_expand;
}
[RegisterModel(typeof(GraniteMoeHybridConfig), "granitemoehybrid")]
[ParityTolerance(nameof(CausalLMOutput.hidden_states), 3e-3f)] // granite-4.0-h-350m
[ParityTolerance(nameof(CausalLMOutput.logits), 9e-5f)]
public class GraniteMoeHybrid : MambaBase {
	public GraniteMoeHybrid(Functional fn, GraniteMoeHybridConfig config): base(fn, config) {}
	public new GraniteMoeHybridConfig config => (GraniteMoeHybridConfig)base.config;

	Tensor GraniteMoeHybridAttention(string path, Tensor hidden_states, TensorView input_ids, Tensor position_embeddings, Cache past_key_values, Tensor residual, LinearProlog prolog) {
		hidden_states = BaseAttention(path, hidden_states, input_ids, position_embeddings, past_key_values, residual:null, prolog:prolog, scaling:config.attention_multiplier);
		return ~ctx|fn.Add(residual, ~hidden_states, config.residual_multiplier);
	}
	Tensor GraniteMoeHybridMambaLayer(string path, Tensor hidden_states, TensorView input_ids, Cache cache_params, Tensor residual, LinearProlog prolog) {
		// adapted from Mamba2Mixer
		var intermediate_size = config.mamba_expand * config.hidden_size;
		var grouped_state_size = config.mamba_n_groups * config.mamba_d_state;
		var (gate, hidden_states_B_C, dt) = nn.Linear($"{path}.in_proj", hidden_states, prolog:prolog)
			.Split((intermediate_size/4, (intermediate_size + 2*grouped_state_size)/4, config.mamba_n_heads/4), axis:1);
		var conv_out = CausalConv1d($"{path}.conv1d", hidden_states_B_C, input_ids, cache_params);
		dt = fn.Act2Fn("softplus", dt); // interleave to hide latency
		var (hidden_part, B, C) = ~ctx|fn.Act2Fn(config.hidden_act, ~conv_out)
			.Split((intermediate_size/4, grouped_state_size/4, grouped_state_size/4), axis:1);

		hidden_states = ~ctx|SelectiveStateUpdate($"{path}.ssm", ~hidden_part, input_ids, cache_params, config.mamba_d_state,
			~dt, state_dict[$"{path}.A.weight"], B, C, state_dict[$"{path}.D.weight"]);
		hidden_states = ~ctx|fn.Act2Fn("silu", ~gate, mul:~hidden_states); // GraniteMoeHybridRMSNormGated
		prolog = nn.LayerNormProlog($"{path}.norm", config.rms_norm_eps, rms:true);
		hidden_states = ~ctx|nn.Linear($"{path}.out_proj", ~hidden_states, prolog:prolog, cmul:config.residual_multiplier, add:residual);
		return hidden_states;
	}
	Tensor GraniteMoeHybridMLP(string path, Tensor hidden_states, Tensor residual, LinearProlog prolog) {
		var (gate, up) = nn.Linear($"{path}.input_linear", hidden_states, prolog:prolog).Chunk2(axis:1);
		hidden_states = ~ctx|nn.Linear($"{path}.output_linear", ~gate, prolog:(config.hidden_act, up),
			cmul:config.residual_multiplier, add:residual);
		return hidden_states;
	}
	void GraniteMoeHybridDecoderLayer(string path, ref Tensor hidden_states, TensorView input_ids, Tensor position_embeddings, Cache past_key_values) {
		var attn_prolog = nn.LayerNormProlog($"{path}.input_layernorm", config.rms_norm_eps, rms:true);
		hidden_states = config.layer_types[LayerIdx(path)] == "linear_attention"
			? ~ctx|GraniteMoeHybridMambaLayer($"{path}.mamba", hidden_states, input_ids, past_key_values, residual:~hidden_states, prolog:attn_prolog)
			: ~ctx|GraniteMoeHybridAttention($"{path}.self_attn", hidden_states, input_ids, position_embeddings, past_key_values, residual:~hidden_states, prolog:attn_prolog);
		var mlp_prolog = nn.LayerNormProlog($"{path}.post_attention_layernorm", config.rms_norm_eps, rms:true);
		hidden_states = ~ctx|GraniteMoeHybridMLP($"{path}.shared_mlp", hidden_states, residual:~hidden_states, prolog:mlp_prolog);
	}
	protected override void LlamaDecoderLayer(string path, ref Tensor hidden_states, TensorView input_ids, Tensor position_embeddings, Cache past_key_values)
		=> GraniteMoeHybridDecoderLayer(path, ref hidden_states, input_ids, position_embeddings, past_key_values);
	protected override string LlamaModel_rotary_emb => config.position_embedding_type == "rope" ? "rotary_emb" : null;
	public Tensor GraniteMoeHybridModel(string path, TensorView input_ids, Cache past_key_values, Tensor input_embeds=default) {
		var path_ = path == null ? "" : path + ".";
		return base.LlamaModel(path, input_ids, past_key_values, input_embeds ? input_embeds :
			~ctx|fn.Mul(~nn.Embedding($"{path_}embed_tokens", (input_ids, 0)), config.embedding_multiplier));
	}
	public override Tensor LlamaModel(string path, TensorView input_ids, Cache past_key_values, Tensor input_embeds=default)
		=> GraniteMoeHybridModel(path, input_ids, past_key_values, input_embeds);
	public CausalLMOutput GraniteForCausalLM(string path, TensorView input_ids, Cache past_key_values) {
		var output = LlamaForCausalLM(path, input_ids, past_key_values);
		output.logits = ~ctx|fn.Mul(~output.logits, 1f/config.logits_scaling);
		return output;
	}
	public override CausalLMOutput ForCausalLM(TensorView input_ids, Cache past_key_values)
		=> GraniteForCausalLM(null, input_ids, past_key_values);
}
}