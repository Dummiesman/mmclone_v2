#ifndef MM_D3D7_INCLUDED
#define MM_D3D7_INCLUDED

#include "UnityCG.cginc"
#include "MMLightModel.cginc"
#include "MMCommon.cginc"
#include "AutoLight.cginc"
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
    float3 normal : NORMAL;
    LIGHTING_COORDS(2, 3)
    UNITY_FOG_COORDS(4)
#if defined(SHADOWMAP)
    float2 shadowUV : TEXCOORD5;
#endif
};

sampler2D _MainTex;
float4 _MainTex_ST;
float4 _AmbientColor;
float4 _Color;
float4 _EmissiveColor;
float _AlphaClipThreshold;

// Globals
sampler2D _ShadowMap;
half _ShadowMapIntensity;
half _ShadowMapScale;
float2 _ShadowMapOffset;

v2f MMD3D7Vert(appdata v)
{
    v2f o;
    UNITY_SETUP_INSTANCE_ID(v);

    o.pos = UnityObjectToClipPos(v.vertex);
    o.uv = TRANSFORM_TEX(v.uv, _MainTex);

#if !defined(UNLIT)
    o.lighting = MMLight8(_AmbientColor, COMPUTE_VIEW_NORMAL);
#else
    o.lighting = float3(1.0, 1.0, 1.0);
#endif

#if defined(SHADOWMAP)
    float3 worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
    float3 worldNormal = UnityObjectToWorldNormal(v.normal); // already normalized
    o.shadowUV = MMShadowMapUV(worldPos, worldNormal, _ShadowMapOffset, _ShadowMapScale);
#endif

    UNITY_TRANSFER_FOG(o, o.pos);
    TRANSFER_VERTEX_TO_FRAGMENT(o);
    return o;
}

inline void MMApplyShadow(inout fixed4 col, v2f i)
{
#if defined(SHADOWMAP)
    col.rgb *= 1.0 - MMSampleShadowMap(_ShadowMap, i.shadowUV, _ShadowMapIntensity);
#endif
}

//Shared shading for both passes. Returns the un-clipped colour.
//emissiveDarkensLighting is always a compile-time literal, so the branch folds away.
fixed4 MMD3D7Shade(v2f i, bool emissiveDarkensLighting)
{
    float4 baseColor = tex2D(_MainTex, i.uv);
    float3 emisColor = _EmissiveColor.rgb * baseColor.rgb;

    //NOTE: the original transparent pass did NOT darken lighting - see the notes.
    float3 lighting = emissiveDarkensLighting
        ? i.lighting * (1.0 - _EmissiveColor.rgb)
        : i.lighting;

    float3 rgb = (baseColor.rgb * lighting * _Color.rgb) + emisColor;

    return fixed4(rgb, baseColor.a * _Color.a);
}


fixed4 MMD3D7FragOpaque(v2f i) : SV_Target
{
    fixed4 col = MMD3D7Shade(i, true);
    MMApplyShadow(col, i);
    UNITY_APPLY_FOG(i.fogCoord, col);
    MMPassOneClip(_AlphaClipThreshold, col);
    return col;
}

fixed4 MMD3D7FragTransparent(v2f i) : SV_Target
{
    fixed4 col = MMD3D7Shade(i, false);
    col.rgb = min(col.rgb, 1.0);
    MMApplyShadow(col, i);
    UNITY_APPLY_FOG(i.fogCoord, col);
    MMPassTwoClip(_AlphaClipThreshold, col);
    return col;
}
#endif // MM_D3D7_INCLUDED
