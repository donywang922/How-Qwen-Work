#ifndef QWEN_FLOOR_BRUSHED_LIGHTING
#define QWEN_FLOOR_BRUSHED_LIGHTING
// Elliptical GGX distribution and correlated Smith visibility, evaluated in the
// floor's brushed axes. See Docs/Qwen35-Implementation.md for formula references.
void QwenBrushAxes(SurfaceOutputQwenFloor s,out float3 n,out float3 t,out float3 b,out float2 rough) {
 n=normalize(s.Normal);float3 projected=s.BrushDirection-n*dot(n,s.BrushDirection);
 t=normalize(dot(projected,projected)>.0001?projected:cross(n,float3(0,1,0)));b=normalize(cross(n,t));
 float alpha=max(.025,(1-s.Smoothness)*(1-s.Smoothness));
 float aspect=sqrt(max(.1,1-.9*_BrushAnisotropy*s.BrushCoverage));rough=max(float2(.025,.025),float2(alpha*aspect,alpha/aspect));
}
inline half4 LightingQwenFloor(SurfaceOutputQwenFloor s,float3 viewDir,UnityGI gi) {
 float3 n,t,b;float2 rough;QwenBrushAxes(s,n,t,b,rough);
 float3 v=normalize(viewDir),l=gi.light.dir,h=Unity_SafeNormalize(l+v);
 float nl=saturate(dot(n,l)),nv=max(.001,abs(dot(n,v))),nh=saturate(dot(n,h)),lh=saturate(dot(l,h));
 float2 projectedH=float2(dot(t,h),dot(b,h))/rough;
 float ellipse=dot(projectedH,projectedH)+nh*nh;
 float distribution=1/max(1e-5,UNITY_PI*rough.x*rough.y*ellipse*ellipse);
 float lengthV=length(float3(dot(t,v)*rough.x,dot(b,v)*rough.y,nv));
 float lengthL=length(float3(dot(t,l)*rough.x,dot(b,l)*rough.y,nl));
 float visibility=.5/max(1e-4,nl*lengthV+nv*lengthL);
 float highlight=max(0,distribution*visibility*UNITY_PI*nl);
 #ifdef UNITY_COLORSPACE_GAMMA
 highlight=sqrt(max(1e-4,highlight));
 #endif
 half3 specColor=lerp(half3(_BrushReflectance,_BrushReflectance,_BrushReflectance),s.Albedo,s.Metallic);
 half reflectivity=max(specColor.r,max(specColor.g,specColor.b));half3 diffuse=s.Albedo*(1-reflectivity)*(1-s.Metallic);
 float3 baseNormal=normalize(s.DiffuseNormal);
 half baseNL=saturate(dot(baseNormal,l)),baseNV=abs(dot(baseNormal,v));
 half diffuseTerm=DisneyDiffuse(baseNV,baseNL,lh,1-s.Smoothness)*baseNL;
 half envReduction=1/(1+rough.x*rough.y);
 half3 specular=gi.light.color*highlight*FresnelTerm(specColor,lh)+envReduction*gi.indirect.specular*FresnelLerp(specColor,saturate(s.Smoothness+reflectivity),nv);
 return half4(diffuse*(gi.indirect.diffuse+gi.light.color*diffuseTerm)+specular*_FloorSpecular,s.Alpha);
}
inline void LightingQwenFloor_GI(SurfaceOutputQwenFloor s,UnityGIInput data,inout UnityGI gi) {
 float3 n,t,b;float2 rough;QwenBrushAxes(s,n,t,b,rough);
 Unity_GlossyEnvironmentData gloss;gloss.roughness=sqrt(rough.x);gloss.reflUVW=reflect(-data.worldViewDir,n);
 gi=UnityGlobalIllumination(data,s.Occlusion,normalize(s.DiffuseNormal),gloss);
 // A fixed three-tap approximation to the elongated environment-reflection lobe.
 float spread=(rough.y-rough.x)*.9;
 gloss.reflUVW=reflect(-data.worldViewDir,normalize(n+b*spread));
 half3 positive=UnityGI_IndirectSpecular(data,s.Occlusion,gloss);
 gloss.reflUVW=reflect(-data.worldViewDir,normalize(n-b*spread));
 half3 negative=UnityGI_IndirectSpecular(data,s.Occlusion,gloss);
 gi.indirect.specular=gi.indirect.specular*.4+(positive+negative)*.3;
}
#endif
