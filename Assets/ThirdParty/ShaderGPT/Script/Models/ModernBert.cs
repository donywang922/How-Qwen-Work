namespace ShaderGPT.Models {
[System.Serializable]
public class ModernBertConfig : PretrainedConfig {
	public int hidden_size;
	public int num_hidden_layers;
	public int num_attention_heads;
	public string hidden_activation;
	public float norm_eps;
	public string[] layer_types;
	public int local_attention;
	public string classifier_activation;
}
[RegisterModel(typeof(ModernBertConfig), "modernbert")]
[ParityTolerance(nameof(CausalLMOutput.hidden_states), 6e-4f)] // ModernBERT-base
[ParityTolerance(nameof(CausalLMOutput.logits), 5e-5f)]
public class ModernBert : ModelForMaskedLM {
	public ModernBert(Functional fn, ModernBertConfig config): base(fn, config) {}
	public new ModernBertConfig config => (ModernBertConfig)base.config;

	Tensor ModernBertAttention(string path, Tensor hidden_states, TensorView input_ids, TensorView position_embeddings, Tensor residual, LinearProlog prolog) {
		var (q,k,v) = nn.Linear($"{path}.Wqkv", hidden_states, prolog:prolog).Chunk3(axis:1);
		var (query,key) = fn.Rotary(new TensorView(q.tensor, q.size0, q.size1+k.size1), position_embeddings,
			groups:config.num_attention_heads+config.num_attention_heads).Split((q.size1, k.size1), axis:1);

		var head_dim = config.hidden_size / config.num_attention_heads;
		var half_size = config.layer_types[LayerIdx(path)] == "sliding_attention" ? config.local_attention/2 : int.MaxValue/2;
		hidden_states = ~ctx|fn.ScaledDotProductAttention(~query, key, ~v,
			heads:config.num_attention_heads, scale:1f/System.MathF.Sqrt(head_dim), stable:true,
			window:new(int.MinValue, 0, 1, (input_ids, 2), -half_size, half_size+1, 1, (input_ids, 1)));
		hidden_states = ~ctx|nn.Linear($"{path}.Wo", ~hidden_states, add:residual);
		return hidden_states;
	}
	Tensor ModernBertMLP(string path, Tensor hidden_states, Tensor residual, LinearProlog prolog) {
		var (input,gate) = nn.Linear($"{path}.Wi", hidden_states, prolog:prolog).Chunk2(axis:1);
		hidden_states = ~ctx|nn.Linear($"{path}.Wo", ~input, prolog:(config.hidden_activation, gate), add:residual);
		return hidden_states;
	}
	void ModernBertEncoderLayer(string path, ref Tensor hidden_states, TensorView input_ids, TensorView position_embeddings) {
		var attn_prolog = LayerIdx(path) == 0 ? default : nn.LayerNormProlog($"{path}.attn_norm", config.norm_eps);
		hidden_states = ~ctx|ModernBertAttention($"{path}.attn", hidden_states, input_ids, position_embeddings, residual:~hidden_states, prolog:attn_prolog);
		var mlp_prolog = nn.LayerNormProlog($"{path}.mlp_norm", config.norm_eps);
		hidden_states = ~ctx|ModernBertMLP($"{path}.mlp", hidden_states, residual:~hidden_states, prolog:mlp_prolog);
	}
	public Tensor ModernBertModel(string path, TensorView input_ids) {
		var path_ = path == null ? "" : path + ".";
		var hidden_states = nn.Embedding($"{path_}embeddings.tok_embeddings", (input_ids, 0));
		hidden_states = ~ctx|nn.LayerNorm($"{path_}embeddings.norm", ~hidden_states, config.norm_eps);
		using(~ctx) {
			var position_embeddings_global = ~nn.Embedding($"{path_}rotary_emb", (input_ids, 1));
			var position_embeddings_local  = ~nn.Embedding($"{path_}rotary_emb_local", (input_ids, 1));
			for(int i=0; i<config.num_hidden_layers; i++) {
				var is_sliding = config.layer_types[i] == "sliding_attention";
				ModernBertEncoderLayer($"{path_}layers.{i}", ref hidden_states, input_ids,
					is_sliding?position_embeddings_local:position_embeddings_global);
			}
		}
		hidden_states = ~ctx|nn.LayerNorm($"{path_}final_norm", ~hidden_states, config.norm_eps);
		return hidden_states;
	}
	Tensor ModernBertPredictionHead(string path, Tensor hidden_states) {
		hidden_states = nn.Linear($"{path}.dense", hidden_states);
		hidden_states = ~ctx|fn.Act2Fn(config.classifier_activation, ~hidden_states);
		hidden_states = ~ctx|nn.LayerNorm($"{path}.norm", ~hidden_states, config.norm_eps);
		return hidden_states;
	}
	public MaskedLMOutput ModernBertForMaskedLM(string path, TensorView input_ids) {
		var path_ = path == null ? "" : path + ".";
		var hidden_states = ModernBertModel($"{path_}model", input_ids);
		var head_output = ModernBertPredictionHead($"{path_}head", hidden_states);
		var logits = ~ctx|nn.Linear($"{path_}decoder", ~head_output, fallback:$"{path_}model.embeddings.tok_embeddings");
		return new MaskedLMOutput{logits=logits, hidden_states=hidden_states};
	}
	public override MaskedLMOutput ForMaskedLM(TensorView input_ids, (TensorView,int)? token_type_ids=null)
		=> ModernBertForMaskedLM(null, input_ids);
}
}