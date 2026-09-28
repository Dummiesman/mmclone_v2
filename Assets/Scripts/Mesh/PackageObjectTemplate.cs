using System;
using System.Collections.Generic;
using UnityEngine;

public class PackageObjectTemplate
{
    public const int NoMaterialSet = -1;

    public class MaterialSet
    {
        public ShaderSource Source;
        public MaterialProperties Properties;
        public bool? LightEnable;
    }

    public class LevelData
    {
        public string Suffix;
        public Mesh Mesh;
        public int[] MaterialMap;

        /// <summary>Distance at which this level hands over to the next, or null for the last one.</summary>
        public float? LodDistance;
    }

    public class EntryData
    {
        public bool IsGroup;

        /// <summary>Part name for a single object, group name for a LOD group.</summary>
        public string Name;

        // Single-object fields.
        public Mesh Mesh;
        public int[] MaterialMap;

        // Group fields.
        public List<LevelData> Levels;

        /// <summary>Bounds magnitude of the group's highest level, measured once at export.</summary>
        public float ObjectSize;

        /// <summary>LOD transition heights, computed on first instantiate and shared by every instance.</summary>
        public float[] LodHeights;
        public Bounds? LocalBounds;

        // Shared.
        public int MaterialSet = NoMaterialSet;
        public Vector3? PivotPosition;
    }

    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int AmbientColorId = Shader.PropertyToID("_AmbientColor");
    private static readonly int SpecColorId = Shader.PropertyToID("_SpecColor");
    private static readonly int EmissiveColorId = Shader.PropertyToID("_EmissiveColor");
    private static readonly int ShininessId = Shader.PropertyToID("_Shininess");
    private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
    private static readonly int ReflectionId = Shader.PropertyToID("_Reflection");
    private const string ShadowMappedKeyword = "SHADOWMAP";
    private const string UnlitKeyword = "UNLIT";

    public string Basename { get; }
    public IReadOnlyList<EntryData> Entries { get; }
    public ShaderSet Shaders { get; }
    public XrefList Xrefs { get; }

    private readonly List<MaterialSet> materialSets;

    private readonly Dictionary<(int Variant, int MaterialSet), Material[]> materialCache =
        new Dictionary<(int, int), Material[]>();

    public PackageObjectTemplate(
        string basename,
        List<EntryData> entries,
        List<MaterialSet> materialSets,
        ShaderSet shaders,
        XrefList xrefs)
    {
        Basename = basename;
        Entries = entries;
        Shaders = shaders;
        Xrefs = xrefs;
        this.materialSets = materialSets ?? new List<MaterialSet>();
    }

    public Material[] GetMaterials(int variant, int materialSet)
    {
        if (materialSet < 0 || materialSet >= materialSets.Count)
            return Array.Empty<Material>();

        var key = (variant, materialSet);
        if (materialCache.TryGetValue(key, out var cached))
            return cached;

        var built = BuildMaterials(variant, materialSets[materialSet]);
        materialCache[key] = built;
        return built;
    }

    /// <summary>
    /// Creates a live copy of this template under parent.
    /// </summary>
    /// <param name="includePart">
    /// Optional filter on entry name. Entries it rejects are not instantiated at all.
    /// Null instantiates everything.
    /// </param>
    public PackageObjectInstance Instantiate(Transform parent, int variant = 0,
                                             Predicate<string> includePart = null)
    {
        return new PackageObjectInstance(this, parent, variant, includePart);
    }

    public void DestroyAssets()
    {
        foreach (var materials in materialCache.Values)
            foreach (var material in materials)
                SharedMaterialCache.Release(material);
        materialCache.Clear();

        foreach (var entry in Entries)
        {
            if (entry.IsGroup)
            {
                if (entry.Levels == null) continue;
                foreach (var level in entry.Levels)
                {
                    if (level.Mesh != null) UnityEngine.Object.Destroy(level.Mesh);
                }
            }
            else if (entry.Mesh != null)
            {
                UnityEngine.Object.Destroy(entry.Mesh);
            }
        }
    }

    private Material[] BuildMaterials(int variant, MaterialSet materialSet)
    {
        var shaderSetEntries = Shaders?.GetShadersForVariant(variant);
        if (shaderSetEntries == null || !materialSet.Source.IsSet)
            return Array.Empty<Material>();

        var materials = new Material[shaderSetEntries.Count];

        for (int i = 0; i < shaderSetEntries.Count; i++)
        {
            var entry = shaderSetEntries[i];
            var mainTex = TextureCache.Get(entry.Name);

            var shader = materialSet.Source.Resolve(entry, mainTex);
            if (shader == null)
            {
                Debug.LogWarning($"{Basename}: no shader resolved for ShaderSet entry '{entry.Name}'.");
                continue;
            }

            // Normalize so shaders without UNLIT share regardless of LightEnable.
            var lightEnable = materialSet.LightEnable;
            if (lightEnable != null && !shader.keywordSpace.FindKeyword(UnlitKeyword).isValid)
                lightEnable = null;

            var key = new MaterialKey(shader, mainTex, entry.Diffuse, entry.Ambient,
                entry.Specular, entry.Reflectivity, lightEnable, materialSet.Properties);

            var name = entry.Name;
            materials[i] = SharedMaterialCache.Acquire(key, k => CreateMaterial(k, name), out _);
        }

        return materials;
    }

    private static Material CreateMaterial(in MaterialKey key, string name)
    {
        var material = new Material(key.Shader) { name = name };

        ApplyBaseProperties(material, key);
        ApplyShadowMapKeyword(material, key.MainTex);
        ApplyLightEnable(material, key.LightEnable);
        key.Properties?.ApplyTo(material);

        return material;
    }

    private static void ApplyBaseProperties(Material material, in MaterialKey key)
    {
        if (material.HasProperty(MainTexId)) material.SetTexture(MainTexId, key.MainTex);
        if (material.HasProperty(ColorId)) material.SetColor(ColorId, key.Diffuse);
        if (material.HasProperty(AmbientColorId)) material.SetColor(AmbientColorId, key.Ambient);
        if (material.HasProperty(SpecColorId)) material.SetColor(SpecColorId, key.Specular);
        if (material.HasProperty(EmissiveColorId)) material.SetColor(EmissiveColorId, Color.black);
        if (material.HasProperty(ShininessId)) material.SetFloat(ShininessId, 1f);
        if (material.HasProperty(ReflectionId)) material.SetFloat(ReflectionId, key.Reflectivity);
    }

    private static void ApplyShadowMapKeyword(Material material, AGETexture mainTex)
    {
        if (!material.shader.keywordSpace.FindKeyword(ShadowMappedKeyword).isValid)
            return;

        var tex = mainTex;
        bool shadowMapped = tex != null &&
            (tex.Flags.HasFlag(AGETexFlags.CloudShadowsHigh) ||
             tex.Flags.HasFlag(AGETexFlags.CloudShadowsLow));

        if (shadowMapped)
            material.EnableKeyword(ShadowMappedKeyword);
        else
            material.DisableKeyword(ShadowMappedKeyword);
    }

    private static void ApplyLightEnable(Material material, bool? lightEnable)
    {
        if (lightEnable == null) return;
        if (!material.shader.keywordSpace.FindKeyword(UnlitKeyword).isValid) return;

        if (lightEnable.Value)
            material.DisableKeyword(UnlitKeyword);
        else
            material.EnableKeyword(UnlitKeyword);
    }
}