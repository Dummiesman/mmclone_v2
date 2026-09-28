using UnityEngine;

/// <summary>
/// Faithful port of MM2 / AGE vehCarSim.
///
/// WHEEL INDICES: 0 = front left, 1 = front right, 2 = rear left, 3 = rear right.
/// Parameters are authored PER AXLE - the original does CopyVars(WHL1, WHL0) and
/// CopyVars(WHL3, WHL2), so whl0 defines the front pair and whl2 the rear.
///
/// DRIVETRAIN LAYOUT: undriven wheels are not left loose. Each gets its own
/// single-wheel "freetrain" - a VehDrivetrain with no engine attached - because
/// they still need the shaft solve for rolling resistance and braking.
///
///   RWD: Primary = {2,3}    Freetrains = {0}, {1}
///   FWD: Primary = {0,1}    Freetrains = {2}, {3}
///   AWD: Primary = {0,1,2,3}  no freetrains
///
/// </summary>
[System.Serializable]
public class VehCarSim
{
    public const float MetricFactor = 2.23693629f;   // m/s -> mph
    private VehCar Car;

    // ---------------------------------------------------------------- config
    public DrivetrainType DrivetrainType = DrivetrainType.RWD;
    public float Mass = 2000f;
    public Vector3 InertiaBox = new Vector3(2f, 1f, 3f);
    public Vector3 CenterOfGravity = Vector3.zero;
    public float CarFrictionHandling = 1f;

    /// <summary>Speed-sensitive steering. Zero threshold disables it.</summary>
    public float SSSThreshold = 0f;
    public float SSSValue = 1f;

    public float BoundElasticity = 0.2f;
    public float BoundFriction = 0.30000001f;

    // ----------------------------------------------------------------- input
    [Range(-1f, 1f)] public float SteeringInput;
    [Range(0f, 1f)] public float BrakeInput;
    /// <summary>Positive applies to the rear axle, negative to the front.</summary>
    [Range(-1f, 1f)] public float HandBrakeInput;

    // ------------------------------------------------------------ components
    public VehWheel[] Wheels = new VehWheel[4] { new VehWheel(), new VehWheel(), new VehWheel(), new VehWheel() };
    public VehEngine Engine = new VehEngine();
    public VehTransmission Transmission = new VehTransmission();
    public VehDrivetrain PrimaryDrivetrain = new VehDrivetrain();
    public VehDrivetrain FreetrainLeft = new VehDrivetrain();
    public VehDrivetrain FreetrainRight = new VehDrivetrain();
    public VehAero VehAero = new VehAero();

    /// <summary>vehAxle AxleFront / AxleRear. Anti-roll bars plus axle camber.</summary>
    public VehAxle AxleFront = new VehAxle();
    public VehAxle AxleRear = new VehAxle();

    [System.NonSerialized] public PhInertialCS InertialCS;
    [System.NonSerialized] public Rigidbody Body;
    [System.NonSerialized] public Transform Transform;

    private Vector3 lastBodyVelocity;
    private Vector3 lastBodyAngularVelocity;

    // ------------------------------------------------------------ live state
    public float Speed;        // forward speed magnitude, m/s
    public float SpeedInMph;

    /// <summary>vehCarSim::WheelCount. Always 4; the gyro divides by it.</summary>
    public int WheelCount => Wheels.Length;

    /// <summary>False until Init has run. Update is a no-op before that.</summary>
    public bool Initialised { get; private set; }

    public Vector3 ResetPos;
    public float ResetRotation;

    private DrivetrainType configuredDrivetrainType;
    private bool configured;

    // =====================================================================

