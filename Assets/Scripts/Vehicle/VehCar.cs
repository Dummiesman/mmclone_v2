using UnityEngine;

public enum vehCarType
{
    Player,
    Opponent
}

/// <summary>vehCar::unknown236. Why the car is undrivable, and how hard the
/// driver is locked out.</summary>
public enum VehUndrivableMode
{
    /// <summary>Flag cleared, but no inputs are overridden.</summary>
    Passive = 0,

    /// <summary>Brakes held, transmission in neutral. Throttle revs the engine
    /// and steering still works; the car just can't move.</summary>
    HoldBrakes = 1,

    /// <summary>Driver fully locked out, gear left as it was.</summary>
    Locked = 2,

    /// <summary>Driver fully locked out and dropped into neutral, so the
    /// engine idles.</summary>
    LockedNeutral = 3,
}

[RequireComponent(typeof(Rigidbody))]
public class VehCar : MonoBehaviour
{
    public bool DrawGizmos = false;
    private const int FlagDrivable = 2;

    /// <summary>Below this, an input counts as not applied at all.</summary>
    private const float InputDeadzone = 0.0099999998f;

    // ------------------------------------------------------------ the sim
    public VehCarSim VehCarSim = new VehCarSim();

    /// <summary>Basename this car was initialised from, e.g. "vppanoz".</summary>
    public string Basename { get; private set; }
    public vehCarType Type { get; private set; }

    // ------------------------------------------------------- subsystems
    // All optional. Leave any of them null and that stage is skipped.
    // Gyro is created automatically by Init if the GameObject doesn't have one.
    public VehicleAudioContainer Audio;

    public VehGyro Gyro;
    public VehStuck Stuck;
    public VehSplash Splash;
    public VehWheelPtx WheelPtx;
    public VehCarDamage Damage;
    public IVehFeedback Feedback;
    public VehSiren Siren;
    public VehDebug Debug;

    public MMBound Bound;
    public VehicleModel Model;

    private SDLCity level;

    public readonly LvlTrackManager[] TrackManagers = new LvlTrackManager[4];

    // ------------------------------------------------------------- state
    public Rigidbody Body { get; private set; }

    /// <summary>vehCar::SomeFlags. Bit 1 (value 2) is "drivable".</summary>
    private int flags = FlagDrivable;

    /// <summary>vehCar::unknown236 - why the car is currently undrivable.</summary>
    public bool IsDrivable => (flags & FlagDrivable) != 0;
    public VehUndrivableMode UndrivableMode => undrivableMode;
    private VehUndrivableMode undrivableMode;

    /// <summary>PSDL room this car is currently in. Drives culling and the
    /// water-level query.</summary>
    public int CurrentRoom { get; private set; }

    //public bool IsInitialised => VehCarSim != null && VehCarSim.Initialised;

    // =====================================================================

    private void Awake()
    {
        // Only cache the body here. The sim is NOT initialised until Init() runs,
        // because Init has to happen after the car file and the wheel pivots are
        // loaded - see the note on ordering in VehCarSim.
        Body = GetComponent<Rigidbody>();
        //Body.collisionDetectionMode = CollisionDetectionMode.Continuous; // Use continuous so we don't get stuck in bangers
                                                                         // but not dynamic so they make their repeated sounds
    }

    private void InitBound(string name)
    {
        if (Bound != null) return;

        var boundObj = new GameObject("Bound");
        boundObj.transform.SetParent(this.transform);

        string vehicleLayer = (Type == vehCarType.Player) ? "PlayerVehicleBody" : "VehicleBody";
        int vehicleLayerIndex = LayerMask.NameToLayer(vehicleLayer);
        boundObj.layer = vehicleLayerIndex;

        Bound = MMBound.LoadFresh(boundObj, $"{name}", true);
        Bound.FlipXZ();
        Bound.Collider.convex = true; // Unity 5+ does not support moving concave colliders

        PhysicMaterial vehicleMat = new PhysicMaterial("VehicleBound")
        {
            staticFriction = VehCarSim.BoundFriction,
            dynamicFriction = VehCarSim.BoundFriction,
            bounciness = VehCarSim.BoundElasticity,
            frictionCombine = PhysicMaterialCombine.Average,
            bounceCombine = PhysicMaterialCombine.Average
        };
        Bound.Collider.sharedMaterial = vehicleMat;
    }

