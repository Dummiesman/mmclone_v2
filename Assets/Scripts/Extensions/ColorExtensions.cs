using UnityEngine;

public static class ColorExtensions
{
    public static float QuantizeChannel(this float v, int steps = 32)
    {
        if (v >= 0.05f)
            return v <= 0.95f ? Mathf.Floor(v * steps) / steps : 1.0f;
        return 0.0f;
    }

    public static Color Quantize(this Color c, int steps = 32)
    {
        return new Color(
            c.r.QuantizeChannel(steps),
            c.g.QuantizeChannel(steps),
            c.b.QuantizeChannel(steps),
            c.a.QuantizeChannel(steps)
        );
    }

    public static Color FlipRB(this Color color)
    {
        return new Color(color.b, color.g, color.r, color.a);
    }

    public static Color32 FlipRB(this Color32 color)
    {
        return new Color32(color.b, color.g, color.r, color.a);
    }
}