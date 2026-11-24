namespace ShaderGPT {
#pragma warning disable CA1715
public interface TextToWaveformOutput {
	public Tensor waveform {get;set;}
	public Tensor sequence_lengths {get;set;}
}
#pragma warning restore CA1715
public abstract class ModelForTextToWaveform : PretrainedModel {
	public abstract int sampling_rate {get;}
	public abstract int upsample_rate {get;}
	protected ModelForTextToWaveform(Functional fn, PretrainedConfig config): base(fn, config) {}
	public abstract Tensor GetSpeakerEmbedding((TensorView,int) speaker_id);
	public abstract TextToWaveformOutput ForTextToWaveform(TensorView input_ids, TensorView speaker_embeddings, TensorView generation_input);
}

public abstract class ModelForSpeechSeq2Seq : ModelForSeq2SeqLM {
	protected ModelForSpeechSeq2Seq(Functional fn, PretrainedConfig config): base(fn, config) {}
	public abstract Seq2SeqLMOutput ForSpeechSeq2Seq(TensorView input_features, TensorView decoder_input_ids, Cache past_key_values, Tensor encoder_hidden_states=default);
	public override Seq2SeqLMOutput ForSeq2SeqLM(TensorView input_ids, TensorView decoder_input_ids, Cache past_key_values, Tensor encoder_hidden_states=default) {
		return ForSpeechSeq2Seq(input_ids, decoder_input_ids, past_key_values, encoder_hidden_states);
	}
}

[System.Serializable]
public class SequenceFeatureExtractorConfig {
	public int feature_size;
	public int sampling_rate;
}
public abstract class AudioProcessor : PretrainedModel {
	public abstract SequenceFeatureExtractorConfig feature_extractor_config {get;}
	protected AudioProcessor(Functional fn, PretrainedConfig config): base(fn, config) {}
	public abstract Tensor Process(TensorView audio);
}
}