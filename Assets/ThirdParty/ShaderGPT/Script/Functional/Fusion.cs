using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Experimental.Rendering;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using FusionKeyword = ShaderGPT.Backends.ShaderOps.FusionKeyword;

namespace ShaderGPT {
public partial class Functional {
	public Tensor Narrow(TensorView input, Window window, int groups=1, int? size0=null, PaddingMode padding_mode=0) {
		var size1 = (Mathf.RoundToInt(window.stop-window.start)+3)/4 * groups;
		var output = ctx.GPUTensor(size0??input.size0, size1, dtype:input.dtype);
		return ops.Narrow(input, window, @out:output, groups:groups, padding_mode:padding_mode);
	}
	public Tensor Reshape(TensorView input, TensorView @out, bool expand=false) {
		Assert.IsTrue(expand || @out.size0*@out.size1 == input.size0*input.size1, "reshape size mismatch");
		const PaddingMode Reshape = (PaddingMode)(-1);
		return ops.Strided(input,
			new(1, 1, @out.size0),    new(1, 1, @out.size1*4),
			new(0, 0, -@out.size1*4), new(0, 0, 1), @out:@out, padding_mode:Reshape);
	}
	public Tensor Reshape(TensorView input, int? size0, int? size1=null) {
		return Reshape(input, @out:ctx.GPUTensor(
			size0??(input.size0*input.size1/size1.Value),
			size1??(input.size0*input.size1/size0.Value), dtype:input.dtype));
	}
	public Tensor RepeatInterleave(TensorView input, (int x,int y) repeats, (int x,int y)? tile_sizes=null, TensorView mul=default, TensorView add=default) {
		var sizes = tile_sizes ?? (1,1);
		return ops.Strided(input,
			new(-1, repeats.x, sizes.x), new(-1, repeats.y, sizes.y),
			new(sizes.x,    0,       1), new(sizes.y,    0,       1), mul:mul, add:add,
			@out:ctx.GPUTensor(input.size0*repeats.x, input.size1*repeats.y, dtype:input.dtype));
	}
	[ExcludeFromCodeCoverage]
	public Tensor AsStrided(TensorView input, (int x,int y,int z) size0, (int x,int y,int z) size1,
			(int x,int y,int z) stride0, (int x,int y,int z) stride1) {
		return ops.Strided(input,
			new(size0.x,   size0.y,   size0.z),   new(size1.x,   size1.y,   size1.z),
			new(stride0.x, stride0.y, stride0.z), new(stride1.x, stride1.y, stride1.z));
	}
	public Tensor Flip(TensorView input, int axis, Window? window=null) {
		if(window is Window win) {
			if(axis == 1) // flip inside window. outside values are undefined
				return ~ctx|Narrow(~Flip(input, axis:axis), window:new(
					input.size1*4 - win.stop - win.start2,
					input.size1*8 - win.stop - win.start2,
					- win.scale - win.scale2, win.index));
			Assert.IsTrue(win.index.Item1.size0 <= 1, "flip on axis 0 only supports global window");
			var output = Transpose(input);
			output = ~ctx|Flip(~output, axis:1, window);
			output = ~ctx|Transpose(~output, size0:input.size0);
			return output;
		}
		return axis == 0
			? ops.Strided(input,
				new(input.size0, 1, 1),                new(input.size1*4, 1, 1),
				new(-1,          0, 0, input.size0-1), new(1,             0, 0))
			: ops.Strided(input,
				new(input.size0, 1, 1), new(input.size1*4, 1, 1),
				new(1,           0, 0), new(-1,            0, 0, input.size1*4-1));
	}
	public Tensor Transpose(TensorView input, int? size0=null, bool expand=false) {
		var output = ctx.GPUTensor(size0??(input.size1*4), (input.size0+3)/4, dtype:input.dtype);
		return ops.Strided(input,
			new(1, 1, output.size0), new(1, 1, output.size1*4),
			new(0, 0,           -1), new(0, 0, expand&&input.size0==1?0:-1), @out:output);
	}

	public TensorView Fill_(TensorView output, float value)
		=> (ops.Fusion(null, cmul:0, cadd:value, @out:output), output).Item2;
	public Tensor Full(int size0, int size1, float value, GraphicsFormat? dtype=null)
		=> ops.Fusion(null, cmul:0, cadd:value, @out:ctx.GPUTensor(size0, size1, dtype:dtype));
	public Tensor Zeros(int size0, int size1, GraphicsFormat? dtype=null)
		=> Full(size0, size1, 0f, dtype:dtype);
	public Tensor EmptyLike(TensorView input, GraphicsFormat? dtype=null)
		=> ctx.GPUTensor(input.size0, input.size1, dtype:dtype??input.dtype);
	public Tensor FullLike(TensorView input, float value)
		=> ops.Fusion(null, cmul:0, cadd:value, @out:EmptyLike(input));
	public Tensor ZerosLike(TensorView input)
		=> FullLike(input, 0f);

