// Upgrade NOTE: replaced 'mul(UNITY_MATRIX_MVP,*)' with 'UnityObjectToClipPos(*)'

Shader "AVProDeckLink/Background/Full Screen"
{
	Properties
	{
		_MainTex ("Texture", 2D) = "black" {}
		_Color("Main Color", Color) = (1,1,1,1)
		[Toggle(APPLY_GAMMA)] _ApplyGamma("Apply Gamma", Float) = 0
	}
	SubShader
	{
		Tags { "Queue" = "Background" "RenderType"="Background" "IgnoreProjector"="True" }
		LOD 100
		Cull Off
		ZWrite Off
		Lighting Off

		Pass
		{
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag

			// TODO: Change XX_OFF to __ for Unity 5.0 and above
			// this was just added for Unity 4.x compatibility as __ causes
			// Android and iOS builds to fail the shader
			#pragma multi_compile APPLY_GAMMA_OFF APPLY_GAMMA
			#pragma multi_compile USE_YPCBCR_OFF USE_YPCBCR

			#include "UnityCG.cginc"
			#include "AVProDeckLink_Shared.cginc"

			struct v2f
			{
				float4 vertex : SV_POSITION;
				float2 uv : TEXCOORD0;
			};

			uniform sampler2D _MainTex;
			uniform float4 _MainTex_ST;
			uniform float4 _MainTex_TexelSize;
			uniform fixed4 _Color;

			v2f vert(appdata_img v)
			{
				v2f o;

				float2 scale = ScaleZoomToFit(_ScreenParams.x, _ScreenParams.y, _MainTex_TexelSize.z, _MainTex_TexelSize.w);
				float2 pos = ((v.vertex.xy) * scale * 2.0);		
				pos = v.vertex.xy;

				// we're rendering with upside-down flipped projection,
				// so flip the vertical UV coordinate too
				if (_ProjectionParams.x < 0.0)
				{
					pos.y = (1.0 - pos.y) - 1.0;
				}

				float farClip = 1.0 - 0.000001;
				#if defined(UNITY_REVERSED_Z)
				farClip = 0.0 + 0.000001;
				#endif

				//o.vertex = float4(pos.xy, farClip, 1.0);
				//o.vertex = float4(pos.xy, v.vertex.zw);

				o.vertex = UnityObjectToClipPos (v.vertex);
				//o.vertex = (v.vertex + float4(0.0, 0.0, 0.0, 0.0))  * float4(20.0, 20.0, 1.0, 1.0);
				//o.vertex.z = farClip;
				o.vertex.w = 1.0;

				//o.uv = TRANSFORM_TEX(o.vertex.xy, _MainTex);
				o.uv = ((v.vertex.xy / (scale / 20.0)) + float2(1.0, 1.0))  * float2(0.5, 0.5);
				o.uv = ComputeScreenPos(float4(o.vertex.xy / scale, o.vertex.zw));
				//o.uv /= 0.25;
				
				return o;
			}
			
			fixed4 frag (v2f i) : SV_Target
			{
				//return 1.0;
				// Sample the texture
				fixed4 col = tex2D(_MainTex, i.uv);
#if APPLY_GAMMA
				col.rgb = linearToGamma(col.rgb);
#endif
				col *= _Color;
				return fixed4(col.rgb, 1.0);
			}
			ENDCG
		}
	}
}