    /// <summary>
    /// Called once by the owning VehCar, AFTER ReadSettings and after the wheel
    /// pivots have been filled in. Everything the sim needs from Unity arrives
    /// here, so the sim itself has no component lifecycle.
    /// </summary>
    public void Init(VehCar car, Transform carTransform, Rigidbody body)
    {
        Car = car;
        Transform = carTransform;
        Body = body;
        Body.mass = Mass;

        // The inertial origin IS the centre of mass, and the model matrix sits at
        // origin + R * CenterOfGravity. So in model-local terms the CoM is at
        // -CenterOfGravity.
        // But Unity has -Z so we leave Z alone
        Body.centerOfMass = -CenterOfGravity;

        //Body.centerOfMass = Vector3.zero; // hmm, works better???

        // InitBoxMass: inertia tensor from a box of the given dimensions.
        Body.inertiaTensor = BoxInertia(Mass, InertiaBox);
        Body.inertiaTensorRotation = Quaternion.identity;

        InertialCS = new PhInertialCS(Body);

        foreach (var wheel in Wheels)
        {
            wheel.ICS = InertialCS;
            wheel.CarTransform = Transform;
            wheel.CarSim = this;
        }

        PrimaryDrivetrain.VehCarSim = this;
        FreetrainLeft.VehCarSim = this;
        FreetrainRight.VehCarSim = this;

        Engine.CarSim = this;
        Engine.Transmission = Transmission;
        Engine.PrimaryDrivetrain = PrimaryDrivetrain;

        VehAero.CarSim = this;

        // Drivetrain has to be configured before the transmission computes its
        // gear table - GearRatioFromMPH reads PrimaryDrivetrain.Wheels[0].Radius.
        ReconfigureDrivetrain();
        PrimaryDrivetrain.Attach(this);

        Transmission.Init(this);

        AxleFront.Init(this, Wheels[0], Wheels[1]);
        AxleRear.Init(this, Wheels[2], Wheels[3]);

        ComputeConstants();

        Initialised = true;
    }

    /// <summary>
    /// The tail of vehCarSim::Init. Derives everything from authored data, so it
    /// must run after the car file AND after the wheel pivots. Safe to call again
    /// any time you change an authored value at runtime.
    /// </summary>
    public void ComputeConstants()
    {
        // Front/rear pairs share authored parameters: whl0 defines the front,
        // whl2 the rear. Note CopyVars deliberately does NOT copy Radius, Width
        // or HandbrakeCoef - that is the original's behaviour, not an oversight.
        Wheels[1].CopyVars(Wheels[0]);
        Wheels[3].CopyVars(Wheels[2]);
        FreetrainRight.CopyVars(FreetrainLeft);

        Engine.ComputeConstants();
        Transmission.ComputeConstants();

        AxleFront.ComputeConstants();
        AxleRear.ComputeConstants();

        Wheels[1].ComputeConstants();
        Wheels[3].ComputeConstants();
        Wheels[0].ComputeConstants();
        Wheels[2].ComputeConstants();
    }

    private static Vector3 BoxInertia(float mass, Vector3 size)
    {
        float k = mass / 12f;
        //float k = mass / 6.0f; // double it?
        float x = size.x * size.x;
        float y = size.y * size.y;
        float z = size.z * size.z;
        return new Vector3(k * (y + z), k * (x + z), k * (x + y));
    }

    // =====================================================================
    // Reset
    // =====================================================================
    public void SetResetPos(Vector3 position)
    {
        ResetPos = position + CenterOfGravity;
    }

    public void SetResetRotation(float yawRadians)
    {
        ResetRotation = yawRadians;
    }

