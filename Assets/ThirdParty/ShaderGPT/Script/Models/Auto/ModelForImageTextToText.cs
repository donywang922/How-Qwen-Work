using System.Collections.Generic;

namespace ShaderGPT {
public struct VlCausalLMOutput {
	public Tensor logits;
	public Tensor hidden_states;
	public Tensor image_hidden_states;
}
public abstract class ModelForImageTextToText : PretrainedModel {
	protected ModelForImageTextToText(Functional fn, PretrainedConfig config): base(fn, config) {}
	public abstract VlCausalLMOutput ForImageTextToText(TensorView input_ids, TensorView pixel_values, TensorView pixel_ids, Cache past_key_values);
	public abstract ModelForCausalLM GetDecoder();
	public abstract int patch_size { get; }
	public abstract int scale_factor { get; }

	public virtual Tensor ProcessImage(ref Tensor pixel_values) {
		var shapeI = pixel_values.size0 / patch_size;
		var shapeJ = pixel_values.size1 / patch_size;
		const int W = 2;
		var pixel_ids_data = new float[shapeI*shapeJ*4*W];
		for(int i=0; i<shapeI; i++)
		for(int j=0; j<shapeJ; j++) {
			int k = i/scale_factor*shapeJ/scale_factor+j/scale_factor;
			pixel_ids_data[(i*shapeJ+j)*4*W+0] = i;
			pixel_ids_data[(i*shapeJ+j)*4*W+1] = j;
			pixel_ids_data[(i*shapeJ+j)*4*W+2] = shapeI * shapeJ;
			pixel_ids_data[(i*shapeJ+j)*4*W+3] = k;
			pixel_ids_data[(i*shapeJ+j)*4*W+4] = shapeI;
			pixel_ids_data[(i*shapeJ+j)*4*W+5] = shapeJ;
		}
		var pixel_ids = ctx.CPUTensor(shapeI, shapeJ*W);
		pixel_ids.SetData(pixel_ids_data);
		pixel_ids = ~ctx|PixelUnshuffle_HWss_Crr(~pixel_ids, 1, scale_factor, C:4*W, D:4*W);
		pixel_values = ~ctx|PixelUnshuffle_HWss_Crr(~pixel_values, patch_size, scale_factor);
		return pixel_ids;
	}
	Tensor PixelUnshuffle_HWss_Crr(TensorView input, int r, int s=1, int C=4, int D=3) {
		int W = input.size1*4/C/r/s, H = input.size0/r/s;
		// [H*s*r, (W*s)*r*C] -> [H*(W*s)*s, D*r*r] -> [H*W*(s*s), D*r*r]
		var output = fn.AsStrided(input, size0:(H,W*s,s), size1:(D,r,r), stride0:(s*r,-r*C,r), stride1:(1,-1,C));
		if(s > 1)
			output = ~ctx|fn.AsStrided(~output, size0:(H*W,s,s), size1:(D,r,r), stride0:(s*s,1,s), stride1:(r*r,r,1));
		return output;
	}
}
}