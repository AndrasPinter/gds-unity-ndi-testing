//-----------------------------------------------------------------------------
// Copyright 2014-2018 RenderHeads Ltd.  All rights reserved.
//-----------------------------------------------------------------------------

float2
ScaleZoomToFit(float targetWidth, float targetHeight, float sourceWidth, float sourceHeight)
{
	float targetAspect = targetHeight / targetWidth;
	float sourceAspect = sourceHeight / sourceWidth;
	float2 scale = float2(1.0, sourceAspect / targetAspect);
	if (targetAspect < sourceAspect)
	{
		scale = float2(targetAspect / sourceAspect, 1.0);
	}
	return scale;
}

// BT470
float4
convertYUV(float y, float u, float v)
{
    float rr = saturate(y + 1.402 * (u - 0.5));
    float gg = saturate(y - 0.344 * (v - 0.5) - 0.714 * (u - 0.5));
    float bb = saturate(y + 1.772 * (v - 0.5));
	return float4(rr, gg, bb, 1.0);
}

// BT709
float4
convertYUV_HD(float y, float u, float v)
{
	float rr = saturate( 1.164 * (y - (16.0 / 255.0)) + 1.793 * (u - 0.5) );
	float gg = saturate( 1.164 * (y - (16.0 / 255.0)) - 0.534 * (u - 0.5) - 0.213 * (v - 0.5) );
	float bb = saturate( 1.164 * (y - (16.0 / 255.0)) + 2.115 * (v - 0.5) );
	return float4(rr, gg, bb, 1.0);
}

// BT709
// from http://www.equasys.de/colorconversion.html
float3
ConvertRGB_YUV_HD(float r, float g, float b)
{
	float3x3 m = float3x3(0.299, 0.587, 0.114, -0.169, -0.331, 0.500, 0.500, -0.419, -0.081);

	float3 yuv = mul(m, float3(r, g, b));
	yuv.y += 0.5;
	yuv.z += 0.5;

	return yuv;
}

float4
yuvToRGB(float y, float u, float v)
{
    float b = saturate(1.164 * (y - 16.0 / 255) + 1.596 * (v - 128.0 / 255));
    float g = saturate(1.164 * (y - 16.0 / 255) - 0.813 * (v - 128.0 / 255) - 0.391 * (u - 128.0 / 255));
    float r = saturate(1.164 * (y - 16.0/255) + 2.018 * (u - 128.0/255));
    return float4(r, g, b, 1.0);
}

float3 YUVEncode2020(float3 rgb)
{
	float3 yuv;

	yuv.x = 0.2627 * rgb.r + 0.678 * rgb.g + 0.0593 * rgb.b;
	yuv.y = -0.13963 * rgb.r - 0.36037 * rgb.g + 0.5 * rgb.b + 0.5;
	yuv.z = 0.5 * rgb.r - 0.459786 * rgb.g - 0.04021 * rgb.b + 0.5;

	return yuv;
}

float3 YUVDecode2020(float3 yuv)
{
	yuv.y -= 0.5;
	yuv.z -= 0.5;

	float3 rgb;

	rgb.r = yuv.x + 1.4746 * yuv.z;
	rgb.g = yuv.x - 0.16455 * yuv.y - 0.57135 * yuv.z;
	rgb.b = yuv.x + 1.8814 * yuv.y;

	return rgb;
}

float3 Encode709to2020(float3 rgb)
{
	float3 rgb2020;

	rgb2020.r = 0.6274 * rgb.r + 0.3283 * rgb.g + 0.0433 * rgb.b;
	rgb2020.g = 0.0691 * rgb.r + 0.9195 * rgb.g + 0.0114 * rgb.b;
	rgb2020.b = 0.0164 * rgb.r + 0.088 * rgb.g + 0.8956 * rgb.b;

	return rgb2020;
}

float3 Encode2020to709(float3 rgb)
{
	float3 rgb709;

	rgb709.r = 1.6603 * rgb.r - 0.58583 * rgb.g - 0.07281 * rgb.b;
	rgb709.g = -0.12455 * rgb.r + 1.1328 * rgb.g - -0.0084 * rgb.b;
	rgb709.b = -0.0182 * rgb.r - 0.10058 * rgb.g + 1.11873 * rgb.b;

	return rgb709;
}

float3 linearToGamma(float3 col)
{
	//return fixed4(pow(col.rgb, 1.0 / 2.2), col.a);
#if SHADER_TARGET < 30
	return max(1.055h * pow(col, 0.416666667h) - 0.055h, 0.0h);
#else
	// Accurate version
	if (col.r <= 0.0031308)
		col.r = col.r * 12.92;
	else
		col.r = 1.055 * pow(col.r, 0.4166667) - 0.055;

	if (col.g <= 0.0031308)
		col.g = col.g * 12.92;
	else
		col.g = 1.055 * pow(col.g, 0.4166667) - 0.055;

	if (col.b <= 0.0031308)
		col.b = col.b * 12.92;
	else
		col.b = 1.055 * pow(col.b, 0.4166667) - 0.055;
	return col;
#endif
}

