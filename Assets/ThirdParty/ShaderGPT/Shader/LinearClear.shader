Shader "GPT/LinearClear" {
Properties {
	DimO("DimO", Vector) = (1, 1, 0, 0)
	DimX("DimX", Vector) = (1, 1, 0, 0)
	DimY("DimY", Vector) = (0, 0, 0, 0)
	DimW("DimW", Vector) = (1, 1, 0, 0)
	DimQ("DimQ", Vector) = (0, 0, 0, 0)
	DimB("DimB", Vector) = (0, 0, 0, 0)
	DimAdd("DimAdd", Vector) = (0, 0, 0, 0)
	DimWin("DimWin", Vector) = (0, 0, 0, 0)
	DimReduce("DimReduce", Vector) = (0, 0, 0, 0)
	[HideInInspector]
	TexO("TexO", 2D) = "black" {}
	TexX("TexX", 2D) = "black" {}
	TexY("TexY", 2D) = "black" {}
	TexW("TexW", 2D) = "black" {}
	TexQ("TexQ", 2D) = "black" {}
	TexB("TexB", 2D) = "black" {}
	TexAdd("TexAdd", 2D) = "black" {}
	TexWin("TexWin", 2D) = "black" {}
	TexReduce("TexReduce", 2D) = "black" {}

	DimHead("DimHead", Vector) = (0, 0, 0, 0)
	Win    ("Win",     Vector) = (0, 1000000000, 0, 0)
	Win2   ("Win2",    Vector) = (0, 1000000000, 0, 0)
	VecMul ("VecMul",  Vector) = (1, 1, 1, 1)
	Bias("Bias", Float) = 0
	DstBlend("DstBlend", Int) = 0
	InstCount("InstCount", Int) = 1

	DimClear ("DimClear",  Vector) = (1, 1, 0, 0)
	TexClear ("TexClear",  2D) = "black" {}
}
SubShader {
	Tags { "PreviewType"="Plane" }
	Pass {
		Cull Off
HLSLPROGRAM
#pragma target 5.0
#pragma vertex vertClear
#pragma fragment fragClear
#include "UnityCG.cginc"
#include "Common.hlsl"
DECLARE_OUTPUT(Clear);
float4 vertClear(float2 uv : TEXCOORD0) : SV_Position {
	return ClipClear(uv);
}
float4 fragClear(float4 screenPos : SV_Position) : SV_Target {
	GetIndexClear(screenPos);
	return 0;
}
ENDHLSL
	}
	UsePass "GPT/Linear/LINEAR"
}
}