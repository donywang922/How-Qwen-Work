Shader "QwenWorld/TensorCellRelief"
{
    Properties
    {
        _MainTex ("Live tensor", 2D) = "black" {}
        _BandWidth ("Texels per band (0 = native layout)", Float) = 0
        _NormalizedData ("Unsigned normalized RGBA", Float) = 0
        _PanelSize ("Physical panel size", Vector) = (1.76,1.24,0,0)
        _ReliefDistance ("Full pixel relief range", Float) = 1.55
        [Enum(Original,0,Vivid,1)] _VividRgb ("Float RGB palette", Float) = 1
    }
    SubShader
    {
        Tags { "Queue"="Geometry" "RenderType"="Opaque" }
        Pass
        {
            Cull Off ZWrite On ZTest LEqual
            CGPROGRAM
            #pragma target 4.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_local __ RELIEF_BANDED_LAYOUT
            #include "UnityCG.cginc"

            Texture2D<float4> _MainTex;
            float4 _MainTex_TexelSize;
            float4 _BenchEye;
            float _BandWidth;
            float _NormalizedData;
            float _VividRgb;
            float4 _PanelSize;
            float _ReliefDistance;
            static const float ReliefDepth = .045;
            static const float NearestTop = .06;
            static const float FarthestTop = .95;

            struct Input
            {
                float4 position : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 position : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 view : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Input v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(Varyings, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.position = UnityObjectToClipPos(v.position);
                o.uv = v.uv;
                float3 eye = _BenchEye.w > 0 ? _BenchEye.xyz : _WorldSpaceCameraPos;
                o.view = mul(unity_WorldToObject, float4(eye, 1)).xyz - v.position.xyz;
                return o;
            }

            float4 loadCell(int2 cell, int bandWidth, int sourceHeight)
            {
                #if defined(RELIEF_BANDED_LAYOUT)
                // Unfold the display coordinate to a single original texel.
                int band = (int)((cell.y + .5) * abs(_MainTex_TexelSize.y));
                cell += band * int2(bandWidth, -sourceHeight);
                #endif
                return _MainTex.Load(int3(cell, 0));
            }

            float finiteOrZero(float value)
            {
                return (asuint(value) & 0x7fffffffu) < 0x7f800000u ? value : 0;
            }

            float topFromAlpha(float alpha)
            {
                if (_NormalizedData > .5) return lerp(FarthestTop, NearestTop, alpha);
                float signedHeight = tanh(clamp(finiteOrZero(alpha), -.4, .4) * 32);
                return lerp(FarthestTop, NearestTop, .5 + .5 * signedHeight);
            }

            float3 displayRgb(float3 rgb)
            {
                if (_NormalizedData > .5) return rgb;
                rgb = float3(finiteOrZero(rgb.r), finiteOrZero(rgb.g), finiteOrZero(rgb.b));
                float3 mapped = .5 + .5 * tanh(clamp(rgb * 8, -8, 8));
                if (_VividRgb < .5) return mapped;

                float luminance = dot(mapped, float3(.2126, .7152, .0722));
                float3 chroma = mapped - luminance;
                float baseLight = .015 + .85 * luminance * luminance * luminance;
                float below = max(-min(min(chroma.r, chroma.g), chroma.b), 1e-6);
                float above = max(max(max(chroma.r, chroma.g), chroma.b), 1e-6);
                // Fit all three channels together to retain their hue at high gain.
                float gain = min(6, min(baseLight / below, (1 - baseLight) / above));
                return baseLight + chroma * gain;
            }

            float4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                int2 sourceSize = int2(_MainTex_TexelSize.zw);
                int2 size = sourceSize;
                #if defined(RELIEF_BANDED_LAYOUT)
                size.x = (int)_BandWidth;
                size.y *= sourceSize.x / size.x;
                #endif
                float strength=smoothstep(0,.35,_ReliefDistance-length(i.view));
                if(strength<=0) {
                    int2 original=clamp(int2(i.uv*size),int2(0,0),size-1);
                    return float4(displayRgb(loadCell(original,size.x,sourceSize.y).rgb),1);
                }
                float3 view = normalize(i.view);
                float2 travel = -view.xy / max(.15, abs(view.z)) * ReliefDepth * strength / max(.01,_PanelSize.xy) * size;
                float2 start = i.uv * size;
                // No column reaches the empty space before NearestTop.
                float entry = NearestTop;
                float2 first = start + travel * entry;
                int2 direction = int2(travel.x >= 0 ? 1 : -1, travel.y >= 0 ? 1 : -1);
                int2 cell = int2(floor(first));
                // At a grid boundary, use the cell the ray enters.
                cell -= int2(first == floor(first)) * max(-direction, 0);
                float2 boundary = cell + max(direction, 0);
                float2 speed = abs(travel);
                float2 delta = 1 / max(speed, 1e-8);
                float2 next = entry + (boundary - first) * direction * delta;
                next = float2(speed.x < 1e-8 ? 2 : next.x, speed.y < 1e-8 ? 2 : next.y);

                float3 side = float3(0,0,1);
                float3 face = side;
                float depth = 1;
                float4 texel = 0;
                // Each step crosses a grid boundary. This bound covers the whole
                // texture even when a grazing ray crosses many original texels.
                [loop] for (int step = 0; step < size.x + size.y; step++)
                {
                    if (any(cell < 0) || any(cell >= size)) break;
                    texel = loadCell(cell, size.x, sourceSize.y);
                    float top = topFromAlpha(texel.a);
                    float exitDepth = min(min(next.x, next.y), FarthestTop);
                    if (top <= entry)
                    {
                        depth = entry;
                        face = side;
                        break;
                    }
                    if (top <= exitDepth)
                    {
                        depth = top;
                        break;
                    }
                    bool crossX = next.x <= next.y;
                    bool crossY = next.y <= next.x;
                    entry = exitDepth;
                    if (crossX)
                    {
                        cell.x += direction.x;
                        next.x += delta.x;
                    }
                    if (crossY)
                    {
                        cell.y += direction.y;
                        next.y += delta.y;
                    }
                    side = crossX ? float3(-direction.x,0,0) : float3(0,-direction.y,0);
                }
                clip(FarthestTop - depth);
                float3 light = normalize(float3(-.52,.44,.73));
                float shade = face.z > 0 ? 1 : .27 + .86 * saturate(dot(face, light));
                return float4(displayRgb(texel.rgb) * shade, 1);
            }
            ENDCG
        }
    }
}
