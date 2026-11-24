#pragma once
#pragma warning (error : 3206) // implicit truncation of vector type
#pragma warning (error : 3203) // signed/unsigned mismatch
// #pragma use_dxc
// #pragma enable_d3d11_debug_symbols

uint4 QuadReadAcrossX(uint4 value, bool2 lane) {
	float w = lane.x ? -1 : 1;
	float4 lo = value & 0xffffu;
	float4 hi = value >> 16;
	lo += w * ddx_fine(lo);
	hi += w * ddx_fine(hi);
	return (uint4)round(lo) | ((uint4)round(hi) << 16);
}
uint4 QuadReadAcrossY(uint4 value, bool2 lane) {
	float w = lane.y ? -1 : 1;
	float4 lo = value & 0xffffu;
	float4 hi = value >> 16;
	lo += w * ddy_fine(lo);
	hi += w * ddy_fine(hi);
	return (uint4)round(lo) | ((uint4)round(hi) << 16);
}
float4 QuadAverage(float4 value, bool2 lane) {
	float2 w = lane ? -0.5 : +0.5;
	value += ddx_fine(value) * w.x;
	value += ddy_fine(value) * w.y;
	return value;
}

float4 Dequant(float4 x, float4 q, float estep=2) {
#if defined(QUANT_E6M11_Z14) || defined(QUANT_E6M11_Z14_W6666)
	// param sweep on qwen3 GGUF shows E5M12_Z14 > E6M11_Z14 > else
	// unfortunately D3D11 flushes denorms to zero, so E5 would break denorm float16
	// https://learn.microsoft.com/en-us/windows/win32/direct3d11/floating-point-rules#deviations-or-additional-requirements-from-ieee-754-rules
	const int E_BITS = 6, M_BITS = 11, Z_BITS = 31-E_BITS-M_BITS;
	// weight == ((q4 ? weight_u4 : weight_u8) - zero_u/(1<<Z_BITS)*(q4?16:256) + 0.5) * scale_f
	int4 i32 = asint(q);
	float4 mul = i32 < 0 ? 15 : 255;
	float4 add = mad(float4(i32 & (1<<Z_BITS)-1), mad(mul, -1.0/(1<<Z_BITS), -1.0/(1<<Z_BITS)), 0.5);
	int4 core = (i32 >> 8-E_BITS) & ((1u << 23+E_BITS)-(1u << 23-M_BITS)-(1u<<31));
	float4 scale = asfloat(core) * ldexp(1, 128-(1 << E_BITS-1));
	#if defined(QUANT_E6M11_Z14_W6666)
		x = ((uint)round(dot(x.rgb, 255 << int3(0,8,16))) >> uint4(0,6,12,18)) & 63u, mul = 1;
	#endif
	return mad(x, mul, add) * scale;
#elif defined(QUANT_E8)
	// weight == (weight_u8 - (weight_u8 > max_u8 ? 255 : 0)) / 256 * exp2(exp_i8 * 0.5)
	float4 i8 = round(q*255 - (q > 0.5 ? 256 : 0));
	float4 type = round(i8/85);
	float4 offset = type * 0.25 + 0.5;
	float4 scale = exp2((i8 - type*85) / estep - log2(256));
	return mad(x, 255, x > offset ? -255 : 0) * scale;
#else
	return x;
#endif
}

float4 vertClip(float2 uv, uint4 dim=uint4(1,1,0,0), int4 st=int4(1,1,0,0)) {
	// SV_VertexID is not reliable on opengl & vulkan due to mesh batching
	// Blit() draws two triangles with UV (0,0)(0,1)(1,1) and (0,0)(1,1)(1,0)
	uv = float2(uv.x, 1-uv.y) * 2; // cover viewport with first triangle to reduce helper lanes
	if(dim.z == 0 && st.x+st.y > 2) // clip only when tiling is off and ST is set
		uv.yx = (uv.yx * dim.xy + st.zw) / st.xy;
	float4 posCS = float4(uv*2-1, 0, 1);
	#if UNITY_UV_STARTS_AT_TOP
		posCS.y *= -1;
	#endif
	return posCS;
}

