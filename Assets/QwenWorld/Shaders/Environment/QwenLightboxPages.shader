Shader "QwenWorld/LightboxPages"
{
    Properties
    {
        _MainTex ("Reference atlas", 2D) = "white" {}
        _PageRect ("First page rect", Vector) = (0.078125,0.00390625,0.84375,0.328125)
        _PageStride ("Page Y stride", Float) = 0.33203125
        _PageOffset ("Cycle offset", Float) = 0
        _Duration ("Seconds per page", Float) = 24
        _Transition ("Scroll seconds", Float) = 1.1
        _ManualPage ("Pinned page (-1 = cycle)", Float) = -1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float4 _PageRect;
            float _PageStride, _PageOffset, _Duration, _Transition, _ManualPage;
            struct v2f { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
            v2f vert(appdata_base v)
            {
                v2f o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_OUTPUT(v2f,o); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos=UnityObjectToClipPos(v.vertex); o.uv=v.texcoord.xy; return o;
            }
            fixed4 frag(v2f i):SV_Target
            {
                float duration=max(_Duration,2);
                float phase=frac((_Time.y+_PageOffset)/(duration*3))*(duration*3);
                float id=floor(phase/duration);
                float slide=smoothstep(duration-_Transition,duration,fmod(phase,duration));
                if(_ManualPage>=0) { id=_ManualPage; slide=0; }
                float2 uv=i.uv-float2(0,slide);
                if(uv.y<0) { uv.y+=1; id=fmod(id+1,3); }
                float2 atlasUV=_PageRect.xy+saturate(uv)*_PageRect.zw+float2(0,id*_PageStride);
                return tex2D(_MainTex,atlasUV);
            }
            ENDCG
        }
    }
    Fallback "Unlit/Texture"
}
