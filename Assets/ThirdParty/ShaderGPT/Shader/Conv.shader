Shader "GPT/Conv" {
Properties {
	DimO("DimO", Vector) = (1, 1, 0, 0)
	DimX("DimX", Vector) = (1, 1, 0, 0)
	DimW("DimW", Vector) = (1, 1, 0, 0)
	DimQ("DimQ", Vector) = (0, 0, 0, 0)
	DimB("DimB", Vector) = (0, 0, 0, 0)
	[HideInInspector]
	TexO("TexO", 2D) = "black" {}
	TexX("TexX", 2D) = "black" {}
	TexW("TexW", 2D) = "black" {}
	TexQ("TexQ", 2D) = "black" {}
	TexB("TexB", 2D) = "black" {}

	DimHead("DimHead", Vector) = (0, 0, 0, 0)
	DstBlend("DstBlend", Int) = 0
	InstCount("InstCount", Int) = 1
}
SubShader {
HLSLINCLUDE
#include "UnityCG.cginc"
#include "Common.hlsl"

#if defined(K1_S1)
static const uint kernel_size = 1, stride = 1;
#elif defined(K3_S1)
static const uint kernel_size = 3, stride = 1;
#elif defined(K5_S1)
static const uint kernel_size = 5, stride = 1;
#elif defined(K7_S1)
static const uint kernel_size = 7, stride = 1;
#elif defined(K9_S1)
static const uint kernel_size = 9, stride = 1;
#elif defined(K11_S1)
static const uint kernel_size = 11, stride = 1;

#elif defined(K4_S2)
static const uint kernel_size = 4, stride = 2;
#elif defined(K8_S4)
static const uint kernel_size = 8, stride = 4;
#elif defined(K12_S6)
static const uint kernel_size = 12, stride = 6;
#elif defined(K16_S8)
static const uint kernel_size = 16, stride = 8;
#elif defined(K20_S10)
static const uint kernel_size = 20, stride = 10;
#elif defined(K24_S12)
static const uint kernel_size = 24, stride = 12;

#elif defined(K20_S5)
static const uint kernel_size = 20, stride = 5;
#endif

DECLARE_OUTPUT(O); // output
DECLARE_TENSOR(X); // input
DECLARE_TENSOR(W); // weight
DECLARE_TENSOR(Q); // weight quant
DECLARE_TENSOR(B); // bias
uniform uint3 DimHead; // x: group size, z: offset
uniform uint DstBlend;
uniform uint InstCount;

float4 main(uint2 pos, uint threadId, uint groupSize) {
	// torch.nn.functional.conv1d/conv_transpose1d for batched short inputs

	int offset = DimHead.z;
	uint S = DimW.y / DimQ.y;
	uint K = DimX.x / DimHead.x;
	uint I = DimO.x / DimHead.x;
	uint g = pos.x / I;
	uint i = pos.x % I;
	float4 O = 0;
#if defined(CONV1D)
	if(kernel_size == 1) {
		// O = B + torch.einsum("gkj,gik->gij", X, W)
		uint stopK = (K+3)/4;
		for(uint k=threadId; k<stopK; k+=groupSize) {
			float4 Q = SampleQ(uint2(pos.x/4, k/S));
			float4 W = Dequant(SampleW(uint2(pos.x, k)), Q[pos.x&3]);
			O += mul(W, float4x4(
				SampleX(uint2(g*K+k*4+0, pos.y)),
				SampleX(uint2(g*K+k*4+1, pos.y)),
				SampleX(uint2(g*K+k*4+2, pos.y)),
				SampleX(uint2(g*K+k*4+3, pos.y))));
		}
	} else {
		// O[g*I+i,j*4:][0:4] += W[g*I+i,k,q] * X[g*K+k,offset+j*stride*4:][q:q+4*stride:stride]
		const uint P = (kernel_size+3*stride+6)/4;
		const uint Q = (kernel_size+3)/4;
		int p0 = uint(offset>>2) + pos.y * stride;
		#define LOOP(mod4) \
		{for(uint k=threadId; k<K; k+=groupSize) { \
			float4 X[P], W; \
			[unroll] for(uint p=0; p<P; p++) \
				X[p] = uint(p0+p) >= DimX.y ? 0 : SampleX(uint2(g*K+k, p0+p)); \
			{[unroll] for(uint q=0; q<kernel_size; q++) { \
				if((q&3) == 0) { \
					float4 WQ = SampleQ(uint2(pos.x>>2, (k*Q+(q>>2))/S)); \
					W = Dequant(SampleW(uint2(pos.x, k*Q+(q>>2))), WQ[pos.x&3]); \
				} \
				uint4 p = mod4 + q + stride*uint4(0,1,2,3); \
				O += W[q&3] * float4( \
					X[p[0]>>2][p[0]&3], \
					X[p[1]>>2][p[1]&3], \
					X[p[2]>>2][p[2]&3], \
					X[p[3]>>2][p[3]&3]); \
			}} \
		}}
		switch(offset&3) {
		default: LOOP(0) break;
		case 1:  LOOP(1) break;
		case 2:  LOOP(2) break;
		case 3:  LOOP(3) break;
		}
	}
#elif defined(CONVTRANSPOSE1D)
	if(kernel_size == 1) {
		// O = B + torch.einsum("gkj,gki->gij", X, W)
		for(uint k=threadId; k<K; k+=groupSize) {
			float4 Q = SampleQ(uint2((g*K+k)/4, i/4/S));
			float4 W = Dequant(SampleW(uint2(g*K+k, i/4)), Q[(g*K+k)&3]);
			O += SampleX(uint2(g*K+k, pos.y)) * W[i%4];
		}
	} else {
		// O[g*I+i,j*4:][0:4] += W[g*K+k,i,offset+j*4-p*stride:][0:4] * X[g*K+k,p]
		const uint ITER = (kernel_size+2)/stride+1;
		const uint P = (ITER+6)/4;
		const uint Q = (kernel_size+3)/4;
		int p0 = uint(max(0, int(uint(offset)+pos.y*4 + stride-kernel_size))) / stride;
		float4 X[P], W[Q]; // avoid allocating for each branch
		#define LOOP(mod4) \
		{for(uint k=threadId; k<K; k+=groupSize) { \
			[unroll] for(uint p=0; p<P; p++) \
				X[p] = uint(p0>>2)+p >= DimX.y ? 0 : SampleX(uint2(g*K+k, uint(p0>>2)+p)); \
			[unroll] for(uint q=0; q<Q; q++) { \
				float4 WQ = SampleQ(uint2((g*K+k)>>2, (i*Q+q)/S)); \
				W[q] = Dequant(SampleW(uint2(g*K+k, i*Q+q)), WQ[(g*K+k)&3]); \
			} \
			{[unroll] for(uint p=0; p<ITER; p++) { \
				int q0 = uint(offset)+pos.y*4 - uint(p0+p)*stride; \
				float4 W0, W1; \
				if(stride <= 6) { \
					W0 = W[min(Q-1, uint(q0>>2)+0)]; \
					W1 = W[min(Q-1, uint(q0>>2)+1)]; \
				} else { \
					float4 WQ0 = SampleQ(uint2((g*K+k)>>2, (i*Q+uint(q0>>2)+0)/S)); \
					float4 WQ1 = SampleQ(uint2((g*K+k)>>2, (i*Q+uint(q0>>2)+1)/S)); \
					W0 = Dequant(SampleW(uint2(g*K+k, i*Q+uint(q0>>2)+0)), WQ0[(g*K+k)&3]); \
					W1 = Dequant(SampleW(uint2(g*K+k, i*Q+uint(q0>>2)+1)), WQ1[(g*K+k)&3]); \
				} \
				if(p == ITER-1) \
					W0 = 0; \
				switch(mod4) { \
				case 1: W0 = float4(W0.yzw, W1.x); break; \
				case 2: W0 = float4(W0.zw, W1.xy); break; \
				case 3: W0 = float4(W0.w, W1.xyz); break; \
				} \
				O += X[(int(p0+p)>>2)-(p0>>2)][(p0+p)&3] * (uint(q0) + uint4(0,1,2,3) >= kernel_size ? 0 : W0); \
			}} \
		}}
		if((stride&3) == 0 && (offset&3) == 0)
			LOOP(0)
		else
			LOOP(q0&3)
	}
#endif
	if(threadId+1 == groupSize) { // accumulate in last thread to improve accuracy
		if(DimB.x)
			O += SampleB(uint2(0, pos.x/4))[pos.x%4];
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
	float4 o = main(pos.xy, pos.z, pos.w);
	return OutputO(screenPos, o);
}
ENDHLSL
	Tags { "PreviewType"="Plane" } // prevent freezing Unity editor
	Pass {
		Cull Off
		Blend One [DstBlend], One [DstBlend]
HLSLPROGRAM
#pragma target 5.0
#pragma vertex vert
#pragma geometry geom
#pragma fragment frag
#pragma shader_feature_local CONV1D CONVTRANSPOSE1D
#pragma shader_feature_local \
	K1_S1 K3_S1 K5_S1 K7_S1 K9_S1 K11_S1\
	K4_S2 K8_S4 K12_S6 K16_S8 K20_S10 K24_S12\
	K20_S5
#pragma shader_feature_local _ QUANT_E6M11_Z14 QUANT_E6M11_Z14_W6666 QUANT_E8
ENDHLSL
	}
}
}