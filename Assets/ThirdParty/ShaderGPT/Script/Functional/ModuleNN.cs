using UnityEngine;
using System.Collections.Generic;

namespace ShaderGPT {
public readonly struct ModuleNN {
	private readonly Functional fn;
	private readonly Dictionary<string, Tensor> state_dict;
	public ModuleNN(Functional fn, Dictionary<string, Tensor> state_dict) {
		this.fn = fn;
		this.state_dict = state_dict;
	}
	public Tensor Embedding(string path, (TensorView,int) input, string fallback=null) {
		if(fallback != null && !state_dict.ContainsKey($"{path}.weight.T") && !state_dict.ContainsKey($"{path}.weight"))
			path = fallback;
		var weightT = state_dict.ContainsKey($"{path}.weight.T");
		return fn.IndexSelect(state_dict[$"{path}.weight{(weightT?".T":"")}"], input, inputT:weightT,
			dtype:fn.ctx.defaultDType); // NOTE: always use default to avoid precision downgrade in evaluation
	}
	public Tensor Linear(string path, TensorView input, LinearProlog prolog=default, string fallback=null, float cmul=1f, TensorView add=default) {
		var hasBias = state_dict.TryGetValue($"{path}.bias", out var bias); // bias may be non-fallback
		if(fallback != null && !state_dict.ContainsKey($"{path}.weight.T") && !state_dict.ContainsKey($"{path}.weight"))
			path = fallback;
		var weightT = state_dict.ContainsKey($"{path}.weight.T");
		if(!hasBias)
			state_dict.TryGetValue($"{path}.bias", out bias);
		return fn.Linear(input, state_dict[$"{path}.weight{(weightT?".T":"")}"], bias, weightT:weightT, prolog:prolog, cmul:cmul, add:add);
	}
	public (TensorView,TensorView) Linear((string,string) path, TensorView input, LinearProlog prolog=default) {
		var weight1 = state_dict[$"{path.Item1}.weight"];
		var weight2 = state_dict[$"{path.Item2}.weight"];
		state_dict.TryGetValue($"{path.Item1}.bias", out var bias1);
		state_dict.TryGetValue($"{path.Item2}.bias", out var bias2);
		return fn.Linear(input, (weight1,weight2), (bias1,bias2), prolog:prolog);
	}
	public (TensorView,TensorView,TensorView) Linear((string,string,string) path, TensorView input, LinearProlog prolog=default) {
		var weight1 = state_dict[$"{path.Item1}.weight"];
		var weight2 = state_dict[$"{path.Item2}.weight"];
		var weight3 = state_dict[$"{path.Item3}.weight"];
		state_dict.TryGetValue($"{path.Item1}.bias", out var bias1);
		state_dict.TryGetValue($"{path.Item2}.bias", out var bias2);
		state_dict.TryGetValue($"{path.Item3}.bias", out var bias3);
		return fn.Linear(input, (weight1,weight2,weight3), (bias1,bias2,bias3), prolog:prolog);
	}
	public Tensor Conv1d(string path, TensorView input, int kernel_size, int stride=1, int dilation=1, int groups=1, int? padding=null) {
		state_dict.TryGetValue($"{path}.bias", out var bias);
		return fn.Conv1d(input, state_dict[$"{path}.weight"], bias, kernel_size, stride:stride, dilation:dilation, groups:groups, padding:padding);
	}
	public Tensor ConvTranspose1d(string path, TensorView input, int kernel_size, int stride=1, int groups=1, int? padding=null) {
		state_dict.TryGetValue($"{path}.bias", out var bias);
		return fn.ConvTranspose1d(input, state_dict[$"{path}.weight"], bias, kernel_size, stride:stride, groups:groups, padding:padding);
	}
	public LinearProlog LayerNormProlog(string path, float eps, bool rms=false) {
		Debug.Assert(!state_dict.ContainsKey($"{path}.bias"));
		return LinearProlog.LayerNorm(state_dict[$"{path}.weight"], eps, rms);
	}
	public Tensor LayerNorm(string path, TensorView input, float eps, int groups=1, bool rms=false) {
		state_dict.TryGetValue($"{path}.bias", out var bias);
		return fn.LayerNorm(input, state_dict[$"{path}.weight"], bias, eps, groups:groups, rms:rms);
	}
	public (TensorView,TensorView) LayerNorm((string,string) path, TensorView input, float eps, (int,int) groups, bool rms=false) {
		var weight1 = state_dict[$"{path.Item1}.weight"];
		var weight2 = state_dict[$"{path.Item2}.weight"];
		state_dict.TryGetValue($"{path.Item1}.bias", out var bias1);
		state_dict.TryGetValue($"{path.Item2}.bias", out var bias2);
		return fn.LayerNorm(input, (weight1,weight2), (bias1,bias2), eps, groups, rms:rms);
	}
	public Tensor LSTM(string path, TensorView input, bool bidirectional=false, Window? length=null) {
		Debug.Assert(!state_dict.ContainsKey($"{path}.bias_hh_l0"));
		return fn.LSTM(input, state_dict[$"{path}.weight_ih_l0"], state_dict[$"{path}.weight_hh_l0"],
			state_dict[$"{path}.bias_ih_l0"], bidirectional:bidirectional, length:length);
	}
}
}