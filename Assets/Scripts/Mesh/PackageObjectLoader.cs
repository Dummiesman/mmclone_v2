using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Build-time half of the template pipeline. Reads named meshes (single
/// objects, LOD groups, and a trailing "shaders" section) out of a
/// PackageFile, records which shader source + property overrides were active
/// for each one, and freezes the lot into a reusable PackageObjectTemplate
/// via ExportTemplate().
///
/// The loader never creates GameObjects, Renderers or Materials - it only
/// produces the data a template needs. Instantiation lives entirely on
/// PackageObjectTemplate / PackageObjectInstance.
///
/// Typical usage:
///
///   var loader = new PackageObjectLoader(basename, packageFile);
///   loader.SetDefaultShaderSelector(VehicleShaderFor);
///   loader.LoadGroup("body", applyPivot: false);
///   loader.SetShader(additiveShader);
///   loader.SetProperties(whiteAdditive);
///   loader.LoadGroup("tlight", applyPivot: false);
///   loader.SetShader(null);            // back to the default source
///   loader.LoadShaders();
///   var template = loader.ExportTemplate();
/// </summary>
public class PackageObjectLoader
{
    private class PendingLevel
    {
        public string Suffix;
        public Mesh Mesh;
        public int[] MaterialMap;
    }

    private class PendingEntry
    {
        public bool IsGroup;
        public string Name;

        // Single-object fields.
        public Mesh Mesh;
        public int[] MaterialMap;

        // Group fields.
        public List<PendingLevel> Levels;

        // Shared.
        public int MaterialSet;
        public string PivotPart;
    }

    private readonly string basename;
    private readonly PackageFile file;
    private readonly List<PendingEntry> pending = new List<PendingEntry>();
    private readonly List<PackageObjectTemplate.MaterialSet> materialSets =
        new List<PackageObjectTemplate.MaterialSet>();

    private ShaderSource defaultSource;
    private ShaderSource currentSource;
    private MaterialProperties currentProperties;
    private bool? currentLightEnable;
    private bool flipXZ;

    private int currentMaterialSet = PackageObjectTemplate.NoMaterialSet;
    private bool currentMaterialSetResolved;

    /// <summary>Set once LoadShaders() has been called.</summary>
    public ShaderSet Shaders { get; private set; }
    public XrefList Xrefs { get; private set; }

    // --- LOD distance thresholds, taken from the original game's lvlInstance constants ---
    public static float ObjMedThresh = 70f;
    public static float ObjLowThresh = 130f;
    public static float ObjVLowThresh = 200f;

    /// <summary>
    /// Approximate vertical field of view (degrees) used only to convert the
    /// distance thresholds above into Unity's screen-relative-height LOD
    /// thresholds.
    /// </summary>
    public static float ReferenceFov = 60f;

    /// <summary>
    /// Global multiplier applied to distance before converting to a screen
    /// relative height - a placeholder hook for a future object-detail
    /// setting. Read at instantiate time, so changing it affects objects
    /// built from then on without rebuilding templates.
    /// </summary>
    public static float DetailBias = 1f;

    private static readonly string[] MainGroupSuffixes = { "H", "M", "L", "VL" };

    public static float DistanceToScreenRelativeHeight(float distance, float objectSize)
    {
        distance *= DetailBias;
        if (distance <= 0f || objectSize <= 0f) return 1f;

        float visibleHeightAtDistance = 2f * distance * Mathf.Tan(ReferenceFov * 0.5f * Mathf.Deg2Rad);
        if (visibleHeightAtDistance <= 0f) return 1f;

        return Mathf.Max(0f, objectSize / visibleHeightAtDistance); // no upper clamp
    }

    public static float? DistanceForSuffix(string suffix)
    {
        switch (suffix)
        {
            case "H": return ObjMedThresh;
            case "M": return ObjLowThresh;
            case "L": return ObjVLowThresh;
            default: return null; // "VL" (or anything unrecognized) has no further transition distance.
        }
    }

    public PackageObjectLoader(string basename, PackageFile file)
    {
        this.basename = basename;
        this.file = file;
    }

    /// <summary>
    /// Forces lighting on or off for every object Load()'d/LoadGroup()'d from
    /// here on, by toggling the shader's "UNLIT" keyword where it exists. Pass
    /// null to leave the keyword at whatever the shader's default is.
    /// </summary>
    public void SetLightEnable(bool? enable)
    {
        currentLightEnable = enable;
        currentMaterialSetResolved = false;
    }

    /// <summary>
    /// The shader used whenever no explicit one is in force - i.e. before the
    /// first SetShader/SetShaderSelector call, and after either is passed
    /// null. Set this once, before loading anything; leaving it unset means
    /// parts loaded without an explicit shader get no ShaderSet-driven
    /// materials and keep Unity's default material.
    /// </summary>
    public void SetDefaultShader(Shader shader)
    {
        defaultSource = ShaderSource.Of(shader);
        currentMaterialSetResolved = false;
    }

    /// <summary>
    /// As SetDefaultShader, but the shader is chosen per ShaderSet entry -
    /// e.g. a one-pass variant for entries whose texture has no alpha channel
    /// and a two-pass one for the rest. Pass a cached delegate rather than a
    /// fresh lambda each call so material sets dedupe properly.
    /// </summary>
    public void SetDefaultShaderSelector(ShaderSelector selector)
    {
        defaultSource = ShaderSource.Of(selector);
        currentMaterialSetResolved = false;
    }

    /// <summary>
    /// Sets the extra shader properties baked into the materials of every
    /// object Load()'d/LoadGroup()'d from here on, until changed again. Pass
    /// null to stop applying any.
    /// </summary>
    public void SetProperties(MaterialProperties properties)
    {
        currentProperties = properties;
        currentMaterialSetResolved = false;
    }

    /// <summary>
    /// Sets one fixed shader for every object Load()'d/LoadGroup()'d from here
    /// on, until changed again. Pass null to go back to the default source.
    /// Replaces any selector previously set - whichever of these two was
    /// called last wins.
    /// </summary>
    public void SetShader(Shader shader)
    {
        currentSource = ShaderSource.Of(shader);
        currentMaterialSetResolved = false;
    }

    /// <summary>
    /// As SetShader, but the shader is chosen per ShaderSet entry. Pass null
    /// to go back to the default source.
    /// </summary>
    public void SetShaderSelector(ShaderSelector selector)
    {
        currentSource = ShaderSource.Of(selector);
        currentMaterialSetResolved = false;
    }

    public void SetFlipXZ(bool flip)
    {
        flipXZ = flip;
    }

    public Mesh Load(string name, string pivotPart = null)
    {
        var mesh = ReadMesh(name, out var materialMap);
        if (mesh == null) return null;

        pending.Add(new PendingEntry
        {
            IsGroup = false,
            Name = name,
            Mesh = mesh,
            MaterialMap = materialMap,
            MaterialSet = ResolveMaterialSet(),
            PivotPart = pivotPart,
        });

        return mesh;
    }

    /// <summary>
    /// Scans forward from the package file's current position for a group's
    /// main LOD chain, which is stored with no name prefix at all - just raw
    /// "H"/"M"/"L"/"VL" entries. Matches lvlFixedAny::Init's
    /// BeginGeom(this, a2, 0, v9) call, where the null name means "no prefix"
    /// rather than "no object."
    /// </summary>
    public void LoadMainGroup(string groupName = "main", bool applyPivot = false)
    {
        var levels = new List<PendingLevel>();

        while (file.CurrentFileName != null &&
               Array.IndexOf(MainGroupSuffixes, file.CurrentFileName.ToUpperInvariant()) >= 0)
        {
            string fileName = file.CurrentFileName;
            var mesh = ReadMesh(fileName, out var materialMap);

            if (mesh != null)
            {
                levels.Add(new PendingLevel
                {
                    Suffix = fileName.ToUpperInvariant(),
                    Mesh = mesh,
                    MaterialMap = materialMap,
                });
            }
        }

        QueueGroup(groupName, levels, applyPivot);
    }

    /// <summary>
    /// Scans forward from the package file's current position, picking up
    /// every entry named "{part}_H", "{part}_M", "{part}_L", "{part}_VL" (in
    /// whatever order they appear), and stops as soon as an entry without
    /// that prefix is encountered. Queues whichever levels were found as a
    /// single root GameObject with a LODGroup, pivoted on <paramref name="part"/>
    /// unless applyPivot is false.
    ///
    /// Reads are positional, so call order must match the package's layout.
    /// </summary>
    public void LoadGroup(string part, bool applyPivot = true)
    {
        var levels = new List<PendingLevel>();
        string prefix = part + "_";

        while (file.CurrentFileName != null &&
               file.CurrentFileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            string fileName = file.CurrentFileName;
            var mesh = ReadMesh(fileName, out var materialMap);

            if (mesh != null)
            {
                levels.Add(new PendingLevel
                {
                    Suffix = fileName.Substring(prefix.Length),
                    Mesh = mesh,
                    MaterialMap = materialMap,
                });
            }
        }

        QueueGroup(part, levels, applyPivot);
    }

    /// <summary>
    /// LoadGroup for a run of parts that share the same shader source,
    /// properties and pivot rule. Order still matters - the parts are read in
    /// the order given.
    /// </summary>
    public void LoadGroups(IEnumerable<string> parts, bool applyPivot = true)
    {
        foreach (var part in parts)
            LoadGroup(part, applyPivot);
    }

    /// <summary>
    /// Reads a shader-set entry out of the package file (typically "shaders",
    /// found after all the mesh entries) and stores it in Shaders. Materials
    /// are built lazily by the template, so the ShaderSet may still be edited
    /// after this returns and before anything is instantiated.
    /// </summary>
    public ShaderSet LoadShaders(string name = "shaders")
    {
        file.SkipTo(name);
        var reader = file.OpenFile(name);
        Shaders = new ShaderSet();
        Shaders.LoadSafe(reader);
        file.CloseFile();
        return Shaders;
    }

    public XrefList LoadXrefs(string name = "xrefs")
    {
        if(file.SkipTo(name))
        {
            var reader = file.OpenFile(name);
            Xrefs = new XrefList();
            Xrefs.Read(reader);
            file.CloseFile();
        }
        return Xrefs;
    }

    /// <summary>
    /// Freezes everything queued via Load()/LoadGroup()/LoadMainGroup() into a
    /// reusable PackageObjectTemplate: resolves pivots and LOD sizes now, so
    /// Instantiate() never touches AssetManager, MatrixFile or the package
    /// file again. Meshes are shared, not copied. Clears the pending queue,
    /// so the loader can be reused for a second template if needed.
    /// </summary>
    public PackageObjectTemplate ExportTemplate()
    {
        var entries = new List<PackageObjectTemplate.EntryData>(pending.Count);

        foreach (var item in pending)
        {
            var entry = new PackageObjectTemplate.EntryData
            {
                IsGroup = item.IsGroup,
                Name = item.Name,
                Mesh = item.Mesh,
                MaterialMap = item.MaterialMap,
                MaterialSet = item.MaterialSet,
                PivotPosition = string.IsNullOrEmpty(item.PivotPart)
                    ? (Vector3?)null
                    : GetPivot(basename, item.PivotPart, flipXZ),
            };

            if (item.IsGroup)
            {
                entry.ObjectSize = MeasureObjectSize(item.Levels[0].Mesh);
                entry.Levels = item.Levels.ConvertAll(level => new PackageObjectTemplate.LevelData
                {
                    Suffix = level.Suffix,
                    Mesh = level.Mesh,
                    MaterialMap = level.MaterialMap,
                    LodDistance = DistanceForSuffix(level.Suffix),
                });
            }

            entries.Add(entry);
        }

        var template = new PackageObjectTemplate(
            basename,
            entries,
            new List<PackageObjectTemplate.MaterialSet>(materialSets),
            Shaders,
            Xrefs);

        pending.Clear();
        return template;
    }

    private Mesh ReadMesh(string fileName, out int[] materialMap)
    {
        var entry = file.OpenFile(fileName); // mismatched name = content-authoring error, same as original
        var loader = new PackageModelLoader(fileName, entry);
        var mesh = loader.Load(out materialMap);

        if (flipXZ) mesh.FlipXZ();

        file.CloseFile();
        return mesh;
    }

    private void QueueGroup(string groupName, List<PendingLevel> levels, bool applyPivot)
    {
        if (levels.Count == 0) return; // optional part simply absent for this object - not an error

        pending.Add(new PendingEntry
        {
            IsGroup = true,
            Name = groupName,
            Levels = levels,
            MaterialSet = ResolveMaterialSet(),
            PivotPart = applyPivot ? groupName : null,
        });
    }

    /// <summary>
    /// The index of the (shader source, properties) pair currently in force,
    /// adding it to the template's material-set list the first time it's seen.
    /// One entry here means one Material[] in the finished template, per
    /// variant - so parts sharing a source and property set share materials,
    /// and parts using the same source with different properties correctly get
    /// their own.
    /// </summary>
    private int ResolveMaterialSet()
    {
        if (currentMaterialSetResolved) return currentMaterialSet;

        var source = currentSource.IsSet ? currentSource : defaultSource;

        currentMaterialSet = source.IsSet
            ? FindOrAddMaterialSet(source, currentProperties, currentLightEnable)
            : PackageObjectTemplate.NoMaterialSet;

        currentMaterialSetResolved = true;
        return currentMaterialSet;
    }

    private int FindOrAddMaterialSet(ShaderSource source, MaterialProperties properties, bool? lightEnable)
    {
        for (int i = 0; i < materialSets.Count; i++)
        {
            var set = materialSets[i];
            if (set.Source.Matches(source) &&
                ReferenceEquals(set.Properties, properties) &&
                set.LightEnable == lightEnable)
                return i;
        }

        materialSets.Add(new PackageObjectTemplate.MaterialSet
        {
            Source = source,
            Properties = properties,
            LightEnable = lightEnable,
        });

        return materialSets.Count - 1;
    }

    private static float MeasureObjectSize(Mesh referenceMesh)
    {
        if (referenceMesh == null) return 0f;
        referenceMesh.RecalculateBounds();
        return referenceMesh.bounds.size.magnitude;
    }

    private static Vector3 GetPivot(string basename, string part, bool flipXZ)
    {
        string fileName = $"{basename}_{part}.mtx";
        var matrixFile = new MatrixFile();

        using (var stream = AssetManager.Open("geometry", fileName))
        {
            matrixFile.Load(stream);
        }

        Vector3 pivot = matrixFile.Origin;

        if (flipXZ)
        {
            pivot.x = -pivot.x;
            pivot.z = -pivot.z;
        }

        return pivot;
    }
}