    private void InitModel(SDLCity level, string name, int variant)
    {
        var modelObj = new GameObject("Model");
        modelObj.transform.SetParent(this.transform);

        Model = modelObj.AddComponent<VehicleModel>();
        Model.Init(level, name);
        Model.Init(this);
        Model.Load(name);
        Model.Variant = variant;

        string vehicleLayer = (Type == vehCarType.Player) ? "PlayerVehicleBody" : "VehicleBody";
        int vehicleLayerIndex = LayerMask.NameToLayer(vehicleLayer);
        modelObj.SetLayer(vehicleLayerIndex, true);
    }

    /// <summary>
    /// vehCar::Init. Does the whole load sequence in the order vehCarSim::Init
    /// uses it:
    ///
    ///   1. parse the .vehCarSim tune file
    ///   2. pull Center / Radius / Width for each wheel out of the model pivots
    ///   3. hand the sim its transform and body, which triggers ComputeConstants
    ///   4. bring up the subsystems
    ///
    /// Steps 1 and 2 must both precede step 3: every derived spring rate, tyre
    /// stiffness and gear ratio is computed from the authored values AND the
    /// wheel radius.
    /// </summary>
    public void Init(SDLCity level, string basename, int variant, vehCarType type)
    {
        this.level = level;

        Basename = basename;
        Type = type;

        if (Body == null)
            Body = GetComponent<Rigidbody>();
        Body.interpolation = RigidbodyInterpolation.Interpolate;
        // Body.maxDepenetrationVelocity = 1f;

        // load bound
        InitBound(basename);

        // ---- 1. tune file ---------------------------------------------------
        string carSimBasename = basename;
        if(!ArgParser.GetFlag("tune_car") && basename == "vpcop" && type == vehCarType.Player)
        {
            // hack from the original game where the cop car uses its own custom tuning for non players only
            carSimBasename = "vpmustang99";
        }
        var node = AssetManager.OpenNode("tune", "vehicle", $"{carSimBasename}.vehCarSim");
        VehCarSim.ReadSettings(node);

        // ---- 2. wheel pivots from the model ---------------------------------
        LoadWheelPivots(basename);

        // ---- 3. the sim -----------------------------------------------------
        VehCarSim.Init(this, transform, Body);
        VehCarSim.SetResetPos(transform.position);
        VehCarSim.SetResetRotation(transform.eulerAngles.y * Mathf.Deg2Rad);

        // ---- 4. subsystems --------------------------------------------------
        var tireTrackTexture = TextureCache.Get("tire_track");
        for (int i = 0; i < 4; i++)
        {
            TrackManagers[i] = gameObject.AddComponent<LvlTrackManager>();
            TrackManagers[i].Init(VehCarSim.Wheels[i], VehCarSim.Wheels[i].Width, tireTrackTexture);
        }

        if (Splash == null) Splash = GetComponent<VehSplash>();
        if (Splash == null) Splash = gameObject.AddComponent<VehSplash>();
        Splash.Init(this);
        Splash.enabled = false;

        if (Gyro == null) Gyro = GetComponent<VehGyro>();
        if (Gyro == null) Gyro = gameObject.AddComponent<VehGyro>();
        Gyro.Init(this);

        if (Stuck == null) Stuck = GetComponent<VehStuck>();
        if (Stuck == null) Stuck = gameObject.AddComponent<VehStuck>();

        LoadStuckSettings(basename);
        Stuck.Init(this);

        if (Debug == null) Debug = GetComponent<VehDebug>();
        if (Debug == null) Debug = gameObject.AddComponent<VehDebug>();
        Debug.Init(this);
        Debug.enabled = false;

        if (WheelPtx == null) WheelPtx = GetComponent<VehWheelPtx>();
        if (WheelPtx == null) WheelPtx = gameObject.AddComponent<VehWheelPtx>();
        WheelPtx.Init(this);

        // ---- 5. model --------------------------------------------------
        InitModel(level, basename, variant);

        // ---- 6. siren --------------------------------------------------
        if (Siren == null) Siren = GetComponent<VehSiren>();
        if (Siren == null) Siren = gameObject.AddComponent<VehSiren>();
        Siren.Init(this);
        Siren.Deactivate();

        if (Damage == null) Damage = GetComponent<VehCarDamage>();
        if (Damage == null) Damage = gameObject.AddComponent<VehCarDamage>();
        Damage.Init(this);

        // ---- 7. audio --------------------------------------------------
        if (Audio == null) Audio = GetComponent<VehicleAudioContainer>();
        if (Audio == null) Audio = gameObject.AddComponent<VehicleAudioContainer>();
        Audio.Init(this);
    }

