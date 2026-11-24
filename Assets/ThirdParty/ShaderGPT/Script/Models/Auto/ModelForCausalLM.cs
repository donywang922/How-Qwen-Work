namespace ShaderGPT {
public struct CausalLMOutput {
	public Tensor logits;
	public Tensor hidden_states;
}
public abstract class ModelForCausalLM : GenerationMixin {
	protected ModelForCausalLM(Functional fn, PretrainedConfig config): base(fn, config) {}
	public abstract CausalLMOutput ForCausalLM(TensorView input_ids, Cache past_key_values);
}

public struct MaskedLMOutput {
	public Tensor logits;
	public Tensor hidden_states;
}
public abstract class ModelForMaskedLM : ModelForCausalLM {
	protected ModelForMaskedLM(Functional fn, PretrainedConfig config): base(fn, config) {}
	public abstract MaskedLMOutput ForMaskedLM(TensorView input_ids, (TensorView,int)? token_type_ids=null);
	public override CausalLMOutput ForCausalLM(TensorView input_ids, Cache past_key_values) {
		var o = ForMaskedLM(input_ids);
		return new CausalLMOutput{logits=o.logits, hidden_states=o.hidden_states};
	}
}

public struct Seq2SeqLMOutput {
	public Tensor logits;
	public Tensor decoder_hidden_states;
	public Tensor encoder_hidden_states;
}
public abstract class ModelForSeq2SeqLM : ModelForCausalLM {
	protected ModelForSeq2SeqLM(Functional fn, PretrainedConfig config): base(fn, config) {}
	public abstract Seq2SeqLMOutput ForSeq2SeqLM(TensorView input_ids, TensorView decoder_input_ids, Cache past_key_values, Tensor encoder_hidden_states=default);
	public override CausalLMOutput ForCausalLM(TensorView input_ids, Cache past_key_values) {
		var o = ForSeq2SeqLM(default, input_ids, past_key_values);
		return new CausalLMOutput{logits=o.logits, hidden_states=o.decoder_hidden_states};
	}
}
}