    /// <summary>vehCarSim::Reset.</summary>
    public void Reset()
    {
        if (Body != null)
        {
            var prevInterp = Body.interpolation;
            Body.interpolation = RigidbodyInterpolation.None;

            Body.velocity = Vector3.zero;
            Body.angularVelocity = Vector3.zero;

            var pos = ResetPos;
            var rot = Quaternion.Euler(0f, ResetRotation * Mathf.Rad2Deg, 0f);

            Body.position = pos;
            Body.rotation = rot;
            Car.transform.SetPositionAndRotation(pos, rot);

            Physics.SyncTransforms();
            Body.interpolation = prevInterp;
        }

        if (InertialCS != null)
        {
            InertialCS.NetPush = Vector3.zero;
            InertialCS.LastTotalAppliedPush = Vector3.zero;
        }

        Engine.Reset();
        Transmission.Reset();
        PrimaryDrivetrain.Reset();
        FreetrainLeft.Reset();
        FreetrainRight.Reset();
        AxleFront.Reset();
        AxleRear.Reset();

        BrakeInput = 0f;
        HandBrakeInput = 0f;
        SteeringInput = 0f;
        Speed = 0f;
        SpeedInMph = 0f;
    }

    // =====================================================================
    // Drivetrain configuration
    // =====================================================================

    public void ReconfigureDrivetrain()
    {
        UnconfigureDrivetrain();
        ConfigureDrivetrain();
    }

    private void UnconfigureDrivetrain()
    {
        PrimaryDrivetrain.WheelCount = 0;
        FreetrainLeft.WheelCount = 0;
        FreetrainRight.WheelCount = 0;
        configured = false;
    }

    private void ConfigureDrivetrain()
    {
        switch (DrivetrainType)
        {
            case DrivetrainType.RWD:
                FreetrainLeft.AddWheel(Wheels[0]);
                FreetrainRight.AddWheel(Wheels[1]);
                PrimaryDrivetrain.AddWheel(Wheels[2]);
                PrimaryDrivetrain.AddWheel(Wheels[3]);
                break;

            case DrivetrainType.FWD:
                FreetrainLeft.AddWheel(Wheels[2]);
                FreetrainRight.AddWheel(Wheels[3]);
                PrimaryDrivetrain.AddWheel(Wheels[0]);
                PrimaryDrivetrain.AddWheel(Wheels[1]);
                break;

            case DrivetrainType.AWD:
                for (int i = 0; i < 4; i++)
                    PrimaryDrivetrain.AddWheel(Wheels[i]);
                break;
        }

        configuredDrivetrainType = DrivetrainType;
        configured = true;
    }

    public void SetInputs(float steering, float throttle, float brake, float handBrake)
    {
        SteeringInput = Mathf.Clamp(steering, -1f, 1f);
        BrakeInput = Mathf.Clamp01(brake);
        HandBrakeInput = Mathf.Clamp(handBrake, -1f, 1f);
        Engine.ThrottleInput = Mathf.Clamp01(throttle);

        ApplyUndrivableOverrides();
    }

    public void ApplyUndrivableOverrides()
    {
        if (Car == null || Car.IsDrivable) return;

        switch (Car.UndrivableMode)
        {
            case VehUndrivableMode.HoldBrakes:
                BrakeInput = 1f;
                break;

            case VehUndrivableMode.Locked:
            case VehUndrivableMode.LockedNeutral:
                BrakeInput = 1f;
                Engine.ThrottleInput = 0f;
                SteeringInput = 0f;
                HandBrakeInput = 0f;
                break;
        }
    }

    // =====================================================================
    // Queries
    // =====================================================================

    /// <summary>How many wheels are currently bottomed out (0-4).</summary>
    public int BottomedOut()
    {
        int count = 0;
        for (int i = 0; i < 4; i++)
            if (Wheels[i].BottomedOut) count++;
        return count;
    }

    /// <summary>How many wheels were grounded last step (0-4).</summary>
    public int OnGround()
    {
        int count = 0;
        for (int i = 0; i < 4; i++)
            if (Wheels[i].LastGroundedStatus) count++;
        return count;
    }

    /// <summary>
    /// Speed-sensitive steering multiplier: ramps linearly from 1 at a standstill
    /// to SSSValue at SSSThreshold, then holds.
    /// </summary>
    public float GetSSSFactor(float speed)
    {
        if (SSSThreshold == 0f) return 1f;
        if (speed < SSSThreshold)
            return (SSSValue - 1f) * (speed / SSSThreshold) + 1f;
        return SSSValue;
    }