struct GeomOutput { float4 posCS : SV_Position; };
void GeomInstance(float4 input[3], inout TriangleStream<GeomOutput> stream, uint instId, uint instCount) {
	if(instId >= instCount)
		return;
	GeomOutput o;
	[unroll] for(int i=0; i<3; i++) {
		o.posCS = input[i];
		o.posCS.z = instId/32.0;
		if(UNITY_NEAR_CLIP_VALUE < 0)
			o.posCS.z = o.posCS.z*2-1;
		stream.Append(o);
	}
}

uint2 getThreadId(float4 screenPos, uint4 dim) {
	uint2 pos = screenPos.yx; // trunc
	pos.y = (pos.y<<dim.z) | (pos.x & ((1<<dim.z)-1));
	pos.x >>= dim.z;
	return pos;
}
uint4 getThreadIdAndGroupSize(float4 screenPos, uint4 dim, uint instCount) {
	uint2 pos = screenPos.yx; // trunc
	uint2 coord = pos & ((1<<dim.w)-1);
	coord = (coord|coord<<4) & 0x0F0F;
	coord = (coord|coord<<2) & 0x3333;
	coord = (coord|coord<<1) & 0x5555;
	uint threadId = coord.x + coord.y*2; // morton order
	uint groupSize = (1<<dim.w)<<dim.w;
	pos >>= dim.w;
	pos.y = (pos.y<<dim.z) | (pos.x & ((1<<dim.z)-1));
	pos.x >>= dim.z;
	threadId += groupSize * (uint)round(screenPos.z*32);
	groupSize *= instCount;
	return uint4(pos, threadId, groupSize);
}
float4 mixGroupOutput(float4 screenPos, uint4 dim, float4 value) {
	if(dim.w != 0) {
		value *= (1<<dim.w)<<dim.w;
		// compute exact mip 1 in mip 0 for two reasons:
		// 1. unity produces inaccurate mip 1 with linear filtering in dx12 & vulkan
		// 2. mip generation can be skipped when mipCount <= 2
		value = QuadAverage(value, uint2(screenPos.xy) & 1);
	}
	return value;
}

float4 loadTensor(Texture2D<float4> tex, uint4 dim, uint2 ij) {
	// we only need to handle lod 0 and 1 now
	return tex[uint2(ij.y>>dim.z, (ij.y & ((1<<dim.z)-1)) | (ij.x<<dim.z))<<dim.w];
	// return tex.mips[dim.w>>(dim.w==1)][uint2(ij.y>>dim.z, (ij.y & ((1<<dim.z)-1)) | (ij.x<<dim.z))<<(dim.w==1)];
}

#define DECLARE_TENSOR(X) Texture2D<float4> Tex##X; int4 Tex##X##_ST; uint4 Dim##X; \
float4 Sample##X(uint2 ij) { \
	return loadTensor(Tex##X, Dim##X, Tex##X##_ST.zw+ij); \
}

#define DECLARE_OUTPUT(X) int4 Tex##X##_ST; uint4 Dim##X; \
uint4 GetIndex##X(float4 screenPos, uint instCount=1) { \
	uint4 pos = getThreadIdAndGroupSize(screenPos, Dim##X, instCount); \
	pos.xy -= Tex##X##_ST.zw; \
	if(any(pos.xy >= Dim##X.xy)) \
		discard; \
	return pos; \
} \
float4 Clip##X(float2 uv) { \
	return vertClip(uv, Dim##X, Tex##X##_ST); \
} \
float4 Output##X(float4 screenPos, float4 value) { \
	return mixGroupOutput(screenPos, Dim##X, value); \
}

float2 GetRange(float4 tex, float4 win1, float4 win2) {
	float2 range1 = win1.xy + win1.z * tex[(int)win1.w];
	float2 range2 = win2.xy + win2.z * tex[(int)win2.w];
	return float2(max(range1.x, range2.x), min(range1.y, range2.y));
}
int4 CalcLogIndex(int4 index, int peek_bits) {
	if(peek_bits < 0)
		return index;
	return index < 0 ? index : (firstbitlow(1+(index >> peek_bits)) << peek_bits) | (index & (1<<peek_bits)-1);
}