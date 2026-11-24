using System.Diagnostics.CodeAnalysis;
using UnityEngine.Experimental.Rendering;

namespace ShaderGPT {
public partial class Functional {
	[ExcludeFromCodeCoverage]
	public Tensor IndexSelect(TensorView input, (TensorView tex, int chan) index, int axis=0, TensorView @out=default,
			bool inputT=false, GraphicsFormat? dtype=null, int bias=0, int? logPeek=null) {
		quantizers.TryGetValue(input.tensor, out var quantizer);
		permuters.TryGetValue(input.tensor, out var permuter);
		return ops.Gather(input, index, axis, @out, inputT, dtype, bias, logPeek, quantizer:quantizer, permuter:permuter);
	}
	[ExcludeFromCodeCoverage]
	public Tensor IndexCopy_(Tensor output, (TensorView tex, int chan) index, TensorView source, int axis=0, int? logPeek=null)
		=> ops.Scatter(output, index, source, axis:axis, logPeek:logPeek);
	[ExcludeFromCodeCoverage]
	public Tensor IndexFill_(Tensor output, (TensorView tex, int chan) index, float value, int axis=0)
		=> ops.Scatter(output, index, null, axis:axis, value:value);
	[ExcludeFromCodeCoverage]
	public Tensor IndexAdd_(Tensor output, (TensorView tex, int chan) index, float value, int axis=0)
		=> ops.Scatter(output, index, null, axis:axis, value:value, add:true);
}
}