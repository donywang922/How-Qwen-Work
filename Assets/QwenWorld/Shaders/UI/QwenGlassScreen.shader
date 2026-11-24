Shader "QwenWorld/UI/GlassScreen" {
 Properties {
  [PerRendererData] _MainTex("Sprite",2D)="white"{}
  _PanelSize("Panel size",Vector)=(640,680,0,0)
  _Tint("Glass tint",Color)=(.018,.040,.065,1)
  _EdgeColor("Polished edge",Color)=(.65,.86,1,1)
  _Opacity("Glass body opacity",Range(0,1))=.88
  _Radius("Corner radius",Float)=13
  _StencilComp("Stencil Comparison",Float)=8 _Stencil("Stencil ID",Float)=0
  _StencilOp("Stencil Operation",Float)=0 _StencilWriteMask("Stencil Write Mask",Float)=255 _StencilReadMask("Stencil Read Mask",Float)=255
  _ColorMask("Color Mask",Float)=15
 }
 SubShader {
  Tags {"Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" "CanUseSpriteAtlas"="True"}
  Stencil {Ref [_Stencil] Comp [_StencilComp] Pass [_StencilOp] ReadMask [_StencilReadMask] WriteMask [_StencilWriteMask]}
  Cull Off Lighting Off ZWrite Off ZTest [unity_GUIZTestMode] Blend SrcAlpha OneMinusSrcAlpha ColorMask [_ColorMask]
  Pass {
   CGPROGRAM
   #pragma target 3.0
   #pragma vertex vert
   #pragma fragment frag
   #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
   #pragma multi_compile_instancing
   #include "UnityCG.cginc"
   #include "UnityUI.cginc"
   float4 _PanelSize,_ClipRect;float4 _Tint,_EdgeColor;float _Opacity,_Radius;
   struct A {float4 vertex:POSITION;float2 uv:TEXCOORD0;float4 color:COLOR;UNITY_VERTEX_INPUT_INSTANCE_ID};
   struct V {float4 pos:SV_POSITION;float2 uv:TEXCOORD0;float4 local:TEXCOORD1;float3 world:TEXCOORD2;fixed4 color:COLOR;UNITY_VERTEX_OUTPUT_STEREO};
   V vert(A v){V o;UNITY_SETUP_INSTANCE_ID(v);UNITY_INITIALIZE_OUTPUT(V,o);UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);o.pos=UnityObjectToClipPos(v.vertex);o.local=v.vertex;o.world=mul(unity_ObjectToWorld,v.vertex).xyz;o.uv=v.uv;o.color=v.color;return o;}
   fixed4 frag(V i):SV_Target {
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
    float2 p=(i.uv-.5)*_PanelSize.xy,q=abs(p)-(_PanelSize.xy*.5-_Radius);
    float sdf=length(max(q,0))+min(max(q.x,q.y),0)-_Radius;clip(-sdf);
    float edge=1-smoothstep(.7,2.4,-sdf);
    float3 view=normalize(_WorldSpaceCameraPos-i.world),normal=UnityObjectToWorldNormal(float3(0,0,-1));
    float fresnel=pow(1-abs(dot(view,normal)),4);
    float vx=dot(view,normalize(unity_ObjectToWorld._m00_m10_m20));
    float sheen=exp2(-pow((i.uv.x+.45*i.uv.y-(.48+vx*.25))/.085,2));
    float3 color=lerp(_Tint.rgb,float3(.88,.94,1),sheen*.035);color=lerp(color,_EdgeColor.rgb,edge);
    float alpha=_Opacity+fresnel*.04+sheen*.01+edge*.10;
    #ifdef UNITY_UI_CLIP_RECT
    alpha*=UnityGet2DClipping(i.local.xy,_ClipRect);
    #endif
    return float4(color*i.color.rgb,saturate(alpha)*i.color.a);
   }
   ENDCG
  }
 }
 Fallback Off
}
