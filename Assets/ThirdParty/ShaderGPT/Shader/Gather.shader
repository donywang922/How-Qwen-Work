Shader "GPT/Gather" {
Properties {
	DimO("DimO", Vector) = (1, 1, 0, 0)
	DimX("DimX", Vector) = (1, 1, 0, 0)
	DimW("DimW", Vector) = (1, 1, 0, 0)
	DimQ("DimQ", Vector) = (0, 0, 0, 0)
	[HideInInspector]
	TexO("TexO", 2D) = "black" {}
	TexX("TexX", 2D) = "black" {}
	TexW("TexW", 2D) = "black" {}
	TexQ("TexQ", 2D) = "black" {}

	ChanX("ChanX", Int) = 0
	Bias ("Bias",  Int) = 0
	LogPeek("LogPeek", Int) = -1
}
SubShader {
HLSLINCLUDE
#include "UnityCG.cginc"
#include "Common.hlsl"

DECLARE_OUTPUT(O); // output
DECLARE_TENSOR(X); // index
DECLARE_TENSOR(W); // input
DECLARE_TENSOR(Q); // input quant
uniform uint ChanX; // index channel
uniform int  Bias;  // index bias
uniform int  LogPeek; // index log peek bits

float4 main(uint2 pos) {
	// torch.index_select with a sliced index
	// O = torch.index_select(W, axis, torch.select(X, 1-axis, ChanX))
	// O[i,j][jj] = axis == 1 ?
	//    (transpose ? W[X[c,j][jj],i/4][i%4] : W[i,X[c,j][jj]/4][X[c,j][jj]%4])
	// 	: (transpose ? W[j*4+jj,X[i,c]/4][X[i,c]%4] : W[X[i,c],j][jj])

	#if defined(TRANSPOSE_W)
		uint S = (DimW.x+3) / 4 / DimQ.x;
	#else
		uint S = DimW.y / DimQ.y;
	#endif
	float4 O;
#if defined(AXIS_1)
	uint4 idx4 = CalcLogIndex((int4)SampleX(uint2(ChanX, pos.y)) + Bias, LogPeek);
	[unroll] for(int c=0; c<4; c++) {
		#if defined(TRANSPOSE_W)
			uint j = pos.x, i = idx4[c];
			float4 Q = SampleQ(uint2(i/4/S, j/4));
		#else
			uint i = pos.x, j = idx4[c];
			float Q = SampleQ(uint2(i/4, j/4/S))[i%4];
		#endif
		O[c] = Dequant(SampleW(uint2(i, j/4)), Q)[j%4];
	}
	// handle out-of-bound index values
	#if defined(TRANSPOSE_W)
		O = idx4   < DimW.x ? O : 0;
	#else
		O = idx4/4 < DimW.y ? O : 0;
	#endif
#else
	uint idx = CalcLogIndex((int)SampleX(uint2(pos.x, ChanX/4))[ChanX%4] + Bias, LogPeek).x;
	#if defined(TRANSPOSE_W)
		float4 Q = SampleQ(uint2(pos.y/S, idx/4));
		[unroll] for(int c=0; c<4; c++)
			O[c] = Dequant(SampleW(uint2(pos.y*4+c, idx/4)), Q)[idx%4];
		O = idx/4 < DimW.y ? O : 0;
	#else
		float4 Q = SampleQ(uint2(idx/4, pos.y/S));
		O = Dequant(SampleW(uint2(idx, pos.y)), Q[idx%4]);
		O = idx   < DimW.x ? O : 0;
	#endif
#endif
	return O;
}
float4 vert(float2 uv : TEXCOORD0) : SV_Position {
	return ClipO(uv);
}
float4 frag(float4 screenPos : SV_Position) : SV_Target {
	uint2 pos = GetIndexO(screenPos).xy;
	return main(pos);
}
ENDHLSL
	Tags { "PreviewType"="Plane" } // prevent freezing Unity editor
	Pass {
		Cull Off
HLSLPROGRAM
#pragma target 5.0
#pragma vertex vert
#pragma fragment frag
#pragma shader_feature_local _ AXIS_1
#pragma shader_feature_local _ TRANSPOSE_W
#pragma shader_feature_local _ QUANT_E6M11_Z14 QUANT_E6M11_Z14_W6666 QUANT_E8
ENDHLSL
	}
}
}