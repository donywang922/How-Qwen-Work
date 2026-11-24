#ifndef QWEN_EXHIBIT_LIGHTING
#define QWEN_EXHIBIT_LIGHTING
#include "UnityCG.cginc"
#include "UnityStandardUtils.cginc"
#include "UnityStandardBRDF.cginc"
half4 _ThemeWarm,_ThemeCool,_ThemeAmbient,_ThemeLed;
half _ThemeReflection;
half3 QwenExhibitSurface(half3 albedo,half metallic,half smoothness,half3 n,float3 world) {
 half3 view=normalize(_WorldSpaceCameraPos-world),spec;half oneMinusReflectivity;
 half3 diffuse=DiffuseAndSpecularFromMetallic(albedo,metallic,spec,oneMinusReflectivity);
 UnityLight key;key.color=_ThemeWarm.rgb;key.dir=normalize(half3(.473,.743,.473));key.ndotl=saturate(dot(n,key.dir));
 UnityIndirect indirect;indirect.diffuse=_ThemeAmbient.rgb+_ThemeCool.rgb*(.2+.8*saturate(dot(n,normalize(half3(.85,.18,.35)))));
 half4 cube=UNITY_SAMPLE_TEXCUBE_LOD(unity_SpecCube0,reflect(-view,n),(1-smoothness)*6);
 indirect.specular=DecodeHDR(cube,unity_SpecCube0_HDR)*_ThemeReflection;
 half3 result=BRDF1_Unity_PBS(diffuse,spec,oneMinusReflectivity,smoothness,n,view,key,indirect).rgb;
 return result+_ThemeCool.rgb*pow(1-saturate(dot(n,view)),3)*.18;
}
#endif
