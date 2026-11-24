namespace ShaderGPT.Models {
[System.Serializable]
public class Qwen3_5VisionConfig : SiglipVisionConfig {
	public int depth;
	public int num_heads;
	public int num_position_embeddings;
	public int spatial_merge_size;
	public int temporal_patch_size;
	protected override void FromPretrained(string json) {
		base.FromPretrained(json);
		num_hidden_layers = depth;
		num_attention_heads = num_heads;
	}
}
public class Qwen3_5Vision : SiglipVision {
	public Qwen3_5Vision(Functional fn, Qwen3_5VisionConfig config) : base(fn, config) {}
	public new Qwen3_5VisionConfig config => (Qwen3_5VisionConfig)base.config;

	Tensor Qwen3_5VisionAttention(string path, ref Tensor hidden_states, TensorView pixel_ids, TensorView position_embeddings, Tensor residual) {
		var (q,k,v) = ~ctx|nn.Linear($"{path}.qkv", ~hidden_states).Chunk3(axis:1);
		var (query,key) = fn.Rotary(new TensorView(q.tensor, q.size0, q.size1+k.size1), position_embeddings,
			groups:config.num_attention_heads*2).Split((q.size1, k.size1), axis:1);
		hidden_states = ~ctx|fn.ScaledDotProductAttention(~query, key, ~v,
			heads:config.num_attention_heads, scale:1f/System.MathF.Sqrt(head_dim),
			window:new(int.MinValue, 0, 1, (pixel_ids, 2)));
		return ~ctx|nn.Linear($"{path}.proj", ~hidden_states, add:residual);
	}
	Tensor Qwen3_5VisionMLP(string path, ref Tensor hidden_states, Tensor residual) {
		hidden_states = ~ctx|nn.Linear($"{path}.linear_fc1", ~hidden_states);
		return ~ctx|nn.Linear($"{path}.linear_fc2", ~hidden_states, prolog:config.hidden_act, add:residual);
	}
	void Qwen3_5VisionBlock(string path, ref Tensor hidden_states, TensorView pixel_ids, TensorView position_embeddings) {
		var attn_input = nn.LayerNorm($"{path}.norm1", hidden_states, eps:1e-6f);
		hidden_states = ~ctx|Qwen3_5VisionAttention($"{path}.attn", ref attn_input, pixel_ids, position_embeddings, residual:~hidden_states);
		var mlp_input = nn.LayerNorm($"{path}.norm2", hidden_states, eps:1e-6f);
		hidden_states = ~ctx|Qwen3_5VisionMLP($"{path}.mlp", ref mlp_input, residual:~hidden_states);
	}
	Tensor RotaryPosEmbed(string path, TensorView pixel_ids) {
		var (row_cos, row_sin) = fn.IndexSelect(state_dict[$"{path}.weight"], (pixel_ids, 0)).Chunk2(axis:1);
		var (col_cos, col_sin) = fn.IndexSelect(state_dict[$"{path}.weight"], (pixel_ids, 1)).Chunk2(axis:1);
		var embeddings = ctx.GPUTensor(pixel_ids.size0, row_cos.size1*4);
		using var _ = ~ctx;
		fn.Copy_(embeddings[.., ..row_cos.size1], row_cos);
		fn.Copy_(embeddings[.., row_cos.size1..(row_cos.size1*2)], col_cos);
		fn.Copy_(embeddings[.., (row_cos.size1*2)..(row_cos.size1*3)], ~row_sin);
		fn.Copy_(embeddings[.., (row_cos.size1*3)..], ~col_sin);
		return embeddings;
	}
	Tensor InterpolatePosEmbed(string path, TensorView pixel_values, TensorView pixel_ids) {
		// F.interpolate(align_corners=True) => i/(size-1)*(sqrt_num_pos-1)
		var sqrt_num_pos = (int)System.MathF.Round(System.MathF.Sqrt(config.num_position_embeddings));
		var inv_step = fn.Mul(pixel_ids[.., 1..2], 1f/(sqrt_num_pos-1), add:-1f/(sqrt_num_pos-1));
		var coords = ~ctx|fn.Div(pixel_ids[.., ..1], ~inv_step);
		var patch_pos_embed = ~ctx|InterpolatePosEncoding(state_dict[$"{path}.pos_embed.weight"], ~coords, sqrt_num_pos, mode:"bilinear");
		return ~ctx|nn.Linear($"{path}.patch_embed.proj", pixel_values, add:~patch_pos_embed);
	}
	void Qwen3_5VisionPatchMerger(string path, ref Tensor hidden_states) {
		hidden_states = ~ctx|nn.LayerNorm($"{path}.norm", ~hidden_states, eps:1e-6f);
		hidden_states = ~ctx|fn.Reshape(~hidden_states, hidden_states.size0 / config.spatial_merge_size / config.spatial_merge_size);
		hidden_states = ~ctx|nn.Linear($"{path}.linear_fc1", ~hidden_states);
		hidden_states = ~ctx|nn.Linear($"{path}.linear_fc2", ~hidden_states, prolog:"gelu");
	}
	public Tensor Qwen3_5VisionModel(string path, TensorView pixel_values, TensorView pixel_ids) {
		var hidden_states = InterpolatePosEmbed(path, pixel_values, pixel_ids);
		using(~ctx) {
			var position_embeddings = ~RotaryPosEmbed($"{path}.rotary_pos_emb", pixel_ids);
			for(int i=0; i<config.num_hidden_layers; i++)
				Qwen3_5VisionBlock($"{path}.blocks.{i}", ref hidden_states, pixel_ids, position_embeddings);
		}
		Qwen3_5VisionPatchMerger($"{path}.merger", ref hidden_states);
		return hidden_states;
	}
}
}