using UnityEngine;
using System.Text.RegularExpressions;
using System.Linq;

namespace ShaderGPT.Models {
[System.Serializable]
public class VitsConfig : PretrainedConfig {
	public int hidden_size;
	public int num_hidden_layers;
	public int num_attention_heads;
	public int window_size;
	public int ffn_kernel_size;
	public string hidden_act;
	public float layer_norm_eps;
	public int num_speakers;
	public int speaker_embedding_size;
	public int[] upsample_rates;
	public int[] upsample_kernel_sizes;
	public int[] resblock_kernel_sizes;
	public int[] resblock_dilation_sizes; // flattened
	public float leaky_relu_slope;
	public int prior_encoder_num_flows;
	public int prior_encoder_num_wavenet_layers;
	public int wavenet_kernel_size;
	public int wavenet_dilation_rate;
	public float speaking_rate;
	public float noise_scale;
	public int sampling_rate;
	protected override void FromPretrained(string json)
		=> base.FromPretrained(Regex.Replace(Regex.Replace(Regex.Replace(
			json, @"\[\s+\[", "["), @"\]\s+\]", "]"), @"\],\s+\[", ",")); // help unity parse nested array
}
[RegisterModel(typeof(VitsConfig), "vits")]
[ParityTolerance(nameof(VitsModelOutput.hidden_states), 4e-6f)] // mms-tts-eng
[ParityTolerance(nameof(VitsModelOutput.spectrogram), 2e-5f)]
[ParityTolerance(nameof(VitsModelOutput.waveform), 4e-4f)]
public partial class Vits : ModelForTextToWaveform {
	public VitsDecoder decoder;
	public Vits(Functional fn, VitsConfig config): base(fn, config) {
		decoder = new VitsDecoder(fn, config){state_dict=state_dict};
	}
	public new VitsConfig config => (VitsConfig)base.config;
	public override int sampling_rate => config.sampling_rate;
	public override int upsample_rate => decoder.upsample_rate;
	public override Tensor GetSpeakerEmbedding((TensorView,int) speaker_id)
		=> config.num_speakers > 1 ? nn.Embedding("embed_speaker", speaker_id) : null;
	public override TextToWaveformOutput ForTextToWaveform(TensorView input_ids, TensorView speaker_embeddings, TensorView generation_input)
		=> VitsModel(null, input_ids, speaker_embeddings, generation_input);

	public VitsModelOutput VitsModel(string path, TensorView input_ids, TensorView speaker_embeddings, TensorView null_indices, bool output_hidden_states=false) {
		Debug.Assert(!null_indices && null_indices.size0 > 0, "null_indices must be a null-backed capacity view.");
		var path_ = path == null ? "" : path + ".";
		var input_length = (input_ids[..1, ..1], 2);
		var input_mask = new Window(-input_ids.size0, 0, 1, input_length);
		var (hidden_states, stats) = VitsTextEncoder($"{path_}text_encoder", input_ids, input_mask);
		if(!output_hidden_states)
			ctx.Release(hidden_states);

		var duration = ~ctx|fn.Mul(~fn.Transpose(input_ids)[3..4, ..], 1f, window:input_mask);
		var indices = ~ctx|MakeIndices(~duration, null_indices.size0);
		var priors = ~ctx|fn.IndexSelect(~stats, (indices, 0));
		var (prior_means, prior_log_variances) = priors.Chunk2(axis:1);
		var prior_variances = ~ctx|fn.Normal(0, ~fn.Exp(prior_log_variances));
		var prior_latents = ~ctx|fn.Add(~prior_means, ~prior_variances, config.noise_scale);
		var latents = ~ctx|fn.Transpose(~prior_latents);

		var output_mask = new Window(-indices.size0, 0, 1, (indices[..1, ..], 2));
		VitsResidualCouplingBlock($"{path_}flow", ref latents, output_mask, speaker_embeddings, reverse:true);
		var waveform = decoder.VitsHifiGan($"{path_}decoder", latents, output_mask, speaker_embeddings);
		if(!output_hidden_states)
			ctx.Release(latents);
		var sequence_lengths = ~ctx|fn.Swizzle(~indices[..1, ..], new(0, 0, upsample_rate, 0), default, default, default);
		return new VitsModelOutput{
			waveform=waveform, sequence_lengths=sequence_lengths,
			spectrogram=latents, hidden_states=hidden_states,
		};
	}
	Tensor MakeIndices(Tensor duration, int max_length) {
		var cum_duration = fn.Cumsum(duration, axis:1);
		var indices = fn.Zeros(1, (max_length+3)/4);
		fn.IndexFill_(indices, (cum_duration, 0), 1f, axis:1);
		var length = ~ctx|fn.Swizzle(~cum_duration[..1, ^1..], default, default, new(0,0,0,1), default);
		indices = ~ctx|fn.Cumsum(~indices, axis:1);
		indices = ~ctx|fn.Transpose(~indices);
		indices = ~ctx|fn.Add(~indices, ~length);
		return indices;
	}

	// approximate VitsDurationPredictor with average duration for blank and non-blank tokens
	public Tensor VitsDurationEmbedding(int[] input_ids, float[] durations) {
		var blank_token_id = input_ids?[0] ?? 0;
		var rows = Enumerable.Range(1, (input_ids?.Length??2)-2);
		var blankDuration = Mathf.Max(1, Mathf.Floor(rows.Where(i => input_ids[i] == blank_token_id).Average(i => durations?[i]) ?? 1));
		var tokenDuration = Mathf.Max(1, Mathf.Ceil (rows.Where(i => input_ids[i] != blank_token_id).Average(i => durations?[i]) ?? 3));
		var embedding = ctx.CPUTensor(state_dict["text_encoder.embed_tokens.weight"].size0, 1);
		embedding.SetData(Enumerable.Range(0, embedding.size0*4).Select(
			i => i%4 != 3 ? 0 : i/4 == blank_token_id ? blankDuration : tokenDuration).ToArray());
		return embedding;
	}
}
public struct VitsModelOutput : TextToWaveformOutput {
	public Tensor waveform {get;set;}
	public Tensor sequence_lengths {get;set;}
	public Tensor spectrogram;
	public Tensor hidden_states;
}
}