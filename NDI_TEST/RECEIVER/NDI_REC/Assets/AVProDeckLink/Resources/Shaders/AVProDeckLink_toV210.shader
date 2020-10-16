// Upgrade NOTE: replaced 'mul(UNITY_MATRIX_MVP,*)' with 'UnityObjectToClipPos(*)'

//-----------------------------------------------------------------------------
// Copyright 2014-2018 RenderHeads Ltd.  All rights reserved.
//-----------------------------------------------------------------------------

Shader "AVProDeckLink/RGBA 4:4:4 to UYVY 4:2:2 10-bit"
{
	Properties 
	{
		_MainTex ("Base (RGB)", 2D) = "white" {}
		_TextureWidth ("Texure Width", Float) = 256.0
	}
	SubShader 
	{
		Pass
		{ 
			ZTest Always
			Cull Off
			ZWrite Off
			Fog { Mode off }
		
CGPROGRAM
#pragma vertex vert
#pragma fragment frag
#pragma target 4.0
#pragma exclude_renderers flash xbox360 ps3 gles
//#pragma fragmentoption ARB_precision_hint_fastest
#pragma fragmentoption ARB_precision_hint_nicest
//#pragma only_renderers d3d11 
//#pragma fragmentoption ARB_precision_hint_fastest 
#pragma multi_compile SWAP_RED_BLUE_ON SWAP_RED_BLUE_OFF
#pragma multi_compile CHROMA_NOLERP CHROMA_LERP CHROMA_SMARTLERP
#pragma multi_compile __ APPLY_GAMMA
#pragma multi_compile USE_REC709 USE_REC2020 USE_REC2100
#include "UnityCG.cginc"
#include "AVProDeckLink_Shared.cginc"

uniform sampler2D _MainTex;
float _TextureWidth;
float4 _MainTex_ST;
float4 _MainTex_TexelSize;

struct v2f {
	float4 pos : POSITION;
	float4 uv : TEXCOORD0;
};

v2f vert( appdata_img v )
{
	v2f o;
	o.pos = UnityObjectToClipPos (v.vertex);
	o.uv = float4(TRANSFORM_TEX(v.texcoord, _MainTex), 0, 0);
	
	o.uv.y = 1-o.uv.y;
	
	// On D3D when AA is used, the main texture & scene depth texture
	// will come out in different vertical orientations.
	// So flip sampling of the texture when that is the case (main texture
	// texel size will have negative Y).
	#if SHADER_API_D3D9
	if (_MainTex_TexelSize.y < 0)
	{
		o.uv.y = 1-o.uv.y;
	}
	#endif
	
	o.uv.z = o.uv.x * _TextureWidth;

	return o;
}


inline float3 Sample(float2 uv, float offset)
{
	float4 col1 = tex2Dlod(_MainTex, float4(uv.xy + float2(_MainTex_TexelSize.x * offset, 0.0), 0.0, 0.0));

#if USE_REC709
#if APPLY_GAMMA
	col1.rgb = linearToGamma(col1.rgb);
#endif
	return ConvertRGB_YUV_HD(col1.r, col1.g, col1.b);
#elif USE_REC2020
#if APPLY_GAMMA
	col1.rgb = linearToGamma(col1.rgb);
#endif
	col1.rgb = Encode709to2020(col1.rgb);
	return YUVEncode2020(col1.rgb);
#elif USE_REC2100
	col1.rgb = HLG_Encode(col1.rgb);
	// for some reason this RGB-YUV conversion breaks
	return HLG_ConvertRGB_YUV(col1);
#endif
	return 0.0;
}

float4 frag (v2f i) : COLOR
{
	float4 uv = i.uv;
	
	int x = floor(i.uv.z) % 4;
	float4 oCol = 0;
	if (x == 0)
	{
		float3 yuv1 = Sample(uv.xy, 0);
		float u0 = yuv1.y;
		float y0 = yuv1.x;
		float v0 = yuv1.z;
		oCol.rgb = float3(u0, y0, v0);
	}
	else if (x == 1) 
	{
		float3 yuv2 = Sample(uv.xy, 0);
		float3 yuv3 = Sample(uv.xy, 1);
		float y0 = yuv2.x;
		float u1 = yuv2.y;
		float y1 = yuv3.x;
		oCol.rgb = float3(y0, u1, y1);
	}
	else if (x == 2)
	{
		float3 yuv3 = Sample(uv.xy, 0);
		float3 yuv4 = Sample(uv.xy, 1);
		float3 yuv5 = Sample(uv.xy, 2);
		float v0 = yuv3.z;
		float y1 = yuv4.x;
		float u2 = yuv5.y;
		oCol.rgb = float3(v0, y1, u2);
	}
	else if (x == 3)
	{
		float3 yuv5 = Sample(uv.xy, 0);
		float3 yuv6 = Sample(uv.xy, 1);
		float y0 = yuv5.x;
		float v0 = yuv5.z;
		float y1 = yuv6.x;
		oCol.rgb = float3(y0, v0, y1);
	}

	oCol.a = 1.0;

	return oCol;
} 
ENDCG
		}
	}
	
	FallBack Off
}