    // =====================================================================

    /// <summary>
    /// One physics step. Call from the owner's FixedUpdate - the ordering inside
    /// here mirrors the original's asNode child ordering and is load-bearing.
    /// </summary>
    public void Update()
    {
        if (!Initialised) return;

        lastBodyVelocity = Body.velocity;
        lastBodyAngularVelocity = Body.angularVelocity;

        if (!configured || configuredDrivetrainType != DrivetrainType)
            ReconfigureDrivetrain();

        // ---- speed along the car's forward axis --------------------------
        Speed = Mathf.Abs(Vector3.Dot(Transform.forward, Body.velocity));
        SpeedInMph = Speed * MetricFactor;

        DistributeInputs();

        // ---- node order: engine, transmission, aero, then drivetrains ------
        Transmission.Update();
        Engine.Update();
        VehAero.Update();

        // Each drivetrain solves its shaft speed, then runs its own wheels. The
        // wheels are children of the drivetrain in the original, which is what
        // guarantees this ordering.
        UpdateDrivetrain(PrimaryDrivetrain);
        if (DrivetrainType != DrivetrainType.AWD)
        {
            UpdateDrivetrain(FreetrainLeft);
            UpdateDrivetrain(FreetrainRight);
        }

        // ---- apply everything the wheels accumulated -------------------------
        Vector3 totalForce = Vector3.zero;
        Vector3 totalTorque = Vector3.zero;
        for (int i = 0; i < 4; i++)
        {
            totalForce += Wheels[i].AccumulatedForce;
            totalTorque += Wheels[i].AccumulatedTorque;
        }

        // AGE rebuilds omega from momentum every step and never adds the Coriolis
        // coupling, so PhysX's w x Iw is a torque MM2 does not have. Cancel it.
        Quaternion toPrincipal = Body.rotation * Body.inertiaTensorRotation;
        Vector3 wLocal = Quaternion.Inverse(toPrincipal) * Body.angularVelocity;
        Vector3 I = Body.inertiaTensor;
        Vector3 Iw = new Vector3(wLocal.x * I.x, wLocal.y * I.y, wLocal.z * I.z);
        Vector3 gyro = toPrincipal * Vector3.Cross(wLocal, Iw);

        totalTorque -= gyro;

        Body.AddForce(totalForce, ForceMode.Force);
        Body.AddTorque(totalTorque, ForceMode.Force);

        // ---- axles ------------------------------------------------------------
        // After the drivetrains, matching the original's child order. Both wheels
        // on an axle must have solved their suspension before the bar can act on
        // the difference between them.
        AxleFront.Update();
        AxleRear.Update();

        // Positional correction last, so LastTotalAppliedPush is ready for the
        // next step's velocity filter.
        InertialCS.ApplyContactSolve();
        InertialCS.ApplyNetPush();
    }

    private void UpdateDrivetrain(VehDrivetrain drivetrain)
    {
        drivetrain.Update();
        for (int i = 0; i < drivetrain.WheelCount; i++)
            drivetrain.Wheels[i].Update(lastBodyVelocity, lastBodyAngularVelocity);
    }

    // =====================================================================
    // Input distribution
    // =====================================================================

