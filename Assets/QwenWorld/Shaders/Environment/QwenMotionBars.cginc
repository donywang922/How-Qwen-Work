#ifndef QWEN_MOTION_BARS_INCLUDED
#define QWEN_MOTION_BARS_INCLUDED

float _ShowBlend, _BusyBlend, _MotionTime, _OrbitIdleTime, _OrbitBusyTime;

float QwenBarEase(float p)
{
    return p * p * (3 - 2 * p);
}

float4 QwenBarPosition(float id, float q, float2 crossSection)
{
    float t = _MotionTime;
    float h = frac(sin(id * 39.17 + 2.5) * 43758.5453);
    float direction = step(.5, frac(h * 13.37)) * 2 - 1;
    float baseAngle = id * 1.0472 + h * .35;
    float radius = .61 + h * .13;
    float length = .22 + h * .14;
    float rate = .55 + h * .18;
    float advance = 1.9;
    float p = frac(_OrbitBusyTime * rate + h * 3);
    float tail = p > .5 ? advance * QwenBarEase((p - .5) * 2) : 0;
    float head = advance * QwenBarEase(min(1, p * 2));

    // The shared orbit keeps its accumulated turns through every state change.
    // Only the bounded, periodic head/tail correction is blended.
    float orbit = _OrbitIdleTime * (.12 + h * .08) + _OrbitBusyTime * rate * advance;
    float correction = lerp(tail, head, q) - p * advance;
    float angle = baseAngle + direction * (orbit + length * q + _BusyBlend * correction);
    float3 hidden = float3(sin(angle) * (radius + crossSection.x),
        .3 + id * .23 + crossSection.y, cos(angle) * (radius + crossSection.x));

    float phase = frac(t * (.30 + h * .11) + h + id * .17);
    float held = phase < .46 ? phase / .46 * .5 : phase > .54 ? .5 + (phase - .54) / .46 * .5 : .5;
    float ascend = 1.0 + tan((held - .5) * 2.6) / tan(1.3) * 1.7 + (h - .5) * .3;
    float y = lerp(.95 + sin(t * (.5 + h * .3) + id * 2.1) * .26 + (h - .5) * .25,
        ascend, _BusyBlend);
    float3 vertical = float3(sin(baseAngle) * (radius + crossSection.x),
        y + (q - .5) * (.25 + h * .17), cos(baseAngle) * (radius + crossSection.x))
        + float3(cos(baseAngle) * crossSection.y, 0, -sin(baseAngle) * crossSection.y);
    float fade = smoothstep(0, .07, phase) * (1 - smoothstep(.93, 1, phase));
    return float4(lerp(hidden, vertical, _ShowBlend), lerp(1, lerp(1, fade, _BusyBlend), _ShowBlend));
}

#endif
