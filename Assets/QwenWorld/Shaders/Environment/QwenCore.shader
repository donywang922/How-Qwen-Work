Shader "QwenWorld/WhiteLayerCore" {
 Properties { _LayerMask("Active layers",Float)=0 
 _ThemeWarm("Warm key",Color)=(.95,.83,.70,1)
 _ThemeCool("Earth blue fill",Color)=(.09,.25,.52,1)
 _ThemeAmbient("Ambient",Color)=(.17,.20,.28,1)
 _ThemeLed("Quiet blue lamps",Color)=(.26,.46,.85,1)
 _ThemeReflection("Environment reflection",Float)=.7
 }
 SubShader {
  Tags {"RenderType"="Opaque"}
  Pass {
   CGPROGRAM
   #pragma target 3.0
   #pragma vertex vert
   #pragma fragment frag
   #pragma multi_compile_instancing
   #include "UnityCG.cginc"
   #include "Lighting.cginc"
   #include "QwenExhibitLighting.cginc"
   float _LayerMask;
   struct A {float4 vertex:POSITION;float3 normal:NORMAL;float2 uv:TEXCOORD0;UNITY_VERTEX_INPUT_INSTANCE_ID};
   struct V {float4 pos:SV_POSITION;float3 normal:TEXCOORD0;float layer:TEXCOORD1;float3 world:TEXCOORD2;UNITY_VERTEX_OUTPUT_STEREO};
   float3 rx(float3 p,float a){float s=sin(a),c=cos(a);return float3(p.x,c*p.y-s*p.z,s*p.y+c*p.z);}
   float3 ry(float3 p,float a){float s=sin(a),c=cos(a);return float3(c*p.x+s*p.z,p.y,-s*p.x+c*p.z);}
   V vert(A v){V o;UNITY_SETUP_INSTANCE_ID(v);UNITY_INITIALIZE_OUTPUT(V,o);UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
    float group=floor(v.uv.x/4),t=_Time.y;
    float ax=group*.71+t*(.035+group*.004),ay=group*1.17+t*(.025+group*.003);
    float3 p=ry(rx(v.vertex.xyz,ax),ay),n=ry(rx(v.normal,ax),ay);
    o.pos=UnityObjectToClipPos(float4(p,1));o.normal=UnityObjectToWorldNormal(n);o.layer=v.uv.x;o.world=mul(unity_ObjectToWorld,float4(p,1)).xyz;return o;}
   fixed4 frag(V i):SV_Target{UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
    float active=fmod(floor(_LayerMask/exp2(floor(i.layer+.1))),2);
    float attention=step(2.5,fmod(floor(i.layer+.1),4));
    float3 glow=lerp(float3(.08,.72,1),float3(1,.37,.06),attention);
    float3 white=QwenExhibitSurface(float3(.88,.90,.94),.08,.65,normalize(i.normal),i.world);
    return float4(lerp(white,glow*1.2,active),1);}
   ENDCG
  }
 }
 Fallback "Diffuse"
}
