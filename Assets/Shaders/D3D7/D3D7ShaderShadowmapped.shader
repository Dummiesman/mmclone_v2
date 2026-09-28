Shader "Custom/D3D7ShadowMapped"
{
    Properties
    {
        [Toggle(EMISSION)] _EmissionEnabled("Enable Emission", Float) = 0
        [Toggle(ALPHA)] _AlphaEnabled("Enable Alpha", Float) = 0
        [Toggle(SHADOWMAP)] _ShadowMapEnabled("Enable Shadow Map", Float) = 0
        [Toggle(UNLIT)] _UnlitEnabled("Disable Lighting", Float) = 0
        [Toggle(GAMMA)] _GammaEnabled("Enable Additional Gamma", Float) = 0

        _Color("Main Color", Color) = (1,1,1,1)
        _AmbientColor("Ambient Color", Color) = (1,1,1,1)
        _SpecColor("Specular Color", Color) = (1,1,1,1)
        _EmissiveColor("Emissive Color", Color) = (0,0,0,0)
        _Shininess("Shininess", Range(0.03, 1)) = 1
        _AlphaClipThreshold("AlphaClipThreshold", Range(0.03, 1)) = 0.9
        _MainTex("Main Texture (RGB)", 2D) = "white" {}
        _Gamma("Gamma", Range(0.0, 2.0)) = 1
    }

    SubShader
    {
        LOD 100

        CGINCLUDE
        #include "UnityCG.cginc"
        #include "MMLightModel.cginc"
        #include "MMCommon.cginc"
        #include "./../Shadowmapping/MMShadowMapping.cginc"

        struct appdata
        {
            UNITY_VERTEX_INPUT_INSTANCE_ID
            float4 vertex : POSITION;
            float2 uv : TEXCOORD0;
            float3 normal : NORMAL;
        };

        struct v2f
        {
            float2 uv : TEXCOORD0;
            float4 pos : SV_POSITION;
            float3 lighting : TEXCOORD1;
            UNITY_FOG_COORDS(2)
        #if defined(SHADOWMAP)
            float2 shadowUV : TEXCOORD3;
        #endif
        };

        sampler2D _MainTex;
        float4 _MainTex_ST;
        fixed4 _AmbientColor;
        fixed4 _Color;
        fixed4 _EmissiveColor;
        float _AlphaClipThreshold;
        half _Gamma;

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

        #if !defined(UNLIT)
            o.lighting = MMLight3(_AmbientColor, COMPUTE_VIEW_NORMAL);
        #else
            o.lighting = float3(1.0, 1.0, 1.0);
        #endif

        #if defined(SHADOWMAP)
            float3 worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
            float3 worldNormal = UnityObjectToWorldNormal(v.normal);
            o.shadowUV = MMShadowMapUV(worldPos, worldNormal, _ShadowMapOffset, _ShadowMapScale);
        #endif

            UNITY_TRANSFER_FOG(o, o.pos);
            return o;
        }

        // Shared shading for both passes, before fog and clip.
        fixed4 MMShade(v2f i)
        {
            fixed4 baseColor = tex2D(_MainTex, i.uv);

        #if defined(SHADOWMAP)
            // Subtractive, applied before lighting (matches original).
            baseColor.rgb = saturate(baseColor.rgb - MMSampleShadowMap(_ShadowMap, i.shadowUV, _ShadowMapIntensity));
        #endif

            fixed4 col;
        #if defined(EMISSION)
            // = base * lit * (1 - E) * C  +  E * base * lit
            float3 rgb = baseColor.rgb * i.lighting
                       * ((1.0 - _EmissiveColor.rgb) * _Color.rgb + _EmissiveColor.rgb);
            col = fixed4(rgb, baseColor.a);
        #elif defined(UNLIT)
            col = baseColor; // lighting is 1 when unlit
        #else
            col = baseColor * _Color;
            col.rgb *= i.lighting;
        #endif

        #if defined(GAMMA)
            col.rgb *= _Gamma;
        #endif
            return col;
        }
        ENDCG

        Pass
        {
            // OPAQUE PASS
            Lighting On // needed, otherwise unity_LightPosition[0] doesn't work
            ZWrite On

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment fragOpaque
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #pragma multi_compile __ EMISSION
            #pragma multi_compile __ ALPHA
            #pragma multi_compile __ SHADOWMAP
            #pragma multi_compile __ UNLIT
            #pragma multi_compile __ GAMMA

            fixed4 fragOpaque(v2f i) : SV_Target
            {
                fixed4 col = MMShade(i);
                UNITY_APPLY_FOG(i.fogCoord, col);
            #if defined(ALPHA)
                MMPassOneClip(_AlphaClipThreshold, col);
            #endif
                return col;
            }
            ENDCG
        }

        Pass
        {
            // TRANSPARENT PASS
            Blend SrcAlpha OneMinusSrcAlpha
            Lighting On // needed, otherwise unity_LightPosition[0] doesn't work
            ZWrite Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment fragTransparent
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #pragma multi_compile __ EMISSION
            #pragma multi_compile __ ALPHA
            #pragma multi_compile __ SHADOWMAP
            #pragma multi_compile __ UNLIT

            fixed4 fragTransparent(v2f i) : SV_Target
            {
            #if defined(ALPHA)
                fixed4 col = MMShade(i);
                UNITY_APPLY_FOG(i.fogCoord, col);
                MMPassTwoClip(_AlphaClipThreshold, col);
                return col;
            #else
                discard;
                return 0;
            #endif
            }
            ENDCG
        }
    }

    Fallback "VertexLit"
}