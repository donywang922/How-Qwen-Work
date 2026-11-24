Shader "GPT/Fusion" {
Properties {
	DimO("DimO", Vector) = (1, 1, 0, 0)
	DimX("DimX", Vector) = (1, 1, 0, 0)
	DimY("DimY", Vector) = (0, 0, 0, 0)
	DimMul("DimMul", Vector) = (0, 0, 0, 0)
	DimAdd("DimAdd", Vector) = (0, 0, 0, 0)
	DimWin("DimWin", Vector) = (0, 0, 0, 0)
	[HideInInspector]
	TexO("TexO", 2D) = "black" {}
	TexX("TexX", 2D) = "black" {}
	TexY("TexY", 2D) = "black" {}
	TexMul("TexMul", 2D) = "black" {}
	TexAdd("TexAdd", 2D) = "black" {}
	TexWin("TexWin", 2D) = "black" {}

	DimHead("DimHead", Vector) = (0, 0, 0, 0)
	Win    ("Win",     Vector) = (0, 1000000000, 0, 0)
	Win2   ("Win2",    Vector) = (0, 1000000000, 0, 0)
	Stride0("Stride0", Vector) = (0, 0, 0, 0)
	Stride1("Stride1", Vector) = (0, 0, 0, 0)
	VecMul ("VecMul",  Vector) = (1, 1, 1, 1)
	VecAdd ("VecAdd",  Vector) = (0, 0, 0, 0)
	Scale("Scale", Float) = 1
	Bias ("Bias",  Float) = 0
	DstBlend("DstBlend", Int) = 0
}
SubShader {
HLSLINCLUDE
#include "UnityCG.cginc"
#include "Common.hlsl"
#include "Function.hlsl"

DECLARE_OUTPUT(O); // output
DECLARE_TENSOR(X); // input
DECLARE_TENSOR(Y); // other
DECLARE_TENSOR(Mul);
DECLARE_TENSOR(Add);
DECLARE_TENSOR(Win);
uniform uint2 DimHead; // xy: group size
uniform float4 Win;
uniform float4 Win2;
uniform int4 Stride0;
uniform int4 Stride1;
uniform float4 VecMul;
uniform float4 VecAdd;
uniform float Scale;
uniform float Bias;
uniform uint DstBlend;
SamplerState samplerTexX;

int2 applyPadding(int2 coord, uint2 size, int mode) {
	switch(mode) {
	case 1: return clamp(coord, 0, int2(size-1)); // replicate
	case 2: return abs(coord) - 2*max(0, coord-int2(size-1)); // reflect
	case 3: return uint2(coord+size) % size; // circular (following torch, "wrapping around more than once" is not handled)
	case -1: return uint2(coord+size) % size + (uint2(coord+size) / size - 1).yx; // reshape
	default: return coord; // constant
	}
}

float4 main(uint2 pos) {
	// O = func(X) * Mul + Add

	float4 X = SampleX(pos.xy % DimX.xy);
	float4 O = X;
	int4 index = pos.y % DimHead.y * 4 + uint4(0,1,2,3);
	int2 range = ceil(GetRange(SampleWin(uint2(min(DimWin.x-1, pos.x), 0)), Win, Win2));
	bool4 mask = range.x <= index && index < range.y;

	// broadcastable math
	#if defined(CLAMP)   // torch.clamp
		O = clamp(X, Bias, Scale);
	#elif defined(COS)   // torch.cos
		O = cos(X);
	#elif defined(EXP)   // torch.exp
		O = exp2(X * (Scale/log(2)));
	#elif defined(LOG)   // torch.log
		O = log2(X) * (Scale*log(2));
	#elif defined(POW)   // torch.pow
		switch(asuint(Scale)) {
		case 0xbf800000: O = rcp(X);   break; // -1.0
		case 0xbf000000: O = rsqrt(X); break; // -0.5
		case 0x3f000000: O = sqrt(X);  break; // +0.5
		case 0x3f800000: O = X;        break; // +1.0
		case 0x40000000: O = pow(X,2); break; // +2.0
		default:         O = pow(abs(X), Scale); break;
		}
	#elif defined(ROUND) // torch.round
		switch(asuint(Bias)) {
		case 0xbf800000: O = floor(X); break; // -1.0
		case 0x3f800000: O = ceil(X);  break; // +1.0
		case 0x80000000: O = trunc(X); break; // -0.0
		default:         O = round(X); break;
		}
	#elif defined(SIN)   // torch.sin
		O = sin(X);

	// broadcastable activations
	#elif defined(GELU)     // torch.nn.functional.gelu
		O = gelu(X);
	#elif defined(GELU_TANH) // torch.nn.functional.gelu(approximate="tanh")
		O = gelu_tanh(X);
	#elif defined(RELU)     // torch.nn.functional.leaky_relu
		O = Scale * max(0,X) + Bias * min(0,X);
	#elif defined(SIGMOID)  // torch.nn.functional.sigmoid
		O = sigmoid(X);
	#elif defined(SILU)     // torch.nn.functional.silu
		O = silu(X);
	#elif defined(SOFTPLUS) // torch.nn.functional.softplus
		O = softplus(X);
	#elif defined(TANH)     // torch.nn.functional.tanh
		O = tanh(X);

	// random generator
	#elif defined(RAND_NORMAL) // torch.distributions.normal
		O = rand_normal(uint4(pos.xy, asuint(_Time.y), asuint(_SinTime.w)));

	// normalizations
	#elif defined(GROUPNORM) // torch.nn.functional.{group|layer}_norm
		// according to ATen/native/cpu/{group|layer}_norm_kernel.cpp in pytorch
		// layer_norm applies rstd/mean before mul/add, while others merge them into mul/add
		if(Scale < 0 && index.x >= range.y)
			discard; // window culling
		float4 R = SampleY(pos.xy / DimHead.xy);
		float mean = R[1]/max(1,R[0]);
		float rstd = rsqrt(Bias + max(0, R[2]/max(1,R[0]) - mean*mean));
		if(Scale) // default is layer_norm
			O = (X - mean) * rstd, mean = 0, rstd = 1;
	#elif defined(SOFTMAX) // torch.nn.functional.softmax
		if(Scale < 0 && index.x >= range.y)
			discard; // window culling
		float4 R = SampleY(pos.xy / DimHead.xy);
		const float4 SUMEXP2_BIAS = float4(3,1,-1,-3)/2*(256-48);
		float2 sumBias = // make sure 1/R[i] is not subnormal
			  R[0] < exp2(126) ? float2(R[0], SUMEXP2_BIAS[0])
			: R[1] < exp2(126) ? float2(R[1], SUMEXP2_BIAS[1])
			: R[2] < exp2(126) ? float2(R[2], SUMEXP2_BIAS[2])
			: float2(R[3], SUMEXP2_BIAS[3]);
		X = X / log(2);
		O = saturate(exp2(X - trunc(X)) * exp2(trunc(X) + sumBias.y) / sumBias.x);

	// common functions
	#elif defined(FROM_IMAGE)
		float2 uv = (pos.yx+0.5)/DimO.yx;
		#if UNITY_UV_STARTS_AT_TOP
			uv.y = 1 - uv.y;
		#endif
		X = TexX.SampleLevel(samplerTexX, uv * TexX_ST.xy + TexX_ST.zw, 0);
		X.r = LinearToGammaSpaceExact(X.r);
		X.g = LinearToGammaSpaceExact(X.g);
		X.b = LinearToGammaSpaceExact(X.b);
		O = round(X*255)/255;
	#elif defined(LSTM_CELL) // torch.nn.LSTMCell
		uint J = DimHead.y;
		uint j = pos.y + pos.y/J * (DimY.y/(DimO.y/DimHead.y)-J);
		[branch]
		if(Bias == 0) {
			float4 Wi = SampleY(uint2(pos.x, j+J*0));
			float4 Wf = SampleY(uint2(pos.x, j+J*1));
			float4 Wg = SampleY(uint2(pos.x, j+J*2));
			O = sigmoid(Wi) * tanh(Wg) + sigmoid(Wf) * X;
		} else {
			float4 Wo = SampleY(uint2(pos.x, j+J*3));
			O = sigmoid(Wo) * tanh(X);
		}
	#elif defined(ROTARY) // transformers.models.llama.apply_rotary_pos_emb
		uint j = pos.y % DimHead.y;
		uint J = DimY.y;
		if(j < J) {
			uint2 k = j+(J+uint2(0,1))/2;
			float4 Z, W, Y = SampleY(uint2(pos.x, j));
			if(J%2 == 0) {
				W = SampleY(uint2(pos.x, k.x%J));
				Z = SampleX(uint2(pos.x, pos.y-j+k.x%J));
			} else {
				W.xy = SampleY(uint2(pos.x, k.x%J)).zw;
				W.zw = SampleY(uint2(pos.x, k.y%J)).xy;
				Z.xy = SampleX(uint2(pos.x, pos.y-j+k.x%J)).zw;
				Z.zw = SampleX(uint2(pos.x, pos.y-j+k.y%J)).xy;
			}
			O = k.xxyy >= J ? Z*Y + X*W : X*Y - Z*W;
		}

	// shape manipulations
	#elif defined(NARROW) // torch.narrow
		uint J = DimX.y/(DimO.y/DimHead.y);
		uint k = pos.y / DimHead.y;
		uint i = pos.x;
		index += range.x;
		mask = index < range.y;
		[unroll] for(uint c=0; c<4; c++) {
			uint2 p = applyPadding(int2(i, index[c]), uint2(DimX.x, J*4), Bias);
			O[c] = p.x < DimX.x && p.y/4 < J ? SampleX(uint2(p.x, k*J+p.y/4))[p.y%4] : 0;
		}
	#elif defined(STRIDED) // torch.as_strided
		// output[(i0*sizeI[0]+i1)*sizeI[1]+i2, (j0*sizeJ[0]+j1)*sizeJ[1]+j2] == input[
		//    offsetI + i0*stepII[0]+i1*stepII[1]+i2*stepII[2] + j0*stepJI[0]+j1*stepJI[1]+j2*stepJI[2],
		//    offsetJ + i0*stepIJ[0]+i1*stepIJ[1]+i2*stepIJ[2] + j0*stepJJ[0]+j1*stepJJ[1]+j2*stepJJ[2]]
		uint2 sizeI = DimY.xy, sizeJ = DimY.zw;
		uint3 stepII = uint3(Stride0.x, max(0, Stride0.yz)), stepIJ = uint3(0, max(0, -Stride0.yz));
		uint3 stepJJ = uint3(Stride1.x, max(0, Stride1.yz)), stepJI = uint3(0, max(0, -Stride1.yz));
		uint offsetI = Stride0.w;
		uint offsetJ = Stride1.w;
		[unroll] for(uint c=0; c<4; c++) {
			uint j = pos.y*4+c;
			uint j2 = j % sizeJ[1];
			uint j1 = j / sizeJ[1] % sizeJ[0];
			uint j0 = j / sizeJ[1] / sizeJ[0];
			uint i2 = pos.x % sizeI[1];
			uint i1 = pos.x / sizeI[1] % sizeI[0];
			uint i0 = pos.x / sizeI[1] / sizeI[0];
			uint k = offsetI + dot(stepII, uint3(i0,i1,i2)) + dot(stepJI, uint3(j0,j1,j2));
			uint l = offsetJ + dot(stepIJ, uint3(i0,i1,i2)) + dot(stepJJ, uint3(j0,j1,j2));
			uint2 p = applyPadding(int2(k, l), uint2(DimX.x, DimX.y*4), Bias);
			O[c] = p.x < DimX.x && p.y/4 < DimX.y ? SampleX(uint2(p.x, p.y/4))[p.y%4] : 0;
		}
	#endif

	O = mask ? O : 0;

	float4 mul = VecMul;
	float4 add = VecAdd;
	if(DimMul.x)
		mul *= SampleMul(pos.xy % DimMul.xy);
	if(DimAdd.x)
		add += SampleAdd(pos.xy % DimAdd.xy);
	#if defined(GROUPNORM)
		mul *= rstd;
		add -= mean * mul;
	#endif
	#if defined(WHERE) // WHERE uses mul/add for true/false values
		O = O ? mul : add;
	#else
		O = mad(VecMul == 0 ? 0 : O, mul, add); // clear infinity/nan when VecMul==0
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
		Blend One [DstBlend], One [DstBlend]
HLSLPROGRAM
#pragma target 5.0
#pragma vertex vert
#pragma fragment frag
#pragma shader_feature_local _\
	CLAMP COS EXP LOG POW ROUND SIN WHERE\
	GELU GELU_TANH RELU SIGMOID SILU SOFTPLUS TANH\
	RAND_NORMAL\
	GROUPNORM SOFTMAX\
	FROM_IMAGE LSTM_CELL ROTARY\
	NARROW STRIDED
ENDHLSL
	}
}
}