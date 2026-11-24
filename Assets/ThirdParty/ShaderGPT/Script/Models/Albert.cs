using UnityEngine;

namespace ShaderGPT.Models {
[System.Serializable]
public class AlbertConfig : PretrainedConfig {
	public int hidden_size;
	public int num_hidden_layers;
	public int num_hidden_groups;
	public int num_attention_heads;
	public int inner_group_num;
	public string hidden_act;
	public float layer_norm_eps;
}
[RegisterModel(typeof(AlbertConfig), "albert")]
[ParityTolerance(nameof(CausalLMOutput.hidden_states), 2e-4f)] // albert-large-v2
[ParityTolerance(nameof(CausalLMOutput.logits), 3e-5f)]
public class Albert : ModelForMaskedLM {
	public Albert(Functional fn, AlbertConfig config): base(fn, config) {}
	public new AlbertConfig config => (AlbertConfig)base.config;

	Tensor AlbertAttention(string path, Tensor hidden_states, TensorView input_ids, Tensor residual) {
		var (q,k,v) = nn.Linear(($"{path}.query", $"{path}.key", $"{path}.value"), hidden_states);

		var head_dim = config.hidden_size / config.num_attention_heads;
		hidden_states = ~ctx|fn.ScaledDotProductAttention(~q, k, v,
			heads:config.num_attention_heads, scale:1f/Mathf.Sqrt(head_dim),
			window:new(int.MinValue, 0, 1, (input_ids, 2))); // non-causal. assume length in chan 2
		hidden_states = ~ctx|nn.Linear($"{path}.dense", ~hidden_states, add:residual);
		return hidden_states;
	}
	Tensor AlbertFFN(string path, Tensor hidden_states, Tensor residual) {
		hidden_states = nn.Linear($"{path}.ffn", hidden_states);
		hidden_states = ~ctx|nn.Linear($"{path}.ffn_output", ~hidden_states, prolog:config.hidden_act, add:residual);
		return hidden_states;
	}
	void AlbertLayer(string path, ref Tensor hidden_states, TensorView input_ids) {
		hidden_states = ~ctx|AlbertAttention($"{path}.attention", hidden_states, input_ids, residual:~hidden_states);
		hidden_states = ~ctx|nn.LayerNorm($"{path}.attention.LayerNorm", ~hidden_states, config.layer_norm_eps);
		hidden_states = ~ctx|AlbertFFN(path, hidden_states, residual:~hidden_states);
		hidden_states = ~ctx|nn.LayerNorm($"{path}.full_layer_layer_norm", ~hidden_states, config.layer_norm_eps);
	}
	public Tensor AlbertModel(string path, TensorView input_ids, (TensorView,int)? token_type_ids=null) {
		var path_ = path == null ? "" : path + ".";
		var inputs_embeds     = nn.Embedding($"{path_}embeddings.word_embeddings", (input_ids, 0));
		var position_embeds   = nn.Embedding($"{path_}embeddings.position_embeddings", (input_ids, 1));
		var token_type_embeds = nn.Embedding($"{path_}embeddings.token_type_embeddings", token_type_ids ??
			(new TensorView(Texture2D.blackTexture, 1, 1), 0));
		var hidden_states = ~ctx|fn.Add(~inputs_embeds, ~token_type_embeds);
		hidden_states = ~ctx|fn.Add(~hidden_states, ~position_embeds);
		hidden_states = ~ctx|nn.LayerNorm($"{path_}embeddings.LayerNorm", ~hidden_states, config.layer_norm_eps);

		hidden_states = ~ctx|nn.Linear($"{path_}encoder.embedding_hidden_mapping_in", ~hidden_states);
		for(int i=0; i<config.num_hidden_groups; i++)
		for(int k=0; k<config.num_hidden_layers; k+=config.num_hidden_groups)
		for(int j=0; j<config.inner_group_num; j++)
			AlbertLayer($"{path_}encoder.albert_layer_groups.{i}.albert_layers.{j}", ref hidden_states, input_ids);
		return hidden_states;
	}
	Tensor AlbertMLMHead(string path, Tensor hidden_states) {
		hidden_states = nn.Linear($"{path}.dense", hidden_states);
		hidden_states = ~ctx|fn.Act2Fn(config.hidden_act, ~hidden_states);
		hidden_states = ~ctx|nn.LayerNorm($"{path}.LayerNorm", ~hidden_states, config.layer_norm_eps);
		hidden_states = ~ctx|nn.Linear($"{path}.decoder", ~hidden_states, fallback:"albert.embeddings.word_embeddings");
		if(!state_dict.ContainsKey($"{path}.decoder.bias") && state_dict.TryGetValue($"{path}.bias", out var bias))
			hidden_states = ~ctx|fn.Add(~hidden_states, bias);
		return hidden_states;
	}
	public MaskedLMOutput AlbertForMaskedLM(string path, TensorView input_ids, (TensorView,int)? token_type_ids=null) {
		var path_ = path == null ? "" : path + ".";
		var hidden_states = AlbertModel($"{path_}albert", input_ids);
		var logits = AlbertMLMHead($"{path_}predictions", hidden_states);
		return new MaskedLMOutput{logits=logits, hidden_states=hidden_states};
	}
	public override MaskedLMOutput ForMaskedLM(TensorView input_ids, (TensorView,int)? token_type_ids=null)
		=> AlbertForMaskedLM(null, input_ids, token_type_ids);
}
}