// Upgrade NOTE: replaced '_Object2World' with 'unity_ObjectToWorld'

Shader "Custom/Dashboard"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _MainColor ("Main Color", Color) = (1, 1, 1, 1)
        _AlphaClip ("Alpha Clip", Range(0, 1)) = 0.1
        _LightingAmount ("Lighting Amount", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags {"Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True"}
        LOD 100

        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Lighting On // remember this! otherwise unity_LightPosition[0] doesnt seem to work

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            //#pragma multi_compile_fwdadd_fullshadows
            #include "UnityCG.cginc"
            #include "MMLightModel.cginc"
            //#include "AutoLight.cginc"

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
                float3 lighting : TEXCOORD1;
                float3 normal : TEXCOORD2;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _MainColor;
            float _AlphaClip;
            float _LightingAmount;

            v2f vert (appdata v)
            {
                v2f o;

                o.vertex = UnityObjectToClipPos(v.vertex);
                o.normal = mul((float3x3)unity_ObjectToWorld, v.normal);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);

                float3 viewpos = UnityObjectToViewPos(o.vertex).xyz;
                float3 viewN = mul((float3x3)UNITY_MATRIX_IT_MV, v.normal);

                float3 lighting = MMLight3(float4(1.f, 1.f, 1.f, 1.f), viewN);

                float inverseLightAmt = 1.f - _LightingAmount;
                o.lighting = (float3(1.0, 1.0, 1.0) * inverseLightAmt)
                           + (lighting * _LightingAmount);

                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 tex = tex2D(_MainTex, i.uv);

                // Alpha clip uses texture alpha * main color alpha.
                clip(tex.a * _MainColor.a - _AlphaClip);

                fixed4 col = tex * _MainColor;
                col.rgb *= i.lighting.rgb;

                return col;
            }
            ENDCG
        }
    }
}