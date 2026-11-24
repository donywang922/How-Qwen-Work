using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace ShaderGPT.Backends {
public partial class ShaderOps {
	const int reduceSplit = 64;

	public enum ReduceKeyword {
		REDUCE_SUMPOW, REDUCE_SUMEXP, REDUCE_MINMAX,
		REDUCE_CONTINUE
	}

	public Tensor Reduce(TensorView input, ReduceKeyword func, Vector2Int groups,
			Window? window=null, Matrix4x4? swizzle=null, int indexMod=0, float bias=0) {
		Assert.IsTrue(indexMod > 0 || (input.size0%groups.x == 0 && input.size1%groups.y == 0), "group size mismatch");
		var groupSize = new Vector2Int((input.size0-1)/groups.x+1, (input.size1-1)/groups.y+1);
		int? forceInst = null;
		if(func != ReduceKeyword.REDUCE_SUMPOW && func != ReduceKeyword.REDUCE_SUMEXP) {
			forceInst = 1;
			if(indexMod == 0 && groupSize.x*groupSize.y >= reduceSplit) {
				var size0 = groups.x << Mathf.CeilToInt(Mathf.Log(groupSize.x, 2)/2);
				var size1 = groups.y << Mathf.CeilToInt(Mathf.Log(groupSize.y, 2)/2);
				// disallow padding if groups > 1
				if((groups.x == 1 || input.size0%size0 == 0) && (groups.y == 1 || input.size1%size1 == 0)) {
					var tmp = Reduce(input, func, groups:new(size0, size1), window:window, indexMod:groupSize.y, bias:bias);
					return ~ctx|Reduce(~tmp, func, groups:groups, swizzle:swizzle, indexMod:-1);
				}
			}
		}
		var (lod, inst) = ctx.ChooseLodAndInst(groups.x, groups.y, groupSize.x*groupSize.y, forceInst:forceInst);
		var output = ctx.GPUTensor(groups.x, groups.y, dtype:indexMod<0?input.dtype:GraphicsFormat.R32G32B32A32_SFloat, lod:lod);
		var clear = inst > 1 && !(func == ReduceKeyword.REDUCE_SUMPOW && float.IsNaN(swizzle?[15]??1));
		if(clear)
			Fusion(null, cmul:0, @out:output);
		var mat = Material("Reduce");
		mat.EnableKeyword(func.ToString());
		if(indexMod < 0)
			mat.EnableKeyword(nameof(ReduceKeyword.REDUCE_CONTINUE));
		SetTensor(mat, "O", output);
		SetTensor(mat, "X", input);
		mat.SetVector("DimHead", new(groupSize.x, groupSize.y, indexMod>0?indexMod:1, 0));
		mat.SetFloat("Bias", bias);
		if(window.HasValue)
			SetWindow(mat, "Win", window.Value);
		if(swizzle.HasValue) {
			mat.SetVector("Swizzle0", swizzle.Value.GetColumn(0));
			mat.SetVector("Swizzle1", swizzle.Value.GetColumn(1));
			mat.SetVector("Swizzle2", swizzle.Value.GetColumn(2));
			mat.SetVector("Swizzle3", swizzle.Value.GetColumn(3));
		}
		if(inst > 1) {
			mat.SetInt("DstBlend", (int)(clear ? BlendMode.One : BlendMode.SrcAlpha));
			mat.SetInt("InstCount", inst);
		}
		ctx.Blit(output, mat);
		return output;
	}

	public Tensor Swizzle(TensorView input, Matrix4x4 swizzle) {
		return Reduce(input, ReduceKeyword.REDUCE_SUMPOW, groups:input.shape, swizzle:swizzle,
			indexMod:-1); // inherit dtype from input
	}
}
}