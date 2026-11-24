Shader "QwenWorld/ClearTensor" {
SubShader { Pass { ZTest Always ZWrite Off Cull Off
CGPROGRAM
#pragma vertex vert_img
#pragma fragment frag
#include "UnityCG.cginc"
float4 frag(v2f_img i):SV_Target { return 0; }
ENDCG
} } }
