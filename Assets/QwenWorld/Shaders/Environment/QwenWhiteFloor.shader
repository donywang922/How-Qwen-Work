Shader "QwenWorld/WhiteSatinFloor" {
 Properties {
  _Color("White ceramic albedo",Color)=(.78,.79,.80,1)
  _DetailTex("Linear micro surface",2D)="gray"{}
  _Smoothness("Brushed surface smoothness",Range(0,1))=.64
  _FloorSpecular("Surface specular contribution",Range(0,1))=1
  [NoScaleOffset] _BrushDirectionMap("Brush axes RG, coverage B, phase A",2D)="gray"{}
  _HexWorldSize("Hex brush repeat in world X Z metres",Vector)=(25.92,24.94153,0,0)
  _BrushAnisotropy("Directional brushed reflection",Range(0,.95))=.9
  _BrushReflectance("Brushed surface reflectance",Range(.02,.12))=.06
  _BrushGrainStrength("Fine brushed normal strength",Range(0,.08))=.025
  _BrushGrainScale("Grain repeats per metre across and along",Vector)=(1.2,.035,0,0)
  [NoScaleOffset] _LogoRelief("Linear icon relief: RG slopes, B footprint, A folds",2D)="gray"{}
  _LogoSize("Icon footprint in metres",Float)=7.2
  _LogoCentre("Icon centre X Z",Vector)=(0,0,0,0)
  _LogoStrength("Icon visibility",Range(0,1))=0
  _LogoNormalStrength("Icon bevel slope",Range(0,.4))=.20
  _LogoSmoothness("Icon polished finish",Range(0,1))=.62
 }
 SubShader {
  Tags {"RenderType"="Opaque"}
  LOD 200
  CGPROGRAM
  #pragma target 3.0
  #pragma surface surf QwenFloor fullforwardshadows addshadow exclude_path:deferred
  #include "UnityPBSLighting.cginc"
  sampler2D _DetailTex,_LogoRelief,_BrushDirectionMap;
  fixed4 _Color;
  float _Smoothness,_FloorSpecular;
  float _BrushAnisotropy,_BrushReflectance,_BrushGrainStrength;
  float4 _HexWorldSize,_BrushGrainScale;
  float _LogoSize,_LogoStrength,_LogoNormalStrength,_LogoSmoothness;
  float4 _LogoCentre;
  struct SurfaceOutputQwenFloor {
   fixed3 Albedo;float3 Normal;half3 Emission;half Metallic;half Smoothness;half Occlusion;fixed Alpha;
   float3 DiffuseNormal;float3 BrushDirection;half BrushCoverage;
  };
  #include "QwenFloorBrushedLighting.cginc"
  struct Input {float3 worldPos;float3 worldNormal;INTERNAL_DATA};
  void surf(Input i,inout SurfaceOutputQwenFloor o) {
   o.Albedo=_Color.rgb;
   half4 brush=tex2D(_BrushDirectionMap,i.worldPos.xz/max(_HexWorldSize.xy,.1));
   float2 axis=brush.rg*2-1;axis/=max(length(axis),.001);
   float2 along=float2(sqrt(saturate(.5+.5*axis.x)),(axis.y>=0?1:-1)*sqrt(saturate(.5-.5*axis.x)));
   float2 across=float2(-along.y,along.x);
   float2 phase=float2(brush.a,frac(brush.a*13.37));
   float2 grainUV=float2(dot(i.worldPos.xz,across),dot(i.worldPos.xz,along))*_BrushGrainScale.xy+phase*7;
   // Phase and axis changes at cell boundaries must not force grain to a flat mip.
   float2 worldDX=ddx(i.worldPos.xz),worldDY=ddy(i.worldPos.xz);
   float2 grainDX=float2(dot(worldDX,across),dot(worldDX,along))*_BrushGrainScale.xy;
   float2 grainDY=float2(dot(worldDY,across),dot(worldDY,along))*_BrushGrainScale.xy;
   half3 detail=tex2Dgrad(_DetailTex,grainUV,grainDX,grainDY).rgb;
   float2 brushedSlope=(across*(detail.r*2-1)+along*(detail.g*2-1)*.06)*_BrushGrainStrength;
   float2 logoUV=(i.worldPos.xz-_LogoCentre.xy)/max(.1,_LogoSize)+.5;
   half4 logo=tex2D(_LogoRelief,logoUV);
   float inside=step(0,logoUV.x)*step(logoUV.x,1)*step(0,logoUV.y)*step(logoUV.y,1);
   float visible=inside*_LogoStrength*step(.9,WorldNormalVector(i,float3(0,0,1)).y);
   o.BrushCoverage=saturate(brush.b)*(1-logo.b*visible);
   float2 slope=(logo.rg*(255.0/127.0)-(128.0/127.0))*_LogoNormalStrength*visible;
   float3 baseNormal=WorldNormalVector(i,float3(0,0,1));
   brushedSlope*=step(.9,baseNormal.y)*o.BrushCoverage;
   o.BrushDirection=float3(along.x,0,along.y);
   o.DiffuseNormal=normalize(baseNormal+float3(slope.x,0,slope.y)*.15);
   float3 worldTilt=float3(slope.x+brushedSlope.x,0,slope.y+brushedSlope.y);
   float2 tangentTilt=float2(dot(worldTilt,WorldNormalVector(i,float3(1,0,0))),dot(worldTilt,WorldNormalVector(i,float3(0,1,0))));
   o.Normal=normalize(float3(tangentTilt,1));
   float finish=logo.b*visible;
   o.Metallic=finish*.06;
   o.Smoothness=saturate(lerp(_Smoothness,lerp(.65,_LogoSmoothness,logo.a),finish)+(detail.b-.5)*.04*o.BrushCoverage);
   o.Occlusion=1;
   o.Emission=0;
   o.Alpha=1;
  }
  ENDCG
 }
 Fallback "Diffuse"
}
