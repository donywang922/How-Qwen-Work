using System.Diagnostics.CodeAnalysis;

namespace ShaderGPT.Models {
[System.Serializable]
public class LlamaConfig : PretrainedConfig {
	public int hidden_size;
	public int num_hidden_layers;
	public int num_attention_heads;
	public int num_key_value_heads;
	public string hidden_act;
	public float rms_norm_eps;
	public int sliding_window;
	public int head_dim;

	public float layer_norm_eps; // phi
}
[RegisterModel(typeof(LlamaConfig), "llama", "qwen2")]
[ParityTolerance(nameof(CausalLMOutput.hidden_states), 5e-5f, ModelType="llama")] // SmolLM2-135M-Instruct-Q8_0
[ParityTolerance(nameof(CausalLMOutput.logits), 4e-5f, ModelType="llama")]
[ParityTolerance(nameof(CausalLMOutput.hidden_states), 3e-4f, ModelType="qwen2")] // Qwen2.5-0.5B-Instruct-Q8_0
[ParityTolerance(nameof(CausalLMOutput.logits), 6e-5f, ModelType="qwen2")]
public class Llama : ModelForCausalLM {
	public Llama(Functional fn, LlamaConfig config): base(fn, config) {}
	public new LlamaConfig config => (LlamaConfig)base.config;
	private int head_dim => config.head_dim > 0 ? config.head_dim : config.hidden_size / config.num_attention_heads;
	private float norm_eps => config.layer_norm_eps == 0f ? config.rms_norm_eps : config.layer_norm_eps;
	private bool rms => config.layer_norm_eps == 0f;

