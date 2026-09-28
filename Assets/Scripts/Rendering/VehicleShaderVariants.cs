using UnityEngine;
using UnityEngine.Experimental.Rendering;

public static class VehicleShaderVariants
{
    private const string twoPassShaderName = "Custom/VehicleShaderD3D7";
    private const string onePassShaderName = "Custom/VehicleShaderD3D7_Opaque";

    private static Shader twoPassShader;
    private static Shader onePassShader;

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