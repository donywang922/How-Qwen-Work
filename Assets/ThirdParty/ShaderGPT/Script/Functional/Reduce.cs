using System.Diagnostics.CodeAnalysis;
using UnityEngine;
using UnityEngine.Assertions;
using FusionKeyword = ShaderGPT.Backends.ShaderOps.FusionKeyword;
using ReduceKeyword = ShaderGPT.Backends.ShaderOps.ReduceKeyword;

namespace ShaderGPT {
public partial class Functional {
	static Matrix4x4 ToColumns(Vector4 a) => new(a,a,a,a);
	public Tensor Sum(TensorView input, Vector2Int? groups=null, int p=1, Window? window=null)
		=> ops.Reduce(input, ReduceKeyword.REDUCE_SUMPOW, groups:groups??Vector2Int.one, window:window, swizzle:ToColumns(Matrix4x4.identity.GetColumn(p)));
	public Tensor Amin(TensorView input, Vector2Int? groups=null, Window? window=null)
		=> ops.Reduce(input, ReduceKeyword.REDUCE_MINMAX, groups:groups??Vector2Int.one, window:window, swizzle:ToColumns(new Vector4(1,0,0,0)));
	public Tensor Amax(TensorView input, Vector2Int? groups=null, Window? window=null)
		=> ops.Reduce(input, ReduceKeyword.REDUCE_MINMAX, groups:groups??Vector2Int.one, window:window, swizzle:ToColumns(new Vector4(0,1,0,0)));
	public Tensor Argmin(TensorView input, Vector2Int? groups=null, Window? window=null)
		=> ops.Reduce(input, ReduceKeyword.REDUCE_MINMAX, groups:groups??Vector2Int.one, window:window, swizzle:ToColumns(new Vector4(0,0,1,0)));
	public Tensor Argmax(TensorView input, Vector2Int? groups=null, Window? window=null, float gumbel=0)
		=> ops.Reduce(input, ReduceKeyword.REDUCE_MINMAX, groups:groups??Vector2Int.one, window:window,
			swizzle:ToColumns(new Vector4(0,0,0,1)), bias:gumbel);
	[ExcludeFromCodeCoverage]
	public Tensor Swizzle(TensorView input, Vector4 weight0, Vector4 weight1, Vector4 weight2, Vector4 weight3)
		=> ops.Swizzle(input, new Matrix4x4(weight0, weight1, weight2, weight3));
	public Tensor Cumsum(TensorView input, int axis) {
		var (limit, scale0, scale1) = axis == 0 ? (input.size0, -1, 0) : (input.size1*4, 0, -1);
		var x = ops.Strided(input, new(input.size0, 1, 1), new(input.size1*4, 1, 1), new(1, 0, 0, scale0), new(1, 0, 0, scale1), add:input);
		for(int step=2; step<limit; step*=2)
			x = ~ctx|ops.Strided(~x, new(input.size0, 1, 1), new(input.size1*4, 1, 1), new(1, 0, 0, scale0*step), new(1, 0, 0, scale1*step), add:x);
		return x;
	}
	public Tensor ParallelScan(TensorView input, TensorView mul, TensorView add) {
		Assert.IsTrue(mul.shape == add.shape);
		if(mul.size0 <= 1)
			return AddCMul(add, mul, input);
		using var _ = ~ctx;
		var mad = ~Concat((mul, add), axis:1);
		var tmp = ~EmptyLike(mad);
		for(int h=mul.size1, p=1; p<mad.size0; p<<=1, (mad,tmp)=(tmp,mad)) {
			ops.Fusion(mad[..p, ..], @out:tmp[..p, ..]); // copy head
			ops.Fusion(mad[..^p, ..h], mul:mad[p.., ..h], @out:tmp[p.., ..h]);
			ops.Fusion(mad[..^p, h..], mul:mad[p.., ..h], add:mad[p.., h..], @out:tmp[p.., h..]);
		}
		return AddCMul(mad[.., mul.size1..], mad[.., ..mul.size1], input);
	}

