using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One live copy of a PackageObjectTemplate: the GameObjects it created, a
/// name lookup for them, and everything needed to re-point their renderers at
/// a different variant's shared Materials.
///
/// This object owns its GameObjects and nothing else. Meshes, Materials and
/// the ShaderSet all belong to the template and are shared with every other
/// instance of it.
/// </summary>
public class PackageObjectInstance
{
    /// <summary>
    /// One per instantiated template entry, in template order: the single mesh object, or the
    /// group's root. Entries rejected by the part filter have no Part at all.
    /// </summary>
    public readonly struct Part
    {
        public readonly string Name;
        public readonly GameObject Object;

        public Part(string name, GameObject go)
        {
            Name = name;
            Object = go;
        }
    }

    private class RendererBinding
    {
        public MeshRenderer Renderer;
        public int MaterialSet;
        public int[] MaterialMap;

        /// <summary>Scratch array reused on every variant switch, to avoid per-switch allocation.</summary>
        public Material[] Slots;
    }

    private const float LodStep = 0.001f;

    /// <summary>
    /// Ceiling for the highest LOD threshold. Unity selects a level when the object's relative
    /// height is strictly greater than that level's threshold, so a threshold of exactly 1.0 can
    /// never be met and the level never renders. Normalization targets this instead of 1.0.
    /// </summary>
    private const float MaxLodHeight = 0.9f;

    /// <summary>
    /// A pivot this far from the geometry (as a multiple of the geometry's own size) means the
    /// entry's authored ObjectSize is measuring the offset rather than the object. Logged once
    /// per template entry, since the bounds are cached.
    /// </summary>
    private const float PivotOffsetWarnRatio = 2f;

    private readonly PackageObjectTemplate template;
    private readonly List<Part> parts;
    private readonly List<RendererBinding> bindings = new List<RendererBinding>();
    private readonly Dictionary<string, GameObject> partsByName =
        new Dictionary<string, GameObject>(System.StringComparer.OrdinalIgnoreCase);

    public PackageObjectTemplate Template => template;
    public IReadOnlyList<Part> Parts => parts;
    public int Variant { get; private set; }

    /// <summary>
    /// Use PackageObjectTemplate.Instantiate rather than calling this directly.
    /// </summary>
    /// <param name="includePart">
    /// Optional filter on entry name. Entries it rejects get no GameObjects, no renderers and
    /// no Part - use it to skip parts this instance will never show. Null instantiates everything.
    /// </param>
    public PackageObjectInstance(PackageObjectTemplate template, Transform parent, int variant,
                                 System.Predicate<string> includePart = null)
    {
        this.template = template;
        Variant = variant;

        parts = new List<Part>(template.Entries.Count);

        foreach (var entry in template.Entries)
        {
            if (includePart != null && !includePart(entry.Name))
                continue;

            var go = entry.IsGroup
                ? CreateGroupObject(entry, parent)
                : CreateSingleObject(entry, parent);

            parts.Add(new Part(entry.Name, go));

            if (!string.IsNullOrEmpty(entry.Name) && !partsByName.ContainsKey(entry.Name))
                partsByName[entry.Name] = go;
        }

        ApplyVariantMaterials();
    }

    /// <summary>Returns the named part, or null if it doesn't exist or was filtered out.</summary>
    public GameObject Find(string name)
    {
        return name != null && partsByName.TryGetValue(name, out var go) ? go : null;
    }

    /// <summary>
    /// Re-points every renderer at the shared Materials for the given
    /// variant. No Materials are created - the template builds each variant's
    /// set once, the first time any instance asks for it.
    /// </summary>
    public void SetVariant(int variant)
    {
        if (Variant == variant) return;
        Variant = variant;
        ApplyVariantMaterials();
    }

