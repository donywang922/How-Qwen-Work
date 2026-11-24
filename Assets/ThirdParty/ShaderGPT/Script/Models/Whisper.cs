using System.Diagnostics.CodeAnalysis;

namespace ShaderGPT.Models {
[System.Serializable]
public class WhisperConfig : PretrainedConfig {
	public int d_model;
	public int encoder_layers;
	public int decoder_layers;
	public int encoder_attention_heads;
	public int decoder_attention_heads;
	public string activation_function;

	public int hidden_size => d_model;
	public string hidden_act => activation_function;
	public float layer_norm_eps => 1e-5f;
}
[RegisterModel(typeof(WhisperConfig), "whisper")]
[ParityTolerance(nameof(CausalLMOutput.logits), 2e-4f)] // whisper-base (CPU)
[ParityTolerance(nameof(CausalLMOutput.hidden_states), 2e-3f)]
[ParityTolerance(nameof(Seq2SeqLMOutput.encoder_hidden_states), 6e-3f)]
public class Whisper : ModelForSpeechSeq2Seq {
	public Whisper(Functional fn, WhisperConfig config): base(fn, config) {}
	public new WhisperConfig config => (WhisperConfig)base.config;

	Tensor WhisperAttention(string path, Tensor hidden_states, TensorView input_ids, Cache past_key_values,
			Tensor key_value_states=default, bool encoder=false, bool cross=false, Tensor residual=default) {
		var num_attention_heads = encoder ? config.encoder_attention_heads : config.decoder_attention_heads;
		using var _ = ~ctx;
		TensorView q=default, k=default, v=default;
		if(cross) {
			if(key_value_states)
				(k,v) = nn.Linear(($"{path}.k_proj", $"{path}.v_proj"), key_value_states);
			if(hidden_states)
				q = ~nn.Linear($"{path}.q_proj", hidden_states);
		} else
			(q,k,v) = nn.Linear(($"{path}.q_proj", $"{path}.k_proj", $"{path}.v_proj"), hidden_states);
		var keys   = past_key_values?.CrossUpdate($"{path}.k", k,  (input_ids, 1), cross, !(cross?hidden_states:encoder)) ?? k;
		var values = past_key_values?.CrossUpdate($"{path}.v", ~v, (input_ids, 1), cross, !(cross?hidden_states:encoder)) ?? ~v;
		if(!hidden_states)
			return hidden_states;

		var head_dim = config.hidden_size / num_attention_heads;
		hidden_states = ~ctx|fn.ScaledDotProductAttention(q, keys, values,
			heads:num_attention_heads, scale:1f/System.MathF.Sqrt(head_dim),
			window:!encoder&&!cross ? new(1-int.MaxValue, 1, 1, (input_ids, 1)) : null);
		hidden_states = ~ctx|nn.Linear($"{path}.out_proj", ~hidden_states, add:residual);
		return hidden_states;
	}
	Tensor WhisperFeedForward(string path, ref Tensor hidden_states, Tensor residual) {
		hidden_states = ~ctx|nn.Linear($"{path}.fc1", ~hidden_states);
		hidden_states = ~ctx|nn.Linear($"{path}.fc2", ~hidden_states, prolog:config.hidden_act, add:residual);
		return hidden_states;
	}
	void WhisperEncoderLayer(string path, ref Tensor hidden_states) {
		var attn_states = nn.LayerNorm($"{path}.self_attn_layer_norm", hidden_states, config.layer_norm_eps);
		hidden_states = ~ctx|WhisperAttention($"{path}.self_attn", ~attn_states, default, null, encoder:true, residual:~hidden_states);
		var mlp_states = nn.LayerNorm($"{path}.final_layer_norm", hidden_states, config.layer_norm_eps);
		hidden_states = ~ctx|WhisperFeedForward(path, ref mlp_states, residual:~hidden_states);
	}
	void WhisperDecoderLayer(string path, ref Tensor hidden_states, TensorView input_ids, Cache past_key_values, Tensor encoder_hidden_states=default) {
		if(!hidden_states) {
			WhisperAttention($"{path}.encoder_attn", hidden_states, input_ids, past_key_values, encoder_hidden_states, cross:true);
			return; // when hidden_states is null, only compute cross attn kv
		}
		var attn_states = nn.LayerNorm($"{path}.self_attn_layer_norm", hidden_states, config.layer_norm_eps);
		hidden_states = ~ctx|WhisperAttention($"{path}.self_attn", ~attn_states, input_ids, past_key_values, residual:~hidden_states);
		attn_states = nn.LayerNorm($"{path}.encoder_attn_layer_norm", hidden_states, config.layer_norm_eps);
		hidden_states = ~ctx|WhisperAttention($"{path}.encoder_attn", ~attn_states, input_ids, past_key_values,
			encoder_hidden_states, cross:true, residual:~hidden_states);
		var mlp_states = nn.LayerNorm($"{path}.final_layer_norm", hidden_states, config.layer_norm_eps);
		hidden_states = ~ctx|WhisperFeedForward(path, ref mlp_states, residual:~hidden_states);
	}
	Tensor WhisperEncoder(string path, TensorView input_features) {
		var inputs_embeds = nn.Conv1d($"{path}.conv1", input_features, kernel_size:3);
		inputs_embeds = ~ctx|fn.Act2Fn("gelu", ~inputs_embeds);
		inputs_embeds = ~ctx|nn.Conv1d($"{path}.conv2", ~inputs_embeds, kernel_size:3, stride:2, padding:1);
		inputs_embeds = ~ctx|fn.Act2Fn("gelu", ~inputs_embeds);
		inputs_embeds = ~ctx|fn.Transpose(~inputs_embeds);
		var hidden_states = ~ctx|fn.Add(~inputs_embeds, state_dict[$"{path}.embed_positions.weight"]);
		for(int i=0; i<config.encoder_layers; i++)
			WhisperEncoderLayer($"{path}.layers.{i}", ref hidden_states);
		hidden_states = ~ctx|nn.LayerNorm($"{path}.layer_norm", ~hidden_states, config.layer_norm_eps);
		return hidden_states;
	}
	Tensor WhisperDecoder(string path, TensorView input_ids, Cache past_key_values, Tensor encoder_hidden_states=default) {
		var hidden_states = default(Tensor);
		if(!input_ids) {
			for(int i=0; i<config.decoder_layers; i++)
				WhisperDecoderLayer($"{path}.layers.{i}", ref hidden_states, input_ids, past_key_values, encoder_hidden_states);
			return hidden_states;
		}
		hidden_states = nn.Embedding($"{path}.embed_tokens", (input_ids, 0));
		{
			var position_embeddings = nn.Embedding($"{path}.embed_positions", (input_ids, 1));
			hidden_states = ~ctx|fn.Add(~hidden_states, ~position_embeddings);
			for(int i=0; i<config.decoder_layers; i++)
				WhisperDecoderLayer($"{path}.layers.{i}", ref hidden_states, input_ids, past_key_values, encoder_hidden_states);
		}
		hidden_states = ~ctx|nn.LayerNorm($"{path}.layer_norm", ~hidden_states, config.layer_norm_eps);
		return hidden_states;
	}
	public Seq2SeqLMOutput WhisperForConditionalGeneration(string path, TensorView input_features, TensorView decoder_input_ids, Cache past_key_values, Tensor encoder_hidden_states=default) {
		var path_ = path == null ? "" : path + ".";
		if(!encoder_hidden_states && input_features)
			encoder_hidden_states = WhisperEncoder($"{path_}model.encoder", input_features);
		var decoder_hidden_states = WhisperDecoder($"{path_}model.decoder", decoder_input_ids, past_key_values, encoder_hidden_states);
		var logits = !decoder_input_ids ? null : nn.Linear($"{path_}proj_out", decoder_hidden_states, fallback:$"{path_}model.decoder.embed_tokens");
		return new Seq2SeqLMOutput{
			logits=logits,
			decoder_hidden_states=decoder_hidden_states,
			encoder_hidden_states=encoder_hidden_states
		};
	}
	public override Seq2SeqLMOutput ForSpeechSeq2Seq(TensorView input_features, TensorView decoder_input_ids, Cache past_key_values, Tensor encoder_hidden_states=default)
		=> WhisperForConditionalGeneration(null, input_features, decoder_input_ids, past_key_values, encoder_hidden_states:encoder_hidden_states);

