using UnityEngine;

public class LightGlow
{
    public static float GlowIntensity = 1f;
    public static float GlowScale = 0.2f;

    public Vector3 Position = Vector3.zero;
    public Color Color = Color.white;
    public float SpotExponent = 0.0f;
    public Vector3 Direction = Vector3.forward;
    public float Intensity = 25.0f;

    public static Texture2D GlowTexture => glowTexture;
    private static Texture2D glowTexture;

    public static void InitLights()
    {
        if(glowTexture == null)
        {
            glowTexture = TextureCache.Get("lt_glow");
        }
    }

    public static void ShutdownLights()
    {
        if (glowTexture != null)
        {
            Object.Destroy(glowTexture);
        }
    }
    public float ComputeIntensity(Vector3 eyePosition, float threshold)
    {
        Vector3 toEye = eyePosition - Position;
        float distSq = toEye.sqrMagnitude;

        // Beyond the range where intensity / distSq could reach the threshold
        if (distSq * threshold > Intensity)
            return 0f;

        // The original divides by zero here if the eye is exactly on the light (gives +inf)
        if (distSq <= 0f)
            return threshold > 0f ? 0f : float.PositiveInfinity;

        float intensity = Intensity / distSq;

        if (SpotExponent != 0f)
        {
            float cosAngle = Vector3.Dot(toEye / Mathf.Sqrt(distSq), Direction);
            if (cosAngle < 0f)
                return 0f;

            intensity *= Mathf.Pow(cosAngle, SpotExponent);
        }

        return Mathf.Max(0f, intensity - threshold);
    }

    public void DrawGlow(Vector3 eyePosition)
    {
        float intensity = ComputeIntensity(eyePosition, 0.0f);
        float distSq = (eyePosition - Position).sqrMagnitude;

        // Guard against NaN; the original would have fed garbage to the draw
        float scale = Mathf.Sqrt(Mathf.Max(0f, intensity * distSq)) * GlowScale;

        var color = new Color(
            GlowIntensity * Color.r,
            GlowIntensity * Color.g,
            GlowIntensity * Color.b,
            1f);

        LightGlowRenderer.Draw(Position, scale, color, glowTexture);
    }
}