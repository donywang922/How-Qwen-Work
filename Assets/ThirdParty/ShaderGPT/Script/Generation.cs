using UnityEngine;
using UnityEngine.Experimental.Rendering;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace ShaderGPT {
[System.Serializable]
public partial class GenerationConfig {
	public int max_length = 16384;
	public bool do_sample = false;
	public float temperature = 1f;
	public float repetition_penalty = 1f;
}
public abstract class GenerationMixin : PretrainedModel {
	public GenerationConfig generation_config {get;set;}
	protected GenerationMixin(Functional fn, PretrainedConfig config): base(fn, config) {
		generation_config = new GenerationConfig(); // TODO: parse config
	}

	private float temperature => generation_config.do_sample ? generation_config.temperature : 0f;
	private float repetition_penalty => generation_config.repetition_penalty;
	const string InputsCacheKey = "$generation.inputs";
	const string SeenTokensCacheKey = "$generation.seen_tokens";
	const int SeenTokensPeekSize = 4;
	void RepetitionPenaltyLogitsProcessor(TensorView input_ids, ref Tensor scores, float penalty, [NotNull] Cache cache) {
		var seen = fn.EmptyLike(scores, dtype:GraphicsFormat.R8G8B8A8_UNorm);
		using(~ctx)
		if(!cache.MinPeekSize.HasValue) {
			var inputs = cache.Update(InputsCacheKey, input_ids, (input_ids, 1));
			var inputs_T = ~fn.Transpose(inputs, size0:1);
			fn.Add_(inputs_T, ~fn.Full(1, 1, 1, dtype:inputs_T.dtype), 4*scores.size1,
				window:new(input_ids.size0, int.MaxValue, 1, (input_ids[..1, ..], 1)));
			fn.Fill_(seen, 0);
			fn.IndexAdd_(seen, (inputs_T, 0), 1f, axis:1);
		} else {
			var peekSize = Mathf.Min(cache.MinPeekSize.Value, SeenTokensPeekSize);
			var fill_ids = ~fn.Swizzle(input_ids[.., ..1], new(1,0,0,0), new(1,0,0,0), new(1,0,0,0), new(1,0,0,0));
			cache.Peek(SeenTokensCacheKey, seen, (input_ids[..1, ..], 1), @out:seen, peekSize:peekSize);
			for(int i=0; i<input_ids.size0; i++) {
				fn.IndexAdd_(seen, (fill_ids[i..(i+1), ..], 0), 1f, axis:1);
				cache.Extend(SeenTokensCacheKey, seen, (input_ids[i..(i+1), ..], 1), peekSize:peekSize);
			}
		}
		var relu = fn.LeakyRelu(scores, penalty, positive_slope:1f/penalty);
		scores = ~ctx|fn.Where(~seen, ~relu, ~scores);
	}
	public virtual Tensor Generate(TensorView input, ref Tensor scores, [NotNull] Cache cache) {
		scores = ~ctx|fn.ToFloat(~scores[^1.., ..]);
		RepetitionPenaltyLogitsProcessor(input, ref scores, repetition_penalty, cache);
		return ~ctx|fn.Argmax(~scores, window:new(0, config.vocab_size), gumbel:temperature);
	}
}

#pragma warning disable CA1724
public class Cache : Dictionary<string,Tensor> {
	private readonly int max_length;
	private readonly Functional fn;
	private TensorContext ctx => fn.ctx;
	internal int? MinPeekSize {get;private set;}
	public Cache(Functional fn, int max_length) : base() {
		this.fn = fn;
		this.max_length = max_length;
	}
	public void Reset() {
		MinPeekSize = null;
		foreach(var kv in this)
			ctx.Release(kv.Value);
		Clear();
	}
	public Tensor GetValueOrDefault(string k) {
		return TryGetValue(k, out var v) ? v : default;
	}

	internal Tensor Get(string layer, TensorView state, int? size0=null) {
		if(!TryGetValue(layer, out var states))
			this[layer] = states = ctx.GPUTensor(size0??state.size0, state.size1, dtype:state.dtype, persistent:true);
		return states;
	}
	public Tensor Update(string layer, TensorView state, (TensorView,int) cache_position, int? maxLen=null)
		=> fn.IndexCopy_(Get(layer, state, maxLen??max_length), cache_position, state);
	public TensorView CrossUpdate(string layer, TensorView state, (TensorView,int) cache_position, bool cross, bool update) {
		if(!cross)
			return update ? Update(layer, state, cache_position) : state;
		if(!update)
			return state ? state : this[layer];
		if(Remove(layer, out var prev))
			ctx.Release(prev);
		return fn.Copy_(Get(layer, state), state);
	}
	Tensor GetStackLike(string layer, TensorView state, int peekBits)
		=> Get(layer, state, ((int)Mathf.Log(((max_length-1)>>peekBits)+1, 2)+1)<<peekBits);
	public void Extend(string layer, TensorView state, (TensorView,int) cache_position, int peekSize=1) {
		MinPeekSize = Mathf.Min(MinPeekSize??int.MaxValue, peekSize);
		var peekBits = (int)Mathf.Log(Mathf.NextPowerOfTwo(peekSize), 2);
		fn.IndexCopy_(GetStackLike(layer, state, peekBits), cache_position, state, logPeek:peekBits);
	}
	public Tensor Peek(string layer, TensorView state, (TensorView,int) cache_position, int peekSize=1, int bias=-1, TensorView @out=default) {
		var peekBits = (int)Mathf.Log(Mathf.NextPowerOfTwo(peekSize), 2);
		return fn.IndexSelect(GetStackLike(layer, state, peekBits), cache_position, logPeek:peekBits, bias:bias, @out:@out);
	}
}
#pragma warning restore CA1724
}