using UnityEngine;

/// <summary>
/// Port of Angel Game Engine's vehSplash (vehicle water buoyancy).
///
/// Original model: a 4x4x4 lattice of 64 probe points spanning the vehicle's local
/// bounding box. Each tick every probe is transformed to world space; probes at or
/// below the water plane each apply (mass * buoyancy) upward and (-mass * damp * pointVelocity)
/// at their own position, which produces the righting/rocking torque for free.
///
/// Buoyancy starts high enough to float the car, then decays over 10 seconds so the
/// vehicle bobs up, settles, and then rides lower in the water.
///
/// Units are SI (meters, kilograms, seconds) and the original constants carry over
/// unchanged. Buoyancy is an acceleration contributed by EACH submerged probe:
///     0.7  m/s^2 per probe  -> 44.8 m/s^2 fully submerged, vs 9.81 of gravity
///     0.4  m/s^2 per probe  -> 25.6 m/s^2 floor after decay
///     0.03 m/s^2 per second of decay, so 0.7 -> 0.4 takes 10 s
/// Equilibrium therefore sits at ~14 of 64 probes underwater (~22% submerged),
/// settling to ~24 probes (~38%) once decayed. That is a car floating with its
/// cabin dry and slowly sinking to the window line.
///
/// The probe count is baked into the lift constant. If Grid changes, scale it by 64/N.
/// </summary>
public class VehSplash : VehSubsystem
{
    const int Grid = 4;
    const int ProbeCount = Grid * Grid * Grid; // 64

    [Header("Water")]
    [Tooltip("World-space Y of the water plane. Set via Activate().")]
    public float waterLevel;

    public Vector3 flow = new Vector3(2.5f, 0.0f, 2.5f);

    [Header("Buoyancy (m/s^2 per submerged probe)")]
    [Tooltip("Original Buoyancy ceiling, restored by Reset.")]
    public float liftMax = 0.7f;

    [Tooltip("Original Buoyancy floor.")]
    public float liftMin = 0.4f;

    [Tooltip("Buoyancy decay, m/s^2 per second.")]
    public float liftDecayPerSecond = 0.03f;

    [Header("Damping")]
    [Tooltip("Original Damp = 0.08, applied per submerged probe (1/s).")]
    public float damp = 0.08f;

    [Header("Probe volume")]
    public bool autoBoundsFromColliders = true;
    public Vector3 boundsMin = new Vector3(-0.85f, -0.45f, -2.10f);
    public Vector3 boundsMax = new Vector3(0.85f, 0.55f, 2.10f);

    [Header("Fidelity")]
    [Tooltip("The original measured lever arms from the ICS matrix origin, which was its " +
             "center of mass. Unity lets those differ, so this uses worldCenterOfMass. " +
             "Turn off to measure from the rigidbody origin instead.")]
    public bool leverArmsFromCenterOfMass = true;

    [Header("Debug")]
    public bool drawProbes = false;

    readonly Vector3[] probes = new Vector3[ProbeCount];
    float lift;
    int submergedCount; // exposed for splash VFX / audio hookups

    public int SubmergedProbes => submergedCount;
    public float Submersion => submergedCount / (float)ProbeCount;
    public float CurrentLift => lift;

    void Awake()
    {
        if (autoBoundsFromColliders)
            ComputeLocalBounds();
        BuildLattice();
        ResetBuoyancy();
    }

    /// <summary>vehSplash::Reset. Call when the vehicle leaves the water so it floats again next time.</summary>
    public void ResetBuoyancy()
    {
        lift = liftMax;
        damp = 0.08f;
    }

    /// <summary>vehSplash::Activate. Sets the water plane and raises the enable flag.</summary>
    public void Activate(float waterPlaneY)
    {
        waterLevel = waterPlaneY;
        this.enabled = true;
    }

    public void Deactivate()
    {
        this.enabled = false;
        submergedCount = 0;
        ResetBuoyancy();
    }