    /// <summary>
    /// vehWheel::Init's pivot lookup. Override if your geometry pipeline differs.
    /// The original derives Radius and Width from the pivot matrix axes; here
    /// they come from the .mtx bounds, which is equivalent for a wheel pivot.
    /// </summary>
    protected virtual void LoadWheelPivots(string basename)
    {
        for (int i = 0; i < 4; i++)
        {
            var wheel = VehCarSim.Wheels[i];

            var matrixFile = new MatrixFile();
            using (var stream = AssetManager.Open("geometry", $"{basename}_whl{i}.mtx"))
            {
                matrixFile.Load(stream);
                matrixFile = matrixFile.FlipXZ();
            }

            wheel.Center = matrixFile.Origin;
            wheel.Radius = matrixFile.Origin.y;
            wheel.Width = Mathf.Abs(matrixFile.BoundsMax.x - matrixFile.BoundsMin.x);
        }
    }

    protected virtual void LoadStuckSettings(string basename)
    {
        try
        {
            var node = AssetManager.OpenNode("tune", "vehicle", $"{basename}.vehStuck");
            if (node != null)
                Stuck.Read(node);
        }
        catch
        {
            // No stuck tune for this car - keep whatever is set in the Inspector.
        }
    }

    /// <summary>
    /// vehCar::SetDrivable. Modes 1 and 3 also drop the transmission into
    /// neutral, so the engine can idle without driving the wheels.
    /// </summary>
    public void SetDrivable(bool drivable, VehUndrivableMode mode = VehUndrivableMode.Passive)
    {
        if (drivable)
        {
            undrivableMode = VehUndrivableMode.Passive;
            flags |= FlagDrivable;
            VehCarSim.Transmission.SetForward();
        }
        else
        {
            flags &= ~FlagDrivable;
            undrivableMode = mode;
            if (mode == VehUndrivableMode.LockedNeutral || mode == VehUndrivableMode.HoldBrakes)
            {
                VehCarSim.Transmission.SetNeutral();
            }
        }
    }

    /// <summary>vehCar::Reset. Puts the car back at its reset position and
    /// clears every subsystem.</summary>
    public void Reset()
    {
        Audio.Reset();
        VehCarSim.Reset();
        Gyro?.Reset();
        Damage?.Reset();
        Splash?.Deactivate();
        Siren.Deactivate();
        Stuck?.Reset();
        for (int i = 0; i < 4; i++)
        {
            TrackManagers[i]?.Clear();
        }
    }

    // =====================================================================

    private void PreUpdate()
    {
        if (IsDrivable) return;

        if (undrivableMode == VehUndrivableMode.HoldBrakes)
        {
            VehCarSim.Transmission.SetNeutral();
        }
        VehCarSim.ApplyUndrivableOverrides();
    }

    private void FixedUpdate()
    {
        PreUpdate();

        // ---- the physics ------------------------------------------------
        VehCarSim.Update();

        // ---- gyro --------------------------------------------------------
        // The gyro wants to know whether the driver is braking, so it can back
        // off its stabilisation during a deliberate slide.
        if (Gyro != null)
        {
            if (Mathf.Abs(VehCarSim.HandBrakeInput) <= InputDeadzone)
                Gyro.Flags &= ~VehGyro.FlagHandBrake;
            else
                Gyro.Flags |= VehGyro.FlagHandBrake;

            if (Mathf.Abs(VehCarSim.BrakeInput) <= InputDeadzone)
                Gyro.Flags &= ~VehGyro.FlagBrake;
            else
                Gyro.Flags |= VehGyro.FlagBrake;
        }

        // ---- water -------------------------------------------------------
        if(level != null)
        {
            var currentRoom = Model.RoomID;

            if (currentRoom > 0)
            {
                var room = level.GetRoom(currentRoom);
                if (room.IsWaterRoom)
                {
                    float waterLevel = level.GetWaterLevel(currentRoom);
                    if (transform.position.y <= waterLevel && !Splash.enabled)
                    {
                        Splash.Activate(waterLevel);
                    }
                }
            }
        }

        // ---- tyre tracks --------------------------------------------------
        for (int i = 0; i < 4; i++)
            UpdateTrack(TrackManagers[i], VehCarSim.Wheels[i]);

        // ---- damage and feedback --------------------------------------------
        Feedback?.Update();
    }

    /// <summary>
    /// vehCar::UpdateTrack. Lays a skid mark where a grounded wheel is sliding
    /// hard enough to mark the road.
    /// </summary>
    private void UpdateTrack(LvlTrackManager track, VehWheel wheel)
    {
        if (track == null) return;

        if (wheel.LastGroundedStatus && wheel.MajorlySlipping)
            track.AddPoint(wheel.ContactPoint, wheel.LastSurfaceNormal, wheel.LastSlippage);
        else
            track.Break();
    }

