Shader "QwenWorld/QwenGirlStandee"
{
    Properties
    {
        _MainTex ("Open eyes and fixed body", 2D) = "white" {}
        _ClosedTex ("Closed expression reference", 2D) = "white" {}
        _EyesClosed ("Eyes closed", Range(0,1)) = 0
        _FaceRect ("Expression patch UV bounds", Vector) = (.435,.817,.595,.887)
        _FaceFeather ("Expression patch feather", Range(.001,.03)) = .008
        _Cutoff ("Silhouette alpha cutoff", Range(0,1)) = .45
    }
    SubShader
    {
        Tags { "Queue"="AlphaTest" "RenderType"="TransparentCutout" "IgnoreProjector"="True" }
        Pass
        {
            Cull Off
            ZWrite On
            AlphaToMask On
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            sampler2D _MainTex, _ClosedTex;
            float _EyesClosed, _FaceFeather, _Cutoff;
            float4 _FaceRect;
            struct Input { float4 vertex:POSITION; float2 uv:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 vertex:SV_POSITION; float2 uv:TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
            Varyings vert(Input v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(Varyings,o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.vertex=UnityObjectToClipPos(v.vertex);
                o.uv=v.uv;
                return o;
            }
            fixed4 frag(Varyings i):SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                fixed4 body=tex2D(_MainTex,i.uv);
                clip(body.a-_Cutoff);
                float2 enter=smoothstep(_FaceRect.xy,_FaceRect.xy+_FaceFeather,i.uv);
                float2 leave=1-smoothstep(_FaceRect.zw-_FaceFeather,_FaceRect.zw,i.uv);
                float mask=enter.x*enter.y*leave.x*leave.y*saturate(_EyesClosed);
                // Only a face patch changes. Both states share the same silhouette and body.
                fixed3 closed=tex2D(_ClosedTex,i.uv).rgb;
                return fixed4(lerp(body.rgb,closed,mask),body.a);
            }
            ENDCG
        }
        Pass
        {
            Tags {"LightMode"="ShadowCaster"}
            Cull Off ZWrite On ZTest LEqual
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex shadowVert
            #pragma fragment shadowFrag
            #pragma multi_compile_shadowcaster
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float _Cutoff;
            struct ShadowVaryings {V2F_SHADOW_CASTER;float2 uv:TEXCOORD1;UNITY_VERTEX_OUTPUT_STEREO};
            ShadowVaryings shadowVert(appdata_base v) {
                ShadowVaryings o;UNITY_SETUP_INSTANCE_ID(v);UNITY_INITIALIZE_OUTPUT(ShadowVaryings,o);UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.uv=v.texcoord;TRANSFER_SHADOW_CASTER_NORMALOFFSET(o);return o;
            }
            float4 shadowFrag(ShadowVaryings i):SV_Target {clip(tex2D(_MainTex,i.uv).a-_Cutoff);SHADOW_CASTER_FRAGMENT(i)}
            ENDCG
        }
    }
    Fallback Off
}
