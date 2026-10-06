using System;
using System.Collections.Generic;
using UnityEngine;

public class UnhitBangerInstance : LevelInstance
{
    // tuning flags
    public bool Unbreakable = false;

    // public fields
    public bool Broken => broken;
    public int DataIndex => dataIndex;
    public BangerData Data => (DataIndex < 0) ? null : Level.BangerDataManager.GetEntry(DataIndex);

    // variant
    public int VariantCount { get; private set; }
    private int variant;

    // virtual properties
    /// <summary>Group that represents the whole body. Subclasses with differently
    /// named art override this; it is also the part spawned when NumParts == 0.</summary>
    protected virtual string MainPartName => MainPart;

    // collision
    protected Collider Collider => collider;
    private Collider collider;

    // ref counter for glow
    static int numUnhitBangers = 0;
    static Texture2D glowTexture;

    // constants
    private const float ImpulseLimitMultiplier = 1.01f; // just greater than required
    private const float UnbreakableImpulse = 1.0e+10f;

    private const string MainPart = "main";
    private const string ShadowPart = "shadow";

    // break geometry lives in the banger's own package as extra groups, BREAK01 upward
    private const string BreakGroupFormat = "BREAK{0:00}";

    // banger data entry for a break part, if one exists
    private const string BreakDataFormat = "_break{0:00}";

    // how the impact gets turned into part velocities
    private const float CarryFactor = 0.55f;    // how much of the hitter's velocity the parts inherit
    private const float ScatterFactor = 0.35f;  // how hard parts are pushed away from the contact
    private const float JitterSpeed = 1.0f;
    private const float LiftSpeed = 1.5f;
    private const float SpinFactor = 0.8f;
    private const float MinLaunchSpeed = 2.0f;
    private const float MaxLaunchSpeed = 25.0f;

    private static readonly ShaderSelector WorldShaderFor =
        (entry, mainTex) => WorldShaderVariants.Select(entry.Diffuse, mainTex);

    private sealed class BreakSet
    {
        public string[] Groups;      // BREAK01, BREAK02, ...
        public string[] DataNames;   // basename_break01, ... (null when there is no entry)
        public int[] DataIndices;
    }

    // break parts are per basename, not per instance - hundreds of instances share one set
    private static readonly Dictionary<string, BreakSet> BreakSets =
        new Dictionary<string, BreakSet>(StringComparer.OrdinalIgnoreCase);

    private string basename;
    private PackageObjectInstance instance;

    private GameObject main;
    private GameObject shadow;

    private int dataIndex = -1;
    private bool broken;

    private Vector3 centerOfGravity;

    private List<Renderer> renderers = new List<Renderer>();

    private Collider breakCollider;
    private int breakColliderId;
    private float breakImpulse = float.PositiveInfinity;

    /// <summary>
    /// Where the art sits, before CG was folded into the transform.
    /// </summary>
    private Vector3 WorldOrigin => transform.position - (transform.rotation * centerOfGravity);


    public static UnhitBangerInstance RequestBanger(SDLCity level, string propType, Vector3 propPos, Quaternion propRot)
    {
        var built = new GameObject(propType);
        var inst = built.AddComponent<UnhitBangerInstance>();
        inst.Init(level, propType, propPos, propRot, Vector3.one);
        return inst;
    }

    private static void InitGlow()
    {
        glowTexture = TextureCache.Get("s_yel_glow");
    }