    /// <summary>Destroys the GameObjects this instance created. Shared template assets are untouched.</summary>
    public void Destroy()
    {
        foreach (var part in parts)
        {
            if (part.Object != null) Object.Destroy(part.Object);
        }

        parts.Clear();
        bindings.Clear();
        partsByName.Clear();
    }

    private void ApplyVariantMaterials()
    {
        foreach (var binding in bindings)
        {
            if (binding.Renderer == null) continue;

            var materials = template.GetMaterials(Variant, binding.MaterialSet);
            if (materials.Length == 0) continue; // no shader for this part - leave Unity's default material alone

            var map = binding.MaterialMap;
            for (int s = 0; s < map.Length; s++)
            {
                int offset = map[s];
                binding.Slots[s] = offset >= 0 && offset < materials.Length ? materials[offset] : null;
            }

            binding.Renderer.sharedMaterials = binding.Slots;
        }
    }

    private GameObject CreateSingleObject(PackageObjectTemplate.EntryData entry, Transform parent)
    {
        var go = new GameObject(entry.Name);
        go.transform.SetParent(parent, false);
        if (entry.PivotPosition.HasValue) go.transform.localPosition = entry.PivotPosition.Value;

        go.AddComponent<MeshFilter>().sharedMesh = entry.Mesh;
        Bind(go.AddComponent<MeshRenderer>(), entry.MaterialSet, entry.MaterialMap);

        return go;
    }

    private GameObject CreateGroupObject(PackageObjectTemplate.EntryData entry, Transform parent)
    {
        var root = new GameObject(entry.Name);
        root.transform.SetParent(parent, false);
        if (entry.PivotPosition.HasValue) root.transform.localPosition = entry.PivotPosition.Value;

        // Size and reference point both come from the meshes themselves, not from the entry's
        // authored ObjectSize. Some objects have their pivot at the origin with the geometry
        // hundreds of metres away, and a pivot-relative size measures the offset rather than the
        // object - which inflates every LOD threshold past 1.0 and kills the highest level.
        // Bounds and thresholds depend only on the template entry, so both are cached on it.
        var localBounds = entry.LocalBounds ??= ComputeLocalBounds(entry);
        float objectSize = localBounds.size.magnitude; // Unity's convention: the AABB diagonal

        // Levels that collapse are already trimmed off, so their GameObjects are never created.
        var heights = entry.LodHeights ??= ComputeLodHeights(entry, objectSize);

        var lods = new LOD[heights.Length];

        for (int i = 0; i < heights.Length; i++)
        {
            var level = entry.Levels[i];

            var child = new GameObject(entry.Name + "_" + level.Suffix);
            child.transform.SetParent(root.transform, false);
            child.AddComponent<MeshFilter>().sharedMesh = level.Mesh;

            var renderer = child.AddComponent<MeshRenderer>();
            Bind(renderer, entry.MaterialSet, level.MaterialMap);

            lods[i] = new LOD(heights[i], new Renderer[] { renderer });
        }

        if (lods.Length == 0)
            return root; // nothing survived - no LODGroup to drive

        var lodGroup = root.AddComponent<LODGroup>();
        lodGroup.SetLODs(lods);

        // Order matters: RecalculateBounds() would overwrite both of these. It is deliberately
        // not called - it measures the union of world-space renderer AABBs, which disagrees with
        // the size the thresholds were computed from, and it moves the reference point.
        //
        // The reference point is what Unity measures camera distance to. Leaving it at the pivot
        // on an offset object means standing next to the geometry reads as hundreds of metres
        // away (lowest LOD, or culled outright), while standing at the pivot forces LOD 0 on
        // something off in the distance.
        lodGroup.localReferencePoint = localBounds.center;
        lodGroup.size = objectSize;

        return root;
    }

