using System.Diagnostics.CodeAnalysis;

namespace ShaderGPT.Models {
[System.Serializable]
public class MambaConfig : LlamaConfig {
	public int state_size;
	public int time_step_rank;
	public float layer_norm_epsilon;
}
[RegisterModel(typeof(MambaConfig), "mamba")]
[ParityTolerance(nameof(CausalLMOutput.hidden_states), 4e-4f)] // mamba-130m-hf
[ParityTolerance(nameof(CausalLMOutput.logits), 3e-4f)]
public class Mamba : MambaBase {
	public Mamba(Functional fn, MambaConfig config): base(fn, config) {}
	public new MambaConfig config => (MambaConfig)base.config;

	Tensor MambaMixer(string path, Tensor hidden_states, TensorView input_ids, Cache cache_params, Tensor residual, LinearProlog prolog) {
		var (hidden_part, gate) = nn.Linear($"{path}.in_proj", hidden_states, prolog:prolog).Chunk2(axis:1);
		var conv_out = CausalConv1d($"{path}.conv1d", hidden_part, input_ids, cache_params);
		hidden_part = ~ctx|fn.Act2Fn(config.hidden_act, ~conv_out);
		var (dt, B, C) = nn.Linear($"{path}.x_proj", hidden_part).Split(
			(config.time_step_rank/4, config.state_size/4, config.state_size/4), axis:1);
		dt = nn.Linear($"{path}.dt_proj", dt);
		dt = ~ctx|fn.Act2Fn("softplus", ~dt);

		hidden_states = ~ctx|SelectiveStateUpdate($"{path}.ssm", ~hidden_part, input_ids, cache_params, config.state_size,
			~dt, state_dict[$"{path}.A.weight"], B, ~C, state_dict[$"{path}.D.weight"]);
		hidden_states = ~ctx|nn.Linear($"{path}.out_proj", ~gate, prolog:(config.hidden_act, ~hidden_states), add:residual);
		return hidden_states;
	}
	void MambaBlock(string path, ref Tensor hidden_states, TensorView input_ids, Cache cache_params) {
		var prolog = nn.LayerNormProlog($"{path}.norm", config.layer_norm_epsilon, rms:true);
		hidden_states = ~ctx|MambaMixer($"{path}.mixer", hidden_states, input_ids, cache_params, residual:~hidden_states, prolog:prolog);
	}
	protected override void LlamaDecoderLayer(string path, ref Tensor hidden_states, TensorView input_ids, Tensor position_embeddings, Cache past_key_values)
		=> MambaBlock(path, ref hidden_states, input_ids, past_key_values);
	protected override string LlamaModel_embed_tokens => "embeddings";
	protected override string LlamaModel_rotary_emb => null;
	protected override string LlamaModel_norm => "norm_f";
	[ExcludeFromCodeCoverage]
	public Tensor MambaModel(string path, TensorView input_ids, Cache cache_params)
		=> LlamaModel(path, input_ids, cache_params);
	protected override string LlamaForCausalLM_model => "backbone";
	[ExcludeFromCodeCoverage]
	public CausalLMOutput MambaForCausalLM(string path, TensorView input_ids, Cache cache_params)
		=> LlamaForCausalLM(path, input_ids, cache_params);
}
public abstract class MambaBase : Llama {
	protected MambaBase(Functional fn, LlamaConfig config): base(fn, config) {}
	protected Tensor CausalConv1d(string path, TensorView input, TensorView input_ids, [NotNull] Cache cache_params, TensorView preMul=default) {
		var weight = state_dict[$"{path}.weight.T"];
		state_dict.TryGetValue($"{path}.bias", out var bias);
		var hidden_states = ctx.GPUTensor(input.size0, weight.size0*input.size1, dtype:input.dtype);
		fn.Mul(input, preMul, @out:hidden_states[.., ^input.size1..]);
		for(int i=weight.size0-1; i>0; i--) {
			cache_params.Peek(path, input, (input_ids[..1, ..], 1), peekSize:weight.size0-1, bias:i-weight.size0,
				@out:hidden_states[..1, ((i-1)*input.size1)..(i*input.size1)]);
			if(input.size0 > 1) using(~ctx)
				fn.Copy_(hidden_states[1.., ((i-1)*input.size1)..(i*input.size1)],
					~fn.Clone(hidden_states[..^1, (i*input.size1)..((i+1)*input.size1)]));
		}
		cache_params.Extend(path, hidden_states[.., ^input.size1..], (input_ids, 1), peekSize:weight.size0-1);
		hidden_states = ~ctx|fn.Linear(~hidden_states, weight, bias:bias, weightT:true, conv:true);
		return hidden_states;
	}
	protected Tensor SelectiveStateUpdate(string path, TensorView input, TensorView input_ids, [NotNull] Cache cache_params, int state_size,
		TensorView dt, TensorView A, TensorView B, TensorView C, TensorView D) {
		// dt.shape=(B,H), A.shape=(1,H*?), B.shape=(B,G*N), C.shape=(B,G*N), D.shape=(1,H)
		var B_ = fn.RepeatInterleave(B, (1, input.size1*state_size/B.size1), (1, state_size));
		var dx = fn.RepeatInterleave(dt, (1, input.size1/dt.size1), mul:input);
		var dA = fn.RepeatInterleave(dt, (1, A.size1/dt.size1), mul:A);
		var dBx = ~ctx|fn.RepeatInterleave(~dx, (1, state_size), mul:~B_);
		dA = ~ctx|fn.Exp(~dA);
		dA = ~ctx|fn.RepeatInterleave(~dA, (1, input.size1*state_size/dA.size1));

		var ssm_state = cache_params.Peek(path, dBx, (input_ids[..1, ..], 1));
		var output = ~ctx|fn.ParallelScan(~ssm_state, ~dA, ~dBx);
		cache_params.Extend(path, output, (input_ids, 1));
		output = ~ctx|fn.RepeatInterleave(C, (1, output.size1/C.size1), (1, state_size), mul:~output);
		output = ~ctx|fn.Sum(~output, groups:new(input.size0, input.size1*4));
		output = ~ctx|fn.DownsampleNearest(~output, 4);
		output = ~ctx|fn.RepeatInterleave(D, (input.size0/D.size0, input.size1/D.size1), mul:input, add:~output);
		return output;
	}
}
}