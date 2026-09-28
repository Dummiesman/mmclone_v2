using UnityEngine;

/// <summary>
/// Faithful port of MM2 / AGE vehEngine.
///
/// NAMING WARNING, inherited from the reverse engineering and kept here only in
/// the comments: the original's CurrentTorque / IdleTorque / OptTorque /
/// MaxTorque fields are NOT torques. They are ANGULAR VELOCITIES in rad/s.
///
///     ComputeConstants: MaxTorque = MaxRPM * (2*PI/60)
///     Update:           CurrentRPM = CurrentTorque * (60/2*PI)
///
/// They have been renamed here to CurrentSpeed / IdleSpeed / OptSpeed / MaxSpeed.
/// If you cross-reference the decompile, that is the mapping. ThrottleTorque IS
/// a real torque (N.m) and keeps its name.
///
/// The drivetrain's "MaxTorque / |gearRatio|" clamp is therefore a max SHAFT
/// SPEED, not a torque expression - it is dimensionally correct.
/// </summary>
[System.Serializable]
public class VehEngine
{
    public const float RadPerSecToRpm = 9.5492964f;   // 60 / (2*PI)
    public const float RpmToRadPerSec = 0.10471976f;  // (2*PI) / 60
    public const float WattsPerHp = 746f;
    public const float InvWattsPerHp = 0.0013404826f;

    // Golden ratio pair. phi * (phi - 1) == 1 exactly, which is what places peak
    // power precisely at OptRPM without any tuning.
    private static readonly float Phi = (Mathf.Sqrt(5f) + 1f) * 0.5f;        // sq5add1
    private static readonly float PhiMinusOne = (Mathf.Sqrt(5f) - 1f) * 0.5f; // sq5min1

    // ---------------------------------------------------------------- config
    public float MaxHorsePower = 200f;
    public float IdleRPM = 750f;
    public float OptRPM = 5000f;
    public float MaxRPM = 8000f;
    public float GCL = 0.25f;          // gear change lag, seconds
    public float PowerScale = 1f;      // unknown648
    public float AngInertia = 1f;

    [System.NonSerialized] public VehCarSim CarSim;
    [System.NonSerialized] public VehTransmission Transmission;
    [System.NonSerialized] public VehDrivetrain PrimaryDrivetrain;

    /// <summary>Optional engine pivot for visual rocking. Null falls back to the body.</summary>
    public Transform EngineVisualRef;
    public Transform EngineVisual;

    // ------------------------------------------ derived (ComputeConstants)
    public float IdleSpeed;            // IdleTorque
    public float OptSpeed;             // OptTorque
    public float MaxSpeed;             // MaxTorque
    private float torqueGap;           // 1 / (MaxSpeed - OptSpeed)^2
    private float powerConstant;       // unknown676

    // ------------------------------------------------------------ live state
    public float CurrentSpeed;         // CurrentTorque - engine omega, rad/s
    public float CurrentRPM;
    public float ThrottleTorque;       // actual torque, N.m
    public float ThrottleInput;
    public float AIThrottle = 1f;

    public float CurrentHorsePower;    // RPMChangeRate - mislabelled, it is HP
    public float GearChangedAtRPM;
    public float GCLTimer;
    public bool WaitingOnGCL;

    /// <summary>
    /// Compatibility shim for VehDrivetrain, which reads and writes the engine
    /// speed under the original's name.
    /// </summary>
    public float CurrentTorque
    {
        get => CurrentSpeed;
        set => CurrentSpeed = value;
    }

    /// <summary>Compatibility shim: the drivetrain's clamp wants max engine speed.</summary>
    public float MaxTorque => MaxSpeed;

    // =====================================================================

