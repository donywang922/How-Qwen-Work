Shader "QwenWorld/StaticTensorGallery"
{
    Properties
    {
        _MainTex ("Model texture or preview atlas", 2D) = "black" {}
        _SignedData ("Signed floating point data", Float) = 0
        [HideInInspector] _SurfaceOffset ("Depth bias", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Geometry" "RenderType"="Opaque" }
        Pass
        {
            Cull Off ZWrite On ZTest LEqual
            Offset 0, [_SurfaceOffset]
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_local _ STATIC_PREVIEW_BAKE
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float _SignedData;

            struct Input
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Input v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(Varyings, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            float3 displayColor(float2 uv)
            {
                float3 rgb = tex2Dlod(_MainTex, float4(saturate(uv), 0, 0)).rgb;
                if (_SignedData < .5) return rgb;
                float3 value = tanh(clamp(rgb * 8, -8, 8));
                return .5 + .5 * value;
            }

            float4 frag(Varyings i) : SV_Target
            {
                #if defined(STATIC_PREVIEW_BAKE)
                // 80-pixel tiles with a two-pixel border on each side.
                float2 center = (i.uv * 80 - 2) / 76;
                float3 color = 0;
                [unroll] for (int y = 0; y < 4; y++)
                    [unroll] for (int x = 0; x < 4; x++)
                        color += displayColor(center + (float2(x, y) - 1.5) / (4 * 76));
                return float4(color / 16, 1);
                #else
                return float4(displayColor(i.uv), 1);
                #endif
            }
            ENDCG
        }
    }
}
