Shader "QwenWorld/UI/GlassText" {
 Properties {
  [PerRendererData] _MainTex("Font atlas",2D)="white"{}
  _OutlineColor("Readable dark edge",Color)=(.012,.025,.05,.95)
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
   sampler2D _MainTex;float4 _MainTex_TexelSize,_ClipRect,_OutlineColor;
   struct A {float4 vertex:POSITION;float2 uv:TEXCOORD0;fixed4 color:COLOR;UNITY_VERTEX_INPUT_INSTANCE_ID};
   struct V {float4 pos:SV_POSITION;float2 uv:TEXCOORD0;float4 local:TEXCOORD1;fixed4 color:COLOR;UNITY_VERTEX_OUTPUT_STEREO};
   V vert(A v){V o;UNITY_SETUP_INSTANCE_ID(v);UNITY_INITIALIZE_OUTPUT(V,o);UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);o.pos=UnityObjectToClipPos(v.vertex);o.local=v.vertex;o.uv=v.uv;o.color=v.color;return o;}
   fixed4 frag(V i):SV_Target {
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
    float glyph=tex2D(_MainTex,i.uv).a;float2 d=_MainTex_TexelSize.xy*.8;
    float outline=max(max(tex2D(_MainTex,i.uv+float2(d.x,0)).a,tex2D(_MainTex,i.uv-float2(d.x,0)).a),max(tex2D(_MainTex,i.uv+float2(0,d.y)).a,tex2D(_MainTex,i.uv-float2(0,d.y)).a));
    float alpha=max(glyph,outline*_OutlineColor.a);
    float3 color=lerp(_OutlineColor.rgb,i.color.rgb,glyph/max(.001,alpha));
    #ifdef UNITY_UI_CLIP_RECT
    alpha*=UnityGet2DClipping(i.local.xy,_ClipRect);
    #endif
    return float4(color,alpha*i.color.a);
   }
   ENDCG
  }
 }
 Fallback Off
}