	// custom logits processors for Whisper
	void SuppressTokensLogitsProcessor(TensorView input_ids, ref Tensor scores, params int[] suppress_tokens) {
		var data = new float[scores.size1*4];
		foreach(var token in suppress_tokens)
			data[token] = float.NegativeInfinity;
		var add = ctx.CPUTensor(1, scores.size1);
		add.SetData(data);
		scores = ~ctx|fn.Add(~scores, ~add);
	}
	void WhisperTimeStampLogitsProcessor(TensorView input_ids, ref Tensor scores, int no_timestamps_token_id, int vocab_size) {
		var timestamp_begin = no_timestamps_token_id+1;
		var probs    = fn.Softmax(scores, window:new(0, vocab_size));
		var ts_prob  = fn.Sum(probs, window:new(timestamp_begin, vocab_size));
		var max_prob = ~ctx|fn.Amax(~probs, window:new(0, timestamp_begin));
		var mask     = ~ctx|fn.Sub(~ts_prob, ~max_prob);
		mask = ~ctx|fn.Copy_(fn.EmptyLike(scores), ~mask).tensor;
		mask = ~ctx|fn.Clamp(~mask, min:0, window:new(0, timestamp_begin));
		scores = ~ctx|fn.Where(~mask, float.NegativeInfinity, ~scores);
	}
	private int no_timestamps_token_id => 50363; // TODO: remove hardcode
	public override Tensor Generate(TensorView input, ref Tensor scores, [NotNull] Cache cache) {
		var inputs = cache.Update("inputs", input, (input, 1));
		scores = ~ctx|fn.ToFloat(~scores[^1.., ..]);
		SuppressTokensLogitsProcessor(inputs, ref scores, no_timestamps_token_id);
		WhisperTimeStampLogitsProcessor(inputs, ref scores, no_timestamps_token_id, config.vocab_size);
		return ~ctx|fn.Argmax(~scores, window:new(0, config.vocab_size));
	}
}

[System.Serializable]
public class WhisperFeatureExtractorConfig : SequenceFeatureExtractorConfig {
	public int n_fft;
	public int hop_length;
}
[System.Serializable]
public class WhisperProcessorConfig : PretrainedConfig {
	public WhisperFeatureExtractorConfig feature_extractor;
}
[RegisterModel(typeof(WhisperProcessorConfig), "WhisperProcessor")]
[ParityTolerance("input_features", 2e-4f)]
public class WhisperProcessor : AudioProcessor {
	public WhisperProcessor(Functional fn, WhisperProcessorConfig config): base(fn, config) {}
	public new WhisperProcessorConfig config => (WhisperProcessorConfig)base.config;
	public override SequenceFeatureExtractorConfig feature_extractor_config => config.feature_extractor;

	public Tensor WhisperFeatureExtractor(string path, TensorView input) {
		var path_ = path == null ? "" : path + ".";
		var (re, im) = fn.STFT(input, config.feature_extractor.n_fft, config.feature_extractor.hop_length, window:UnityEngine.FFTWindow.Hanning);
		var x = ~ctx|fn.AddCMul_(fn.Mul(~re, re), im, im);
		x = ~ctx|nn.ConvTranspose1d($"{path_}mel_filters", ~x, kernel_size:1);
		x = ~ctx|fn.Clamp(~x, min:1e-10f);
		x = ~ctx|fn.Log(~x, 10);
		x = ~ctx|fn.Mul(~x, 0.25f, add:1);

		var max = fn.Amax(x);
		max = ~ctx|fn.Add(~max, -2);
		max = ~ctx|fn.Sub(~max, x);
		max = ~ctx|fn.Clamp(~max, min:0f);
		return ~ctx|fn.Add(~x, ~max); // max(x, max-2)
	}
	public override Tensor Process(TensorView audio) => WhisperFeatureExtractor("feature_extractor", audio);
}
}