    public void ComputeConstants()
    {
        MaxSpeed = MaxRPM * RpmToRadPerSec;
        OptSpeed = OptRPM * RpmToRadPerSec;
        IdleSpeed = IdleRPM * RpmToRadPerSec;

        float span = MaxSpeed - OptSpeed;
        torqueGap = 1f / (span * span);

        // k such that peak torque at OptSpeed yields exactly MaxHorsePower.
        powerConstant = PowerScale * MaxHorsePower * WattsPerHp
                        / (OptSpeed * OptSpeed * OptSpeed);
    }

    public void Reset()
    {
        CurrentRPM = IdleRPM;
        GearChangedAtRPM = IdleRPM;
        CurrentSpeed = 0f;
        CurrentHorsePower = 0f;
        ThrottleTorque = 0f;
        ThrottleInput = 0f;
        WaitingOnGCL = true;
        GCLTimer = GCL;
        PowerScale = 1f;
        AIThrottle = 1f;
    }

    // =====================================================================
    // Torque curve
    // =====================================================================

    /// <summary>
    /// Wide-open-throttle torque at engine speed <paramref name="omega"/>.
    ///
    /// Below OptSpeed this is a downward parabola whose roots sit at
    /// -(phi-1)*OptSpeed and phi*OptSpeed. Because phi*(phi-1) == 1, its value at
    /// OptSpeed is exactly k*OptSpeed^2, which makes power there exactly
    /// MaxHorsePower.
    ///
    /// Above OptSpeed the same parabola is multiplied by a second factor that
    /// equals 1 at OptSpeed (so the curve is continuous) and falls to 0 at
    /// MaxSpeed - the redline drop-off.
    /// </summary>
    public float CalcTorqueAtFullThrottle(float omega)
    {
        float parabola = (PhiMinusOne * OptSpeed + omega)
                       * (Phi * OptSpeed - omega)
                       * powerConstant;

        if (omega <= OptSpeed)
            return parabola;

        if (omega > MaxSpeed)
            return 0f;

        return (omega + MaxSpeed - (OptSpeed + OptSpeed))
             * parabola
             * (MaxSpeed - omega)
             * torqueGap;
    }

    /// <summary>
    /// Closed-throttle torque. Negative above idle - this is the engine braking
    /// that slows the car when you lift off.
    /// </summary>
    public float CalcTorqueAtZeroThrottle()
    {
        return PowerScale * MaxHorsePower * WattsPerHp / OptSpeed
             * (IdleSpeed - CurrentSpeed) * 0.75f
             / (OptSpeed - IdleSpeed);
    }

    /// <summary>Linear blend between the closed- and open-throttle curves.</summary>
    public float CalcTorque(float throttle)
    {
        float full = CalcTorqueAtFullThrottle(CurrentSpeed);
        return CalcTorqueAtZeroThrottle() * (1f - throttle) + full * throttle;
    }

    public float CalcHPAtFullThrottle(float omega)
        => CalcTorqueAtFullThrottle(omega) * omega;

    // =====================================================================

