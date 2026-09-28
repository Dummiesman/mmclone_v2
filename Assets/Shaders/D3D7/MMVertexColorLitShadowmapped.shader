Shader "Custom/MMVertexColorLitShadowMapped"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _AlphaCutoff ("Alpha Cutoff", Float) = 0.5
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        LOD 100

        Pass
        {
            Tags { "LightMode" = "ForwardBase" }

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fwdbase
            #pragma multi_compile_fog
            #pragma multi_compile __ SHADOWMAP

            #include "UnityCG.cginc"
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
                fixed3 color : COLOR; // baked vertex lighting
                LIGHTING_COORDS(2, 3)
                UNITY_FOG_COORDS(4)
            #if defined(SHADOWMAP)
                float2 shadowUV : TEXCOORD5;
            #endif
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float _AlphaCutoff;

            // Globals
            sampler2D _ShadowMap;
            half _ShadowMapIntensity;
            half _ShadowMapScale;
            float2 _ShadowMapOffset;

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);

                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color.rgb;

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

                fixed4 col = tex2D(_MainTex, i.uv);
                col.rgb *= i.color * attenuation;

            #if defined(SHADOWMAP)
                col.rgb *= 1.0 - MMSampleShadowMap(_ShadowMap, i.shadowUV, _ShadowMapIntensity);
            #endif

                UNITY_APPLY_FOG(i.fogCoord, col);
                clip(col.a - _AlphaCutoff);
                return col;
            }
            ENDCG
        }
    }

    Fallback "VertexLit"
}