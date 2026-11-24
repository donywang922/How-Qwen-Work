Shader "Hidden/QwenWorld/MotionProbe"
{
    Properties
    {
        _ShowBlend("Show", Float) = 0
        _BusyBlend("Busy", Float) = 0
        _MotionTime("Clock", Float) = 0
        _OrbitIdleTime("Idle clock", Float) = 0
        _OrbitBusyTime("Busy clock", Float) = 0
    }
    SubShader
    {
        Pass
        {
            Cull Off ZWrite Off ZTest Always
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "../../../Shaders/Environment/QwenMotionBars.cginc"
            float4 frag(v2f_img input) : SV_Target
            {
                float index = floor(input.uv.x * 12);
                return QwenBarPosition(floor(index / 2), fmod(index, 2), float2(0, 0));
            }
            ENDCG
        }
    }
    Fallback Off
}
