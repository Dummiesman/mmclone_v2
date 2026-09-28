using System;
using System.Collections.Generic;
using UnityEngine;
using static UnityEngine.ParticleSystem;

/// <summary>
/// A dynamic (rigidbody) banger piece, spawned when an UnhitBangerInstance breaks.
/// Geometry does NOT come from its own package - break parts are groups inside the
/// unhit banger's package (BREAK01, BREAK02, ...), so this instantiates the unhit's
/// template and shows exactly one part of it.
/// These are never created directly - HitBangerPool allocates and recycles them.
/// </summary>
public class HitBangerInstance : LevelInstance
{
    private static readonly Vector3 ParkPosition = new Vector3(0.0f, -10000.0f, 0.0f);

    private const float StandaloneLifetime = 20.0f;
    private const float DefaultMass = 10.0f;
    private const float LinearDamping = 0.05f;
    private const float AngularDamping = 0.35f;

    public struct SpawnRequest
    {
        public string Package;      // package to instantiate - the unhit banger's basename
        public string Part;         // part name within that package ("main", "BREAK01", ...)
        public string DataName;     // banger data entry used for mass / collision
        public Vector3 Position;    // world space, already CG adjusted by the caller
        public Quaternion Rotation;
        public Vector3 Scale;
        public int Variant;
        public float FallbackMass;  // used when DataName has no entry
    }

    private int variant;
    public int VariantCount { get; private set; }

    private string package;
    private string part;

    private PackageObjectInstance instance;
    private GameObject main;

    private int dataIndex = -1;

    private Rigidbody body;
    private BoxCollider box;
    private CapsuleCollider capsule;
    private SphereCollider sphere;
    private MeshCollider meshCollider;
    private ColliderIdOverride colliderId;

    private ParticleSim particles;
    private bool hasParticles;

    private List<Renderer> renderers = new List<Renderer>();

    private HitBangerPool pool;
    private float expireTime;
    private bool spawned;

    public Rigidbody Body => body;
    public string Package => package;
    public string Part => part;
    public bool IsSpawned => spawned;
    public float ExpireTime => expireTime;

    /// <summary>
    /// Setup for bangers whose geometry already exists and isn't pooled - vehicle
    /// breakables are Instantiate()d off the car, not built from a package template.
    /// </summary>
    public void AllocateStandalone()
    {
        Allocate(null);
    }

    /// <summary>
    /// One-time setup done by the pool. No geometry and no banger data yet - this only
    /// builds the component set so that Spawn never has to AddComponent.
    /// </summary>
    public void Allocate(HitBangerPool owner)
    {
        pool = owner;

        body = gameObject.GetComponent<Rigidbody>();
        if (body == null)
            body = gameObject.AddComponent<Rigidbody>();

        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        body.mass = DefaultMass;
        SetDamping(LinearDamping, AngularDamping);
        SetKinematic(true);

        // all three primitives live on the object permanently, only one is ever enabled
        box = gameObject.AddComponent<BoxCollider>();
        capsule = gameObject.AddComponent<CapsuleCollider>();
        sphere = gameObject.AddComponent<SphereCollider>();
        meshCollider = gameObject.AddComponent<MeshCollider>();
        meshCollider.convex = true;
        meshCollider.enabled = false;

        box.enabled = capsule.enabled = sphere.enabled = meshCollider.enabled = false;
        colliderId = gameObject.AddComponent<ColliderIdOverride>();

        var ptxObj = new GameObject("Ptx");
        ptxObj.transform.SetParent(transform, false);
        particles = ptxObj.AddComponent<ParticleSim>();
        particles.EmitOverTime = false;
        ptxObj.SetActive(false);

        gameObject.SetActive(false);
        this.gameObject.SetLayer(LayerMask.NameToLayer("Banger"), true);
    }

    /// <summary>
    /// Bring this banger to life. Returns false if the package or the part is missing,
    /// in which case the caller should put it straight back in the pool.
    /// </summary>
    public bool Spawn(SDLCity level, in SpawnRequest request)
    {
        Init(level, string.IsNullOrEmpty(request.DataName) ? request.Package : request.DataName);
        Flags &= ~LevelInstanceFlags.Static;

        dataIndex = string.IsNullOrEmpty(request.DataName)
            ? -1
            : level.BangerDataManager.AddEntry(request.DataName);

        // recycling onto the same package is the common case - a wall of identical cones, or
        // the sibling parts of the banger we just broke. Only the visible part changes.
        if (instance == null || !string.Equals(package, request.Package, StringComparison.OrdinalIgnoreCase))
        {
            ReleaseGeometry();

            if (!Load(level, request.Package))
                return false;
        }

        if (!SetActivePart(request.Part))
        {
            Debug.LogError($"HitBangerInstance: package '{request.Package}' has no part '{request.Part}'.");
            return false;
        }

        part = request.Part;
        SetVariant(request.Variant);

        transform.SetPositionAndRotation(request.Position, request.Rotation);
        transform.localScale = request.Scale;

        gameObject.SetActive(true);

        // renderer bounds are world space, so the collider fit has to happen after the
        // transform is final and the part is active
        ApplyPhysics(level, request.FallbackMass);

        SetKinematic(false);
        SetVelocity(Vector3.zero, Vector3.zero);
        body.WakeUp();

        spawned = true;
        expireTime = Time.time + (pool != null ? pool.Lifetime : 20.0f);

        return true;
    }

