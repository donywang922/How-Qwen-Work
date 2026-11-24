namespace ShaderGPT.Models {
[System.Serializable]
public class Idefics3VisionConfig : SiglipVisionConfig {}
public class Idefics3Vision : SiglipVision {
	public Idefics3Vision(Functional fn, Idefics3VisionConfig config) : base(fn, config) {}
	public new Idefics3VisionConfig config => (Idefics3VisionConfig)base.config;

	Tensor Idefics3VisionEmbeddings(string path, TensorView pixel_values, TensorView pixel_ids) {
		// floor(i/size*sqrt_num_patches) => round((i*sqrt_num_pos+0.5)/size-0.5)
		var sqrt_num_pos = config.image_size/config.patch_size;
		var coords = fn.Mul(pixel_ids[.., ..1], sqrt_num_pos, add:0.5f);
		coords = ~ctx|fn.Div(~coords, pixel_ids[.., 1..2], add:-0.5f);
		var patch_pos_embed = ~ctx|InterpolatePosEncoding(state_dict[$"{path}.position_embedding.weight"], ~coords, sqrt_num_pos);
		return ~ctx|nn.Linear($"{path}.patch_embedding", pixel_values, add:~patch_pos_embed);
	}
	protected override Tensor SiglipVisionEmbeddings(string path, TensorView pixel_values, TensorView pixel_ids)
		=> Idefics3VisionEmbeddings(path, pixel_values, pixel_ids);
	public Tensor Idefics3VisionTransformer(string path, TensorView pixel_values, TensorView pixel_ids)
		=> SiglipVisionTransformer(path, pixel_values, pixel_ids);
}
}