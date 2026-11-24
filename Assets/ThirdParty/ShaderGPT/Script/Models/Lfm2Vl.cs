using System.Diagnostics.CodeAnalysis;

namespace ShaderGPT.Models {
[System.Serializable]
public class Lfm2VlConfig : PretrainedConfig {
	public Lfm2Config text_config;
	public Siglip2VisionConfig vision_config;
	public int downsample_factor;
	public string projector_hidden_act;
	public bool projector_use_layernorm;
	public int image_token_id;
	protected override void FromPretrained(string json) {
		base.FromPretrained(json);
		text_config.FromPretrained();
	}
}
[RegisterModel(typeof(Lfm2VlConfig), "lfm2_vl")]
[ParityTolerance(nameof(VlCausalLMOutput.image_hidden_states), 8e-3f)] // LFM2.5-VL-450M-Q8_0
[ParityTolerance(nameof(VlCausalLMOutput.hidden_states), 7e-4f)]
[ParityTolerance(nameof(VlCausalLMOutput.logits), 2e-4f)]
public class Lfm2Vl : ModelForImageTextToText {
	public Lfm2 language_model;
	public Siglip2Vision vision_model;
	public Lfm2Vl(Functional fn, [NotNull] Lfm2VlConfig config): base(fn, config) {
		language_model = new Lfm2(fn, config.text_config){state_dict=state_dict};
		vision_model = new Siglip2Vision(fn, config.vision_config){state_dict=state_dict};
	}
	public new Lfm2VlConfig config => (Lfm2VlConfig)base.config;
	public override ModelForCausalLM GetDecoder() => language_model;
	public override int patch_size => config.vision_config.patch_size;
	public override int scale_factor => config.downsample_factor;

	public override Tensor ProcessImage(ref Tensor pixel_values) {
		var pixel_ids = base.ProcessImage(ref pixel_values);
		pixel_values = ~ctx|fn.AsStrided(~pixel_values, size0:(pixel_values.size0,1,1), size1:(patch_size,patch_size,3),
			stride0:(1,0,0), stride1:(patch_size,1,patch_size*patch_size));
		return pixel_ids;
	}
	Tensor Lfm2VlMultiModalProjector(string path, ref Tensor hidden_states) {
		hidden_states = ~ctx|fn.Reshape(~hidden_states, hidden_states.size0/scale_factor/scale_factor);
		if(config.projector_use_layernorm)
			hidden_states = ~ctx|nn.LayerNorm($"{path}.layer_norm", ~hidden_states, eps:1e-5f);
		hidden_states = ~ctx|nn.Linear($"{path}.linear_1", ~hidden_states);
		hidden_states = ~ctx|nn.Linear($"{path}.linear_2", ~hidden_states, prolog:config.projector_hidden_act);
		return hidden_states;
	}
	public (Tensor,Tensor) Lfm2VlModel(string path, TensorView input_ids, TensorView pixel_values, TensorView pixel_ids, [NotNull] Cache past_key_values) {
		var path_ = path == null ? "" : path + ".";
		Tensor image_features = default, hidden_states = default;
		if(pixel_values) {
			var image_outputs = vision_model.Siglip2VisionTransformer($"{path_}vision_tower", pixel_values, pixel_ids);
			image_features = Lfm2VlMultiModalProjector($"{path_}multi_modal_projector", ref image_outputs);
			using(~ctx) {
				var merged_pixel_ids = ~fn.Reshape(pixel_ids, pixel_ids.size0/scale_factor/scale_factor);
				past_key_values.Update($"{path}.image_features", image_features, (merged_pixel_ids, 3),
					maxLen:language_model.generation_config.max_length/scale_factor/scale_factor);
			}
		}
		if(input_ids) {
			var inputs_embeds = nn.Embedding($"{path_}language_model.embed_tokens", (input_ids, 0));
			var input_ids_neg = fn.Mul(input_ids, -1, add:-1);
			var image_feats = ~ctx|fn.IndexSelect(past_key_values.GetValueOrDefault($"{path}.image_features"), (~input_ids_neg, 0));
			inputs_embeds = ~ctx|fn.Add(~inputs_embeds, ~image_feats);
			hidden_states = language_model.Lfm2Model($"{path_}language_model", input_ids, past_key_values, inputs_embeds);
		}
		return (hidden_states, image_features);
	}
	public VlCausalLMOutput Lfm2VlForConditionalGeneration(string path, TensorView input_ids, TensorView pixel_values, TensorView pixel_ids, Cache past_key_values) {
		var path_ = path == null ? "" : path + ".";
		var (hidden_states, image_features) = Lfm2VlModel($"{path_}model", input_ids, pixel_values, pixel_ids, past_key_values);
		var logits = !hidden_states ? null : nn.Linear($"{path_}lm_head", hidden_states, fallback:$"{path_}model.language_model.embed_tokens");
		return new VlCausalLMOutput{logits=logits, hidden_states=hidden_states, image_hidden_states=image_features};
	}
	public override VlCausalLMOutput ForImageTextToText(TensorView input_ids, TensorView pixel_values, TensorView pixel_ids, Cache past_key_values)
		=> Lfm2VlForConditionalGeneration(null, input_ids, pixel_values, pixel_ids, past_key_values);
}
}