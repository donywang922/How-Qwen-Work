Shader "QwenWorld/OrbitalRingPBR" {
Properties {
    _MainTex("Base color",2D)="white"{}
    _NormalMap("Normal",2D)="bump"{}
    _MetallicMap("Metallic",2D)="black"{}
    _RoughnessMap("Roughness",2D)="white"{}
    _EmissionMap("Emission",2D)="black"{}
    _NormalStrength("Normal strength",Range(0,1))=.18
    _EmissionStrength("Emission strength",Range(0,8))=3
    _WhitePaint("White armour finish, preserve framework",Range(0,1))=0

 _ThemeWarm("Warm key",Color)=(.95,.83,.70,1)
 _ThemeCool("Earth blue fill",Color)=(.09,.25,.52,1)
 _ThemeAmbient("Ambient",Color)=(.17,.20,.28,1)
 _ThemeLed("Quiet blue lamps",Color)=(.26,.46,.85,1)
 _ThemeReflection("Environment reflection",Float)=.7
    _KeyColor("Static key",Color)=(1.65,1.62,1.55,1)
    _AmbientColor("Static ambient",Color)=(.20,.22,.25,1)
}
SubShader {
    Tags {"Queue"="Geometry" "RenderType"="Opaque"}
    Pass {
        Cull Off ZWrite On
        CGPROGRAM
        #pragma target 3.0
        #pragma vertex vert
        #pragma fragment frag
        #pragma multi_compile_instancing
        #include "UnityCG.cginc"
        #include "UnityStandardUtils.cginc"
        #include "UnityLightingCommon.cginc"
        #include "UnityStandardBRDF.cginc"
        #include "QwenExhibitLighting.cginc"
        sampler2D _MainTex,_NormalMap,_MetallicMap,_RoughnessMap,_EmissionMap;
        half _NormalStrength,_EmissionStrength,_WhitePaint;
        half4 _KeyColor,_AmbientColor;
        struct appdata {
            float4 vertex:POSITION;float3 normal:NORMAL;float4 tangent:TANGENT;float2 uv:TEXCOORD0;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };
        struct v2f {
            float4 pos:SV_POSITION;float2 uv:TEXCOORD0;float3 world:TEXCOORD1;
            half3 n:TEXCOORD2;half3 t:TEXCOORD3;half3 b:TEXCOORD4;
            UNITY_VERTEX_OUTPUT_STEREO
        };
        v2f vert(appdata v) {
            v2f o;UNITY_SETUP_INSTANCE_ID(v);UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
            o.pos=UnityObjectToClipPos(v.vertex);o.world=mul(unity_ObjectToWorld,v.vertex).xyz;o.uv=v.uv;
            o.n=UnityObjectToWorldNormal(v.normal);o.t=UnityObjectToWorldDir(v.tangent.xyz);
            o.b=cross(o.n,o.t)*(v.tangent.w*unity_WorldTransformParams.w);return o;
        }
        half4 frag(v2f i,half facing:VFACE):SV_Target {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
            half3 tn=UnpackNormal(tex2D(_NormalMap,i.uv));
            tn.xy*=_NormalStrength;tn.z=sqrt(saturate(1-dot(tn.xy,tn.xy)));
            half3 n=normalize(i.t*tn.x+i.b*tn.y+i.n*tn.z)*(facing>=0 ? 1 : -1);
            half3 view=normalize(_WorldSpaceCameraPos-i.world);
            half3 albedo=tex2D(_MainTex,i.uv).rgb;
            // Approved Blender atlas: 4x4 material regions, numbered from top left.
            // Only painted armour regions 0, 1, 10 and 14 receive the white finish.
            // Preserve rails, dark recesses, pipes, vents, labels and wear in the atlas.
            float2 cell=clamp(floor(i.uv*4),0,3);
            float region=(3-cell.y)*4+cell.x;
            half pale=1-step(.5,min(abs(region),abs(region-14)));
            half armour=pale+(1-step(.5,abs(region-1)))+(1-step(.5,abs(region-10)));
            half paint=saturate(armour)*_WhitePaint;
            half luminance=dot(albedo,half3(.2126,.7152,.0722));
            half3 whiteArmour=saturate(luminance*lerp(3.35,2.50,pale))*half3(.96,.97,1);
            half sourceMetallic=saturate(tex2D(_MetallicMap,i.uv).r);
            half exposedMetal=step(.5,sourceMetallic)*_WhitePaint;
            // Keep the baked dark-metal atlas readable against space; retain its variation.
            albedo=lerp(albedo,albedo*.85+.045,exposedMetal);
            albedo=lerp(albedo,whiteArmour,paint);
            half metallic=lerp(lerp(sourceMetallic,min(sourceMetallic,.58),exposedMetal),.04,paint);
            half smoothness=1-clamp(tex2D(_RoughnessMap,i.uv).r,.10,.98);
            half3 color=QwenExhibitSurface(albedo,metallic,smoothness,n,i.world);
            half3 emission=tex2D(_EmissionMap,i.uv).rgb;
            half strength=max(emission.r,max(emission.g,emission.b));
            color+=lerp(emission,strength*_ThemeLed.rgb,_WhitePaint)*_EmissionStrength;
            return half4(color,1);
        }
        ENDCG
    }
}
Fallback Off
}
