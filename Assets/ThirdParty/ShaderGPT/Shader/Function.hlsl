#pragma once

uint4 pcg4d(uint4 v) {
	// https://jcgt.org/published/0009/03/02/
	v = v * 1664525u + 1013904223u;
	v.x += v.y*v.w; v.y += v.z*v.x; v.z += v.x*v.y; v.w += v.y*v.z;
	v ^= v >> 16u;
	v.x += v.y*v.w; v.y += v.z*v.x; v.z += v.x*v.y; v.w += v.y*v.z;
	return v;
}
float4 rand_gumbel(uint4 seed) {
	uint4 rand = pcg4d(seed);
	return -log(-log((rand>>9u)/8388608.0 + 0.5/8388608.0)); // avoid input 0 and 1
}
float4 rand_normal(uint4 seed) {
	uint4 rand = pcg4d(seed);
	float2 radius = sqrt(-2*log((rand.xy>>9u)/8388608.0 + 0.5/8388608.0));
	float2 angle  = int2(rand.zw-2147483648) / 2147483648.0 * UNITY_PI;
	return float4(cos(angle), sin(angle)) * radius.xyxy;
}

float4 log1p(float4 x) {
	// converted from CUDA log1p PTX. error <= 1 ULP
	int4   i = (asint(x + 1.0) - asint(0.75)) &- (1<<23);
	float4 t = mad(asfloat(asint(4.0) - i), 0.25, -1.0) + asfloat(asint(x) - i);
	float4 r =    -0.04534861445426941;
	r = mad(r, t, +0.10546888411045074);
	r = mad(r, t, -0.13229703903198242);
	r = mad(r, t, +0.14491446316242218);
	r = mad(r, t, -0.16641564667224884);
	r = mad(r, t, +0.199888676404953);
	r = mad(r, t, -0.2500019669532776);
	r = mad(r, t, +0.33333510160446167);
	r = mad(r, t, -0.5);
	r = mad(r*t, t, t);
	r = mad(i, 8.262958317573066e-08, r);
	return r;
}
#define tanh tanh_
float4 tanh(float4 x) {
	// converted from CUDA tanhf PTX. error <= 1 ULP
	float4 t = x*x;
	float4 r =    +0.01573968306183815;
	r = mad(r, t, -0.05230396240949631);
	r = mad(r, t, +0.13315297663211823);
	r = mad(r, t, -0.33332768082618713);
	r = mad(r, t, 1.0);
	r = r * x;
	t = mad(max(0, 1.0 / (exp2(abs(x) * 2.885390043258667) + 1.0)), -2.0, 1.0);
	return (abs(x) >= 0.6 ? (x < 0 ? -t : t) : r);
}
float4 erf(float4 x) {
	// converted from CUDA erff PTX. error <= 1 ULP
	bool4 p = abs(x) >= +1.00295997e+00;
	float4 t = (p ? abs(x) : x * x);
	float4 r =    (p ? +0.00011219871521461755 : 8.483494457323104e-05);
	r = mad(r, t, (p ? -0.0013275252422317863 : -0.0008213091641664505));
	r = mad(r, t, (p ? +0.00839653518050909   : +0.005213488824665546));
	r = mad(r, t, (p ? -0.04024658352136612   : -0.026868773624300957));
	r = mad(r, t, (p ? +0.15950430929660797   : +0.11284004896879196));
	r = mad(r, t, (p ? +0.9129176735877991    : -0.37612664699554443));
	r = mad(r, t, (p ? +0.629060029983520     : +0.12837915122509003));
	t = (p ? -abs(x) : x);
	r = mad(r, t, t);
	t = 1 - exp2(r);
	return (p ? (x < 0 ? -t : t) : r);
}
float4 gelu(float4 x) {
	// torch.nn.functional.gelu. error <= 2 ULP
	return x * (0.5 + 0.5 * erf(x * 0.7071067811865475));
}
float4 gelu_tanh(float4 x) {
	// torch.nn.functional.gelu(approximate="tanh"). error <= 2 ULP
	return x * (0.5 + 0.5 * tanh(0.7978845608028654 * (x + x*x*x*0.044715)));
}
float4 sigmoid(float4 x) {
	// torch.nn.functional.sigmoid
	return max(0, 1.0 / (1.0 + exp(-x)));
}
float4 silu(float4 x) {
	// torch.nn.functional.silu
	return x * max(0, 1.0 / (1.0 + exp(-x)));
}
float4 softplus(float4 x, float threshold=20) {
	// torch.nn.functional.softplus
	return x > threshold ? x : log1p(exp(x));
}