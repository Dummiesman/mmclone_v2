using System.Collections.Generic;
using System;
using UnityEngine;
using System.Linq;

internal static class SharedMaterialCache
{
    public static int Requests { get; private set; }
    public static int Hits { get; private set; }
    public static int LiveCount => slotsByKey.Count;

    private sealed class Slot
    {
        public Material Material;
        public int RefCount;
    }

    private static readonly Dictionary<MaterialKey, Slot> slotsByKey =
        new Dictionary<MaterialKey, Slot>();
    private static readonly Dictionary<Material, MaterialKey> keysByMaterial =
        new Dictionary<Material, MaterialKey>();

    public static Material Acquire(in MaterialKey key, Func<MaterialKey, Material> create, out bool reused)
    {
        Requests++;
        reused = slotsByKey.TryGetValue(key, out var slot);

        if (!reused)
        {
            slot = new Slot { Material = create(key) };
            slotsByKey[key] = slot;
            keysByMaterial[slot.Material] = key;
        }
        else
        {
            Hits++;
        }

        slot.RefCount++;
        return slot.Material;
    }

    public static void LogStats()
    {
        int sharedSlots = 0, totalRefs = 0;
        foreach (var slot in slotsByKey.Values)
        {
            totalRefs += slot.RefCount;
            if (slot.RefCount > 1) sharedSlots++;
        }

        Debug.Log($"SharedMaterialCache: {LiveCount} live materials serving {totalRefs} references " +
                  $"({totalRefs - LiveCount} saved). {sharedSlots} materials are shared. " +
                  $"Lifetime: {Hits}/{Requests} requests reused an existing material.");
    }

    public static void LogSingletonCauses()
    {
        var singles = new List<MaterialKey>();
        foreach (var kv in slotsByKey)
            if (kv.Value.RefCount == 1) singles.Add(kv.Key);

        int Groups(Func<MaterialKey, object> ignoring) =>
            singles.GroupBy(ignoring).Count();

        Debug.Log($"{singles.Count} singletons. Distinct if ignoring:\n" +
            $"  Properties: {Groups(k => (k.Shader, k.MainTex, k.Diffuse, k.Ambient, k.Specular, k.Reflectivity, k.LightEnable))}\n" +
            $"  Colors: {Groups(k => (k.Shader, k.MainTex, k.Reflectivity, k.LightEnable, k.Properties))}\n" +
            $"  Texture: {Groups(k => (k.Shader, k.Diffuse, k.Ambient, k.Specular, k.Reflectivity, k.LightEnable, k.Properties))}");
    }

    public static void Release(Material material)
    {
        // ReferenceEquals so an externally destroyed material still gets cleaned up.
        if (ReferenceEquals(material, null) || !keysByMaterial.TryGetValue(material, out var key))
            return;

        var slot = slotsByKey[key];
        if (--slot.RefCount > 0)
            return;

        slotsByKey.Remove(key);
        keysByMaterial.Remove(material);
        if (material != null) UnityEngine.Object.Destroy(material);
    }
}