Shader "QwenWorld/AcrylicHexGround"
{
    Properties
    {
        _Color("Acrylic tint", Color) = (.10,.19,.24,1)
        _Opacity("Body opacity", Range(0,1)) = .10
        _Smoothness("Brushed polish", Range(0,1)) = .82
        _HexSize("Grid flat width in metres", Float) = .85
        _LineWidth("Fine hex line width", Float) = .008
        _GridStrength("Hex lines", Range(0,1)) = .1
        _LineColor("Fine line tint", Color) = (.27,.56,.70,1)
        [NoScaleOffset] _BrushDirectionMap("Hexagonal brush axes",2D)="gray"{}
        _DetailTex("Micro grain",2D)="gray"{}
        _HexWorldSize("Brush repeat X Z",Vector)=(25.92,24.94153,0,0)
        _BrushGrainScale("Grain across and along",Vector)=(1.7,.035,0,0)
        _BrushAnisotropy("Directional reflection",Range(0,.95))=.8
        _BrushReflectance("Acrylic reflectance",Range(.02,.12))=.04
        _BrushGrainStrength("Brushed normal strength",Float)=.015
        _FloorSpecular("Reflections",Float)=1
    }
    SubShader
    {
        Tags {"Queue"="Transparent" "RenderType"="Transparent"}
        Cull Back ZWrite Off
        CGPROGRAM
        #pragma target 3.0
        #pragma surface surf QwenFloor alpha:fade keepalpha noforwardadd nolightmap exclude_path:deferred
        #include "UnityPBSLighting.cginc"
        sampler2D _BrushDirectionMap, _DetailTex;
        float4 _Color, _LineColor, _HexWorldSize, _BrushGrainScale;
        float _Opacity, _Smoothness, _HexSize, _LineWidth, _GridStrength;
        float _BrushAnisotropy, _BrushReflectance, _BrushGrainStrength, _FloorSpecular;
        struct SurfaceOutputQwenFloor
        {
            fixed3 Albedo; float3 Normal; half3 Emission;
            half Metallic; half Smoothness; half Occlusion; fixed Alpha;
            float3 DiffuseNormal; float3 BrushDirection; half BrushCoverage;
        };
        #include "QwenFloorBrushedLighting.cginc"
        struct Input { float3 worldPos; float3 worldNormal; float3 viewDir; INTERNAL_DATA };

        void surf(Input input, inout SurfaceOutputQwenFloor output)
        {
            float2 p = input.worldPos.xz;
            float radius = max(.1, _HexSize) / 1.7320508;
            float2 repeat = float2(1.7320508,3) * radius;
            float2 lattice = p.yx;
            float2 a = (frac(lattice / repeat) - .5) * repeat;
            float2 b = (frac((lattice - repeat * .5) / repeat) - .5) * repeat;
            float2 local = dot(a,a) < dot(b,b) ? a : b;
            float edgeDistance = .8660254 * radius - max(abs(local.x), dot(abs(local), float2(.5,.8660254)));
            float aa = max(.001, fwidth(edgeDistance));
            float gridLine = (1 - smoothstep(_LineWidth - aa, _LineWidth + aa, edgeDistance)) * _GridStrength;
            float4 brush = tex2D(_BrushDirectionMap, p / _HexWorldSize.xy);
            float2 axis = brush.rg * 2 - 1;
            axis /= max(.001, length(axis));
            float2 along = float2(sqrt(saturate(.5 + .5 * axis.x)),
                (axis.y >= 0 ? 1 : -1) * sqrt(saturate(.5 - .5 * axis.x)));
            float2 across = float2(-along.y, along.x);
            float2 grainUV = float2(dot(p,across), dot(p,along)) * _BrushGrainScale.xy + brush.a * 7;
            float2 dx = float2(dot(ddx(p),across),dot(ddx(p),along)) * _BrushGrainScale.xy;
            float2 dy = float2(dot(ddy(p),across),dot(ddy(p),along)) * _BrushGrainScale.xy;
            float3 grain = tex2Dgrad(_DetailTex, grainUV, dx, dy).rgb;
            float2 slope = (across * (grain.r * 2 - 1) + along * (grain.g * 2 - 1) * .06) * _BrushGrainStrength;
            float3 baseNormal = WorldNormalVector(input, float3(0,0,1));
            float3 tilt = float3(slope.x,0,slope.y) * abs(baseNormal.y);
            output.Normal = normalize(float3(dot(tilt,WorldNormalVector(input,float3(1,0,0))),
                dot(tilt,WorldNormalVector(input,float3(0,1,0))),1));
            output.DiffuseNormal = baseNormal;
            output.BrushDirection = float3(along.x,0,along.y);
            output.BrushCoverage = 1;
            output.Albedo = lerp(_Color.rgb,_LineColor.rgb,gridLine * .3);
            output.Metallic = 0;
            output.Smoothness = saturate(_Smoothness + (grain.b - .5) * .025);
            output.Occlusion = 1;
            output.Emission = _LineColor.rgb * gridLine * .18;
            float fresnel = pow(1 - saturate(abs(dot(normalize(input.viewDir),output.Normal))),5);
            output.Alpha = saturate(_Opacity + gridLine * .34 + fresnel * .22 + (1 - abs(baseNormal.y)) * .16);
        }
        ENDCG
    }
    Fallback Off
}
