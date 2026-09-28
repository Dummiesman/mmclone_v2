// Upgrade NOTE: replaced '_Object2World' with 'unity_ObjectToWorld'

Shader "Custom/MMVertexLit"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
		_AlphaCutoff("Alpha Cutoff", Float) = 0.5
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        LOD 100

        Pass
        {
			Lighting On //remember this! otherwise unity_LightPosition[0] doesnt seem to work

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fwdbase
            #include "UnityCG.cginc"
            #include "MMLightModel.cginc"
            #include "AutoLight.cginc"

            struct appdata
            {
                UNITY_VERTEX_INPUT_INSTANCE_ID
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
				float3 normal : NORMAL;
                float4 color : COLOR;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 pos : SV_POSITION;
				float3 lighting: TEXCOORD1;
				float3 normal: NORMAL;
                float4 color : COLOR;
                LIGHTING_COORDS(2, 3)
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
			float _AlphaCutoff;

            v2f vert (appdata v)//This runs first, once per vertex
            {
                v2f o;//this gets passed to frag
                UNITY_SETUP_INSTANCE_ID(v);

                o.pos = UnityObjectToClipPos(v.vertex);
				o.normal = mul((float3x3)unity_ObjectToWorld, v.normal);//convert normal to world space
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color;

                o.lighting = MMLight3(float4(1.f, 1.f, 1.f, 1.f), COMPUTE_VIEW_NORMAL);
                TRANSFER_VERTEX_TO_FRAGMENT(o);

                return o;
            }

            fixed4 frag(v2f i) : SV_Target//This runs after vert, once per pixel. it recieves the v2f struct that was created in vert
            {
                float attenuation = LIGHT_ATTENUATION(i);
                attenuation = max(attenuation, 0.5);

                //attenuation debug
                //return fixed4(max(attenuation, unity_AmbientSky.r), max(attenuation, unity_AmbientSky.g), max(attenuation, unity_AmbientSky.b), 1.f);

				fixed4 col = tex2D(_MainTex, i.uv) * float4(i.lighting.rgb,1) * i.color;
                col.rgb *= attenuation;
				
				clip(col.a - _AlphaCutoff);
				
                return col;
            }
            ENDCG
        }

     
    }

    Fallback "VertexLit"
}
