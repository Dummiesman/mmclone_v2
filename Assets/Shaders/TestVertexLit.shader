// Upgrade NOTE: replaced '_Object2World' with 'unity_ObjectToWorld'

Shader "Custom/TestVertLit"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
		_LightIntensity("Overall Light Intensity", Float) = 1
		_AmbientIntensity("Ambient Intensity", Float) = 0.5
		_AlphaCutoff("Alpha Cutoff", Float) = 0.5
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100

        Pass
        {
			Lighting On //remember this! otherwise unity_LightPosition[0] doesnt seem to work

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
				float3 normal : NORMAL;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
				float3 lighting: TEXCOORD1;
				float3 normal: TEXCOORD2;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
			float _LightIntensity;
			float _AmbientIntensity;
			float _AlphaCutoff;

            v2f vert (appdata v)//This runs first, once per vertex
            {
                v2f o;//this gets passed to frag

                o.vertex = UnityObjectToClipPos(v.vertex);
				o.normal = mul((float3x3)unity_ObjectToWorld, v.normal);//convert normal to world space
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);

				float3 viewpos = UnityObjectToViewPos(o.vertex).xyz;
				float3 viewN = mul((float3x3)UNITY_MATRIX_IT_MV, v.normal);

				o.lighting = float3(0, 0, 0);
				for (int i = 0; i < 8; i++) {
					o.lighting += unity_LightColor[i] * max(dot(unity_LightPosition[i].xyz, viewN),0) * (1-unity_LightPosition[i].w);
				}
				o.lighting += unity_AmbientSky.rgb * _AmbientIntensity;

				o.lighting.r = (o.lighting.r > 1.0) ? (o.lighting.r - (o.lighting.r - 1.0f)) : o.lighting.r;
				o.lighting.g = (o.lighting.g > 1.0) ? (o.lighting.g - (o.lighting.g - 1.0f)) : o.lighting.g;
				o.lighting.b = (o.lighting.b > 1.0) ? (o.lighting.b - (o.lighting.b - 1.0f)) : o.lighting.b;

				o.lighting = max(0, o.lighting);

                return o;
            }

            fixed4 frag (v2f i) : SV_Target//This runs after vert, once per pixel. it recieves the v2f struct that was created in vert
            {
				fixed4 col = tex2D(_MainTex, i.uv) * (float4(i.lighting.rgb,1) * _LightIntensity);
				
				clip(col.a - _AlphaCutoff);
				
                return col;
            }
            ENDCG
        }
    }
}
