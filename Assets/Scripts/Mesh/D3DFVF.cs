using System;

/// <summary>
/// Direct3D flexible vertex format flags, describing which components are
/// present on each vertex and in what order they appear in the buffer.
/// </summary>
[Flags]
public enum D3DFVF
{
    None = 0x0000,

    XYZ = 0x0002,
    XYZRHW = 0x0004,
    XYZB1 = 0x0006,
    XYZB2 = 0x0008,
    XYZB3 = 0x000A,
    XYZB4 = 0x000C,
    XYZB5 = 0x000E,
    PositionMask = 0x000E,

    Normal = 0x0010,
    PointSize = 0x0020,
    Diffuse = 0x0040,
    Specular = 0x0080,

    Tex0 = 0x0000,
    Tex1 = 0x0100,
    Tex2 = 0x0200,
    Tex3 = 0x0300,
    Tex4 = 0x0400,
    Tex5 = 0x0500,
    Tex6 = 0x0600,
    Tex7 = 0x0700,
    Tex8 = 0x0800,
    TexCountMask = 0x0F00,

    LastBeta_UByte4 = 0x1000,
}

public static class D3DFVFExtensions
{
    private const int TexCountShift = 8;

    /// <summary>True when the vertex carries an untransformed XYZ position.</summary>
    public static bool HasPosition(this D3DFVF fvf) => (fvf & D3DFVF.PositionMask) != 0;

    /// <summary>Number of texture coordinate sets on each vertex (0-8).</summary>
    public static int TextureCount(this D3DFVF fvf) =>
        ((int)(fvf & D3DFVF.TexCountMask)) >> TexCountShift;

    /// <summary>
    /// Floats used by texture coordinate set <paramref name="index"/>, taken from the
    /// D3DFVF_TEXCOORDSIZEn bit pair. Defaults to 2 when unspecified.
    /// </summary>
    public static int TexCoordSize(this D3DFVF fvf, int index)
    {
        int bits = ((int)fvf >> (16 + index * 2)) & 0x3;
        switch (bits)
        {
            case 0: return 2; // TEXCOORDSIZE2
            case 1: return 3; // TEXCOORDSIZE3
            case 2: return 4; // TEXCOORDSIZE4
            default: return 1; // TEXCOORDSIZE1
        }
    }
}