	public TensorView Copy_(TensorView output, TensorView src)
		=> (output.shape != src.shape ? ops.Fusion(null, cmul:0f, add:src, @out:output) : ops.Fusion(src, @out:output), output).Item2;
	public Tensor Clone(TensorView input)
		=> ops.Fusion(input, @out:EmptyLike(input));
	public Tensor Concat((TensorView,TensorView) inputs, int axis)
		=> Concat((inputs.Item1, inputs.Item2, default), axis);
	public Tensor Concat((TensorView,TensorView,TensorView) inputs, int axis) {
		var (x,y,z) = inputs;
		if(axis == 0) {
			var output = ctx.GPUTensor(x.size0+y.size0+z.size0, x.size1, dtype:x.dtype);
			Copy_(output[..x.size0, ..], x);
			Copy_(output[x.size0..(x.size0+y.size0), ..], y);
			if(z)
				Copy_(output[(x.size0+y.size0).., ..], z);
			return output;
		} else {
			var output = ctx.GPUTensor(x.size0, x.size1+y.size1+z.size1, dtype:x.dtype);
			Copy_(output[.., ..x.size1], x);
			Copy_(output[.., x.size1..(x.size1+y.size1)], y);
			if(z)
				Copy_(output[.., (x.size1+y.size1)..], z);
			return output;
		}
	}
	public Tensor ToFloat(TensorView input)
		=> ops.Fusion(input, @out:EmptyLike(input, dtype:GraphicsFormat.R32G32B32A32_SFloat));

	private Tensor Broadcast(TensorView input, TensorView tensor1, TensorView tensor2=default) {
		var size0 = Mathf.Max(input.size0, tensor1.size0, tensor2.size0);
		var size1 = Mathf.Max(input.size1, tensor1.size1, tensor2.size1);
		Assert.IsTrue(size0 % input.size0 == 0 && size1 % input.size1 == 0, "broadcast size mismatch");
		if(tensor1)
			Assert.IsTrue(size0 % tensor1.size0 == 0 && size1 % tensor1.size1 == 0, "broadcast size mismatch");
		if(tensor2)
			Assert.IsTrue(size0 % tensor2.size0 == 0 && size1 % tensor2.size1 == 0, "broadcast size mismatch");
		return ctx.GPUTensor(size0, size1, dtype:input.dtype);
	}
	public Tensor Add(TensorView input, float other)
		=> ops.Fusion(input, cadd:other);
	public Tensor Add(TensorView input, TensorView other)
		=> ops.Fusion(input, add:other, @out:Broadcast(input, other));
	public Tensor Add(TensorView input, TensorView other, float alpha, Window? window=null)
		=> ops.Fusion(other, cmul:alpha, add:input, window:window, @out:Broadcast(input, other));
	public TensorView Add_(TensorView output, float other)
		=> (ops.Fusion(null, cmul:0, cadd:other, add:output, @out:output), output).Item2;
	public TensorView Add_(TensorView output, TensorView other)
		=> (ops.Fusion(other, add:output, @out:output), output).Item2;
	public TensorView Add_(TensorView output, TensorView other, float alpha, Window? window=null)
		=> (ops.Fusion(other, window:window, cmul:alpha, add:output, @out:output), output).Item2;
	public Tensor AddCMul(TensorView input, TensorView tensor1, TensorView tensor2, float value=1)
		=> ops.Fusion(tensor1, mul:tensor2, cmul:value, add:input, @out:Broadcast(input, tensor1, tensor2));
	public TensorView AddCMul_(TensorView output, TensorView tensor1, TensorView tensor2, float value=1)
		=> (ops.Fusion(tensor1, mul:tensor2, cmul:value, add:output, @out:output), output).Item2;
	public Tensor Mul(TensorView input, TensorView other, Window? window=null, TensorView @out=default)
		=> ops.Fusion(input, mul:other, window:window, @out:@out);
	public Tensor Mul(TensorView input, float other, Window? window=null, TensorView @out=default)
		=> ops.Fusion(input, cmul:other, window:window, @out:@out);
	public Tensor Mul(TensorView input, float other, float add)
		=> ops.Fusion(input, cmul:other, cadd:add);
	public Tensor Div(float input, TensorView other)
		=> ops.Fusion(other, func:FusionKeyword.POW, scale:-1f, cmul:input);
	public Tensor Div(TensorView input, TensorView other)
		=> ops.Fusion(other, func:FusionKeyword.POW, scale:-1f, mul:input, @out:Broadcast(input, other));
	public Tensor Div(TensorView input, TensorView other, float add)
		=> ops.Fusion(other, func:FusionKeyword.POW, scale:-1f, mul:input, cadd:add);
	public Tensor Sub(TensorView input, TensorView other)
		=> Add(input, other, alpha:-1f);
	public Tensor Sub(float input, TensorView other)
		=> Mul(other, -1f, add:input);
	public Tensor Lerp(TensorView input, TensorView end, TensorView weight)
		=> AddCMul_(AddCMul(input, input, weight, -1), end, weight).tensor;

