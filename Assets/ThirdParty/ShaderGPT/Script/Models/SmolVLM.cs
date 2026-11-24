using System.Diagnostics.CodeAnalysis;

namespace ShaderGPT.Models {
[System.Serializable]
public class SmolVLMConfig : PretrainedConfig {
	public LlamaConfig text_config;
	public Idefics3VisionConfig vision_config;
	public int scale_factor;
	public int image_token_id;
	protected override void FromPretrained(string json) {
		base.FromPretrained(json);
		text_config.FromPretrained();
	}
}
[RegisterModel(typeof(SmolVLMConfig), "smolvlm")]
[ParityTolerance(nameof(VlCausalLMOutput.image_hidden_states), 2e-4f)] // SmolVLM2-256M-Video-Instruct-Q8_0
[ParityTolerance(nameof(VlCausalLMOutput.hidden_states), 3e-4f)]
[ParityTolerance(nameof(VlCausalLMOutput.logits), 9e-5f)]
public class SmolVLM : ModelForImageTextToText {
	public Llama text_model;
	public Idefics3Vision vision_model;
	public SmolVLM(Functional fn, [NotNull] SmolVLMConfig config): base(fn, config) {
		text_model = new Llama(fn, config.text_config){state_dict=state_dict};
		vision_model = new Idefics3Vision(fn, config.vision_config){state_dict=state_dict};
	}
	public new SmolVLMConfig config => (SmolVLMConfig)base.config;
	public override ModelForCausalLM GetDecoder() => text_model;
	public override int patch_size => config.vision_config.patch_size;
	public override int scale_factor => config.scale_factor;

	Tensor SmolVLMConnector(string path, ref Tensor hidden_states) {
		hidden_states = ~ctx|fn.Reshape(~hidden_states, hidden_states.size0/scale_factor/scale_factor);
		hidden_states = ~ctx|nn.Linear($"{path}.modality_projection.proj", ~hidden_states);
		return hidden_states;
	}
	public (Tensor,Tensor) SmolVLMModel(string path, TensorView input_ids, TensorView pixel_values, TensorView pixel_ids, [NotNull] Cache past_key_values) {
		var path_ = path == null ? "" : path + ".";
		Tensor image_features = default, hidden_states = default;
		if(pixel_values) {
			var image_outputs = vision_model.Idefics3VisionTransformer($"{path_}vision_model", pixel_values, pixel_ids);
			image_features = SmolVLMConnector($"{path_}connector", ref image_outputs);
			using(~ctx) {
				var merged_pixel_ids = ~fn.Reshape(pixel_ids, pixel_ids.size0/scale_factor/scale_factor);
				past_key_values.Update($"{path}.image_features", image_features, (merged_pixel_ids, 3),
					maxLen:text_model.generation_config.max_length/scale_factor/scale_factor);
			}
		}
		if(input_ids) {
			var inputs_embeds = nn.Embedding($"{path_}text_model.embed_tokens", (input_ids, 0));
			var input_ids_neg = fn.Mul(input_ids, -1, add:-1);
			var image_feats = ~ctx|fn.IndexSelect(past_key_values.GetValueOrDefault($"{path}.image_features"), (~input_ids_neg, 0));
			inputs_embeds = ~ctx|fn.Add(~inputs_embeds, ~image_feats);
			hidden_states = text_model.LlamaModel($"{path_}text_model", input_ids, past_key_values, inputs_embeds);
		}
		return (hidden_states, image_features);
	}
	public VlCausalLMOutput SmolVLMForConditionalGeneration(string path, TensorView input_ids, TensorView pixel_values, TensorView pixel_ids, Cache past_key_values) {
		var path_ = path == null ? "" : path + ".";
		var (hidden_states, image_features) = SmolVLMModel($"{path_}model", input_ids, pixel_values, pixel_ids, past_key_values);
		var logits = !hidden_states ? null : nn.Linear($"{path_}lm_head", hidden_states, fallback:$"{path_}model.text_model.embed_tokens");
		return new VlCausalLMOutput{logits=logits, hidden_states=hidden_states, image_hidden_states=image_features};
	}
	public override VlCausalLMOutput ForImageTextToText(TensorView input_ids, TensorView pixel_values, TensorView pixel_ids, Cache past_key_values)
		=> SmolVLMForConditionalGeneration(null, input_ids, pixel_values, pixel_ids, past_key_values);
}
}