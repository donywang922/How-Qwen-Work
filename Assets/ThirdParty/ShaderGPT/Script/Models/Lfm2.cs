using System.Diagnostics.CodeAnalysis;

namespace ShaderGPT.Models {
[System.Serializable]
public class Lfm2Config : LlamaConfig {
	public float norm_eps;
	public string[] layer_types;
	protected override void FromPretrained(string json) {
		base.FromPretrained(json);
		hidden_act = "silu";
		rms_norm_eps = norm_eps;
	}
}
[RegisterModel(typeof(Lfm2Config), "lfm2")]
[ParityTolerance(nameof(CausalLMOutput.hidden_states), 3e-5f)] // LFM2.5-230M-Q8_0
[ParityTolerance(nameof(CausalLMOutput.logits), 2e-5f)]
public class Lfm2 : MambaBase {
	public Lfm2(Functional fn, Lfm2Config config): base(fn, config) {}
	public new Lfm2Config config => (Lfm2Config)base.config;

	Tensor Lfm2Attention(string path, Tensor hidden_states, TensorView input_ids, Tensor position_embeddings, Cache past_key_values, Tensor residual, LinearProlog prolog) {
		return BaseAttention(path, hidden_states, input_ids, position_embeddings, past_key_values, residual,
			prolog:prolog, q_norm:"q_layernorm", k_norm:"k_layernorm", o_proj:"out_proj");
	}
	Tensor Lfm2ShortConv(string path, Tensor hidden_states, TensorView input_ids, Cache cache_params, Tensor residual, LinearProlog prolog) {
		var (B,C,x) = nn.Linear($"{path}.in_proj", hidden_states, prolog:prolog).Chunk3(axis:1);
		hidden_states = CausalConv1d($"{path}.conv", x, input_ids, cache_params, preMul:B);
		hidden_states = ~ctx|nn.Linear($"{path}.out_proj", ~hidden_states, prolog:(null, ~C), add:residual);
		return hidden_states;
	}
	Tensor Lfm2MLP(string path, Tensor hidden_states, Tensor residual, LinearProlog prolog) {
		var (gate,up) = nn.Linear(($"{path}.w1", $"{path}.w3"), hidden_states, prolog:prolog);
		hidden_states = ~ctx|nn.Linear($"{path}.w2", ~gate, prolog:(config.hidden_act, up), add:residual);
		return hidden_states;
	}
	void Lfm2DecoderLayer(string path, ref Tensor hidden_states, TensorView input_ids, Tensor position_embeddings, Cache past_key_values) {
		var attn_prolog = nn.LayerNormProlog($"{path}.operator_norm", config.rms_norm_eps, rms:true);
		hidden_states = config.layer_types[LayerIdx(path)] == "conv"
			? ~ctx|Lfm2ShortConv($"{path}.conv", hidden_states, input_ids, past_key_values, residual:~hidden_states, prolog:attn_prolog)
			: ~ctx|Lfm2Attention($"{path}.self_attn", hidden_states, input_ids, position_embeddings, past_key_values, residual:~hidden_states, prolog:attn_prolog);
		var mlp_prolog = nn.LayerNormProlog($"{path}.ffn_norm", config.rms_norm_eps, rms:true);
		hidden_states = ~ctx|Lfm2MLP($"{path}.feed_forward", hidden_states, residual:~hidden_states, prolog:mlp_prolog);
	}
	protected override void LlamaDecoderLayer(string path, ref Tensor hidden_states, TensorView input_ids, Tensor position_embeddings, Cache past_key_values)
		=> Lfm2DecoderLayer(path, ref hidden_states, input_ids, position_embeddings, past_key_values);
	protected override string LlamaModel_norm => "embedding_norm";
	public Tensor Lfm2Model(string path, TensorView input_ids, Cache past_key_values, Tensor input_embeds=default)
		=> LlamaModel(path, input_ids, past_key_values, input_embeds);
	[ExcludeFromCodeCoverage]
	public CausalLMOutput Lfm2ForCausalLM(string path, TensorView input_ids, Cache past_key_values)
		=> LlamaForCausalLM(path, input_ids, past_key_values);
}
}