	public Tensor Clamp(TensorView input, float? min=null, float? max=null, Window? window=null)
		=> ops.Fusion(input, func:FusionKeyword.CLAMP, bias:min??float.NegativeInfinity, scale:max??float.PositiveInfinity, window:window);
	public Tensor Exp(TensorView input, float? @base=null)
		=> ops.Fusion(input, func:FusionKeyword.EXP, scale:Mathf.Log(@base??System.MathF.E));
	public Tensor Floor(TensorView input)
		=> ops.Fusion(input, func:FusionKeyword.ROUND, bias:-1f);
	public Tensor Log(TensorView input, float? @base=null)
		=> ops.Fusion(input, func:FusionKeyword.LOG, scale:1f/Mathf.Log(@base??System.MathF.E));
	public Tensor Pow(TensorView input, float exponent, Window? window=null)
		=> ops.Fusion(input, func:FusionKeyword.POW, scale:exponent, window:window);
	public Tensor Round(TensorView input)
		=> ops.Fusion(input, func:FusionKeyword.ROUND);
	public Tensor Where(TensorView condition, TensorView input, TensorView other)
		=> ops.Fusion(condition, func:FusionKeyword.WHERE, mul:input, add:other, @out:Broadcast(input, other, condition));
	public Tensor Where(TensorView condition, float input, TensorView other)
		=> ops.Fusion(condition, func:FusionKeyword.WHERE, cmul:input, add:other);
	public Tensor Where(TensorView condition, float input, float other)
		=> ops.Fusion(condition, func:FusionKeyword.WHERE, cmul:input, cadd:other);

	static readonly Dictionary<string, FusionKeyword> Act2Keyword =
		System.Enum.GetNames(typeof(FusionKeyword))
			.Select(name => (key: name.ToLowerInvariant(), value: System.Enum.Parse<FusionKeyword>(name)))
			.Concat(new (string key, FusionKeyword value)[] {
				("swish", FusionKeyword.SILU),
				("gelu_new", FusionKeyword.GELU_TANH),
				("gelu_pytorch_tanh", FusionKeyword.GELU_TANH),
			})
			.ToDictionary(t => t.key, t => t.value);
	public Tensor Act2Fn(string name, TensorView input, Window? window=null, TensorView mul=default)
		=> ops.Fusion(input, func:Act2Keyword[name], mul:mul, window:window);
	public Tensor Act2Fn(string name, TensorView input, float mul)
		=> ops.Fusion(input, func:Act2Keyword[name], cmul:mul);
	public Tensor LeakyRelu(TensorView input, float negative_slope=0.01f, float positive_slope=1f, Window? window=null)
		=> ops.Fusion(input, func:FusionKeyword.RELU, bias:negative_slope, scale:positive_slope, window:window);
	public Tensor Sigmoid(TensorView input, Window? window=null)
		=> ops.Fusion(input, func:FusionKeyword.SIGMOID, window:window);
	public Tensor Tanh(TensorView input, Window? window=null)
		=> ops.Fusion(input, func:FusionKeyword.TANH, window:window);

	public Tensor Normal(float loc, TensorView input)
		=> ops.Fusion(input, func:FusionKeyword.RAND_NORMAL, mul:input, cadd:loc);
	public Tensor FromImage(Texture input, float mul, float add, TensorView @out=default)
		=> ops.Fusion(input, func:FusionKeyword.FROM_IMAGE, cmul:mul, cadd:add, @out:@out);
	[ExcludeFromCodeCoverage]
	public Tensor Rotary(TensorView input, TensorView rotary, int groups=1)
		=> ops.Rotary(input, rotary, groups);
}
}