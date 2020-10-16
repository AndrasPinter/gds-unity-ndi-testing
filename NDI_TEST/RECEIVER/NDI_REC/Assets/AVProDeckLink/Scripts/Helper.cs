using UnityEngine;

//-----------------------------------------------------------------------------
// Copyright 2014-2018 RenderHeads Ltd.  All rights reserved.
//-----------------------------------------------------------------------------

namespace RenderHeads.Media.AVProDeckLink
{
	public enum Eye
	{
		Left = -1,
		Both = 0,
		Right = 1,
	}

	public class Helper
    {
        public static string Version = "1.6.6";

		public static void SavePNG(string filePath, RenderTexture rt)
		{
			if (rt != null)
			{
				Texture2D tex = new Texture2D(rt.width, rt.height, TextureFormat.ARGB32, false);
				RenderTexture.active = rt;
				tex.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0, false);
				tex.Apply(false, false);

#if !UNITY_WEBPLAYER
				byte[] pngBytes = tex.EncodeToPNG();
				System.IO.File.WriteAllBytes(filePath, pngBytes);
#endif
				RenderTexture.active = null;
				Texture2D.Destroy(tex);
				tex = null;
			}
		}

		// EXR exporting is only supported in Unity 5.6 and above
#if UNITY_5_6_OR_NEWER
		public static void SaveEXR(string filePath, RenderTexture rt)
		{
			if (rt != null)
			{
				if (rt.format != RenderTextureFormat.ARGBFloat &&
					rt.format != RenderTextureFormat.ARGBHalf &&
					rt.format != RenderTextureFormat.ARGB2101010)
				{
					Debug.LogError("Writing to EXR requires floating point texture " + rt.format);
					return;
				}

				Texture2D tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBAFloat, false);
				RenderTexture.active = rt;
				tex.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0, false);
				tex.Apply(false, false);

#if !UNITY_WEBPLAYER
				byte[] exrBytes = tex.EncodeToEXR(Texture2D.EXRFlags.OutputAsFloat);
				System.IO.File.WriteAllBytes(filePath, exrBytes);
#endif
				RenderTexture.active = null;
				Texture2D.Destroy(tex);
				tex = null;
			}
		}
#endif
	}
}
