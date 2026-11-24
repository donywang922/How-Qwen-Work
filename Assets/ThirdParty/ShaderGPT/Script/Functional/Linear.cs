using UnityEngine;
using UnityEngine.Assertions;
using FusionKeyword = ShaderGPT.Backends.ShaderOps.FusionKeyword;
using LinearKeyword = ShaderGPT.Backends.ShaderOps.LinearKeyword;
using ReduceKeyword = ShaderGPT.Backends.ShaderOps.ReduceKeyword;

namespace ShaderGPT {
public readonly struct LinearProlog {
	public readonly string func;
	public readonly TensorView other;
	public readonly float eps;
	private LinearProlog(string func, TensorView other, float eps)
		=> (this.func, this.other, this.eps) = (func, other, eps);
	public static implicit operator LinearProlog(string func)
		=> new(func, default, 0);
	public static implicit operator LinearProlog((string func, TensorView mul) value)
		=> new(value.func, value.mul, 0);
	public static LinearProlog LayerNorm(TensorView weight, float eps, bool rms=false)
		=> new(rms ? "rmsnorm" : "layernorm", weight, eps);
	public static LinearProlog Softmax(bool stable=true)
		=> new("softmax", default, stable?0:1);
}
public partial class Functional {
	private (LinearKeyword, TensorView) ResolveProlog(ref TensorView input, LinearProlog prolog, int groups=1, Window? window=null) {
		if(prolog.func == "layernorm" || prolog.func == "rmsnorm")
			return (LinearKeyword.LINEAR_GROUPNORM, ~ops.Reduce(input, ReduceKeyword.REDUCE_SUMPOW,
				groups:new Vector2Int(input.size0, groups), window:window,
				swizzle:new(new(1,0), new(0,prolog.func == "rmsnorm"?0:1), new(0,0,1), new(0,0,0,float.NaN)))); // no clear
		else if(prolog.func == "softmax") {
			if(prolog.eps == 0) // stable
				input = ~Normalize(input, FusionKeyword.GROUPNORM, ReduceKeyword.REDUCE_MINMAX,
					groups:new Vector2Int(input.size0, groups), window:window, scale:-1, // enable culling
					eps:1, swizzle:new(default, new(0,1,0,0), default, default)); // subtract max
			return (LinearKeyword.LINEAR_SOFTMAX, ~ops.Reduce(input, ReduceKeyword.REDUCE_SUMEXP,
				groups:new Vector2Int(input.size0, groups), window:window));
		}
		var func = prolog.func != null || prolog.other ? LinearKeyword.LINEAR_MUL : default;
		if(prolog.func != null && !System.Enum.TryParse($"LINEAR_{prolog.func.ToUpper()}_MUL", out func))
			input = ~Act2Fn(prolog.func, input, mul:prolog.other);
		return (func, default);
	}
	public Tensor Linear(TensorView input, TensorView weight, TensorView bias=default, TensorView @out=default,
			bool conv=false, bool weightT=false, int heads=1, int? weightHeads=null,
			LinearProlog prolog=default, float cmul=1f, TensorView add=default, Window? window=null) {
		Assert.IsFalse(conv && (prolog.func != null || prolog.other), "prolog is not supported for conv");
		using var _ = ~ctx;
		var (func, reduce) = conv ? (LinearKeyword.LINEAR_CONV, default) : ResolveProlog(ref input, prolog, heads, window);
		quantizers.TryGetValue(weight.tensor, out var quantizer);
		permuters.TryGetValue(weight.tensor, out var permuter);
		return ops.Linear(input, weight, bias, @out, weightT, heads, weightHeads, cmul:cmul, add:add, window:window,
			func:func, other:prolog.other, reduce:reduce, eps:prolog.eps, quantizer:quantizer, permuter:permuter);
	}
	public (TensorView,TensorView,TensorView) Linear(TensorView input, (TensorView x,TensorView y,TensorView z) weight,
			(TensorView x,TensorView y,TensorView z) bias=default, bool weightT=false, LinearProlog prolog=default) {
		var idim0 = !weightT ? weight.x.size1 : (weight.x.size0+3)/4;
		var odim0 =  weightT ? weight.x.size1 : (weight.x.size0+3)/4;
		var odim1 =  weightT ? weight.y.size1 : (weight.y.size0+3)/4;
		var odim2 =  weightT ? weight.z.size1 : (weight.z.size0+3)/4;
		var (lod, inst) = ctx.ChooseLodAndInst(input.size0, Mathf.Max(odim0,odim1,odim2), idim0);
		using var _ = ~ctx;
		var (func, reduce) = ResolveProlog(ref input, prolog);
		var output = ctx.GPUTensor(input.size0, odim0+odim1+odim2, dtype:input.dtype, lod:lod);
		var (output0, output1, output2) = output.Split((odim0,odim1,odim2), axis:1);
		quantizers.TryGetValue(weight.x.tensor, out var quantizer);
		permuters.TryGetValue(weight.x.tensor, out var permuter);
		ops.Linear(input, weight.x, bias.x, @out:output0, weightT:weightT, accum:true, clear:output,
			func:func, other:prolog.other, reduce:reduce, eps:prolog.eps, quantizer:quantizer, permuter:permuter);
		quantizers.TryGetValue(weight.y.tensor, out quantizer);
		permuters.TryGetValue(weight.y.tensor, out permuter);
		ops.Linear(input, weight.y, bias.y, @out:output1, weightT:weightT, accum:true,
			func:func, other:prolog.other, reduce:reduce, eps:prolog.eps, quantizer:quantizer, permuter:permuter);
		if(weight.z) {
			quantizers.TryGetValue(weight.z.tensor, out quantizer);
			permuters.TryGetValue(weight.z.tensor, out permuter);
			ops.Linear(input, weight.z, bias.z, @out:output2, weightT:weightT, accum:true,
				func:func, other:prolog.other, reduce:reduce, eps:prolog.eps, quantizer:quantizer, permuter:permuter);
		}
		return (output0, output1, output2);
	}
	public (TensorView,TensorView) Linear(TensorView input, (TensorView x,TensorView y) weight,
			(TensorView x,TensorView y) bias=default, bool weightT=false, LinearProlog prolog=default) {
		var (output0, output1, _) = Linear(input, (weight.x,weight.y,default), (bias.x,bias.y,default), weightT, prolog);
		return (output0, output1);
	}