    private void DistributeInputs()
    {
        float steer = GetSSSFactor(Speed) * SteeringInput;

        // Negative handbrake goes to the FRONT axle, positive to the REAR.
        float frontHandBrake = HandBrakeInput < 0f ? -HandBrakeInput : 0f;
        float rearHandBrake = HandBrakeInput > 0f ? HandBrakeInput : 0f;

        Wheels[0].SetInputs(steer, BrakeInput, frontHandBrake);
        Wheels[1].SetInputs(steer, BrakeInput, frontHandBrake);

        // Line-lock burnout: full brake plus full throttle at a standstill
        // releases the rear brakes so the driven wheels can spin up.
        float rearBrake = BrakeInput;
        if (BrakeInput > 0.94999999f && Engine.ThrottleInput > 0.94999999f && Speed < 1f)
            rearBrake = 0f;

        // Rear handbrake is tapered by steering, one side at a time - this is
        // what makes a handbrake turn rotate rather than just lock up straight.
        float leftFactor = SteeringInput > 0f ? 1f - SteeringInput : 1f;
        float rightFactor = SteeringInput < 0f ? SteeringInput + 1f : 1f;

        // Rear wheels take the negated steer angle. Harmless on cars, where the
        // rear SteeringLimit is zero, but it is what drives rear-steer vehicles.
        Wheels[2].SetInputs(-steer, rearBrake, leftFactor * rearHandBrake);
        Wheels[3].SetInputs(-steer, rearBrake, rightFactor * rearHandBrake);
    }

    public void ReadSettings(TokenFileParser parser)
    {
        parser.SeekToData();

        Mass = parser.Read("Mass", Mass);
        InertiaBox = parser.Read("InertiaBox", InertiaBox);
        
        CenterOfGravity = parser.Read("CenterOfGravity", CenterOfGravity);
        CenterOfGravity.z *= -1.0f;

        BoundFriction = parser.Read("BoundFriction", BoundFriction);
        BoundElasticity = parser.Read("BoundElasticity", BoundElasticity);
        DrivetrainType = parser.Read("DrivetrainType", DrivetrainType);
        SSSValue = parser.Read("SSSValue", SSSValue);
        SSSThreshold = parser.Read("SSSThreshold", SSSThreshold);
        CarFrictionHandling = parser.Read("CarFrictionHandling", CarFrictionHandling);

        parser.SkipToSection("Aero");
        VehAero.Read(parser);

        parser.SkipToSection("Engine");
        Engine.Read(parser);

        parser.SkipToSection("Trans");
        Transmission.Read(parser);

        parser.SkipToSection("Drivetrain");
        PrimaryDrivetrain.Read(parser);

        parser.SkipToSection("Freetrain");
        FreetrainLeft.Read(parser);

        parser.SkipToSection("WheelFront");
        Wheels[0].Read(parser);

        parser.SkipToSection("WheelBack");
        Wheels[2].Read(parser);

        parser.SkipToSection("AxleFront");
        AxleFront.Read(parser);

        parser.SkipToSection("AxleBack");
        AxleRear.Read(parser);
    }

    private float PushGizmoScale = 20f;        // NetPush is metres - tiny, needs blowing up
    private float ContactForceScale = 0.0002f; // newtons
    private float VelocityDeltaScale = 2f;     // m/s


    public void DrawGizmos()
    {
        foreach (var wheel in Wheels)
            wheel.DrawGizmos();

        AxleFront.DrawGizmos();
        AxleRear.DrawGizmos();

        DrawContactSolveGizmos();
    }

