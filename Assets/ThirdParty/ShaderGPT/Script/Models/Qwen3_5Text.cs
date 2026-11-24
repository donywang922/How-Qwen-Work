using System.Diagnostics.CodeAnalysis;
using UnityEngine;

namespace ShaderGPT.Models {
[System.Serializable]
public class Qwen3_5TextConfig : LlamaConfig {
	public string[] layer_types;
	public int linear_num_key_heads;
	public int linear_num_value_heads;
	public int linear_key_head_dim;
	public int linear_value_head_dim;
}
[RegisterModel(typeof(Qwen3_5TextConfig), "qwen3_5_text")]
[ParityTolerance(nameof(CausalLMOutput.hidden_states), 5e-5f)] // Qwen3.5-0.8B
[ParityTolerance(nameof(CausalLMOutput.logits), 4e-5f)]
public class Qwen3_5Text : DeltaBase {
	public Qwen3_5Text(Functional fn, Qwen3_5TextConfig config) : base(fn, config) {}
	public new Qwen3_5TextConfig config => (Qwen3_5TextConfig)base.config;

	internal Tensor Prepare3DPositionIds(TensorView pixel_ids, int scale_factor) {
		var (coords, sizes) = fn.Mul(pixel_ids, 1f/scale_factor).Chunk2(axis:1);
		var steps = fn.Swizzle(sizes, default, default, new(1,0,0,0), new(0,1,0,0));
		fn.Fill_(steps[..^1, ..], 0);
		fn.Add_(steps, -1);
		coords = fn.Floor(coords);
		return ~ctx|fn.Where(~sizes, ~coords, ~steps);
	}
	Tensor Qwen3_5TextRotaryEmbedding(string path, TensorView input_ids, Cache cache_params) {
		if(input_ids.size1 == 1)
			return nn.Embedding($"{path}", (input_ids, 1));
		var mrope_ids = input_ids[.., 1..2];
		var cache = cache_params.Get(path, mrope_ids[..1, ..], generation_config.max_length);
		var last_id = fn.IndexSelect(cache, (input_ids[..1, ..], 1), bias:-1);
		var accum_ids = fn.Add(mrope_ids, 1);
		if(accum_ids.size0 > 1)
			accum_ids = ~ctx|fn.Cumsum(~accum_ids, axis:0);
		accum_ids = ~ctx|fn.Add(~accum_ids, last_id);
		fn.IndexCopy_(cache, (input_ids, 1), accum_ids);
		accum_ids = fn.Concat((~last_id, ~accum_ids[..^1, ..]), axis:0);

		var mrope0 = ~ctx|nn.Embedding($"{path}", (~accum_ids, 2));
		var mrope1 = nn.Embedding($"{path}_mrope1", (mrope_ids, 0));
		var mrope2 = nn.Embedding($"{path}_mrope2", (mrope_ids, 1));
		mrope1 = ~ctx|fn.Rotary(~mrope1, ~mrope2);
		mrope0 = ~ctx|fn.Rotary(~mrope0, ~mrope1);
		return mrope0;
	}
	Tensor Qwen3_5Attention(string path, Tensor hidden_states, TensorView input_ids, Tensor position_embeddings, Cache past_key_values, Tensor residual, LinearProlog prolog) {
		return BaseAttention(path, hidden_states, input_ids, position_embeddings, past_key_values, residual, prolog:prolog,
			q_norm:"q_norm", k_norm:"k_norm", gate_proj:"gate_proj");
	}
	Tensor Qwen3_5GatedDeltaNet(string path, Tensor hidden_states, TensorView input_ids, Cache cache_params, Tensor residual, LinearProlog prolog) {
		var num_k_heads = config.linear_num_key_heads;
		var num_v_heads = config.linear_num_value_heads;
		var k_head_dim = config.linear_key_head_dim;
		var v_head_dim = config.linear_value_head_dim;
		var (z,b,a) = nn.Linear(($"{path}.in_proj_z", $"{path}.in_proj_b", $"{path}.in_proj_a"), hidden_states, prolog:prolog);
		var qkv = nn.Linear($"{path}.in_proj_qkv", hidden_states, prolog:prolog);
		qkv = ~ctx|CausalConv1d($"{path}.conv1d", ~qkv, input_ids, cache_params);
		var (qk,v) = ~ctx|fn.Act2Fn(config.hidden_act, ~qkv).Split((num_k_heads*k_head_dim/2, num_v_heads*v_head_dim/4), axis:1);
		qk = fn.L2Normalize(qk, eps:1e-6f, groups:num_k_heads*2);
		var (q,k) = ~ctx|fn.RepeatInterleave(~qk, (1, num_v_heads/num_k_heads), (1, k_head_dim)).Chunk2(axis:1);

		var beta = fn.Sigmoid(b);
		var gate = fn.Act2Fn("softplus", a, mul:state_dict[$"{path}.A.weight"]);
		var core_attn = ~ctx|RecurrentGatedDelta(path, input_ids, cache_params, q, ~k, ~v, ~gate, ~beta);
		core_attn = ~ctx|nn.LayerNorm($"{path}.norm", ~core_attn, config.rms_norm_eps, groups:num_v_heads, rms:true);
		hidden_states = ~ctx|nn.Linear($"{path}.out_proj", ~z, prolog:(config.hidden_act, ~core_attn), add:residual);
		return hidden_states;
	}
	void Qwen3_5DecoderLayer(string path, ref Tensor hidden_states, TensorView input_ids, Tensor position_embeddings, Cache past_key_values) {
		var attn_prolog = nn.LayerNormProlog($"{path}.input_layernorm", config.rms_norm_eps, rms:true);
		hidden_states = config.layer_types[LayerIdx(path)] == "linear_attention"
			? ~ctx|Qwen3_5GatedDeltaNet($"{path}.linear_attn", hidden_states, input_ids, past_key_values, residual:~hidden_states, prolog:attn_prolog)
			: ~ctx|Qwen3_5Attention($"{path}.self_attn", hidden_states, input_ids, position_embeddings, past_key_values, residual:~hidden_states, prolog:attn_prolog);
		var mlp_prolog = nn.LayerNormProlog($"{path}.post_attention_layernorm", config.rms_norm_eps, rms:true);
		hidden_states = ~ctx|LlamaMLP($"{path}.mlp", hidden_states, residual:~hidden_states, prolog:mlp_prolog);
	}
	public Tensor Qwen3_5TextModel(string path, TensorView input_ids, [NotNull] Cache past_key_values, Tensor input_embeds=default) {
		var path_ = path == null ? "" : path + ".";
		var hidden_states = input_embeds ? input_embeds : nn.Embedding($"{path_}{LlamaModel_embed_tokens}", (input_ids, 0));
		using(~ctx) {
			var position_embeddings = ~Qwen3_5TextRotaryEmbedding($"{path_}{LlamaModel_rotary_emb}", input_ids, past_key_values);
			for(int i = 0; i < config.num_hidden_layers; i++)
				Qwen3_5DecoderLayer($"{path_}layers.{i}", ref hidden_states, input_ids, position_embeddings, past_key_values);
		}
		hidden_states = ~ctx|nn.LayerNorm($"{path_}{LlamaModel_norm}", ~hidden_states, config.rms_norm_eps, rms:true);
		return hidden_states;
	}
	public override Tensor LlamaModel(string path, TensorView input_ids, Cache past_key_values, Tensor input_embeds=default)
		=> Qwen3_5TextModel(path, input_ids, past_key_values, input_embeds);
	[ExcludeFromCodeCoverage]
	public CausalLMOutput Qwen3_5ForCausalLM(string path, TensorView input_ids, Cache past_key_values)
		=> LlamaForCausalLM(path, input_ids, past_key_values);
}
public abstract class DeltaBase : MambaBase {
	protected DeltaBase(Functional fn, LlamaConfig config): base(fn, config) {}
	protected Tensor RecurrentGatedDelta(string path, TensorView input_ids, [NotNull] Cache cache_params,
		TensorView q, TensorView k, TensorView v, TensorView gate, TensorView beta) {
		var num_heads = gate.size1*4;
		var q_head_dim = q.size1/gate.size1;
		var v_head_dim = v.size1/gate.size1;
		Debug.Assert(gate.size1*4 == num_heads && beta.size1*4 == num_heads);
		Debug.Assert(q.size1*4 == num_heads*q_head_dim && k.size1*4 == num_heads*q_head_dim && v.size1*4 == num_heads*v_head_dim);
		using var _ = ~ctx;
		gate = ~fn.RepeatInterleave(~fn.Exp(gate), (1, q_head_dim));
		beta = ~fn.RepeatInterleave(beta, (1, v_head_dim));

		var last_state = cache_params.Peek(path, new TensorView(v.tensor, 1, v_head_dim*num_heads*q_head_dim/4), (input_ids[..1, ..], 1));
		last_state = ~ctx|fn.Reshape(~last_state, size0:v_head_dim);
		var core_attn = ctx.GPUTensor(input_ids.size0, last_state.size1, dtype:last_state.dtype);
		for(int i = 0; i < input_ids.size0; i++) {
			last_state = ~ctx|fn.Mul(~last_state, gate[i..(i+1), ..]);
			var kv_mem = fn.Linear(k[i..(i+1), ..], weight:last_state, heads:num_heads);
			var delta = ~ctx|fn.Sub(v[i..(i+1), ..], ~kv_mem);
			delta = ~ctx|fn.Mul(~delta, beta[i..(i+1), ..]);
			delta = ~ctx|fn.AsStrided(~delta, size0:(1, v_head_dim, 1), size1:(1, num_heads, q_head_dim),
				stride0:(0, -1, 0), stride1:(0, v_head_dim, 0));
			using(~ctx) {
				fn.AddCMul_(last_state, ~delta, k[i..(i+1), ..]);
				cache_params.Extend(path, ~fn.Reshape(last_state, size0:1), (input_ids[i..(i+1), ..], 1));
			}
			fn.Linear(q[i..(i+1), ..], weight:last_state, heads:num_heads,
				cmul:1f/Mathf.Sqrt(q_head_dim), @out:core_attn[i..(i+1), ..]);
		}
		ctx.Release(last_state);
		return core_attn;
	}
}
}