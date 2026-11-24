Shader "QwenWorld/LocalDataFlow" {
 Properties {_ActivityTex("Local event state",2D)="black"{} _Clock("Clock",Float)=0 _Duration("Decay seconds",Float)=.18}
 SubShader {Tags {"Queue"="Transparent+5" "RenderType"="Transparent"}
  Pass {Blend SrcAlpha OneMinusSrcAlpha Cull Off ZWrite Off ZTest LEqual
   Offset -1,-1
   CGPROGRAM
   #pragma target 3.5
   #pragma vertex vert
   #pragma fragment frag
   #pragma multi_compile_instancing
   #include "UnityCG.cginc"
   sampler2D _ActivityTex;float4 _ActivityTex_TexelSize;float _Clock,_Duration;
   struct A {float4 vertex:POSITION;float3 normal:NORMAL;float4 tangent:TANGENT;float2 uv:TEXCOORD0;float4 data:TEXCOORD1;UNITY_VERTEX_INPUT_INSTANCE_ID};
   struct V {float4 pos:SV_POSITION;float distance:TEXCOORD0;float alpha:TEXCOORD1;float write:TEXCOORD2;UNITY_VERTEX_OUTPUT_STEREO};
   V vert(A v){V o;UNITY_SETUP_INSTANCE_ID(v);UNITY_INITIALIZE_OUTPUT(V,o);UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
    float4 state=tex2Dlod(_ActivityTex,float4((v.data.x+.5)*_ActivityTex_TexelSize.x,.5,0,0));
    float envelope=max(state.g,saturate(1-(_Clock-state.r)/max(.001,_Duration)));
    float s=lerp(lerp(v.data.y,v.data.z,1-envelope),v.data.z,v.uv.x);
    if(v.data.w>.5)s=v.uv.x*envelope;
    float3 p=v.vertex.xyz+v.normal*s+v.tangent.xyz*v.tangent.w*v.uv.y*envelope;
    o.pos=UnityObjectToClipPos(float4(p,1));o.distance=s*length(v.normal);o.alpha=envelope;o.write=fmod(v.data.x,2);return o;}
   fixed4 frag(V i):SV_Target{UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
    float h=pow(saturate(1-abs(frac(i.distance*.42-_Time.y*1.1)-.5)*2),12);
    float3 color=lerp(float3(1,.32,.055),float3(.055,.52,1),i.write);
    return float4(lerp(color,float3(1,1,1),h*.85),i.alpha*.92);}
   ENDCG
  }
 }
 Fallback Off
}
