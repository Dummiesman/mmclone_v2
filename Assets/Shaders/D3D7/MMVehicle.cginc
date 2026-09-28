#ifndef MM_VEHICLE_INCLUDED
#define MM_VEHICLE_INCLUDED

#include "UnityCG.cginc"
#include "MMLightModel.cginc"
#include "MMCommon.cginc"
#include "AutoLight.cginc"

//Maximum dent depth, in OBJECT-SPACE units (was 0.05 in clip space, which was
//neither a percentage nor a consistent distance). Override with
//#define MM_DAMAGE_DEPTH x before including this file, or swap the body of
//MMApplyDamageDeform for a material property if you add one to the .shader.
#ifndef MM_DAMAGE_DEPTH
#define MM_DAMAGE_DEPTH 0.025
#endif

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
    float4 color : COLOR;
    float3 normal : NORMAL;
    float3 lighting : TEXCOORD1;
    float2 envUV : TEXCOORD2;
    LIGHTING_COORDS(3, 4)
    UNITY_FOG_COORDS(5)
    float4 screenPos : TEXCOORD6;
};

float4 _MainTex_ST;
float4 _AmbientColor;
float4 _Color;
float4 _EmissiveColor;
float _AlphaClipThreshold;
float _ReflectionIntensity; // Global
uniform sampler2D _MainTex;
uniform sampler2D _ReflTex;
uniform sampler2D _DamageTex;
uniform half _Reflection;

void MMApplyDamageDeform(inout float4 vertex, float3 normal, float vcolorAlpha)
{
    float dent = min(1.0 - vcolorAlpha, MM_DAMAGE_DEPTH);

    // normal may be unnormalised on import; guard against a degenerate one so we
    // never feed NaN into the position.
    float nLen = length(normal);
    float3 n = (nLen > 1e-5) ? (normal / nLen) : float3(0, 0, 0);

    vertex.xyz -= n * dent;
}

v2f MMVehicleVert(appdata v)
{
    v2f o;
    UNITY_SETUP_INSTANCE_ID(v);

    // allow some 3d damage up to MM_DAMAGE_DEPTH
    MMApplyDamageDeform(v.vertex, v.normal, v.color.a);

    o.pos = UnityObjectToClipPos(v.vertex);
    o.screenPos = ComputeScreenPos(o.pos);

    o.normal = v.normal;
    o.color = v.color;

    // ENV MAP, as per modShader::BeginEnvMap.
    // D3DTSS_TCI_CAMERASPACENORMAL feeds the camera-space normal as the texcoord, then
    // v7 (sm_Camera, translation stripped) rotates it back out to world space before
    // mtxEnvMap applies the scale/bias. The two camera transforms cancel, so the uv is
    // just the world normal - hence the quirk: only the car's heading moves the
    // reflection, never the camera.
    float3 envNormal = normalize(mul((float3x3) unity_ObjectToWorld, v.normal));

    // mtxEnvMap: u = 0.5x + 0.5, v = 0.5y + 0.5
    // (m11 is -0.5 in the original; sign flipped here for Unity's bottom-up uv origin)
    o.envUV = envNormal.xy * 0.5 + 0.5;

    o.uv = TRANSFORM_TEX(v.uv, _MainTex);

    // compute lighting
    o.lighting = MMLight8(_AmbientColor, COMPUTE_VIEW_NORMAL);
    UNITY_TRANSFER_FOG(o, o.pos);
    TRANSFER_VERTEX_TO_FRAGMENT(o);

    return o;
}

// Shared shading for both passes. Returns the un-fogged, un-clipped colour.
// emissiveDarkensLighting is always a compile-time literal, so the branch folds away.
fixed4 MMVehicleShade(v2f IN, bool emissiveDarkensLighting)
{
    float damageIntensity = 1.0 - IN.color.a;
    float invDamageIntensity = IN.color.a;

    // diffuse + damage blend
    float4 baseColor = tex2D(_MainTex, IN.uv);
    float4 dmgColor = tex2D(_DamageTex, IN.uv);
    float3 diffuseDmg = (baseColor.rgb * invDamageIntensity) + (dmgColor.rgb * damageIntensity);

    // apply reflection (uv comes straight from the vertex stage now)
    float scaledReflection = _Reflection * _ReflectionIntensity;
    float3 reflectionColor = tex2D(_ReflTex, IN.envUV).rgb * clamp(scaledReflection - (damageIntensity * scaledReflection), 0, 1);
    reflectionColor *= baseColor.a;

    float3 emisColor = _EmissiveColor.rgb * baseColor.rgb;

    // emissive eats into the lit contribution, same as the D3D7 shader.
    // NOTE: the original transparent pass did NOT do this - see the notes.
    float3 lighting = emissiveDarkensLighting
        ? IN.lighting * (1.0 - _EmissiveColor.rgb)
        : IN.lighting;

    float3 rgb = (diffuseDmg * lighting * _Color.rgb);
    rgb += reflectionColor;
    rgb += emisColor;

    return fixed4(rgb, baseColor.a);
}

fixed4 MMVehicleFragOpaque(v2f IN) : SV_Target
{
    fixed4 col = MMVehicleShade(IN, true);

    UNITY_APPLY_FOG(IN.fogCoord, col);
    MMPassOneClip(_AlphaClipThreshold, col);

    //TEST, DITHER
    //MMDither(max(0,  min(1.f, IN.screenPos.w * IN.screenPos.w * IN.screenPos.w) - 0.4f), IN.screenPos);

    return col;
}

fixed4 MMVehicleFragTransparent(v2f IN) : SV_Target
{
    fixed4 col = MMVehicleShade(IN, false);

    col.rgb = min(col.rgb, 1.0);

    UNITY_APPLY_FOG(IN.fogCoord, col);
    MMPassTwoClip(_AlphaClipThreshold, col);

    return col;
}

struct v2f_shadow
{
    V2F_SHADOW_CASTER;
};

v2f_shadow MMVehicleShadowVert(appdata v)
{
    v2f_shadow o;
    UNITY_SETUP_INSTANCE_ID(v);

    MMApplyDamageDeform(v.vertex, v.normal, v.color.a);

    TRANSFER_SHADOW_CASTER_NORMALOFFSET(o);
    return o;
}

float4 MMVehicleShadowFrag(v2f_shadow i) : SV_Target
{
    SHADOW_CASTER_FRAGMENT(i);
}

#endif // MM_VEHICLE_INCLUDED
