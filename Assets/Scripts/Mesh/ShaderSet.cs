using System.Collections.Generic;
using System.IO;
using UnityEngine;

public class ShaderSet
{
    public static float FlatColorIntensity = 1.0f;

    public class Shader
    {
        public string Name = string.Empty;
        public Color Ambient = Color.white;
        public Color Diffuse = Color.white;
        public Color Specular = Color.black;
        public Color Emissive = Color.black;
        public float Reflectivity = 0.0f;

        public static Shader Read(BinaryReader reader, bool compressed)
        {
            var s = new Shader();

            s.Name = reader.ReadAGEString();
            bool hasTexture = !string.IsNullOrEmpty(s.Name);

            if (compressed)
            {
                // Packed byte colors; Ambient is NOT stored here.
                s.Diffuse = reader.ReadColor4d();   // Color32 -> Color (auto /255)
                s.Specular = reader.ReadColor4d();
                s.Emissive = reader.ReadColor4d();
            }
            else
            {
                // Full float colors in order: Diffuse, Ambient, Specular, Emissive.
                s.Diffuse = reader.ReadColor4f();
                reader.ReadColor4f();                // ambient read but discarded
                s.Specular = reader.ReadColor4f();
                s.Emissive = reader.ReadColor4f();

                s.Diffuse = s.Diffuse.Quantize();
                s.Specular = s.Specular.Quantize();
                s.Emissive = s.Emissive.Quantize();
            }

            s.Reflectivity = reader.ReadSingle();

            if (!hasTexture)
            {
                s.Diffuse.r *= FlatColorIntensity;
                s.Diffuse.g *= FlatColorIntensity;
                s.Diffuse.b *= FlatColorIntensity;
            }

            s.Ambient = s.Diffuse; // reference.Ambient = reference.Diffuse
            return s;
        }
    }

    public int ShadersPerVariant = 0;
    public int VariantCount = 0;
    public List<Shader> Shaders = new List<Shader>();

    public List<Shader> GetShadersForVariant(int index)
    {
        if (index > VariantCount || index < 0) return new List<Shader>();

        List<Shader> shadersForVariant = new List<Shader>();
        int startIndex = (ShadersPerVariant * index);
        for (int i = startIndex; i < startIndex + ShadersPerVariant; i++)
        {
            shadersForVariant.Add(Shaders[i]);
        }
        return shadersForVariant;
    }

    public void Load(BinaryReader reader)
    {
        int variantCount = reader.ReadInt32();
        bool compressed = false;
        if (variantCount >= 128)
        {
            compressed = true;
            variantCount -= 128;
        }

        ShadersPerVariant = reader.ReadInt32();
        VariantCount = variantCount;

        Shaders.Clear();
        for (int v = 0; v < variantCount; v++)
            for (int i = 0; i < ShadersPerVariant; i++)
                Shaders.Add(Shader.Read(reader, compressed));
    }

    public void LoadSafe(BinaryReader reader)
    {
        // the original game is tolerant of broken shader sets
        try
        {
            Load(reader);
        }
        catch(EndOfStreamException)
        {
        }
    }
}