    public void Update()
    {
        float dt = Time.fixedDeltaTime;

        // ---- gear change lag ---------------------------------------------
        if (Transmission.GearChanged && !WaitingOnGCL)
        {
            GearChangedAtRPM = CurrentRPM;
            GCLTimer = GCL;
            WaitingOnGCL = true;
        }

        ThrottleTorque = CalcTorque(ThrottleInput);

        float gearRatio = Transmission.IsAutomatic
            ? Transmission.AutoGearRatios[Transmission.CurrentGear]
            : Transmission.ManualGearRatios[Transmission.CurrentGear];

        // ---- clutch ------------------------------------------------------
        // Disengage in neutral or when the engine has dropped below idle, so a
        // stalling engine can't drag the wheels. Re-engage only once it has
        // climbed past twice idle - a deliberate hysteresis band.
        if (gearRatio == 0f || CurrentSpeed < IdleSpeed)
        {
            if (PrimaryDrivetrain.AttachedEngine != null)
                PrimaryDrivetrain.Detach();
        }
        else if (IdleSpeed + IdleSpeed < CurrentSpeed)
        {
            if (PrimaryDrivetrain.AttachedEngine == null)
                PrimaryDrivetrain.Attach(CarSim);
        }

        // ---- free revving -------------------------------------------------
        // When declutched the drivetrain isn't setting engine speed, so the
        // engine integrates its own torque against its own inertia.
        if (PrimaryDrivetrain.AttachedEngine == null)
        {
            CurrentSpeed += ThrottleTorque / AngInertia * dt;
            CurrentSpeed = CurrentSpeed < 0f ? 0f : Mathf.Min(CurrentSpeed, MaxSpeed);
        }

        if (CurrentSpeed < 0f)
            Debug.Log("Negative RPM's on the engine!!!");

        CurrentRPM = CurrentSpeed * RadPerSecToRpm;

        // ---- gear change interpolation -------------------------------------
        if (WaitingOnGCL)
        {
            if (GCLTimer <= 0f)
            {
                WaitingOnGCL = false;
                Transmission.GearChanged = false;
                CurrentSpeed = CurrentRPM * RpmToRadPerSec;
            }
            else
            {
                // Throttle is cut for the duration and RPM is dragged from where
                // it was at the shift toward where the new gear puts it.
                float elapsed = GCL - GCLTimer;
                ThrottleTorque = 0f;
                CurrentRPM = (elapsed * CurrentRPM + GearChangedAtRPM * GCLTimer) / GCL;
                GCLTimer -= dt;
            }
        }

        // Power output in HP: T * omega / 746.
        CurrentHorsePower = ThrottleTorque * CurrentSpeed * InvWattsPerHp;

        // ---- engine rocking ------------------------------------------------
        float peakTorque = CalcTorqueAtFullThrottle(OptSpeed);
        float loadFactor = peakTorque != 0f ? ThrottleTorque / peakTorque * AngInertia : 0f;

        if (EngineVisualRef != null && EngineVisual != null)
        {
            // Roll the engine mesh about the ref pivot's forward axis.
            EngineVisual.rotation = EngineVisualRef.rotation
                * Quaternion.AngleAxis(loadFactor * 0.050000001f * Mathf.Rad2Deg, Vector3.forward);
            EngineVisual.position = EngineVisualRef.position;
        }

        // In neutral the reaction torque has nowhere to go through the wheels, so
        // it goes into the body instead - blip the throttle and the car rocks.
        if (Transmission.CurrentGear == 1)
            ApplyRockingTorque(loadFactor);
    }

    private void ApplyRockingTorque(float loadFactor)
    {
        if (CarSim == null || CarSim.Body == null) return;

        Vector3 axis;

        if (EngineVisualRef != null)
        {
            // Along the pivot's own forward axis, opposing the engine's rotation.
            axis = EngineVisualRef.forward * -(loadFactor * AngInertia);
        }
        else if (CarSim.DrivetrainType == DrivetrainType.FWD)
        {
            axis = new Vector3(loadFactor * AngInertia, 0f, 0f);
        }
        else
        {
            axis = new Vector3(0f, 0f, -(loadFactor * AngInertia));
        }

        // Scaled by the body's principal inertia, then rotated into world space.
        Vector3 inertia = CarSim.Body.inertiaTensor;
        Vector3 scaled = new Vector3(axis.x * inertia.x, axis.y * inertia.y, axis.z * inertia.z);

        Vector3 worldTorque = CarSim.Body.rotation * scaled;
        CarSim.Body.AddTorque(worldTorque, ForceMode.Force);
    }

    public void Read(TokenFileParser parser)
    {
        AngInertia = parser.Read("AngInertia", AngInertia);
        MaxHorsePower = parser.Read("MaxHorsePower", MaxHorsePower);
        IdleRPM = parser.Read("IdleRPM", IdleRPM);
        OptRPM = parser.Read("OptRPM", OptRPM);
        MaxRPM = parser.Read("MaxRPM", MaxRPM);
        GCL = parser.Read("GCL", GCL);
    }
}

public enum DrivetrainType
{
    RWD,
    FWD,
    AWD
}
