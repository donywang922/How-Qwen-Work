Shader "QwenWorld/LaserKeyboard" {
 Properties {_MainTex("Projected key pattern",2D)="white"{} _Intensity("Brightness",Float)=1.15}
 SubShader {
  Tags {"Queue"="Transparent+2" "RenderType"="Transparent"}
  Pass {Blend SrcAlpha OneMinusSrcAlpha ZWrite Off Cull Off Offset -1,-1
   CGPROGRAM
   #pragma target 3.0
   #pragma vertex vert
   #pragma fragment frag
   #pragma multi_compile_instancing
   #include "UnityCG.cginc"
   sampler2D _MainTex;float _Intensity;
   struct A {float4 vertex:POSITION;float2 uv:TEXCOORD0;UNITY_VERTEX_INPUT_INSTANCE_ID};
   struct V {float4 pos:SV_POSITION;float2 uv:TEXCOORD0;UNITY_VERTEX_OUTPUT_STEREO};
   V vert(A v){V o;UNITY_SETUP_INSTANCE_ID(v);UNITY_INITIALIZE_OUTPUT(V,o);UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);o.pos=UnityObjectToClipPos(v.vertex);o.uv=v.uv;return o;}
   fixed4 frag(V i):SV_Target{UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);fixed4 c=tex2D(_MainTex,i.uv);return fixed4(c.rgb*_Intensity,c.a);}
   ENDCG
  }
 }
 Fallback Off
}