    private void OnCollisionEnter(Collision collision)
    {
        NotifyImpact(collision);
    }

    private void OnCollisionStay(Collision collision)
    {
        NotifyImpact(collision);
    }

    private void NotifyImpact(Collision collision)
    {
        if (enabled)
        {
            if (Stuck != null) Stuck.Impact();
            if (Audio != null) 
            {
                int colliderId = 0;
                if (collision.gameObject.TryGetComponent<ColliderIdOverride>(out var colliderIdOverride))
                {
                    colliderId = colliderIdOverride.ColliderID;
                }
                Audio.ImpactAudio.Collision(colliderId, collision);
            }
            if(Model != null && Model.Breakables != null)
            {
                Model.Breakables.Impact(collision.impulse.magnitude, collision.contacts[0].point, Model.RoomID);
            }
            if(Damage != null)
            {
                Damage.Impact(collision);
            }
        }
    }

    private void OnDrawGizmos()
    {
        if (!DrawGizmos) return;

        VehCarSim.DrawGizmos();

        if (Body == null)
            return;

        Vector3 cog = Body.worldCenterOfMass;
        Quaternion rotation = Body.rotation;

        // COG
        Gizmos.color = Color.yellow;
        Gizmos.DrawSphere(cog, 0.08f);

        // Local axes
        float axisLength = 0.5f;

        // X = Red
        Gizmos.color = Color.red;
        Gizmos.DrawLine(cog, cog + rotation * Vector3.right * axisLength);

        // Y = Green
        Gizmos.color = Color.green;
        Gizmos.DrawLine(cog, cog + rotation * Vector3.up * axisLength);

        // Z = Blue
        Gizmos.color = Color.blue;
        Gizmos.DrawLine(cog, cog + rotation * Vector3.forward * axisLength);

        // Inertia tensor visualization
        Vector3 inertia = Body.inertiaTensor;

        // Visual scale. sqrt() makes the dimensions behave more like
        // the physical dimensions that generated the inertia.
        float scale = 0.5f;

        Vector3 halfSize = new Vector3(
            Mathf.Sqrt(inertia.y + inertia.z - inertia.x) * scale,
            Mathf.Sqrt(inertia.x + inertia.z - inertia.y) * scale,
            Mathf.Sqrt(inertia.x + inertia.y - inertia.z) * scale
        );

        Vector3[] corners = new Vector3[8];

        Vector3 x = rotation * Vector3.right * halfSize.x;
        Vector3 y = rotation * Vector3.up * halfSize.y;
        Vector3 z = rotation * Vector3.forward * halfSize.z;

        corners[0] = cog - x - y - z;
        corners[1] = cog + x - y - z;
        corners[2] = cog + x + y - z;
        corners[3] = cog - x + y - z;

        corners[4] = cog - x - y + z;
        corners[5] = cog + x - y + z;
        corners[6] = cog + x + y + z;
        corners[7] = cog - x + y + z;

        Gizmos.color = Color.white;

        // Bottom
        Gizmos.DrawLine(corners[0], corners[1]);
        Gizmos.DrawLine(corners[1], corners[2]);
        Gizmos.DrawLine(corners[2], corners[3]);
        Gizmos.DrawLine(corners[3], corners[0]);

        // Top
        Gizmos.DrawLine(corners[4], corners[5]);
        Gizmos.DrawLine(corners[5], corners[6]);
        Gizmos.DrawLine(corners[6], corners[7]);
        Gizmos.DrawLine(corners[7], corners[4]);

        // Vertical edges
        Gizmos.DrawLine(corners[0], corners[4]);
        Gizmos.DrawLine(corners[1], corners[5]);
        Gizmos.DrawLine(corners[2], corners[6]);
        Gizmos.DrawLine(corners[3], corners[7]);
    }
}

// ---------------------------------------------------------------------------
// Subsystem stubs. Signatures match how VehCar drives them; fill in the bodies
// from the corresponding originals.
//
// VehGyro is NOT here any more - it lives in VehGyro.cs as a concrete class.
// ---------------------------------------------------------------------------

public abstract class VehSubsystem : MonoBehaviour
{
    protected VehCar Car;
    public virtual void Init(VehCar car) { Car = car; }
    public abstract void Update();
}


/// <summary>vehFeedback - force feedback on the wheel.</summary>
public interface IVehFeedback
{
    void Update();
}