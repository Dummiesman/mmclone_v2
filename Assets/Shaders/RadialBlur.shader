Shader "Custom/RadialBlur"
{
	Properties{
		_MainTex("Color (RGB) Alpha (A)", 2D) = "white" {}
		_AmbientColor("Ambient Color", Color) = (1,1,1,1)
		_Angle("Angle", Int) = 1
		_RotationMultiplier("Rotation Multiplier", Range(-10, 10)) = 1
	}
		SubShader{
			Pass {
				Tags{ "Queue" = "Transparent" "RenderType" = "Transparent" }
				LOD 200
				Cull Off
				CGPROGRAM
				#pragma vertex vert
				#pragma fragment frag
				#pragma multi_compile_instancing
				#pragma multi_compile_fwdbase
				#include "UnityCG.cginc"
				#include ".\D3D7\MMLightModel.cginc"
				#include ".\D3D7\MMCommon.cginc"
				#include "AutoLight.cginc"

				struct appdata {
					UNITY_VERTEX_INPUT_INSTANCE_ID
					float4 vertex : POSITION;
					float2 uv : TEXCOORD0;
					float3 normal : NORMAL;
				};

				struct v2f
				{
					float2 uv : TEXCOORD0;
					float4 vertex : SV_POSITION;
					float3 lighting: TEXCOORD1;
					float3 normal: NORMAL;
					LIGHTING_COORDS(2, 3)
				};


				float2 rotateUV(float2 uv, float degrees) {
					const float Deg2Rad = (UNITY_PI * 2.0) / 360.0;
					float rotationRadians = degrees * Deg2Rad;
					float s = sin(rotationRadians);
					float c = cos(rotationRadians);
					float2x2 rotationMatrix = float2x2(c, -s, s, c);
					uv -= 0.5;
					uv = mul(rotationMatrix, uv);
					uv += 0.5;
					return uv;
				}

				sampler2D _MainTex;
				float4 _MainTex_ST;
				float4 _AmbientColor;
				uint _Angle;
				fixed _RotationMultiplier;


				v2f vert(appdata v)//This runs first, once per vertex
				{
					v2f o;//this gets passed to frag
					UNITY_SETUP_INSTANCE_ID(v);

					o.vertex = UnityObjectToClipPos(v.vertex);
					o.normal = mul((float3x3)unity_ObjectToWorld, v.normal);//convert normal to world space
					o.uv = TRANSFORM_TEX(v.uv, _MainTex);

					o.lighting = MMLight8(_AmbientColor, COMPUTE_VIEW_NORMAL);
					TRANSFER_VERTEX_TO_FRAGMENT(o);

					return o;
				}


				fixed4 frag(v2f i) : SV_Target
				{
					const float Deg2Rad = (UNITY_PI * 2.0) / 360.0;
					const float Rad2Deg = 180.0 / UNITY_PI;

					float2 coord = i.uv;
					float4 originalTexel = tex2D(_MainTex, coord);

					float illuminationDecay = 1.0;
					float4 FragColor = float4(0.0, 0.0, 0.0, 0.0);
					int samp = clamp(_Angle, 1, 512);

					for (float i = 0; i < samp; i++) {
						coord = rotateUV(coord, (_Angle / samp) * _RotationMultiplier);

						float4 texel = tex2D(_MainTex, coord);
						texel *= illuminationDecay * 1 / samp;

						FragColor += texel;
					}

					FragColor.a = originalTexel.a;
					return FragColor;
				}
				ENDCG
			}
	}
		FallBack "VertexLit"
}