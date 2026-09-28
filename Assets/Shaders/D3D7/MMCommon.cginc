#ifndef MM_COMMON_INCLUDED
#define MM_COMMON_INCLUDED

void MMDither(float opacity, float4 screenPos)
{
    float4x4 thresholdMatrix =
    { 1.0 / 17.0,  9.0 / 17.0,  3.0 / 17.0, 11.0 / 17.0,
      13.0 / 17.0,  5.0 / 17.0, 15.0 / 17.0,  7.0 / 17.0,
       4.0 / 17.0, 12.0 / 17.0,  2.0 / 17.0, 10.0 / 17.0,
      16.0 / 17.0,  8.0 / 17.0, 14.0 / 17.0,  6.0 / 17.0
    };
    float4x4 _RowAccess = { 1,0,0,0, 0,1,0,0, 0,0,1,0, 0,0,0,1 };
    float2 stipplePos = screenPos.xy / screenPos.w;
    stipplePos *= _ScreenParams.xy / 1.f; // pixel position
    clip(opacity - thresholdMatrix[fmod(stipplePos.x, 4)] * _RowAccess[fmod(stipplePos.y, 4)]);
}

//Pass one keeps texels at or above the threshold (the solid body of the car).
void MMPassOneClip(float alphaClipThreshold, fixed4 color)
{
    clip(color.a - alphaClipThreshold);
}

//Pass two keeps the band 0.1 <= a < threshold (glass, decals, mesh grilles).
//If no texel in _MainTex falls inside that band, this pass draws nothing at all
//and can be skipped entirely - see VehicleShaderD3D7_Opaque.shader.
void MMPassTwoClip(float alphaClipThreshold, fixed4 color)
{
    clip(alphaClipThreshold - color.a);
    clip(color.a - 0.1f);
}

#endif // MM_COMMON_INCLUDED