    /// <summary>
    /// vehSplash::Init. Uniform 4x4x4 lattice across the local bounding box, faces included.
    ///
    /// The decompile generates a random unit vector scaled by 1.5 and stores it in each
    /// slot, then immediately overwrites all three components with the lattice position.
    /// The pointer arithmetic hides that both writes hit the same element, so the jitter
    /// is dead code and is not reproduced.
    /// </summary>
    void BuildLattice()
    {
        const float step = 1f / (Grid - 1); // 0.33333334 in the original
        Vector3 size = boundsMax - boundsMin;
        int n = 0;
        for (int x = 0; x < Grid; x++)
            for (int y = 0; y < Grid; y++)
                for (int z = 0; z < Grid; z++)
                    probes[n++] = new Vector3(
                        boundsMin.x + size.x * x * step,
                        boundsMin.y + size.y * y * step,
                        boundsMin.z + size.z * z * step);
    }

    void ComputeLocalBounds()
    {
        var colliders = GetComponentsInChildren<Collider>();
        bool any = false;
        Vector3 min = Vector3.zero, max = Vector3.zero;

        foreach (var c in colliders)
        {
            if (c.isTrigger) continue;
            Bounds b = c.bounds;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = new Vector3(
                    (i & 1) == 0 ? b.min.x : b.max.x,
                    (i & 2) == 0 ? b.min.y : b.max.y,
                    (i & 4) == 0 ? b.min.z : b.max.z);
                Vector3 local = transform.InverseTransformPoint(corner);
                if (!any) { min = max = local; any = true; }
                else { min = Vector3.Min(min, local); max = Vector3.Max(max, local); }
            }
        }

        if (any) { boundsMin = min; boundsMax = max; }
    }

    /// <summary>vehSplash::Update.</summary>
    void FixedUpdate()
    {
        var rb = Car.Body;
        if (rb == null) return;

        float mass = rb.mass;
        float probeLiftForce = mass * lift;  // v38, hoisted out of the loop as in the original
        float dampCoef = mass * damp;        // per probe, NOT divided by ProbeCount

        // The ICS matrix is the physics transform, so read it off the rigidbody rather
        // than the Transform, which lags or leads when interpolation is on.
        Vector3 position = rb.position;
        Quaternion rotation = rb.rotation;
        Vector3 origin = leverArmsFromCenterOfMass ? rb.worldCenterOfMass : position;

#if UNITY_6000_0_OR_NEWER
        Vector3 linearVel = rb.linearVelocity;
#else
        Vector3 linearVel = rb.velocity;
#endif
        Vector3 angularVel = rb.angularVelocity;

        Vector3 current = flow.sqrMagnitude > 0f ? flow.normalized : Vector3.zero;

        Vector3 totalForce = Vector3.zero;
        Vector3 totalTorque = Vector3.zero;
        submergedCount = 0;

        for (int i = 0; i < ProbeCount; i++)
        {
            // Local -> world. The ICS matrix carries no scale, so rotation + translation.
            Vector3 world = position + rotation * probes[i];

            // Original test is (y < level) || (y == level).
            if (world.y > waterLevel) continue;
            submergedCount++;

            // omega x r + linearVelocity, r measured from the origin chosen above.
            Vector3 r = world - origin;
            Vector3 pointVel = linearVel + Vector3.Cross(angularVel, r) + current;

            // F = mass * (lift * up - damp * pointVel)
            Vector3 force = new Vector3(
                -pointVel.x * dampCoef,
                probeLiftForce - pointVel.y * dampCoef,
                -pointVel.z * dampCoef);

            totalForce += force;
            totalTorque += Vector3.Cross(r, force);
        }

        if (submergedCount > 0)
        {
            rb.AddForce(totalForce, ForceMode.Force);
            rb.AddTorque(totalTorque, ForceMode.Force);
        }

        DecayBuoyancy(Time.fixedDeltaTime);
    }

    void DecayBuoyancy(float dt)
    {
        if (lift <= liftMin) return;
        lift = Mathf.Clamp(lift - liftDecayPerSecond * dt, liftMin, liftMax);
    }

    void OnDrawGizmosSelected()
    {
        if (!drawProbes || !Application.isPlaying) return;
        for (int i = 0; i < ProbeCount; i++)
        {
            Vector3 world = transform.TransformPoint(probes[i]);
            Gizmos.color = world.y <= waterLevel ? Color.cyan : new Color(1f, 1f, 1f, 0.25f);
            Gizmos.DrawSphere(world, 0.04f);
        }
    }

    public override void Update()
    {
    }
}