    /// <summary>
    /// Spawn using the geometry already parented to this object, with a banger data index
    /// resolved by the caller. Pivot/CG handling is the caller's job here.
    /// </summary>
    public bool SpawnDetached(SDLCity level, int dataIndex, Vector3 position,
                              Quaternion rotation, Vector3 scale, float fallbackMass)
    {
        Init(level, $"vehbreak{dataIndex}");
        Flags &= ~LevelInstanceFlags.Static;

        this.dataIndex = dataIndex;

        if (main == null)
        {
            main = gameObject;
            renderers.Clear();

            // skip the emitter's own renderer, it isn't part of the art
            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                if (particles == null || !r.transform.IsChildOf(particles.transform))
                    renderers.Add(r);
            }
        }

        transform.SetPositionAndRotation(position, rotation);
        transform.localScale = scale;

        gameObject.SetActive(true);

        ApplyPhysics(level, fallbackMass);

        SetKinematic(false);
        SetVelocity(Vector3.zero, Vector3.zero);
        body.WakeUp();

        spawned = true;
        expireTime = Time.time + (pool != null ? pool.Lifetime : StandaloneLifetime);

        return true;
    }

    /// <summary>
    /// Park this banger. Geometry is deliberately kept so a reuse of the same package is free.
    /// </summary>
    public void Despawn()
    {
        if (!spawned)
            return;

        spawned = false;

        SetVelocity(Vector3.zero, Vector3.zero);
        SetKinematic(true);
        StopParticles();

        transform.SetPositionAndRotation(ParkPosition, Quaternion.identity);

        gameObject.SetActive(false);
    }

    public void Launch(Vector3 velocity, Vector3 spin)
    {
        SetVelocity(velocity, spin);
    }

    private bool Load(SDLCity level, string name)
    {
        package = name;

        // in practice the unhit banger built this at load time and we just take the cache
        // hit. The builder is only here for the case where nothing has loaded it yet.
        var template = GetOrBuildTemplate(name, loader =>
            UnhitBangerInstance.BuildDefaultUnhitBangerTemplate(loader, level, level.BangerDataManager.AddEntry(name)));

        if (template == null)
        {
            Debug.LogError($"HitBangerInstance: template '{name}' failed to build.");
            package = null;
            return false;
        }

        instance = template.Instantiate(transform);
        VariantCount = template.Shaders.VariantCount;
        return true;
    }

    /// <summary>
    /// Shows one group out of the package and hides everything else - the main body, the
    /// shadow and the sibling break parts all live in the same instance.
    /// </summary>
    private bool SetActivePart(string name)
    {
        main = null;

        foreach (var p in instance.Parts)
        {
            bool wanted = string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase);

            if (p.Object != null)
                p.Object.SetActive(wanted);

            if (wanted)
                main = p.Object;
        }

        renderers.Clear();

        if (main == null)
            return false;

        renderers.AddRange(main.GetComponentsInChildren<Renderer>(true));
        return true;
    }

    private void ApplyParticles(BangerData data)
    {
        if (data.TexNumber <= 0 || data.BirthRule == null)
        {
            StopParticles();
            return;
        }

        int sheet = Mathf.Clamp(data.TexNumber, 1, 21);

        particles.gameObject.SetActive(true);
        particles.BirthRule = data.BirthRule;
        particles.Init();
        particles.SetTextureSheet($"fxpt{sheet}");

        particles.transform.localPosition = Vector3.zero;
        particles.transform.localRotation = Quaternion.identity;

        particles.Emit(data.BirthRule.InitialBlast);
        particles.EmitOverTime = true;

        hasParticles = true;
    }

    private void StopParticles()
    {
        if (!hasParticles)
            return;

        particles.EmitOverTime = false;
        particles.gameObject.SetActive(false);
        hasParticles = false;
    }

    private void ApplyPhysics(SDLCity level, float fallbackMass)
    {
        meshCollider.enabled = box.enabled = capsule.enabled = sphere.enabled = false;

        bool fitted = false;

        if (dataIndex < 0)
        {
            // no banger data for this part - approximate everything from the art
            fitted = FitBoxToRenderers();
            body.mass = fallbackMass > 0.0f ? fallbackMass : DefaultMass;
            body.ResetCenterOfMass();
            SetDamping(LinearDamping, AngularDamping);
            StopParticles();
            return;
        }

        var data = level.BangerDataManager.GetEntry(dataIndex);

        switch (data.CollisionPrim)
        {
            case BangerCollisionPrimitive.Capsule:
                capsule.center = Vector3.zero;
                capsule.height = data.Size.y;
                capsule.radius = data.YRadius;
                capsule.enabled = true;
                break;

            case BangerCollisionPrimitive.Sphere:
                sphere.center = Vector3.zero;
                sphere.radius = data.YRadius;
                sphere.enabled = true;
                break;

            case BangerCollisionPrimitive.Mesh:
                if (data.CollisionMesh != null)
                {
                    // skip the reassign when a pool slot is reused for the same part - it re-cooks
                    if (meshCollider.sharedMesh != data.CollisionMesh)
                        meshCollider.sharedMesh = data.CollisionMesh;

                    meshCollider.convex = true;
                    meshCollider.enabled = true;
                }
                else
                {
                    fitted = FitBoxToRenderers();
                }
                break;

            default:
                // Box. A concave mesh can't be dynamic, so anything without a usable
                // size falls back to a fitted box too.
                if (IsUsableSize(data.Size))
                {
                    box.center = Vector3.zero;
                    box.size = data.Size;
                    box.enabled = true;
                }
                else
                {
                    fitted = FitBoxToRenderers();
                }
                break;
        }

        colliderId.ColliderID = data.ColliderId;
        ApplyParticles(data);

        float mass = data.Mass;
        if (mass <= 0.0f)
            mass = fallbackMass > 0.0f ? fallbackMass : DefaultMass;

        // the transform origin IS the CG - primitives sit at zero and the mesh was baked
        // to -CG, so don't let Unity derive a center of mass from the hull. A fitted box
        // is measured from the art instead, so let Unity work that one out.
        if (fitted)
            body.ResetCenterOfMass();
        else
            body.centerOfMass = Vector3.zero;

        body.mass = mass;
        SetDamping(LinearDamping, AngularDamping);
    }

    private static bool IsUsableSize(Vector3 size)
    {
        return size.x > 0.0f && size.y > 0.0f && size.z > 0.0f;
    }

    /// <summary>
    /// Break parts frequently have no usable collision entry, so measure the art instead.
    /// </summary>
    private bool FitBoxToRenderers()
    {
        Bounds bounds = default;
        bool any = false;

        for (int i = 0; i < renderers.Count; i++)
        {
            if (renderers[i] == null)
                continue;

            if (!any)
            {
                bounds = renderers[i].bounds;
                any = true;
            }
            else
            {
                bounds.Encapsulate(renderers[i].bounds);
            }
        }

        if (!any)
        {
            box.center = Vector3.zero;
            box.size = Vector3.one * 0.5f;
            box.enabled = true;
            return true;
        }

        Vector3 size = transform.InverseTransformVector(bounds.size);
        size = new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z));

        box.center = transform.InverseTransformPoint(bounds.center);
        box.size = Vector3.Max(size, Vector3.one * 0.05f);
        box.enabled = true;
        return true;
    }

    private void ReleaseGeometry()
    {
        instance?.Destroy();
        instance = null;
        VariantCount = 0;

        main = null;
        package = null;
        part = null;

        renderers.Clear();
    }

    public override void Reset()
    {
        base.Reset();

        if (spawned)
            pool?.Release(this);
    }

    private void Update()
    {
        if (Level != null && spawned)
        {
            int curRoom = Level.FindRoomIdWithWarpsCheckMiss(this.transform.position, RoomID);
            if (curRoom != RoomID)
            {
                Level.MoveToRoom(this, curRoom);
            }
        }

        if (!spawned || !hasParticles)
            return;

        // stop feeding the emitter once the piece settles, but leave the sim alive so
        // the existing particles finish their lifetime
        bool awake = !body.IsSleeping();

        if (particles.EmitOverTime != awake)
            particles.EmitOverTime = awake;
        if (transform.position.y < -100.0f)
        {
            if (pool != null) pool.Release(this);
            else Despawn();
            return;
        }
    }

    public override IEnumerable<Renderer> GetRenderers()
    {
        return renderers;
    }

    public override void SetVariant(int index)
    {
        variant = index;
        instance?.SetVariant(index);
    }

    private void OnDestroy()
    {
        ReleaseGeometry();
    }


    // --- rigidbody API shims (Unity 6 renamed these) ---

    public void SetVelocity(Vector3 linear, Vector3 angular)
    {
#if UNITY_6000_0_OR_NEWER
        body.linearVelocity = linear;
#else
        body.velocity = linear;
#endif
        body.angularVelocity = angular;
    }

    public static Vector3 GetVelocity(Rigidbody body)
    {
#if UNITY_6000_0_OR_NEWER
        return body.linearVelocity;
#else
        return body.velocity;
#endif
    }

    private void SetDamping(float linear, float angular)
    {
#if UNITY_6000_0_OR_NEWER
        body.linearDamping = linear;
        body.angularDamping = angular;
#else
        body.drag = linear;
        body.angularDrag = angular;
#endif
    }

    private void SetKinematic(bool value)
    {
        body.isKinematic = value;
        body.detectCollisions = !value;
    }
}