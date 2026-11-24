Shader "GPT/Linear" {
Properties {
	DimO("DimO", Vector) = (1, 1, 0, 0)
	DimX("DimX", Vector) = (1, 1, 0, 0)
	DimY("DimY", Vector) = (0, 0, 0, 0)
	DimW("DimW", Vector) = (1, 1, 0, 0)
	DimQ("DimQ", Vector) = (0, 0, 0, 0)
	DimB("DimB", Vector) = (0, 0, 0, 0)
	DimAdd("DimAdd", Vector) = (0, 0, 0, 0)
	DimWin("DimWin", Vector) = (0, 0, 0, 0)
	DimReduce("DimReduce", Vector) = (0, 0, 0, 0)
	[HideInInspector]
	TexO("TexO", 2D) = "black" {}
	TexX("TexX", 2D) = "black" {}
	TexY("TexY", 2D) = "black" {}
	TexW("TexW", 2D) = "black" {}
	TexQ("TexQ", 2D) = "black" {}
	TexB("TexB", 2D) = "black" {}
	TexAdd("TexAdd", 2D) = "black" {}
	TexWin("TexWin", 2D) = "black" {}
	TexReduce("TexReduce", 2D) = "black" {}

	DimHead("DimHead", Vector) = (0, 0, 0, 0)
	Win    ("Win",     Vector) = (0, 1000000000, 0, 0)
	Win2   ("Win2",    Vector) = (0, 1000000000, 0, 0)
	VecMul ("VecMul",  Vector) = (1, 1, 1, 1)
	Bias("Bias", Float) = 0
	DstBlend("DstBlend", Int) = 0
	InstCount("InstCount", Int) = 1
}
SubShader {
HLSLINCLUDE
#include "UnityCG.cginc"
#include "Common.hlsl"
#include "Function.hlsl"

DECLARE_OUTPUT(O); // output
DECLARE_TENSOR(X); // input
DECLARE_TENSOR(Y); // other
DECLARE_TENSOR(W); // weight
DECLARE_TENSOR(Q); // weight quant
DECLARE_TENSOR(B); // bias
DECLARE_TENSOR(Add);
DECLARE_TENSOR(Win);
DECLARE_TENSOR(Reduce);
uniform uint3 DimHead; // x: input head size, y: output head size, z: input head count / weight head count
uniform float4 Win;
uniform float4 Win2;
uniform float4 VecMul;
uniform float Bias;
uniform uint DstBlend;
uniform uint InstCount;

float4 main(uint2 pos, uint threadId, uint groupSize) {
	// torch.nn.functional.linear with multi-head support
	// O = B + torch.einsum(f"ihk,{'khj' if transpose else 'jhk'}->ihj", X, W)
	// O[i,h*J+j][jj] += X[i,h*K+k][kk] * (transpose ? W[k*4+kk,g(h)*J+j][jj] : W[j*4+jj,g(h)*K+k][kk])
	// LINEAR_CONV transposes last two dims of X,W,O
	// O = B + torch.einsum(f"ikj,{'khj' if transpose else 'hkj'}->ihj", X, W)
	// O[i,h*J+j][jj] += X[i,k*J+j][jj] * (transpose ? W[k,h*J+j][jj] : W[h,k*J+j][jj])

	#if defined(TRANSPOSE_W)
		uint S = (DimW.x+3) / 4 / DimQ.x;
	#else
		uint S = DimW.y / DimQ.y;
	#endif
	uint J = DimHead.y;
	uint K = DimHead.x;
	uint j = pos.y % J;
	uint h = pos.y / J;
	uint g = h / DimHead.z;
	uint stopK = K;
#if !defined(LINEAR_CONV)
	#if defined(LINEAR_SOFTMAX)
		float2 range = GetRange(SampleWin(uint2(min(DimWin.x-1, pos.x), 0)), Win, Win2);
	#endif
	if(Win.z != 0) { // early stop/return for sdpa
		int2 range4 = ceil(GetRange(SampleWin(uint2(min(DimWin.x-1, pos.x), 0)), Win, Win2)/4);
		#if defined(TRANSPOSE_W)
			stopK = min(K, (uint)max(0, range4.y));
		#else
			if(int(j) < range4.x-1 || int(j) >= range4.y)
				discard;
		#endif
	}
#endif
	float4 R = SampleReduce(uint2(pos.x, h));
#if defined(LINEAR_GROUPNORM)
	float mean = R[1]/max(1,R[0]);
	float rstd = rsqrt(Bias + max(0, R[2]/max(1,R[0]) - mean*mean));
#elif defined(LINEAR_SOFTMAX)
	const float4 SUMEXP2_BIAS = float4(3,1,-1,-3)/2*(256-48);
	float2 sumBias =
		  R[0] < exp2(126) ? float2(R[0], SUMEXP2_BIAS[0])
		: R[1] < exp2(126) ? float2(R[1], SUMEXP2_BIAS[1])
		: R[2] < exp2(126) ? float2(R[2], SUMEXP2_BIAS[2])
		: float2(R[3], SUMEXP2_BIAS[3]);
#endif
	float4 O = 0;
	for(uint k=threadId; k<stopK; k+=groupSize) {
#if !defined(LINEAR_CONV)
		float4 X = SampleX(uint2(pos.x, h*K+k));
#if defined(LINEAR_MUL)
		X = (DimY.x ? SampleY(uint2(pos.x, h*K+k)) : 1) * X;
#elif defined(LINEAR_GELU_MUL)
		X = (DimY.x ? SampleY(uint2(pos.x, h*K+k)) : 1) * gelu(X);
#elif defined(LINEAR_SILU_MUL)
		X = (DimY.x ? SampleY(uint2(pos.x, h*K+k)) : 1) * silu(X);
#elif defined(LINEAR_GROUPNORM)
		X = (DimY.x ? SampleY(uint2(pos.x, h*K+k) % DimY.xy) : 1) * ((X-mean)*rstd);
#elif defined(LINEAR_SOFTMAX)
		float4 index = float(k) * 4 + float4(0,1,2,3);
		X = X / log(2);
		X = saturate(exp2(X - trunc(X)) * exp2(trunc(X) + sumBias.y) / sumBias.x);
		X = range.x <= index && index < range.y ? X : 0;
#endif
		// for performance reason, out-of-bound read is not handled when DimW.x%4 != 0
		#if defined(TRANSPOSE_W)
			float4 Q = SampleQ(uint2(k/S, g*J+j));
			O += (X[0] * Dequant(SampleW(uint2(k*4+0, g*J+j)), Q)
				+ X[1] * Dequant(SampleW(uint2(k*4+1, g*J+j)), Q)
				+ X[2] * Dequant(SampleW(uint2(k*4+2, g*J+j)), Q)
				+ X[3] * Dequant(SampleW(uint2(k*4+3, g*J+j)), Q));
		#else
			float4 Q = SampleQ(uint2(j, (g*K+k)/S));
			O[0] += dot(X, Dequant(SampleW(uint2(j*4+0, g*K+k)), Q[0]));
			O[1] += dot(X, Dequant(SampleW(uint2(j*4+1, g*K+k)), Q[1]));
			O[2] += dot(X, Dequant(SampleW(uint2(j*4+2, g*K+k)), Q[2]));
			O[3] += dot(X, Dequant(SampleW(uint2(j*4+3, g*K+k)), Q[3]));
		#endif
#else // TODO: quant
		float4 X = SampleX(uint2(pos.x, k*J+j));
		#if defined(TRANSPOSE_W)
			O += X * SampleW(uint2(k, h*J+j));
		#else
			O += X * SampleW(uint2(h, k*J+j));
		#endif
#endif
	}
	O *= VecMul;
	if(threadId+1 == groupSize) { // accumulate in last thread to improve accuracy
		if(DimB.x)
			O += VecMul * SampleB(uint2(0, pos.y));
		if(DimAdd.x)
			O += SampleAdd(pos.xy % DimAdd.xy);
	}
	return O;
}
float4 vert(float2 uv : TEXCOORD0) : SV_Position {
	return ClipO(uv);
}
[maxvertexcount(3)][instance(32)]
void geom(triangle float4 input[3] : SV_Position, inout TriangleStream<GeomOutput> stream, uint instId : SV_GSInstanceID) {
	GeomInstance(input, stream, instId, InstCount);
}
float4 frag(float4 screenPos : SV_Position) : SV_Target {
	uint4 pos = GetIndexO(screenPos, InstCount);
	float4 o = main(pos.xy, pos.z, pos.w); // save in temp variable so that discard works
	return OutputO(screenPos, o);
}
ENDHLSL
	Tags { "PreviewType"="Plane" } // prevent freezing Unity editor
	Pass {
		Name "LINEAR"
		Cull Off
		Blend One [DstBlend], One [DstBlend]
HLSLPROGRAM
#pragma target 5.0
#pragma vertex vert
#pragma geometry geom
#pragma fragment frag
#pragma shader_feature_local _ \
	LINEAR_CONV LINEAR_GROUPNORM LINEAR_SOFTMAX \
	LINEAR_MUL LINEAR_GELU_MUL LINEAR_SILU_MUL
#pragma shader_feature_local _ TRANSPOSE_W
#pragma shader_feature_local _ QUANT_E6M11_Z14 QUANT_E6M11_Z14_W6666 QUANT_E8
ENDHLSL
	}
}
}