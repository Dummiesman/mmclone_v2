fixed MMComputeShadowMap(sampler2D shadowSampler, float3 worldPos, float3 worldNormal,
                         float2 offset, float scale, float intensity, float groundHeight)
{
    float3 n = normalize(worldNormal);
    float h = worldPos.y - groundHeight;

    float2 floorPos = worldPos.xz;
    float2 wallPos = worldPos.xz + h * n.xz;

    float2 uv = lerp(wallPos, floorPos, saturate(n.y)) * scale + offset;

    fixed a = tex2D(shadowSampler, uv).a;
    return (1.0 - a) * intensity;
}

float2 MMShadowMapUV(float3 worldPos, float3 worldNormal, float2 offset, float scale)
{
    float2 wallPos = worldPos.xz + worldPos.y * worldNormal.xz;
    return lerp(wallPos, worldPos.xz, saturate(worldNormal.y)) * scale + offset;
}

fixed MMSampleShadowMap(sampler2D shadowSampler, float2 uv, float intensity)
{
    return (1.0 - tex2D(shadowSampler, uv).a) * intensity;
}