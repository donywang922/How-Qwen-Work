using System.Diagnostics.CodeAnalysis;

namespace ShaderGPT.Models {
[System.Serializable]
public class Gemma3TextConfig : GemmaConfig {
	public float query_pre_attn_scalar;
	public string[] layer_types;
}
[RegisterModel(typeof(Gemma3TextConfig), "gemma3_text")]
[ParityTolerance(nameof(CausalLMOutput.hidden_states), 5e-4f)] // functiongemma-270m-it-Q8_0
[ParityTolerance(nameof(CausalLMOutput.logits), 6e-5f)]
public class Gemma3Text : Gemma {
	public Gemma3Text(Functional fn, Gemma3TextConfig config): base(fn, config) {}
	public new Gemma3TextConfig config => (Gemma3TextConfig)base.config;

	Tensor Gemma3Attention(string path, Tensor hidden_states, TensorView input_ids, Tensor position_embeddings, Cache past_key_values, LinearProlog prolog) {
		var is_sliding = config.layer_types[LayerIdx(path)] == "sliding_attention";
		return BaseAttention(path, hidden_states, input_ids, position_embeddings, past_key_values, residual:null, prolog:prolog,
			q_norm:"q_norm", k_norm:"k_norm", sliding_window:is_sliding?config.sliding_window:null, scaling:1f/System.MathF.Sqrt(config.query_pre_attn_scalar));
	}
	void Gemma3DecoderLayer(string path, ref Tensor hidden_states, TensorView input_ids, Tensor position_embeddings, Cache past_key_values) {
		var attn_prolog = nn.LayerNormProlog($"{path}.input_layernorm", config.rms_norm_eps, rms:true);
		var attn_states = Gemma3Attention($"{path}.self_attn", hidden_states, input_ids, position_embeddings, past_key_values, attn_prolog);
		attn_states = ~ctx|nn.LayerNorm($"{path}.post_attention_layernorm", ~attn_states, config.rms_norm_eps, rms:true);
		_ = ~ctx|fn.Add_(hidden_states, ~attn_states);
		var mlp_prolog = nn.LayerNormProlog($"{path}.pre_feedforward_layernorm", config.rms_norm_eps, rms:true);
		var mlp_states = LlamaMLP($"{path}.mlp", hidden_states, residual:null, prolog:mlp_prolog);
		mlp_states = ~ctx|nn.LayerNorm($"{path}.post_feedforward_layernorm", ~mlp_states, config.rms_norm_eps, rms:true);
		_ = ~ctx|fn.Add_(hidden_states, ~mlp_states);
	}
	public Tensor Gemma3TextModel(string path, TensorView input_ids, Cache past_key_values, Tensor input_embeds=default) {
		var path_ = path == null ? "" : path + ".";
		var hidden_states = input_embeds ? input_embeds :
			~ctx|fn.Mul(~nn.Embedding($"{path}.embed_tokens", (input_ids, 0)), System.MathF.Sqrt(config.hidden_size));
		using(~ctx) {
			var position_embeddings_global = ~nn.Embedding($"{path_}rotary_emb", (input_ids, 1));
			var position_embeddings_local  = ~nn.Embedding($"{path_}rotary_emb_local", (input_ids, 1));
			for(int i=0; i<config.num_hidden_layers; i++) {
				var is_sliding = config.layer_types[i] == "sliding_attention";
				Gemma3DecoderLayer($"{path_}layers.{i}", ref hidden_states, input_ids,
					is_sliding?position_embeddings_local:position_embeddings_global, past_key_values);
			}
		}
		hidden_states = ~ctx|nn.LayerNorm($"{path_}norm", ~hidden_states, config.rms_norm_eps, rms:true);
		return hidden_states;
	}
	public override Tensor LlamaModel(string path, TensorView input_ids, Cache past_key_values, Tensor input_embeds=default)
		=> Gemma3TextModel(path, input_ids, past_key_values, input_embeds);
	[ExcludeFromCodeCoverage]
	public CausalLMOutput Gemma3ForCausalLM(string path, TensorView input_ids, Cache past_key_values)
		=> LlamaForCausalLM(path, input_ids, past_key_values);
}
}