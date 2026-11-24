namespace ShaderGPT.Models {
[System.Serializable]
public class MoonshineConfig : PretrainedConfig {
	public int hidden_size;
	public int encoder_num_hidden_layers;
	public int decoder_num_hidden_layers;
	public int encoder_num_attention_heads;
	public int decoder_num_attention_heads;
	public int encoder_num_key_value_heads;
	public int decoder_num_key_value_heads;
	public string encoder_hidden_act;
	public string decoder_hidden_act;

	public float layer_norm_eps => 1e-5f;
}
[RegisterModel(typeof(MoonshineConfig), "moonshine")]
[ParityTolerance(nameof(Seq2SeqLMOutput.encoder_hidden_states), 4e-5f)] // moonshine-tiny
[ParityTolerance(nameof(CausalLMOutput.hidden_states), 1e-5f)]
[ParityTolerance(nameof(CausalLMOutput.logits), 2e-5f)]
public class Moonshine : ModelForSpeechSeq2Seq {
	public Moonshine(Functional fn, MoonshineConfig config): base(fn, config) {}
	public new MoonshineConfig config => (MoonshineConfig)base.config;

	Tensor MoonshineAttention(string path, Tensor hidden_states, TensorView input_ids, TensorView position_embeddings, Cache past_key_values,
			Tensor key_value_states=default, bool encoder=false, bool cross=false, Tensor residual=default, LinearProlog prolog=default) {
		var num_attention_heads = encoder ? config.encoder_num_attention_heads : config.decoder_num_attention_heads;
		var num_key_value_heads = encoder ? config.encoder_num_key_value_heads : config.decoder_num_key_value_heads;
		using var _ = ~ctx;
		TensorView q=default, k=default, v=default;
		if(cross) {
			if(key_value_states)
				(k,v) = nn.Linear(($"{path}.k_proj", $"{path}.v_proj"), key_value_states);
			if(hidden_states)
				q = nn.Linear($"{path}.q_proj", hidden_states, prolog:prolog);
		} else {
			(q,k,v) = nn.Linear(($"{path}.q_proj", $"{path}.k_proj", $"{path}.v_proj"), hidden_states, prolog:prolog);
			(q,k) = fn.Rotary(new TensorView(q.tensor, q.size0, q.size1+k.size1), position_embeddings,
				groups:num_attention_heads+num_key_value_heads).Split((q.size1, k.size1), axis:1);
		}
		var keys   = past_key_values?.CrossUpdate($"{path}.k", k,  (input_ids, 1), cross, !(cross?hidden_states:encoder)) ?? k;
		var values = past_key_values?.CrossUpdate($"{path}.v", ~v, (input_ids, 1), cross, !(cross?hidden_states:encoder)) ?? ~v;
		if(!hidden_states)
			return hidden_states;

		var head_dim = config.hidden_size / num_attention_heads;
		hidden_states = ~ctx|fn.ScaledDotProductAttention(~q, keys, values,
			heads:num_attention_heads, scale:1f/System.MathF.Sqrt(head_dim),
			window:!encoder&&!cross ? new(1-int.MaxValue, 1, 1, (input_ids, 1)) : null);
		hidden_states = ~ctx|nn.Linear($"{path}.o_proj", ~hidden_states, add:residual);
		return hidden_states;
	}
	Tensor MoonshineEncoderMLP(string path, Tensor hidden_states, Tensor residual, LinearProlog prolog) {
		hidden_states = nn.Linear($"{path}.fc1", hidden_states, prolog:prolog);
		hidden_states = ~ctx|nn.Linear($"{path}.fc2", ~hidden_states, prolog:config.encoder_hidden_act, add:residual);
		return hidden_states;
	}
	Tensor MoonshineDecoderMLP(string path, Tensor hidden_states, Tensor residual, LinearProlog prolog) {
		var (up,gate) = nn.Linear($"{path}.fc1", hidden_states, prolog:prolog).Chunk2(axis:1);
		hidden_states = ~ctx|nn.Linear($"{path}.fc2", ~gate, prolog:(config.decoder_hidden_act, up), add:residual);
		return hidden_states;
	}
	void MoonshineEncoderLayer(string path, ref Tensor hidden_states, TensorView position_embeddings) {
		var attn_prolog = nn.LayerNormProlog($"{path}.input_layernorm", config.layer_norm_eps);
		hidden_states = ~ctx|MoonshineAttention($"{path}.self_attn", hidden_states, default, position_embeddings, null, encoder:true, residual:~hidden_states, prolog:attn_prolog);
		var mlp_prolog = nn.LayerNormProlog($"{path}.post_attention_layernorm", config.layer_norm_eps);
		hidden_states = ~ctx|MoonshineEncoderMLP($"{path}.mlp", hidden_states, residual:~hidden_states, prolog:mlp_prolog);
	}
	void MoonshineDecoderLayer(string path, ref Tensor hidden_states, TensorView input_ids, TensorView position_embeddings, Cache past_key_values, Tensor encoder_hidden_states) {
		if(!hidden_states) {
			MoonshineAttention($"{path}.encoder_attn", hidden_states, input_ids, position_embeddings, past_key_values, encoder_hidden_states, cross:true);
			return; // when hidden_states is null, only compute cross attn kv
		}
		var attn_prolog = nn.LayerNormProlog($"{path}.input_layernorm", config.layer_norm_eps);
		hidden_states = ~ctx|MoonshineAttention($"{path}.self_attn", hidden_states, input_ids, position_embeddings,
			past_key_values, residual:~hidden_states, prolog:attn_prolog);
		attn_prolog = nn.LayerNormProlog($"{path}.post_attention_layernorm", config.layer_norm_eps);
		hidden_states = ~ctx|MoonshineAttention($"{path}.encoder_attn", hidden_states, input_ids, position_embeddings,
			past_key_values, encoder_hidden_states, cross:true, residual:~hidden_states, prolog:attn_prolog);
		var mlp_prolog = nn.LayerNormProlog($"{path}.final_layernorm", config.layer_norm_eps);
		hidden_states = ~ctx|MoonshineDecoderMLP($"{path}.mlp", hidden_states, residual:~hidden_states, prolog:mlp_prolog);
	}
	Tensor MoonshineEncoder(string path, TensorView input_features) {
		var length = input_features.size1*4;
		var hidden_states = nn.Conv1d($"{path}.conv1", input_features, kernel_size:127, stride:64, padding:0);
		hidden_states = ~ctx|fn.Act2Fn("tanh", ~hidden_states, window:new(0, length=(length-127)/64+1));
		hidden_states = ~ctx|fn.GroupNorm(~hidden_states, 1,
			state_dict[$"{path}.groupnorm.weight"], state_dict[$"{path}.groupnorm.bias"],
			config.layer_norm_eps, window:new(0, length));
		hidden_states = ~ctx|nn.Conv1d($"{path}.conv2", ~hidden_states, kernel_size:7, stride:3, padding:0);
		hidden_states = ~ctx|fn.Act2Fn("gelu", ~hidden_states, window:new(0, length=(length-7)/3+1));
		hidden_states = ~ctx|nn.Conv1d($"{path}.conv3", ~hidden_states, kernel_size:3, stride:2, padding:0);
		hidden_states = ~ctx|fn.Act2Fn("gelu", ~hidden_states, window:new(0, length=(length-3)/2+1));
		hidden_states = ~ctx|fn.Transpose(~hidden_states, size0:length);
		for(int i=0; i<config.encoder_num_hidden_layers; i++)
			MoonshineEncoderLayer($"{path}.layers.{i}", ref hidden_states, state_dict[$"{path}.rotary_emb.weight"][..length, ..]);
		hidden_states = ~ctx|nn.LayerNorm($"{path}.layer_norm", ~hidden_states, config.layer_norm_eps);
		return hidden_states;
	}
	Tensor MoonshineDecoder(string path, TensorView input_ids, Cache past_key_values, Tensor encoder_hidden_states=default) {
		var hidden_states = default(Tensor);
		if(!input_ids) {
			for(int i=0; i<config.decoder_num_hidden_layers; i++)
				MoonshineDecoderLayer($"{path}.layers.{i}", ref hidden_states, input_ids, default, past_key_values, encoder_hidden_states);
			return hidden_states;
		}
		hidden_states = nn.Embedding($"{path}.embed_tokens", (input_ids, 0));
		using(~ctx) {
			var position_embeddings = ~nn.Embedding($"{path}.rotary_emb", (input_ids, 1));
			for(int i=0; i<config.decoder_num_hidden_layers; i++)
				MoonshineDecoderLayer($"{path}.layers.{i}", ref hidden_states, input_ids, position_embeddings, past_key_values, encoder_hidden_states);
		}
		hidden_states = ~ctx|nn.LayerNorm($"{path}.norm", ~hidden_states, config.layer_norm_eps);
		return hidden_states;
	}
	public Seq2SeqLMOutput MoonshineForConditionalGeneration(string path, TensorView input_features, TensorView decoder_input_ids, Cache past_key_values, Tensor encoder_hidden_states=default) {
		var path_ = path == null ? "" : path + ".";
		if(!encoder_hidden_states && input_features)
			encoder_hidden_states = MoonshineEncoder($"{path_}model.encoder", input_features);
		var decoder_hidden_states = MoonshineDecoder($"{path_}model.decoder", decoder_input_ids, past_key_values, encoder_hidden_states);
		var logits = !decoder_input_ids ? null : nn.Linear($"{path_}lm_head", decoder_hidden_states, fallback:$"{path_}model.decoder.embed_tokens");
		return new Seq2SeqLMOutput{
			logits=logits,
			decoder_hidden_states=decoder_hidden_states,
			encoder_hidden_states=encoder_hidden_states
		};
	}
	public override Seq2SeqLMOutput ForSpeechSeq2Seq(TensorView input_features, TensorView decoder_input_ids, Cache past_key_values, Tensor encoder_hidden_states=default)
		=> MoonshineForConditionalGeneration(null, input_features, decoder_input_ids, past_key_values, encoder_hidden_states:encoder_hidden_states);
}

// Moonshine uses Wav2Vec2Processor
[System.Serializable]
public class Wav2Vec2ProcessorConfig : PretrainedConfig {
	public SequenceFeatureExtractorConfig feature_extractor;
}
[RegisterModel(typeof(Wav2Vec2ProcessorConfig), "Wav2Vec2Processor")]
[ParityTolerance("input_features", 0f)]
public class Wav2Vec2Processor : AudioProcessor {
	public Wav2Vec2Processor(Functional fn, Wav2Vec2ProcessorConfig config): base(fn, config) {}
	public new Wav2Vec2ProcessorConfig config => (Wav2Vec2ProcessorConfig)base.config;
	public override SequenceFeatureExtractorConfig feature_extractor_config => config.feature_extractor;
	public override Tensor Process(TensorView audio) => fn.Clone(audio);
}
}