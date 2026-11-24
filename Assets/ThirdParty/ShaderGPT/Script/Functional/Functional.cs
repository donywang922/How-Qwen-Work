using UnityEngine;
using System.Collections.Generic;
using ShaderGPT.Backends;

namespace ShaderGPT {
public partial class Functional {
	internal ShaderOps ops = new();
	public TensorContext ctx {
		get => ops.ctx;
		set => ops.ctx = value;
	}
	public Dictionary<string, Shader> kernels {
		get => ops.kernels;
		set => ops.kernels = value;
	}
	public Dictionary<Tensor, Tensor> quantizers = new();
	public Dictionary<Tensor, Tensor> permuters = new();

	public System.IDisposable SetBatchSize(int maxBatchSize) => ops.SetBatchSize(maxBatchSize);
}
}