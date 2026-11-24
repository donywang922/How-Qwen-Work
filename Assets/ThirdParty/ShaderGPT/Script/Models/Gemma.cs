using System.Diagnostics.CodeAnalysis;

namespace ShaderGPT.Models {
[System.Serializable]
public class GemmaConfig : LlamaConfig {
	public string hidden_activation;
	protected override void FromPretrained(string json) {
		base.FromPretrained(json);
		hidden_act = string.IsNullOrEmpty(hidden_activation) ? "gelu_pytorch_tanh" : hidden_activation;
	}
}
[RegisterModel(typeof(GemmaConfig), "gemma")]
[ParityTolerance(nameof(CausalLMOutput.hidden_states), 1e-5f)] // gemma-tiny-random
[ParityTolerance(nameof(CausalLMOutput.logits), 1e-5f)]
public class Gemma : Llama {
	public Gemma(Functional fn, GemmaConfig config): base(fn, config) {}
	public new GemmaConfig config => (GemmaConfig)base.config;

	public Tensor GemmaModel(string path, TensorView input_ids, Cache past_key_values, Tensor input_embeds=default) {
		var path_ = path == null ? "" : path + ".";
		return base.LlamaModel(path, input_ids, past_key_values, input_embeds ? input_embeds :
			~ctx|fn.Mul(~nn.Embedding($"{path_}embed_tokens", (input_ids, 0)), System.MathF.Sqrt(config.hidden_size)));
	}
	public override Tensor LlamaModel(string path, TensorView input_ids, Cache past_key_values, Tensor input_embeds=default)
		=> GemmaModel(path, input_ids, past_key_values, input_embeds);
	[ExcludeFromCodeCoverage]
	public CausalLMOutput GemmaForCausalLM(string path, TensorView input_ids, Cache past_key_values)
		=> LlamaForCausalLM(path, input_ids, past_key_values);
}
}