    private void InitCollider(BangerData data)
    {
        Collider col = null;
        breakImpulse = data.ImpulseLimit;
        bool overallUnbreakable = breakImpulse >= UnbreakableImpulse || Unbreakable;

        switch (data.CollisionPrim)
        {
            case BangerCollisionPrimitive.Box:
                var box = gameObject.AddComponent<BoxCollider>();
                box.size = data.Size;
                col = box;
                break;
            case BangerCollisionPrimitive.Capsule:
                var cap = gameObject.AddComponent<CapsuleCollider>();
                cap.height = data.Size.y;
                cap.radius = data.YRadius;
                col = cap;
                break;
            case BangerCollisionPrimitive.Sphere:
                var sph = gameObject.AddComponent<SphereCollider>();
                sph.radius = data.YRadius;
                col = sph;
                break;
            case BangerCollisionPrimitive.Mesh:
                if (data.CollisionMesh != null)
                {
                    var mc = gameObject.AddComponent<MeshCollider>();
                    mc.sharedMesh = data.CollisionMesh;
                    mc.convex = !overallUnbreakable; // convex makes breaking more reliable
                    col = mc;
                }
                break;
        }

        if (col != null && !overallUnbreakable)
        {
            col.hasModifiableContacts = true;
            breakCollider = col;
            breakColliderId = col.GetInstanceID();
            BangerContactModifier.Register(breakColliderId, breakImpulse * ImpulseLimitMultiplier);
        }

        gameObject.AddComponent<ColliderIdOverride>().ColliderID = data.ColliderId;
        this.collider = col;
    }

    public virtual void Init(SDLCity level, string basename, Vector3 position, Quaternion rotation, Vector3 scale)
    {
        Flags |= LevelInstanceFlags.Static | LevelInstanceFlags.DisableWhenRoomHidden;
        base.Init(level, basename);

        dataIndex = level.BangerDataManager.AddEntry(basename);
        var data = dataIndex >= 0 ? level.BangerDataManager.GetEntry(dataIndex) : null;

        if (data != null)
        {
            centerOfGravity = data.CG;
            position += rotation * data.CG;
        }

        // place first, so neither the parts nor the collider get moved after creation
        transform.SetLocalPositionAndRotation(position, rotation);
        transform.localScale = scale;

        Load(basename);

        if (data != null)
        {
            InitCollider(data);
        }

        renderers.AddRange(this.gameObject.GetComponentsInChildren<Renderer>(true));
        GetBreakSet();

        string layer = "Banger";
        if(data != null && data.CollisionType.HasFlag(BangerCollisionType.CollideWithWheels))
        {
            layer = "BangerStatic";
        }
        this.gameObject.SetLayer(LayerMask.NameToLayer(layer), true);
    }

    private void Load(string basename)
    {
        this.basename = basename;

        var template = GetOrBuildTemplate(basename, BuildTemplate);
        if (template == null)
        {
            Debug.LogError($"UnhitBangerInstance: template '{basename}' failed to build.");
            return;
        }

        // the template holds every group (HitBangerInstance needs the break parts), but an
        // unhit banger only ever shows its main body - don't build objects just to hide them
        instance = template.Instantiate(transform, 0, ShouldInstantiatePart);
        VariantCount = template.Shaders.VariantCount;
        AssignNamedParts(instance);
    }

    /// <summary>
    /// Which template parts get GameObjects on this instance. Anything left out costs nothing.
    /// Subclasses that turn on extra groups at runtime override this to include them.
    /// </summary>
    protected virtual bool ShouldInstantiatePart(string partName)
    {
        return partName == MainPartName;
    }

    protected virtual void BuildTemplate(PackageObjectLoader loader)
    {
        BuildDefaultUnhitBangerTemplate(loader, this.Level, dataIndex, LoadExtraGroups);
    }

    /// <summary>
    /// Subclasses load any additional groups here. They are loaded after the break groups
    /// and before shaders are resolved, and start hidden like every other non-main part.
    /// Remember to include them in ShouldInstantiatePart if this instance needs them.
    /// </summary>
    protected virtual void LoadExtraGroups(PackageObjectLoader loader)
    {
    }

