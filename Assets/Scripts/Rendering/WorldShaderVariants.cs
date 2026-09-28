using UnityEngine;
using UnityEngine.Experimental.Rendering;

public static class WorldShaderVariants
{
    private const string twoPassShaderName = "Custom/D3D7";
    private const string onePassShaderName = "Custom/D3D7_Opaque";

    private static Shader twoPassShader;
    private static Shader onePassShader;

    public static Shader Select(Color color, Texture mainTex)
    {
        return (color.a >= 0.99f) && IsOpaqueOnly(mainTex) ? GetOnePassShader() : GetTwoPassShader();
    }

    public static Shader Select(Texture mainTex)
    {
        return IsOpaqueOnly(mainTex) ? GetOnePassShader() : GetTwoPassShader();
    }

    private static bool IsOpaqueOnly(Texture mainTex)
    {
        if (mainTex == null) return false;
        return !GraphicsFormatUtility.HasAlphaChannel(mainTex.graphicsFormat);
    }

    private static Shader GetTwoPassShader()
    {
        if (twoPassShader == null) twoPassShader = Shader.Find(twoPassShaderName);
        return twoPassShader;
    }

    private static Shader GetOnePassShader()
    {
        if (onePassShader == null) onePassShader = Shader.Find(onePassShaderName);
        return onePassShader != null ? onePassShader : GetTwoPassShader();
    }
}