using System.Diagnostics.CodeAnalysis;

namespace ShaderGPT.Models {
[System.Serializable]
public class SiglipVisionConfig : PretrainedConfig {
	public int hidden_size;
	public int num_hidden_layers;
	public int num_attention_heads;
	public int patch_size;
	public string hidden_act;
	public float layer_norm_eps;
	public int image_size;
}
[RegisterModel(typeof(SiglipVisionConfig), "siglip_vision_model")]
public class SiglipVision : PretrainedModel {
	public SiglipVision(Functional fn, SiglipVisionConfig config) : base(fn, config) {}
	public new SiglipVisionConfig config => (SiglipVisionConfig)base.config;
	protected int head_dim => config.hidden_size / config.num_attention_heads;

	Tensor SiglipAttention(string path, ref Tensor hidden_states, TensorView pixel_ids, Tensor residual) {
		var (q,k,v) = ~ctx|nn.Linear(($"{path}.q_proj", $"{path}.k_proj", $"{path}.v_proj"), ~hidden_states);
		hidden_states = ~ctx|fn.ScaledDotProductAttention(q, k, ~v,
			heads:config.num_attention_heads, scale:1f/System.MathF.Sqrt(head_dim),
			window:new(int.MinValue, 0, 1, (pixel_ids, 2)));
		return ~ctx|nn.Linear($"{path}.out_proj", ~hidden_states, add:residual);
	}
	Tensor SiglipMLP(string path, ref Tensor hidden_states, Tensor residual) {
		hidden_states = ~ctx|nn.Linear($"{path}.fc1", ~hidden_states);
		return ~ctx|nn.Linear($"{path}.fc2", ~hidden_states, prolog:config.hidden_act, add:residual);
	}
	void SiglipEncoderLayer(string path, ref Tensor hidden_states, TensorView pixel_ids) {
		var attn_input = nn.LayerNorm($"{path}.layer_norm1", hidden_states, config.layer_norm_eps);
		hidden_states = ~ctx|SiglipAttention($"{path}.self_attn", ref attn_input, pixel_ids, residual:~hidden_states);
		var mlp_input = nn.LayerNorm($"{path}.layer_norm2", hidden_states, config.layer_norm_eps);
		hidden_states = ~ctx|SiglipMLP($"{path}.mlp", ref mlp_input, residual:~hidden_states);
	}
	[ExcludeFromCodeCoverage]
	protected virtual Tensor SiglipVisionEmbeddings(string path, TensorView pixel_values, TensorView pixel_ids) {
		var sqrt_num_pos = config.image_size/config.patch_size;
		var coords = fn.Mul(pixel_ids[.., ..1], sqrt_num_pos, add:0.5f*sqrt_num_pos);
		coords = ~ctx|fn.Div(~coords, pixel_ids[.., 1..2], add:-0.5f);
		var patch_pos_embed = ~ctx|InterpolatePosEncoding(state_dict[$"{path}.position_embedding.weight"], ~coords, sqrt_num_pos, mode:"bicubic");
		return ~ctx|nn.Linear($"{path}.patch_embedding", pixel_values, add:~patch_pos_embed);
	}
	public Tensor SiglipVisionTransformer(string path, TensorView pixel_values, TensorView pixel_ids) {
		var hidden_states = SiglipVisionEmbeddings($"{path}.embeddings", pixel_values, pixel_ids);
		for(int i=0; i<config.num_hidden_layers; i++)
			SiglipEncoderLayer($"{path}.encoder.layers.{i}", ref hidden_states, pixel_ids);
		hidden_states = ~ctx|nn.LayerNorm($"{path}.post_layernorm", ~hidden_states, config.layer_norm_eps);
		return hidden_states;
	}

	protected Tensor InterpolatePosEncoding(TensorView embeddings, TensorView coords, int sqrt_num_pos, string mode="nearest") {
		coords = fn.Clamp(coords, 0, sqrt_num_pos-1);
		switch(mode) {
		case "nearest": {
			var floor = ~ctx|fn.Round(~coords);
			var index = ~ctx|Swizzle(~floor, new(sqrt_num_pos,1,0,0));
			return ~ctx|fn.IndexSelect(embeddings, (~index, 0));
		}
		case "bilinear": {
			using var _ = ~ctx;
			var floor = ~fn.Floor(~coords);
			var fract = ~fn.Sub(coords, floor);
			var index = ~Swizzle(floor, new(sqrt_num_pos,1,0,0));
			var fracI = Swizzle(fract, new(1,0,0,0));
			var fracJ = Swizzle(fract, new(0,1,0,0));
			var emb00 = fn.IndexSelect(embeddings, (index, 0), bias:0);
			var emb01 = fn.IndexSelect(embeddings, (index, 0), bias:1);
			var emb10 = fn.IndexSelect(embeddings, (index, 0), bias:sqrt_num_pos);
			var emb11 = fn.IndexSelect(embeddings, (index, 0), bias:sqrt_num_pos+1);
			var emb0 = ~ctx|fn.Lerp(~emb00, ~emb01, fracJ);
			var emb1 = ~ctx|fn.Lerp(~emb10, ~emb11, ~fracJ);
			return ~ctx|fn.Lerp(~emb0, ~emb1, ~fracI);
		}
		default:
			throw new System.ArgumentException($"Unsupported interpolation mode: {mode}");
		}
	}
	protected Tensor Swizzle(TensorView input, UnityEngine.Vector4 swizzle)
		=> fn.Swizzle(input, swizzle, swizzle, swizzle, swizzle);
}
}