    /// <summary>
    /// Builds the whole banger package: main body, shadow, and every BREAKXX group. The
    /// template holds all of them - HitBangerInstance shares it and instantiates one break
    /// group, while UnhitBangerInstance only instantiates the main body.
    /// </summary>
    public static void BuildDefaultUnhitBangerTemplate(PackageObjectLoader loader, SDLCity level, int dataIndex,
                                                       Action<PackageObjectLoader> loadExtraGroups = null)
    {
        int numParts = 0;

        if (dataIndex >= 0)
        {
            var data = level.BangerDataManager.GetEntry(dataIndex);
            if (data.BillFlags.HasFlag(BangerDataFlags.Unlit))
            {
                loader.SetLightEnable(false);
            }

            numParts = data.NumParts;
        }

        loader.SetDefaultShaderSelector(WorldShaderFor);

        loader.LoadMainGroup(MainPart, applyPivot: false);
        loader.LoadGroup(ShadowPart, applyPivot: false);

        for (int i = 0; i < numParts; i++)
        {
            loader.LoadGroup(BreakGroupName(i), applyPivot: false);
        }

        loadExtraGroups?.Invoke(loader);

        loader.LoadShaders();
    }

    private static string BreakGroupName(int part)
    {
        return string.Format(BreakGroupFormat, part + 1);
    }

    private BreakSet GetBreakSet()
    {
        if (string.IsNullOrEmpty(basename))
            return null;

        if (BreakSets.TryGetValue(basename, out var set))
            return set;

        var level = this.Level;

        int count = 0;
        if (dataIndex >= 0)
            count = level.BangerDataManager.GetEntry(dataIndex).NumParts;

        set = new BreakSet
        {
            Groups = new string[count],
            DataNames = new string[count],
            DataIndices = new int[count],
        };

        for (int i = 0; i < count; i++)
        {
            string dataName = basename + string.Format(BreakDataFormat, i + 1);
            int index = level.BangerDataManager.AddEntry(dataName);

            set.Groups[i] = BreakGroupName(i);
            set.DataNames[i] = index >= 0 ? dataName : null;
            set.DataIndices[i] = index;
        }

        BreakSets[basename] = set;
        return set;
    }

    /// <summary>
    /// Call on level unload - these indices don't outlive the BangerDataManager.
    /// </summary>
    public static void ClearBreakCache()
    {
        BreakSets.Clear();
    }

    protected virtual void AssignNamedParts(PackageObjectInstance instance)
    {
        foreach (var part in instance.Parts)
        {
            if (part.Name == MainPartName) main = part.Object;
            else if (part.Name == ShadowPart) shadow = part.Object;

            if (part.Name != MainPartName && part.Object != null)
                part.Object.SetActive(false);
        }
    }

    public override void SetVariant(int index)
    {
        variant = index;
        instance?.SetVariant(index);
    }

    public override void Reset()
    {
        base.Reset();
        broken = false;

        bool wasActive = this.gameObject.activeSelf;
        this.gameObject.SetActive(true); // reappear

        if (breakCollider != null && breakColliderId != 0 && !wasActive)
        {
            breakCollider.hasModifiableContacts = true; // even though its still true post-activation, it doesn't work unless set again
            BangerContactModifier.Register(breakColliderId, breakImpulse * ImpulseLimitMultiplier);
        }
    }


    private void OnCollisionEnter(Collision collision)
    {
        if (Unbreakable) return;
        if (broken || breakImpulse >= UnbreakableImpulse) return;
        if (collision.impulse.magnitude < breakImpulse * 0.99f) return;

        Break(collision);
    }

    private void Break(Collision collision)
    {
        broken = true;

        Vector3 hitPoint = collision.contactCount > 0
            ? collision.GetContact(0).point
            : transform.position;

        // relativeVelocity is sign-stable, the other body's velocity is more accurate when we have it
        Vector3 hitVelocity = collision.rigidbody != null
            ? HitBangerInstance.GetVelocity(collision.rigidbody)
            : -collision.relativeVelocity;

        SpawnParts(hitPoint, hitVelocity);

        this.gameObject.SetActive(false);
    }