float3 gammaToLinear(float3 col)
{
	//return fixed4(pow(col.rgb, 2.2), col.a);
#if SHADER_TARGET < 30
	return col * (col * (col * 0.305306011h + 0.682171111h) + 0.012522878h);
#else
	if (col.r <= 0.04045)
		col.r = col.r / 12.92;
	else
		col.r = pow((col.r + 0.055) / 1.055, 2.4);

	if (col.g <= 0.04045)
		col.g = col.g / 12.92;
	else
		col.g = pow((col.g + 0.055) / 1.055, 2.4);

	if (col.b <= 0.04045)
		col.b = col.b / 12.92;
	else
		col.b = pow((col.b + 0.055) / 1.055, 2.4);
#endif

	return col;
}

bool IsStereoEyeLeft(float3 worldNosePosition, float3 worldCameraRight)
{
#if defined(UNITY_SINGLE_PASS_STEREO)
	return (unity_StereoEyeIndex == 0);
#else

	float dRight = distance(worldNosePosition + worldCameraRight, _WorldSpaceCameraPos);
	float dLeft = distance(worldNosePosition - worldCameraRight, _WorldSpaceCameraPos);

	return (dRight > dLeft);
#endif
}


// Input is linear in range 0..12
// Output is non-linear in range 0..1
// "OETF"
inline float HLG_Encode(float e)
{
	const float a = 0.17883277;
	const float b = 0.28466892;		// 1.0 - 4.0 * a;
	const float c = 0.55991073;		// 0.5 - a * log(4.0 * a);

	float ep;
	if (e >= 0.0 && e <= 1.0)
	{
		ep = sqrt(e) / 2.0;
	}
	else
	{
		ep = a * log(e - b) + c;
	}

	// Is this needed?
	ep = saturate(ep);

	return ep;
}

// Input is non-linear in range 0..1
// Output is linear in range 0..12
// "inverse OETF"
inline float HLG_Decode(float ep)
{
	const float a = 0.17883277;
	const float b = 0.28466892;		// 1.0 - 4.0 * a;
	const float c = 0.55991073;		// 0.5 - a * log(4.0 * a);

	float e;
	if (ep >= 0.0 && ep <= 0.5)
	{
		e = 4.0 * (ep * ep);
	}
	else
	{
		e = exp((ep - c) / a) + b;
	}

	e = clamp(e, 0.0, 12.0);

	return e;
}

// Input is linear in range 0..12
// Output is non-linear in range 0..1
// "OETF"
float3 HLG_Encode(float3 col)
{
	return float3(HLG_Encode(col.r), HLG_Encode(col.g), HLG_Encode(col.b));
}

// Input is non-linear in range 0..1
// Output is linear in range 0..12
// "inverse OETF"
float3 HLG_Decode(float3 col)
{
	return float3(HLG_Decode(col.r), HLG_Decode(col.g), HLG_Decode(col.b));
}

// For displays with nominal peak luminance (LW) greater than 1 000 cd/m2
// displayPeakLuminance in cd/m2
float HLG_SystemGamma(float displayPeakLuminance)
{
	return 1.2 + 0.42 * log10(displayPeakLuminance / 1000.0);
}

// Input is non-linear in range 0..1
// Output is linear in range 0..1
// displayPeakLuminance default is 1000 cd/m2
// displayBlackLuminance default is 0 cd/m2
// NOTE: Not sure if this function is correct...
// "EOTF"
float3 HLG_DecodeForDisplay(float3 col, float displayPeakLuminance, float displayBlackLuminance)
{
	float3 e = HLG_Decode(col);
	float ys = 0.2627 * e.r + 0.6780 * e.g + 0.0593 * e.b;

	float y = 1.2;		// for nominal display peak luminance of 1000cd/m2
	if (displayPeakLuminance > 1000.0)
	{
		y = HLG_SystemGamma(displayPeakLuminance);
	}

	float alpha = (displayPeakLuminance - displayBlackLuminance) / pow(12.0, y);

	float rd = alpha * pow(ys, (y - 1.0)) * e.r + displayBlackLuminance;
	float gd = alpha * pow(ys, (y - 1.0)) * e.g + displayBlackLuminance;
	float bd = alpha * pow(ys, (y - 1.0)) * e.b + displayBlackLuminance;

	return saturate(float3(rd, gd, bd));
}

// Input is non-linear in range 0..1
// Output is non-linear in range 0..1
float3 HLG_ConvertRGB_YUV(float3 colp)
{
	float yp = 0.2627 * colp.r + 0.6780 * colp.g + 0.0593 * colp.b;
	
	float cbp = ((colp.b - yp) / 1.8814) + 0.5;
	float crp = ((colp.r - yp) / 1.4746) + 0.5;

	return saturate(float3(yp, cbp, crp));
}
	
// Input is non-linear in range 0..1
// Output is non-linear in range 0..1
float3 HLG_ConvertYUV_RGB(float3 col)
{
	col -= float3(0.0, 0.5, 0.5);
	float cb = 1.8814 * col.y + col.x;
	float cr = 1.4746 * col.z + col.x;
	float cg = (col.x - 0.0593 * cb - 0.2627 * cr) / 0.6780;
	return saturate(float3(cr, cg, cb));
}

/*
// Input is non-linear in range 0..1
float3 HLG_QuantiseRange10Bit(float3 col, bool full)
{
	// TODO
}*/