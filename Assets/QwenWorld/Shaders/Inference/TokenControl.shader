Shader "QwenWorld/TokenControl" {
Properties { _Token("Token", Float)=0 _Position("Position", Float)=0 _Epoch("Epoch", Float)=0 }
SubShader { Pass { ZTest Always ZWrite Off Cull Off
CGPROGRAM
#pragma vertex vert_img
#pragma fragment frag
#include "UnityCG.cginc"
float _Token, _Position, _Epoch;
float4 frag(v2f_img i):SV_Target { return float4(_Token,_Position,0,_Epoch); }
ENDCG
} } }