    /// <summary>
    /// Parts are placed by pushing their CG through our own transform, so they inherit our
    /// rotation and scale and land exactly where the art they replace was standing.
    /// </summary>
    private Vector3 PartPosition(Vector3 cg)
    {
        return WorldOrigin + (transform.rotation * Vector3.Scale(transform.localScale, cg));
    }

    private void SpawnParts(Vector3 hitPoint, Vector3 hitVelocity)
    {
        var pool = HitBangerPool.Current;
        if (pool == null)
            return;

        var set = GetBreakSet();
        if (set == null)
            return;

        var level = this.Level;

        float totalMass = dataIndex >= 0 ? level.BangerDataManager.GetEntry(dataIndex).Mass : 0.0f;

        var request = new HitBangerInstance.SpawnRequest
        {
            Package = basename,
            Rotation = transform.rotation,
            Scale = transform.localScale,
            Variant = variant,
        };

        // no break parts authored - the whole banger becomes one loose object
        if (set.Groups.Length == 0)
        {
            request.Part = MainPartName;
            request.DataName = basename;
            request.Position = PartPosition(centerOfGravity);
            request.FallbackMass = totalMass;

            Launch(pool.Request(request), hitPoint, hitVelocity);
            return;
        }

        float shareMass = totalMass / set.Groups.Length;

        for (int i = 0; i < set.Groups.Length; i++)
        {
            int index = set.DataIndices[i];
            Vector3 cg = index >= 0 ? level.BangerDataManager.GetEntry(index).CG : centerOfGravity;

            request.Part = set.Groups[i];
            request.DataName = set.DataNames[i];
            request.Position = PartPosition(cg);
            request.FallbackMass = shareMass;

            var banger = pool.Request(request);
            if (banger == null)
                continue;

            Launch(banger, hitPoint, hitVelocity);
        }
    }

    private static void Launch(HitBangerInstance banger, Vector3 hitPoint, Vector3 hitVelocity)
    {
        if (banger == null)
            return;

        Vector3 away = banger.transform.position - hitPoint;
        away.y += 0.25f;

        if (away.sqrMagnitude < 1.0e-4f)
            away = UnityEngine.Random.onUnitSphere;

        away.Normalize();

        float speed = Mathf.Clamp(hitVelocity.magnitude, MinLaunchSpeed, MaxLaunchSpeed);

        Vector3 velocity = (hitVelocity * CarryFactor)
                         + (away * (speed * ScatterFactor))
                         + (UnityEngine.Random.insideUnitSphere * JitterSpeed);

        velocity.y += LiftSpeed;

        Vector3 spin = UnityEngine.Random.insideUnitSphere * (speed * SpinFactor);

        banger.Launch(Vector3.ClampMagnitude(velocity, MaxLaunchSpeed), spin);
    }

    public override IEnumerable<Renderer> GetRenderers()
    {
        return renderers;
    }

    protected virtual void Update()
    {
        if (GameState.SelectedTimeOfDay == MMTimeOfDay.Night && dataIndex >= 0 && glowTexture != null)
        {
            var data = Level.BangerDataManager.GetEntry(dataIndex);
            foreach (var glowOffset in data.GlowOffsets)
            {
                LightGlowRenderer.Draw(transform.TransformPoint(glowOffset), 1.5f, Color.white, glowTexture);
            }
        }
    }

    private void Awake()
    {
        numUnhitBangers++;
        if (numUnhitBangers == 1)
        {
            // first, init glow
            InitGlow();
        }
    }

    private void OnDestroy()
    {
        instance?.Destroy();
        instance = null;

        main = shadow = null;

        if (breakColliderId != 0)
        {
            BangerContactModifier.Unregister(breakColliderId);
            breakColliderId = 0;
        }

        numUnhitBangers--;
        if (numUnhitBangers == 0 && glowTexture != null)
        {
            UnityEngine.Object.Destroy(glowTexture);
        }
    }
}