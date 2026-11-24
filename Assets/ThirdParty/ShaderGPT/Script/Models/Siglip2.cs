namespace ShaderGPT.Models {
[System.Serializable]
public class Siglip2VisionConfig : SiglipVisionConfig {
	public int num_patches;
}
[RegisterModel(typeof(Siglip2VisionConfig), "siglip2_vision_model")]
public class Siglip2Vision : SiglipVision {
	public Siglip2Vision(Functional fn, Siglip2VisionConfig config) : base(fn, config) {}
	public new Siglip2VisionConfig config => (Siglip2VisionConfig)base.config;

	Tensor Siglip2VisionEmbeddings(string path, TensorView pixel_values, TensorView pixel_ids) {
		// F.interpolate(align_corners=False) => (i+0.5)/size*sqrt_num_pos-0.5
		var sqrt_num_pos = (int)System.MathF.Round(System.MathF.Sqrt(config.num_patches));
		var coords = fn.Mul(pixel_ids[.., ..1], sqrt_num_pos, add:0.5f*sqrt_num_pos);
		coords = ~ctx|fn.Div(~coords, pixel_ids[.., 1..2], add:-0.5f);
		var patch_pos_embed = ~ctx|InterpolatePosEncoding(state_dict[$"{path}.position_embedding.weight"], ~coords, sqrt_num_pos, mode:"bilinear");
		return ~ctx|nn.Linear($"{path}.patch_embedding", pixel_values, add:~patch_pos_embed);
	}
	protected override Tensor SiglipVisionEmbeddings(string path, TensorView pixel_values, TensorView pixel_ids)
		=> Siglip2VisionEmbeddings(path, pixel_values, pixel_ids);
	public Tensor Siglip2VisionTransformer(string path, TensorView pixel_values, TensorView pixel_ids)
		=> SiglipVisionTransformer(path, pixel_values, pixel_ids);
}
}