	public Tensor ScaledDotProductAttention(TensorView query, TensorView key, TensorView value,
			TensorView attn_mask=default, int heads=1, float? scale=null, Window? window=null, bool stable=false) {
		window ??= new(0, key.size0); // handle size0%4 != 0
		var kv_heads = key.size1 / (query.size1 / heads);
		var attn_weight = Linear(query, key, heads:heads, weightHeads:kv_heads, window:window, cmul:scale.Value, add:attn_mask);
		return ~ctx|Linear(~attn_weight, value, heads:heads, weightHeads:kv_heads, window:window, weightT:true,
			prolog:LinearProlog.Softmax(stable)); // prefer unstable on small weights to save reduce cost
	}

	public Tensor LSTM(TensorView input, TensorView weight_ih, TensorView weight_hh, TensorView bias, bool bidirectional=false, Window? length=null) {
		var size0 = input.size0;
		var size1 = weight_hh.size1;
		var layers = bidirectional ? 2 : 1;
		Assert.IsTrue(weight_ih.size0 == weight_hh.size0 && weight_hh.size0*layers == size1*16, "weight size mismatch");
		var h = ctx.GPUTensor(size0, size1, dtype:input.dtype);
		using(~ctx) {
			var ih = ~(~ctx|Linear(bidirectional?(~Concat((input, ~Flip(input, axis:0, window:length)), axis:1)):input,
				weight_ih, bias, heads:layers, weightHeads:layers));
			var c0 = ~Zeros(1, size1, dtype:input.dtype);
			var c1 = ~EmptyLike(c0);
			for(int i=0; i<size0; i++, (c0,c1)=(c1,c0)) {
				using var _ = ~ctx;
				var h0 = i==0 ? c0 : h[(i-1)..i, ..];
				var h1 = h[i..(i+1), ..];
				var w = ~Linear(h0, weight_hh, ih[i..(i+1), ..], heads:layers, weightHeads:layers);
				ops.Fusion(c0, @out:c1, func:FusionKeyword.LSTM_CELL, other:w, bias:0, groups:new(c1.size0, layers));
				ops.Fusion(c1, @out:h1, func:FusionKeyword.LSTM_CELL, other:w, bias:1, groups:new(h1.size0, layers));
			}
			if(bidirectional) {
				var (_, half1) = h.Chunk2(axis:1);
				Copy_(half1, ~Flip(half1, axis:0, window:length));
			}
		}
		return h;
	}
}
}