    /// <summary>
    /// Union of the entry's level meshes in root-local space. Child level objects are created at
    /// identity under the root, so mesh-local and root-local coincide and the mesh bounds drop
    /// straight in. Falls back to the entry's authored ObjectSize if no mesh is available.
    /// </summary>
    private static Bounds ComputeLocalBounds(PackageObjectTemplate.EntryData entry)
    {
        Bounds? acc = null;

        foreach (var level in entry.Levels)
        {
            if (level.Mesh == null) continue;

            var b = level.Mesh.bounds;

            if (acc == null)
            {
                acc = b;
            }
            else
            {
                var merged = acc.Value;
                merged.Encapsulate(b);
                acc = merged;
            }
        }

        if (acc == null)
        {
            Debug.LogWarning($"{entry.Name}: no level meshes to measure; falling back to authored ObjectSize.");
            return new Bounds(Vector3.zero, Vector3.one * (entry.ObjectSize / Mathf.Sqrt(3f)));
        }

        var bounds = acc.Value;

        float size = bounds.size.magnitude;
        float offset = bounds.center.magnitude;

        if (size > 0f && offset > size * PivotOffsetWarnRatio && Application.isEditor)
        {
            Debug.LogWarning(
                $"{entry.Name}: geometry sits {offset:0.#}m from its pivot but is only {size:0.#}m across " +
                $"(authored ObjectSize {entry.ObjectSize:0.#}). Using mesh bounds for LOD sizing.");
        }

        return bounds;
    }

    /// <summary>
    /// Screen-relative transition heights for a LOD group, one per surviving level, with the
    /// last forced to 0. Levels whose thresholds collapse are cut off the end of the array.
    /// </summary>
    /// <param name="objectSize">
    /// The size the thresholds are measured against - must be the same value handed to
    /// LODGroup.size, or the group switches at different distances than the maths assumed.
    /// </param>
    private static float[] ComputeLodHeights(PackageObjectTemplate.EntryData entry, float objectSize)
    {
        int count = entry.Levels.Count;
        var heights = new float[count];
        float maxHeight = 0f;

        for (int i = 0; i < count; i++)
        {
            var level = entry.Levels[i];

            float raw = level.LodDistance.HasValue
                ? PackageObjectLoader.DistanceToScreenRelativeHeight(level.LodDistance.Value, objectSize)
                : 0f;

            if (float.IsNaN(raw) || float.IsInfinity(raw) || raw < 0f) raw = 0f;

            heights[i] = raw;
            if (raw > maxHeight) maxHeight = raw;
        }

        // With a sane objectSize the raw heights should already sit below the ceiling; this is a
        // safety net rather than the thing making LODs work. Normalizing to exactly 1.0 would
        // leave the top level unreachable, hence MaxLodHeight.
        float scale = maxHeight > MaxLodHeight ? MaxLodHeight / maxHeight : 1f;

        for (int i = 0; i < count; i++)
            heights[i] *= scale;

        for (int i = 1; i < heights.Length; i++)
        {
            float ceiling = heights[i - 1] - LodStep;

            if (ceiling <= 0f)
            {
                Debug.LogWarning($"{entry.Name}: LOD thresholds collapsed at level {i} of {count}; dropping remaining levels.");
                System.Array.Resize(ref heights, i);
                break;
            }

            if (heights[i] >= ceiling)
                heights[i] = ceiling;
        }

        if (heights.Length > 0)
            heights[heights.Length - 1] = 0f;

        return heights;
    }

    /// <summary>
    /// Records a renderer for variant switching. A null map means the mesh had
    /// no ShaderSet-driven materials at all (loaded with no shader in force) -
    /// such renderers are left with Unity's default material rather than
    /// guessing, and are never revisited.
    /// </summary>
    private void Bind(MeshRenderer renderer, int materialSet, int[] materialMap)
    {
        if (materialMap == null || materialSet == PackageObjectTemplate.NoMaterialSet) return;

        bindings.Add(new RendererBinding
        {
            Renderer = renderer,
            MaterialSet = materialSet,
            MaterialMap = materialMap,
            Slots = new Material[materialMap.Length],
        });
    }
}