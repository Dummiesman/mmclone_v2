using System;
using System.Collections.Generic;
using UnityEngine;

[System.Flags]
public enum LevelInstanceFlags
{
    None = 0,
    Static = 1 << 0, // This instance will always stay in the room it is first assigned to. These are also reparented.
    DisableWhenRoomHidden = 1 << 1, // disable this entity (.enabled = false) when a room is hidden
    DisableCulling = 1 << 2,
}

public class LevelInstance : MonoBehaviour
{
    public SDLCity Level { get; private set; }
    public int RoomID { get; private set; }
    public LevelInstanceFlags Flags { get; set; }

    public virtual void MoveToRoom(int newRoom)
    {
        int old = RoomID;
        RoomID = newRoom;
        OnMovedRoom(old, newRoom);
    }

    public virtual void Init(SDLCity level, string name) 
    {
        Level = level;
    }

    public virtual void SetVariant(int variant) { }
    public virtual void OnMovedRoom(int oldRoom, int newRoom) { }
    public virtual void Reset() {}
    public virtual IEnumerable<Renderer> GetRenderers() { return Array.Empty<Renderer>(); }

    // Shared helpers that are used on multiple instance types
    private const float shadowProbeDown = 5.0f;
    private const float shadowMinNormalY = 0.7f;
    private const float shadowProbeUp = 1.0f;
    private static readonly RaycastHit[] shadowHits = new RaycastHit[8];

    protected bool ProbeGround(Vector3 origin, out Vector3 point, out Vector3 normal)
    {
        var ShadowGroundMask = LayerMask.GetMask("Default");

        point = Vector3.zero;
        normal = Vector3.up;

        int count = Physics.RaycastNonAlloc(
            origin, Vector3.down, shadowHits,
            shadowProbeUp + shadowProbeDown,
            ShadowGroundMask, QueryTriggerInteraction.Ignore);

        float best = float.MaxValue;
        bool found = false;

        for (int i = 0; i < count; i++)
        {
            var hit = shadowHits[i];
            if (hit.collider == null) continue;
            if (hit.collider.transform.IsChildOf(transform)) continue;
            if (hit.distance >= best) continue;

            best = hit.distance;
            point = hit.point;
            normal = hit.normal;
            found = true;
        }

        return found && normal.y >= shadowMinNormalY;
    }

    // Template handling
    private static readonly Dictionary<string, PackageObjectTemplate> templates =
        new Dictionary<string, PackageObjectTemplate>();

    /// <summary>
    /// The already-built template for a geometry package, or null if nothing
    /// has built it yet. Use GetOrBuildTemplate when the caller is also the one
    /// that knows how to build it.
    /// </summary>
    protected static PackageObjectTemplate GetTemplate(string basename)
    {
        if (templates.TryGetValue(basename, out var template))
            return template;

        Debug.LogError($"LevelInstance: template '{basename}' was never built.");
        return null;
    }

    /// <summary>
    /// The shared template for a geometry package, building it on first
    /// request and returning the cached one after that. The build callback
    /// runs exactly once per basename, gets a loader already pointed at the
    /// open package, and is responsible only for saying which parts, shaders
    /// and property overrides the object wants - the package file is opened,
    /// exported and disposed here.
    ///
    /// Returns null if the package could not be opened.
    /// </summary>
    protected static PackageObjectTemplate GetOrBuildTemplate(string basename, Action<PackageObjectLoader> build)
    {
        if (templates.TryGetValue(basename, out var cached))
            return cached;

        var stream = AssetManager.Open("geometry", $"{basename}.pkg");
        if (stream == null)
        {
            Debug.LogError($"LevelInstance: could not open geometry package '{basename}.pkg'.");
            return null;
        }

        PackageObjectTemplate template;
        using (var package = new PackageFile(stream))
        {
            var loader = new PackageObjectLoader(basename, package);
            build(loader);
            template = loader.ExportTemplate();
        }

        templates[basename] = template;
        return template;
    }

    /// <summary>
    /// Instantiates an already-built template. Use GetOrBuildTemplate when the
    /// caller is also the one that knows how to build it.
    /// </summary>
    protected static PackageObjectInstance InstantiateTemplate(string basename, Transform parent, int variant = 0)
    {
        if (!templates.TryGetValue(basename, out var template))
        {
            Debug.LogError($"LevelInstance: template '{basename}' was never built.");
            return null;
        }
        return template.Instantiate(parent, variant);
    }

    /// <summary>
    /// Drops every cached template. Call on level unload, once all instances
    /// built from them are gone - destroyAssets also releases the shared
    /// meshes and materials, which any surviving instance is still pointing at.
    /// </summary>
    public static void ClearTemplateCache(bool destroyAssets = true)
    {
        if (destroyAssets)
        {
            foreach (var template in templates.Values)
                template.DestroyAssets();
        }

        templates.Clear();
    }
}