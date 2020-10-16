// Upgrade NOTE: replaced 'mul(UNITY_MATRIX_MVP,*)' with 'UnityObjectToClipPos(*)'

//-----------------------------------------------------------------------------
// Copyright 2014-2018 RenderHeads Ltd.  All rights reserved.
//-----------------------------------------------------------------------------

Shader "AVProDeckLink/CompositeV210"
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
#pragma only_renderers d3d11 
#pragma multi_compile __ APPLY_LINEAR
#pragma multi_compile USE_REC709 USE_REC2020 USE_REC2100
//#pragma exclude_renderers flash
//#pragma fragmentoption ARB_precision_hint_fastest 
#pragma fragmentoption ARB_precision_hint_nicest
//#pragma multi_compile SWAP_RED_BLUE_ON SWAP_RED_BLUE_OFF
#include "UnityCG.cginc"
#include "AVProDeckLink_Shared.cginc"

uniform sampler2D _MainTex;
float _TextureWidth;
float4 _MainTex_TexelSize;

#if UNITY_VERSION >= 530
uniform float4 _MainTex_ST2; 
#else
uniform float4 _MainTex_ST;
#endif

struct v2f {
  float4 pos : POSITION;
  float4 uv : TEXCOORD0;
};

v2f vert( appdata_img v )
{
  v2f o;
  o.pos = UnityObjectToClipPos (v.vertex);
  o.uv = float4(0.0, 0.0, 0.0, 0.0);

#if UNITY_VERSION >= 530
  o.uv.xy = (v.texcoord.xy * _MainTex_ST2.xy + _MainTex_ST2.zw);
#else
  o.uv.xy = TRANSFORM_TEX(v.texcoord, _MainTex);
#endif
 
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
  
  o.uv.z = v.vertex.x * _TextureWidth;
  

  return o;
}

float3 repack(uint4 src)
{
 uint3 res = uint3(0,0,0);
 src.xyzw = src.wzyx;
 //src.rgba = src.abgr;
 //src.rgba = src.bgra;

 res.x = ((src.x & 63) << 4) + (src.y >> 4);
 res.y = ((src.y&15) << 6) + (src.z >> 2);
 res.z = ((src.z&3) << 8) + src.w;

 res.x &= 1023;
 res.y &= 1023;
 res.z &= 1023;

 //return res.xyz / 1023.0;
 return saturate(res.xyz / float3(1023.0, 1023.0, 1023.0));
}

float3 Convert(float3 yCbCr)
{
#if USE_REC709

	float3 col = convertYUV_HD(yCbCr.x, yCbCr.z, yCbCr.y);
#if APPLY_LINEAR
	col = gammaToLinear(col);
#endif

#elif USE_REC2020

	float3 col = YUVDecode2020(yCbCr);
	col = Encode2020to709(col);
	col = convertYUV_HD(yCbCr.x, yCbCr.z, yCbCr.y);
#if APPLY_LINEAR
	col = gammaToLinear(col);
#endif

#elif USE_REC2100

	float3 col = HLG_ConvertYUV_RGB(yCbCr);
	col = HLG_Decode(col);

#else 

	float3 col = 0.0;

#endif

	return col;
}

