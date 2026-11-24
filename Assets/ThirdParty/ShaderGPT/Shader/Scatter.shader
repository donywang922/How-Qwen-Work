Shader "GPT/Scatter" {
Properties {
	DimO("DimO", Vector) = (1, 1, 0, 0)
	DimX("DimX", Vector) = (1, 1, 0, 0)
	DimY("DimY", Vector) = (0, 0, 0, 0) // use VecY if zero
	[HideInInspector]
	TexO("TexO", 2D) = "black" {}
	TexX("TexX", 2D) = "black" {}
	TexY("TexY", 2D) = "black" {}

	MaskO("MaskO", Int) = 15
	ChanX("ChanX", Int) = 0
	Bias ("Bias",  Int) = 0
	LogPeek("LogPeek", Int) = -1
	VecY ("VecY",  Vector) = (0, 0, 0, 0)
	DstBlend("DstBlend", Int) = 0
}
SubShader {
HLSLINCLUDE
#include "UnityCG.cginc"
#include "Common.hlsl"

DECLARE_OUTPUT(O); // output
DECLARE_TENSOR(X); // index
DECLARE_TENSOR(Y); // source
uniform uint MaskO; // output channel mask
uniform uint ChanX; // index channel
uniform int  Bias;  // index bias
uniform int  LogPeek; // index log peek bits
uniform float4 VecY; // constant source
uniform uint DstBlend;

static const uint PrimCount = 2;
static const uint InstCount = 32;
static const uint MaxPayload = 1<<20;
[maxvertexcount(8*4)] // param sweep shows 8 quads per invocation performs best for both axes
[instance(InstCount)]
void geom(triangle float4 input[3] : SV_Position, inout TriangleStream<GeomOutput> stream, uint instId : SV_GSInstanceID, uint primId : SV_PrimitiveID) {
	if(primId >= PrimCount)
		return;
#if defined(AXIS_1)
	uint count = DimX.y*4, multiple = 4;
#else
	uint count = DimX.x, multiple = 1;
#endif
	uint loopSize = (count + (PrimCount*InstCount*multiple-1)) / (PrimCount*InstCount*multiple) * multiple;

	GeomOutput o;
	// output primitive order: https://wikis.khronos.org/opengl/Geometry_Shader#Instancing
	uint start = (primId * InstCount + instId) * loopSize;
	uint stop = min(start + loopSize, count);
#if defined(AXIS_1)
	for(uint src4=start; src4<stop; src4+=4) { float4 X = SampleX(uint2(ChanX, src4/4));
	for(uint src=src4; src<min(src4+4, stop); src++)
#else
	for(uint src=start; src<stop; src++)
#endif
	{
		uint payload = src;
		float4 aabb = float4(0,0,1,1);
#if defined(AXIS_1)
		uint dst = CalcLogIndex((int)X[src%4] + Bias, LogPeek).x;
		if((MaskO & 1<<(3-dst%4)) == 0) // R=8,G=4,B=2,A=1
			continue;
		aabb.xz = (float2(0,1) + (dst/4 >> DimO.z)) / (DimO.y>>DimO.z);
		payload = (payload<<(DimO.z+2)) | (dst & ((1<<(DimO.z+2))-1));
#else
		uint dst = CalcLogIndex((int)SampleX(uint2(src, ChanX/4))[ChanX%4] + Bias, LogPeek).x;
		aabb.yw = (float2(0,1) + dst) / DimO.x;
#endif
		float clipZ = payload/(float)MaxPayload;
		aabb = aabb*2-1;
		#if UNITY_REVERSED_Z
			aabb.yw *= -1;
		#else
			clipZ = clipZ*2-1;
		#endif
		o.posCS = float4(aabb.xy, clipZ, 1); stream.Append(o);
		o.posCS = float4(aabb.xw, clipZ, 1); stream.Append(o);
		o.posCS = float4(aabb.zy, clipZ, 1); stream.Append(o);
		o.posCS = float4(aabb.zw, clipZ, 1); stream.Append(o);
		stream.RestartStrip();
	}
#if defined(AXIS_1)
	}
#endif
}
float4 main(uint2 pos, uint payload) {
	// torch.index_copy with a sliced index
	// O.index_copy_(axis, torch.select(X, 1-axis, ChanX), Y)
	// (axis == 1 ? O[i,X[c,j][jj]/4][X[c,j][jj]%4] : O[X[i,c/4][c%4],j][jj]) = Y[i,j][jj]

#if defined(AXIS_1)
	if((pos.y-(payload>>2)) & ((1<<DimO.z)-1))
		discard;
	uint src = payload>>(DimO.z+2);
	bool4 mask = (payload & 3) == uint4(0,1,2,3);
	return !mask ? 0 : DimY.x ? SampleY(uint2(pos.x, src/4))[src%4] : VecY;
#else
	uint src = payload;
	return DimY.x ? SampleY(uint2(src, pos.y)) : VecY;
#endif
}
float4 vert() : SV_Position { return 0; }
float4 frag(float4 screenPos : SV_Position) : SV_Target {
	uint2 pos = GetIndexO(screenPos).xy;
	return main(pos, round(screenPos.z*MaxPayload));
}
ENDHLSL
	Tags { "PreviewType"="Plane" } // prevent freezing Unity editor
	Pass {
		Cull Off
		Blend One [DstBlend], One [DstBlend]
		ColorMask [MaskO]
HLSLPROGRAM
#pragma target 5.0
#pragma vertex vert
#pragma geometry geom
#pragma fragment frag
#pragma shader_feature_local _ AXIS_1
ENDHLSL
	}
}
}