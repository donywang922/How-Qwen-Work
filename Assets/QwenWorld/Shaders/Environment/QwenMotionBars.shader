Shader "QwenWorld/WhiteMotionBars" {
 Properties {_ShowBlend("Show textures",Range(0,1))=0 _BusyBlend("Computing",Range(0,1))=0 _MotionTime("Motion time",Float)=0
 _OrbitIdleTime("Idle orbit time",Float)=0 _OrbitBusyTime("Busy orbit time",Float)=0
 [HDR] _GlowColor("Uniform white emission",Color)=(1.15,1.15,1.15,1)
}
 SubShader {Tags {"Queue"="Transparent" "RenderType"="Transparent"}
  Pass {Blend SrcAlpha OneMinusSrcAlpha Cull Off ZWrite Off
   CGPROGRAM
   #pragma target 3.0
   #pragma vertex vert
   #pragma fragment frag
   #pragma multi_compile_instancing
   #include "UnityCG.cginc"
   #include "QwenMotionBars.cginc"
   float4 _GlowColor;
   struct A {float4 vertex:POSITION;float2 uv:TEXCOORD0;UNITY_VERTEX_INPUT_INSTANCE_ID};
   struct V {float4 pos:SV_POSITION;float alpha:TEXCOORD0;UNITY_VERTEX_OUTPUT_STEREO};
   V vert(A v){V o;UNITY_SETUP_INSTANCE_ID(v);UNITY_INITIALIZE_OUTPUT(V,o);UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
    float4 position=QwenBarPosition(v.uv.x,v.uv.y,v.vertex.xz);
    o.alpha=position.w;
    o.pos=UnityObjectToClipPos(float4(position.xyz,1));return o;}
   fixed4 frag(V i):SV_Target{UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);return float4(_GlowColor.rgb,i.alpha*_GlowColor.a);}
   ENDCG
  }
 }
 Fallback Off
}