    private void DrawContactSolveGizmos()
    {
        if (InertialCS == null || Body == null) return;

        Vector3 com = Body.worldCenterOfMass;

        // ---- per-wheel stiffness participation -------------------------------
        // A wheel only enters the implicit solve when DampingCoefficient > 0.
        // Green ring = in the solve. Red ring = gated out (bottomed, clamped,
        // or at droop). This is the single most useful readout here: if none of
        // them are green while the car is sitting flat, the solve is a no-op.
        for (int i = 0; i < Wheels.Length; i++)
        {
            var w = Wheels[i];
            if (!w.LastGroundedStatus) continue;

            bool inSolve = w.DampingCoefficient > 0f;
            Gizmos.color = inSolve ? Color.green : Color.red;
            DrawCircle(w.ContactPoint, w.LastSurfaceNormal, 0.18f);

            if (inSolve)
            {
                // The predicted force delta this wheel contributed (k * rate * dt).
                float predicted = w.TargetSuspensionTravel >= w.SuspensionLimit
                    ? 0f
                    : w.SuspensionMaxForce * w.SuspensionCompressionRate * Time.fixedDeltaTime;

                Gizmos.color = new Color(0.4f, 1f, 0.4f);
                DrawArrow(w.ContactPoint,
                          w.ContactPoint + w.LastSurfaceNormal * (predicted * ContactForceScale),
                          0.06f);
            }
        }

        // ---- aggregate contact force / torque --------------------------------
        if (InertialCS.LastContactSolved)
        {
            Gizmos.color = Color.cyan;
            DrawArrow(com, com + InertialCS.LastContactForce * ContactForceScale, 0.1f);

            Gizmos.color = new Color(0f, 0.7f, 1f);
            DrawArrow(com, com + InertialCS.LastContactTorque * (ContactForceScale * 0.5f), 0.1f);

            // ---- what the solve actually did to the body ---------------------
            // Magenta = linear velocity correction, yellow = angular. These are
            // the payoff: on a settled car they should be near zero. Persistent
            // large arrows mean the solve is fighting the explicit force path.
            Gizmos.color = Color.magenta;
            DrawArrow(com, com + InertialCS.LastDeltaV * VelocityDeltaScale, 0.08f);

            Gizmos.color = Color.yellow;
            DrawArrow(com, com + InertialCS.LastDeltaOmega * VelocityDeltaScale, 0.08f);
        }

        // ---- NetPush ----------------------------------------------------------
        // Positional hack, applied with no velocity change. Fires on bottom-out.
        Vector3 push = InertialCS.LastAppliedPush;
        if (push.sqrMagnitude > 1e-12f)
        {
            Gizmos.color = new Color(1f, 0.5f, 0f);
            DrawArrow(com, com + push * PushGizmoScale, 0.1f);

            for (int i = 0; i < Wheels.Length; i++)
            {
                if (!Wheels[i].BottomedOut) continue;
                Gizmos.color = new Color(1f, 0.3f, 0f);
                DrawCircle(Wheels[i].ContactPoint, Wheels[i].LastSurfaceNormal, 0.26f);
            }
        }
    }

    private static void DrawCircle(Vector3 center, Vector3 normal, float radius)
    {
        if (normal.sqrMagnitude < 1e-9f) return;

        Vector3 a = Vector3.Cross(normal, Mathf.Abs(normal.y) > 0.9f ? Vector3.right : Vector3.up).normalized;
        Vector3 b = Vector3.Cross(normal, a);

        const int Segments = 20;
        Vector3 prev = center + a * radius;
        for (int i = 1; i <= Segments; i++)
        {
            float ang = i / (float)Segments * Mathf.PI * 2f;
            Vector3 p = center + (a * Mathf.Cos(ang) + b * Mathf.Sin(ang)) * radius;
            Gizmos.DrawLine(prev, p);
            prev = p;
        }
    }

    private static void DrawArrow(Vector3 from, Vector3 to, float headSize)
    {
        Gizmos.DrawLine(from, to);

        Vector3 dir = to - from;
        if (dir.sqrMagnitude < 1e-10f) return;
        dir.Normalize();

        Vector3 a = Vector3.Cross(dir, Mathf.Abs(dir.y) > 0.9f ? Vector3.right : Vector3.up).normalized;
        Vector3 b = Vector3.Cross(dir, a);
        Vector3 basePoint = to - dir * headSize;

        Gizmos.DrawLine(to, basePoint + a * headSize * 0.4f);
        Gizmos.DrawLine(to, basePoint - a * headSize * 0.4f);
        Gizmos.DrawLine(to, basePoint + b * headSize * 0.4f);
        Gizmos.DrawLine(to, basePoint - b * headSize * 0.4f);
    }
}