	protected Tensor BaseAttention(string path, Tensor hidden_states, TensorView input_ids, Tensor position_embeddings, [NotNull] Cache past_key_values, Tensor residual,
		LinearProlog prolog=default, string qkv_proj=null, string q_norm=null, string k_norm=null,
		int? sliding_window=null, float? scaling=null, bool stable=false, string gate_proj=null, string o_proj="o_proj"
	) {
		var gate = gate_proj != null ? nn.Linear($"{path}.{gate_proj}", hidden_states, prolog:prolog) : default;
		var (q,k,v) = qkv_proj != null ? nn.Linear($"{path}.{qkv_proj}", hidden_states, prolog:prolog).Split(
			(config.hidden_size/4, config.num_key_value_heads*head_dim/4, config.num_key_value_heads*head_dim/4), axis:1)
			: nn.Linear(($"{path}.q_proj", $"{path}.k_proj", $"{path}.v_proj"), hidden_states, prolog:prolog);
		if(q_norm != null)
			(q,k) = nn.LayerNorm(($"{path}.{q_norm}", $"{path}.{k_norm}"), new TensorView(q.tensor, q.size0, q.size1+k.size1),
				norm_eps, groups:(config.num_attention_heads, config.num_key_value_heads), rms:rms);
		var (query,key) = ~ctx|fn.Rotary(new TensorView(q.tensor, q.size0, q.size1+k.size1), position_embeddings,
			groups:config.num_attention_heads+config.num_key_value_heads).Split(((q_norm!=null?~q:q).size1, k.size1), axis:1);
		var keys   = past_key_values.Update($"{path}.k", key, (input_ids, 1));
		var values = ~ctx|past_key_values.Update($"{path}.v", ~v, (input_ids, 1));

		hidden_states = ~ctx|fn.ScaledDotProductAttention(~query, keys, values,
			heads:config.num_attention_heads, scale:scaling ?? 1f/System.MathF.Sqrt(head_dim), stable:stable,
			window:new(1-(sliding_window ?? int.MaxValue), 1, 1, (input_ids, 1)));
		hidden_states = gate_proj != null
			? ~ctx|nn.Linear($"{path}.{o_proj}", ~gate, prolog:("sigmoid", ~hidden_states), add:residual)
			: ~ctx|nn.Linear($"{path}.{o_proj}", ~hidden_states, add:residual);
		return hidden_states;
	}
	protected virtual Tensor LlamaAttention(string path, Tensor hidden_states, TensorView input_ids, Tensor position_embeddings, Cache past_key_values, Tensor residual, LinearProlog prolog=default) {
		return BaseAttention(path, hidden_states, input_ids, position_embeddings, past_key_values, residual,
			prolog:prolog, sliding_window:config.sliding_window>0?config.sliding_window:null, stable:config.model_type == "qwen2");
	}
	protected virtual Tensor LlamaMLP(string path, Tensor hidden_states, Tensor residual, LinearProlog prolog=default) {
		var (gate,up) = nn.Linear(($"{path}.gate_proj", $"{path}.up_proj"), hidden_states, prolog:prolog);
		hidden_states = ~ctx|nn.Linear($"{path}.down_proj", ~gate, prolog:(config.hidden_act, up), add:residual);
		return hidden_states;
	}
	protected virtual void LlamaDecoderLayer(string path, ref Tensor hidden_states, TensorView input_ids, Tensor position_embeddings, Cache past_key_values) {
		var attn_prolog = nn.LayerNormProlog($"{path}.input_layernorm", norm_eps, rms:rms);
		hidden_states = ~ctx|LlamaAttention($"{path}.self_attn", hidden_states, input_ids, position_embeddings, past_key_values, residual:~hidden_states, prolog:attn_prolog);
		var mlp_prolog = nn.LayerNormProlog($"{path}.post_attention_layernorm", norm_eps, rms:rms);
		hidden_states = ~ctx|LlamaMLP($"{path}.mlp", hidden_states, residual:~hidden_states, prolog:mlp_prolog);
	}
	protected virtual string LlamaModel_embed_tokens => "embed_tokens";
	protected virtual string LlamaModel_rotary_emb => "rotary_emb";
	protected virtual string LlamaModel_norm => "norm";
	public virtual Tensor LlamaModel(string path, TensorView input_ids, Cache past_key_values, Tensor input_embeds=default) {
		var path_ = path == null ? "" : path + ".";
		var hidden_states = input_embeds ? input_embeds : nn.Embedding($"{path_}{LlamaModel_embed_tokens}", (input_ids, 0));
		using(~ctx) {
			var position_embeddings = LlamaModel_rotary_emb == null ? null :
				~nn.Embedding($"{path_}{LlamaModel_rotary_emb}", (input_ids, 1));
			for(int i=0; i<config.num_hidden_layers; i++)
				LlamaDecoderLayer($"{path_}layers.{i}", ref hidden_states, input_ids, position_embeddings, past_key_values);
		}
		hidden_states = ~ctx|nn.LayerNorm($"{path_}{LlamaModel_norm}", ~hidden_states, norm_eps, rms:rms);
		return hidden_states;
	}
	protected virtual string LlamaForCausalLM_lm_head => "lm_head";
	protected virtual string LlamaForCausalLM_model => "model";
	public CausalLMOutput LlamaForCausalLM(string path, TensorView input_ids, Cache past_key_values) {
		var path_ = path == null ? "" : path + ".";
		FixSize0($"{path_}{LlamaForCausalLM_lm_head}.weight.T", config.hidden_size);
		FixSize0($"{path_}{LlamaForCausalLM_model}.{LlamaModel_embed_tokens}.weight.T", config.hidden_size);
		var hidden_states = LlamaModel($"{path_}{LlamaForCausalLM_model}", input_ids, past_key_values);
		var logits = nn.Linear($"{path_}{LlamaForCausalLM_lm_head}", hidden_states, fallback:$"{path_}{LlamaForCausalLM_model}.{LlamaModel_embed_tokens}");
		return new CausalLMOutput{logits=logits, hidden_states=hidden_states};
	}
	public override CausalLMOutput ForCausalLM(TensorView input_ids, Cache past_key_values)
		=> LlamaForCausalLM(null, input_ids, past_key_values);
}
}