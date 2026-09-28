using System.IO;
using UnityEngine;

public class LevelPhysMaterial
{
    public string Name;

    public float Elasticity = 0.9f;
    public float Friction = 0.9f;
    public string Effect = "none";
    public int Sound = 0;
    public float Drag = 0f;
    public float Width = 0f;
    public float Height = 0f;
    public float Depth = 0f;

    public int[] PtxIndex = new int[] { -1, -1 };
    public float[] PtxThreshold = new float[] { 999f, 999f };

    public void Read(BinaryReader reader)
    {
        string name = new string(reader.ReadChars(32));
        Elasticity = reader.ReadSingle();
        Friction = reader.ReadSingle();
        string effect = new string(reader.ReadChars(32));
        string sound = new string(reader.ReadChars(32));

        // Clean strings
        Name = name.Substring(0, name.IndexOf('\0'));
        Effect = effect.Substring(0, effect.IndexOf('\0'));

        // Parse sound
        sound = sound.Substring(0, sound.IndexOf('\0'));
        if (int.TryParse(sound, out int soundParsed))
        {
            Sound = soundParsed;
        }
    }

    public void Read(TokenFileParser reader)
    {
        Elasticity = reader.Read("elasticity:", Elasticity);
        Friction = reader.Read("friction:", Friction);
        Effect = reader.Read("effect:", Effect);

        string sound = reader.Read("sound:", "none");
        if (int.TryParse(sound, out int soundParsed))
            Sound = soundParsed;

        // These fields only exist in lvlMaterial
        if (reader.SkipTo("drag:", 1))
        {
            Drag = reader.Read("drag:", Drag);
            Width = reader.Read("width:", Width);
            Height = reader.Read("height:", Height);
            Depth = reader.Read("depth:", Depth);

            Vector2Int ptxIndices = reader.Read<Vector2Int>("ptxindex:");
            Vector2 ptxThresholds = reader.Read<Vector2>("ptxthreshold:");

            PtxThreshold = new float[] { ptxThresholds.x, ptxThresholds.y };
            PtxIndex = new int[] { ptxIndices.x, ptxIndices.y };
        }
    }

    public void Copy(LevelPhysMaterial from)
    {
        Name = from.Name;
        Elasticity = from.Elasticity;
        Friction = from.Friction;
        Effect = from.Effect;
        Sound = from.Sound;
        Drag = from.Drag;
        Width = from.Width;
        Height = from.Height;
        Depth = from.Depth;

        PtxIndex = new int[] { from.PtxIndex[0], from.PtxIndex[1] };
        PtxThreshold = new float[] { from.PtxThreshold[0], from.PtxThreshold[1] };
    }

    public float ComputeDepth(Vector3 coordinate)
    {
        return ComputeDepth(coordinate.ToVec2XZ());
    }

    public float ComputeDepth(Vector2 coordinate)
    {
        if (Width == 0f || Height == 0f)
            return 0f;

        float halfWidth = Width / 2f;
        float halfHeight = Height / 2f;

        float widthMod = Mathf.Abs(coordinate.x) % Mathf.Abs(Width);
        float heightMod = Mathf.Abs(coordinate.y) % Mathf.Abs(Height);

        // Subtract half to put 0 as the middle of the surface
        widthMod -= halfWidth;
        heightMod -= halfHeight;

        // If we're in the center of the surface, we'll be at 100% depth.
        // If we're on the outside of the surface, we'll be at 0% depth.
        float widthFactor = 1f - (Mathf.Abs(widthMod) / halfWidth);
        float heightFactor = 1f - (Mathf.Abs(heightMod) / halfHeight);
        float depthFactor = (widthFactor + heightFactor) / 2f;

        return Depth * depthFactor;
    }
}
