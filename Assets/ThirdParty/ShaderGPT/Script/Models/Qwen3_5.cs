using System.Diagnostics.CodeAnalysis;

namespace ShaderGPT.Models {
[System.Serializable]
public class Qwen3_5Config : PretrainedConfig {
	public Qwen3_5TextConfig text_config;
	public Qwen3_5VisionConfig vision_config;
	protected override void FromPretrained(string json) {
		base.FromPretrained(json);
		text_config.FromPretrained();
		vision_config.FromPretrained();
	}
}
[RegisterModel(typeof(Qwen3_5Config), "qwen3_5")]
[ParityTolerance(nameof(VlCausalLMOutput.logits), 3e-4f)] // Qwen3.5-0.8B (512px)
[ParityTolerance(nameof(VlCausalLMOutput.hidden_states), 5e-2f)]
[ParityTolerance(nameof(VlCausalLMOutput.image_hidden_states), 2e-3f)]
public class Qwen3_5 : ModelForImageTextToText {
	public Qwen3_5Text language_model;
	public Qwen3_5Vision vision_model;
	public Qwen3_5(Functional fn, [NotNull] Qwen3_5Config config): base(fn, config) {
		language_model = new Qwen3_5Text(fn, config.text_config){state_dict=state_dict};
		vision_model = new Qwen3_5Vision(fn, config.vision_config){state_dict=state_dict};
	}
	public new Qwen3_5Config config => (Qwen3_5Config)base.config;
	public override ModelForCausalLM GetDecoder() => language_model;
	public override int patch_size => config.vision_config.patch_size;
	public override int scale_factor => config.vision_config.spatial_merge_size;

	public override Tensor ProcessImage(ref Tensor pixel_values) {
		var pixel_ids = base.ProcessImage(ref pixel_values);
		if(config.vision_config.temporal_patch_size > 1)
			pixel_values = ~ctx|fn.RepeatInterleave(~pixel_values, (1, config.vision_config.temporal_patch_size),
				(pixel_values.size0, pixel_values.size1*4));
		return pixel_ids;
	}
	public (Tensor, Tensor) Qwen3_5Model(string path, TensorView input_ids, TensorView pixel_values, TensorView pixel_ids, [NotNull] Cache past_key_values) {
		var path_ = path == null ? "" : path + ".";
		Tensor image_features = default, hidden_states = default;
		if(pixel_values) {
			image_features = vision_model.Qwen3_5VisionModel($"{path_}visual", pixel_values, pixel_ids);
			using(~ctx) {
				var merged_pixel_ids = ~fn.Reshape(pixel_ids, pixel_ids.size0/scale_factor/scale_factor);
				past_key_values.Update($"{path}.image_features", image_features, (merged_pixel_ids, 3),
					maxLen:language_model.generation_config.max_length/scale_factor/scale_factor);
				var mrope_ids = ~language_model.Prepare3DPositionIds(merged_pixel_ids[.., ^2..], scale_factor);
				past_key_values.Update($"{path}.mrope_ids", mrope_ids, (merged_pixel_ids, 3),
					maxLen:language_model.generation_config.max_length/scale_factor/scale_factor);
			}
		}
		if(input_ids) {
			var inputs_embeds = nn.Embedding($"{path_}language_model.embed_tokens", (input_ids, 0));
			var input_ids_neg = fn.Mul(input_ids, -1, add:-1);
			var mrope_ids = fn.IndexSelect(past_key_values.GetValueOrDefault($"{path}.mrope_ids"), (input_ids_neg, 0));
			var image_feats = ~ctx|fn.IndexSelect(past_key_values.GetValueOrDefault($"{path}.image_features"), (~input_ids_neg, 0));
			inputs_embeds = ~ctx|fn.Add(~inputs_embeds, ~image_feats);
			input_ids = ~ctx|fn.Concat((input_ids, ~mrope_ids), axis:1);
			hidden_states = ~ctx|language_model.Qwen3_5TextModel($"{path_}language_model", ~input_ids, past_key_values, inputs_embeds);
		}
		return (hidden_states, image_features);
	}
	public VlCausalLMOutput Qwen3_5ForConditionalGeneration(string path, TensorView input_ids, TensorView pixel_values, TensorView pixel_ids, Cache past_key_values) {
		var path_ = path == null ? "" : path + ".";
		FixSize0($"{path_}lm_head.weight.T", config.text_config.hidden_size);
		FixSize0($"{path_}model.language_model.embed_tokens.weight.T", config.text_config.hidden_size);
		var (hidden_states, image_features) = Qwen3_5Model($"{path_}model", input_ids, pixel_values, pixel_ids, past_key_values);
		var logits = !hidden_states ? null : nn.Linear($"{path_}lm_head", hidden_states, fallback:$"{path_}model.language_model.embed_tokens");
		return new VlCausalLMOutput{logits=logits, hidden_states=hidden_states, image_hidden_states=image_features};
	}
	public override VlCausalLMOutput ForImageTextToText(TensorView input_ids, TensorView pixel_values, TensorView pixel_ids, Cache past_key_values)
		=> Qwen3_5ForConditionalGeneration(null, input_ids, pixel_values, pixel_ids, past_key_values);
}
}