float4 frag (v2f i) : COLOR
{
	float4 oCol = 0.0;
	
	float4 uv = i.uv;

	//_TextureWidth = 1920.0;
	//_MainTex_TexelSize.x = 1.0 / 1280.0;  

	//int x = floor(uv.z) % 6;
	int x = floor(fmod(uv.z, 6.0));
	
	uint range = 255;
	
	//[branch]
	if (x == 0)
	{	
		float3 r1 = tex2Dlod(_MainTex, float4(uv.xy, 0.0, 0.0)).bgr;
		//uint4 c1 = (tex2Dlod(_MainTex, float4(uv.xy, 0.0, 0.0)) * range);
		//float3 r1 = repack(c1); // Cr0-Y0-Cb0


		oCol.rgb = Convert(float3(r1.g, r1.b, r1.r));
		//oCol.rgb = r1.g;
		//oCol.rgb = tex2Dlod(_MainTex, float4(uv.xy, 0.0, 0.0)).rgb;
	}
	//else 
	//[branch]
	if (x == 1)
	{
		//uv.x = ((float(x)/1280.0)) + (uv.x/5.0);

#if AMD
		float3 r1 = tex2Dlod(_MainTex, float4(uv.xy - float2(_MainTex_TexelSize.x*1.0, 0.0), 0.0, 0.0)).bgr;
		float3 r2 = tex2Dlod(_MainTex, float4(uv.xy + float2(_MainTex_TexelSize.x*0.0, 0.0), 0.0, 0.0)).bgr;
#else
		float3 r1 = tex2Dlod(_MainTex, float4(uv.xy - float2(_MainTex_TexelSize.x*0.0, 0.0), 0.0, 0.0)).bgr;
		float3 r2 = tex2Dlod(_MainTex, float4(uv.xy + float2(_MainTex_TexelSize.x*1.0, 0.0), 0.0, 0.0)).bgr;
#endif

		//uint4 c1 = (tex2Dlod(_MainTex, float4(uv.xy - float2(_MainTex_TexelSize.x*1.0, 0.0), 0.0, 0.0)) * range);
		//uint4 c2 = (tex2Dlod(_MainTex, float4(uv.xy + float2(_MainTex_TexelSize.x*0.0, 0.0), 0.0, 0.0)) * range);
		//float3 r1 = repack(c1); // Cr0-Y0-Cb0
		//float3 r2 = repack(c2); // Y2-Cb1-Y1


		//r2.rb = r2.br;
		oCol.rgb = Convert(float3(r2.b, r1.b, r1.r));
		//oCol.rgb = Convert(float3(r2.r, r1.g, r2.b));

		//oCol.rgb = tex2Dlod(_MainTex, float4(uv.xy, 0.0, 0.0)).rgb;
		//oCol.rgb = tex2Dlod(_MainTex, float4(uv.xy - float2(_MainTex_TexelSize.x*1.0, 0.0), 0.0, 0.0));
	}
	
	if (x == 2)
	{
		//uint4 c1 = (tex2D(_MainTex, uv.xy) * range);
		//uint4 c2 = (tex2D(_MainTex, uv.xy + float2(_MainTex_TexelSize.x, 0.0)) * range);

		float3 r1 = tex2Dlod(_MainTex, float4(uv.xy - float2(_MainTex_TexelSize.x*0.0, 0.0), 0.0, 0.0)).bgr;
		float3 r2 = tex2Dlod(_MainTex, float4(uv.xy + float2(_MainTex_TexelSize.x*1.0, 0.0), 0.0, 0.0)).bgr;

		//uint4 c1 = (tex2Dlod(_MainTex, float4(uv.xy - float2(_MainTex_TexelSize.x*0.0, 0.0), 0.0, 0.0)) * range);
		//uint4 c2 = (tex2Dlod(_MainTex, float4(uv.xy + float2(_MainTex_TexelSize.x*1.0, 0.0), 0.0, 0.0)) * range);
		//float3 r1 = repack(c1); // Cr0-Y0-Cb0
		//float3 r2 = repack(c2); // Y2-Cb1-Y1

		//r1.rb = r1.br;
		oCol.rgb = Convert(float3(r1.r, r1.g, r2.b));
	}
	else if (x == 3)
	{
#if AMD
		uv.x -= _MainTex_TexelSize.x * 1;
		float3 r1 = (tex2D(_MainTex, uv.xy).bgr);
		float3 r2 = tex2D(_MainTex, uv.xy + float2(_MainTex_TexelSize.x, 0.0)).bgr;
#else
		uv.x -= _MainTex_TexelSize.x * 1;
		float3 r1 = (tex2D(_MainTex, uv.xy).bgr);
		float3 r2 = tex2D(_MainTex, uv.xy + float2(_MainTex_TexelSize.x, 0.0)).bgr;
#endif
		//uint4 c1 = (tex2D(_MainTex, uv.xy) * range);
		//uint4 c2 = (tex2D(_MainTex, uv.xy + float2(_MainTex_TexelSize.x, 0.0)) * range);
		//float3 r1 = repack(c1); // Cr0-Y0-Cb0
		//float3 r2 = repack(c2); // Y2-Cb1-Y1

		oCol.rgb = Convert(float3(r2.g, r1.g, r2.b));
	}
	else if (x == 4)
	{
#if AMD
		float3 r1 = tex2Dlod(_MainTex, float4(uv.xy - float2(_MainTex_TexelSize.x*1.0, 0.0), 0.0, 0.0)).bgr;
		float3 r2 = tex2Dlod(_MainTex, float4(uv.xy + float2(_MainTex_TexelSize.x*0.0, 0.0), 0.0, 0.0)).bgr;
#else
		float3 r1 = tex2Dlod(_MainTex, float4(uv.xy - float2(_MainTex_TexelSize.x*0.0, 0.0), 0.0, 0.0)).bgr;
		float3 r2 = tex2Dlod(_MainTex, float4(uv.xy + float2(_MainTex_TexelSize.x*1.0, 0.0), 0.0, 0.0)).bgr;
#endif
		//uint4 c1 = (tex2Dlod(_MainTex, float4(uv.xy - float2(_MainTex_TexelSize.x*1.0, 0.0), 0.0, 0.0)) * range);
		//uint4 c2 = (tex2Dlod(_MainTex, float4(uv.xy + float2(_MainTex_TexelSize.x*0.0, 0.0), 0.0, 0.0)) * range);
		////uint4 c1 = (tex2D(_MainTex, uv.xy) * range);
		////uint4 c2 = (tex2D(_MainTex, uv.xy + float2(_MainTex_TexelSize.x, 0.0)) * range);
		//float3 r1 = repack(c1); // Cr0-Y0-Cb0
		//float3 r2 = repack(c2); // Y2-Cb1-Y1

		oCol.rgb = Convert(float3(r2.b, r1.r, r2.g));
	}
	else if (x == 5)
	{
		//uv.x -= _MainTex_TexelSize.x * 1;
		//uint4 c1 = (tex2D(_MainTex, uv.xy) * range);
		//uint4 c2 = (tex2D(_MainTex, uv.xy + float2(_MainTex_TexelSize.x, 0.0)) * range);

		float3 r1 = tex2Dlod(_MainTex, float4(uv.xy - float2(_MainTex_TexelSize.x*1.0, 0.0), 0.0, 0.0)).bgr;
		float3 r2 = tex2Dlod(_MainTex, float4(uv.xy + float2(_MainTex_TexelSize.x*0.0, 0.0), 0.0, 0.0)).bgr;
		//uint4 c1 = (tex2Dlod(_MainTex, float4(uv.xy - float2(_MainTex_TexelSize.x*1.0, 0.0), 0.0, 0.0)) * range);
		//uint4 c2 = (tex2Dlod(_MainTex, float4(uv.xy + float2(_MainTex_TexelSize.x*0.0, 0.0), 0.0, 0.0)) * range);
		//float3 r1 = repack(c1); // Cr0-Y0-Cb0
		//float3 r2 = repack(c2); // Y2-Cb1-Y1

		oCol.rgb = Convert(float3(r2.r, r1.r, r2.g));
	}

	// good = 0, 2, 5
	// bad = 1, 3, 4
	if (x > 5)
	{
		oCol.rgb = 0;
	}

	oCol.a = 1.0;
	

	return oCol;
} 
ENDCG


		}
	}
	
	FallBack Off
}