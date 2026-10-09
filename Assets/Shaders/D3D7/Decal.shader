Shader "Custom/Decal"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
    }

    SubShader
    {
        Tags { "Queue" = "Transparent-1" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        LOD 100

        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Lighting On
            Offset -3.5, 0

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_instancing
            #pragma multi_compile_fwdbase
            #pragma multi_compile_fog
            #pragma multi_compile __ SHADOWMAP
            #include "UnityCG.cginc"
            #include "MMLightModel.cginc"
            #include "AutoLight.cginc"
            #include "./../Shadowmapping/MMShadowMapping.cginc"

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
                float3 lighting : TEXCOORD1;
                fixed4 color : COLOR;
                LIGHTING_COORDS(2, 3)
                UNITY_FOG_COORDS(4)
            #if defined(SHADOWMAP)
                float2 shadowUV : TEXCOORD5;
            #endif
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;

            // Globals
            sampler2D _ShadowMap;
            half _ShadowMapIntensity;
            half _ShadowMapScale;
            float2 _ShadowMapOffset;

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);

                float bias = 0.001;
                float3 viewPos = UnityObjectToViewPos(v.vertex);
                viewPos.z *= (1.0 - bias); 
                o.pos = mul(UNITY_MATRIX_P, float4(viewPos, 1.0));


                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color;
                o.lighting = MMLight3(float4(1.0, 1.0, 1.0, 1.0), COMPUTE_VIEW_NORMAL);

            #if defined(SHADOWMAP)
                float3 worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                float3 worldNormal = UnityObjectToWorldNormal(v.normal);
                o.shadowUV = MMShadowMapUV(worldPos, worldNormal, _ShadowMapOffset, _ShadowMapScale);
            #endif

                UNITY_TRANSFER_FOG(o, o.pos);
                TRANSFER_VERTEX_TO_FRAGMENT(o);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed attenuation = max(LIGHT_ATTENUATION(i), 0.5);

                // V always repeats; U keeps whatever wrap mode the texture asset is set to.
                // Derivatives come from the unwrapped UV so mip selection doesn't blow up at the seam.
                float2 uv = i.uv;
                float2 uvdx = ddx(uv);
                float2 uvdy = ddy(uv);
                uv.y = frac(uv.y);

                fixed4 col = tex2Dgrad(_MainTex, uv, uvdx, uvdy) * fixed4(i.lighting, 1.0) * i.color;
                col.rgb *= attenuation;

            #if defined(SHADOWMAP)
                col.rgb *= 1.0 - MMSampleShadowMap(_ShadowMap, i.shadowUV, _ShadowMapIntensity);
            #endif

                UNITY_APPLY_FOG(i.fogCoord, col);
                return col;
            }
            ENDCG
        }
    }

    Fallback Off
}