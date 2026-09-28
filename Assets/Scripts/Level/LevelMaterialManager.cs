using System.Collections.Generic;
using System.IO;

public static class LevelMaterialManager 
{
    private static Dictionary<string, LevelPhysMaterial> materials = new Dictionary<string, LevelPhysMaterial>();

    public static void Init()
    {
        var defaultMtl = new LevelPhysMaterial()
        {
            Elasticity = 0.5f,
            Friction = 1.0f,
            Name = "default",
            Effect = "none",
            Sound = 0,
            Drag = 0,
            Height = 0,
            Width = 1,
            Depth = 0,
            PtxIndex = new[] { -1, -1 },
            PtxThreshold = new[] { 0.25f, 0.5f }
        };
        materials[defaultMtl.Name] = defaultMtl;
    }

    public static LevelPhysMaterial GetDefault()
    {
        var def = Find("_default");
        if(def != null) return def;
        return null;
    }

    public static LevelPhysMaterial Find(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return GetDefault();
        }
        if (materials.TryGetValue(name, out var material))
        {
            return material;
        }
        return null;
    }

    public static LevelPhysMaterial Load(string name, TokenFileParser reader)
    {
        var loaded = new LevelPhysMaterial();
        loaded.Read(reader);
        loaded.Name = name;

        var existing = Find(loaded.Name);
        if(existing != null)
        {
            // weird behavior inherited from base game
            if(loaded.Name == "_default")
            {
                existing.Copy(loaded);
                existing.Name = "default";
            }
            return existing;
        }
        else
        {
            materials[loaded.Name] = loaded;
            return loaded;
        }
    }

    public static LevelPhysMaterial Load(BinaryReader reader)
    {
        var loaded = new LevelPhysMaterial();
        loaded.Read(reader);

        var existing = Find(loaded.Name);
        if (existing != null)
        {
            return existing;
        }
        else
        {
            materials[loaded.Name] = loaded;
            return loaded;
        }
    }

    public static void Cleanup()
    {
        materials.Clear();
    }
}