	// multiple Normalize with shared Reduce to reduce context switch
	private (TensorView,TensorView) Normalize(TensorView input, FusionKeyword func, ReduceKeyword reduceFunc,
			Vector3Int groups, (TensorView y,TensorView z) mul=default, (TensorView y,TensorView z) add=default,
			float eps=0f, float scale=1f, Window? window=null, Matrix4x4? swizzle=null) {
		var reduce = ops.Reduce(input, reduceFunc, groups:new(groups.x, groups.y+groups.z), window:window, swizzle:swizzle);
		var (reduce0, reduce1) = reduce.Split((groups.y, groups.z), axis:1);
		var output = ctx.GPUTensor(input.size0, input.size1, dtype:input.dtype);
		var groupSize = input.size1 / (groups.y+groups.z);
		var (input0,  input1)  = input.Split((groupSize*groups.y, groupSize*groups.z), axis:1);
		var (output0, output1) = output.Split((groupSize*groups.y, groupSize*groups.z), axis:1);
		ops.Fusion(input0, @out:output0, func:func, scale:scale, bias:eps, other:reduce0, groups:new(groups.x, groups.y), mul:mul.y, add:add.y, window:window);
		if(groups.z != 0)
			ops.Fusion(input1, @out:output1, func:func, scale:scale, bias:eps, other:reduce1, groups:new(groups.x, groups.z), mul:mul.z, add:add.z, window:window);
		ctx.Release(reduce);
		return (output0, output1);
	}
	private Tensor Normalize(TensorView input, FusionKeyword func, ReduceKeyword reduceFunc,
			Vector2Int groups, TensorView mul=default, TensorView add=default,
			float eps=0f, float scale=1f, Window? window=null, Matrix4x4? swizzle=null)
		=> Normalize(input, func, reduceFunc, new(groups.x,groups.y,0), (mul,default), (add,default),
			eps, scale, window, swizzle).Item1.tensor;

	public Tensor Softmax(TensorView input, int groups=1, Window? window=null, bool stable=false) {
		using var _ = ~ctx;
		if(stable)
			input = ~Normalize(input, FusionKeyword.GROUPNORM, ReduceKeyword.REDUCE_MINMAX,
				groups:new Vector2Int(input.size0, groups), window:window,
				eps:1, swizzle:new(default, new(0,1,0,0), default, default)); // subtract max
		return Normalize(input, FusionKeyword.SOFTMAX, ReduceKeyword.REDUCE_SUMEXP,
			groups:new Vector2Int(input.size0, groups), window:window);
	}
	internal Tensor L2Normalize(TensorView input, float eps, int groups=1) { // TODO: only used by qwen3.5
		return Normalize(input, FusionKeyword.GROUPNORM, reduceFunc:ReduceKeyword.REDUCE_SUMPOW, groups:new Vector2Int(input.size0, groups),
			eps:eps, swizzle:new(new(1,0), new(0,0), new(0,0,input.size1*4/groups), new(0,0,0,float.NaN))); // no clear
	}
	public (TensorView,TensorView) LayerNorm(TensorView input, (TensorView x,TensorView y) weight,
			(TensorView x,TensorView y) bias, float eps, (int x,int y) groups, bool rms=false) {
		Assert.IsTrue(weight.x.size0 == 1 && (weight.x.size1*groups.x+weight.y.size1*groups.y) % input.size1 == 0, "weight size mismatch");
		Assert.IsTrue((!bias.x && !bias.y) || (bias.x.shape == weight.x.shape && bias.y.shape == weight.y.shape), "bias size mismatch");
		return Normalize(input, FusionKeyword.GROUPNORM, reduceFunc:ReduceKeyword.REDUCE_SUMPOW, groups:new(input.size0, groups.x, groups.y),
			mul:weight, add:bias, eps:eps, swizzle:new(new(1,0), new(0,rms?0:1), new(0,0,1), new(0,0,0,float.NaN))); // no clear
	}
	public Tensor LayerNorm(TensorView input, TensorView weight, TensorView bias, float eps, int groups=1, bool rms=false)
		=> LayerNorm(input, (weight, default), (bias, default), eps, (groups, 0), rms:rms).Item1.tensor;
	public Tensor GroupNorm(TensorView input, int groups, TensorView weight, TensorView bias, float eps, bool weightT=false, Window? window=null) {
		int G = groups, C = input.size0/groups;
		Assert.IsTrue((weightT?weight.size1:weight.size0) == 1 && (weightT?weight.size0:weight.size1*4) == G*C, "weight size mismatch");
		Assert.IsTrue(!bias || bias.shape == weight.shape, "bias size mismatch");
		var output = ~ctx|Normalize(input, FusionKeyword.GROUPNORM, reduceFunc:ReduceKeyword.REDUCE_SUMPOW, groups:new(G, 1),
			mul:weightT?weight:~Transpose(weight, size0:G*C, expand:true), add:weightT||!bias?bias:~Transpose(bias, size0:G*C, expand:true),
			eps:eps, window:window, scale:0); // scale=0 uses groupnorm
		if(window.HasValue && bias)
			output = ~ctx|ops.Fusion(~output, window:window); // mask bias
		return output;
	}
}
}