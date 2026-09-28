using UnityEngine;

/// <summary>
/// Faithful port of MM2 / AGE vehDrivetrain.
///
/// The entire drivetrain collapses to ONE scalar, Rate (driveshaft angular
/// velocity). Wheels are rigidly slaved to it through the differential; there is
/// no per-wheel spin integration anywhere in the original.
///
/// SIGN CONVENTION: Rate is NEGATIVE when driving forward. Wheel RotationRate
/// inherits this. See VehWheel for the full note.
///
/// CALL ORDER PER FIXED STEP:
///   1. VehDrivetrain.Update()   - raycasts + suspension (via ComputeDwtdw),
///                                 then solves Rate and assigns RotationRate
///   2. wheel.Update() for each  - tire forces onto the body
///   3. apply accumulated force/torque, apply NetPush
/// </summary>
[System.Serializable]
public class VehDrivetrain
{
    // Statics, from the original .data segment.
    public static float diffRatioMax = 1.25f;
    public static float diffRatioMaxHighSpeed = 1.03f;
    public static float diffRatioHighSpeedLevel = 50f;
    private static float dRHSLinv => 1f / diffRatioHighSpeedLevel;

    public const float Unbounded = 9.9999998e10f;
    public const int MaxWheels = 4;

    /// <summary>
    /// The shipping build seeds the bound search so that it never fires (see the
    /// header note). Leave false for a faithful port; set true only to experiment
    /// with the constraint solver the seeds appear to have disabled.
    /// </summary>
    public static bool EnableSpinConstraints = false;

    // ---------------------------------------------------------------- config
    [System.NonSerialized] public VehCarSim VehCarSim;
    [System.NonSerialized] public VehEngine AttachedEngine;
    [System.NonSerialized] public VehTransmission AttachedTransmission;

    [System.NonSerialized] public VehWheel[] Wheels = new VehWheel[MaxWheels];
    public int WheelCount;

    public float AngInertia = 5000f;
    public float BrakeDynamicCoef = 1f;
    public float BrakeStaticCoef = 1.2f;

    // ------------------------------------------------------------ live state
    public float Rate;
    public float DiffRatio = 1f;

    private readonly float[] wheelBounds = new float[MaxWheels];

    public void Reset()
    {
        Rate = 0f;
        DiffRatio = 1f;
    }

    public bool AddWheel(VehWheel wheel)
    {
        if (WheelCount == MaxWheels)
        {
            Debug.LogError("Too many wheels");
            return false;
        }
        Wheels[WheelCount++] = wheel;
        return true;
    }

    public void Attach(VehCarSim carSim)
    {
        VehCarSim = carSim;
        AttachedEngine = carSim.Engine;
        AttachedTransmission = carSim.Transmission;
    }

    public void Detach()
    {
        AttachedEngine = null;
        AttachedTransmission = null;
    }

    private float GearRatio()
    {
        var t = AttachedTransmission;
        return t.IsAutomatic ? t.AutoGearRatios[t.CurrentGear] : t.ManualGearRatios[t.CurrentGear];
    }

    // =====================================================================

    public void Update()
    {
        float dt = Time.fixedDeltaTime;
        float invDt = 1f / dt;

        // ---- brake torque magnitude ----------------------------------------
        // A constant 50 of parasitic drag is always present. Static coefficient
        // applies only when the shaft is already stopped.
        float brakeTorque = 50f;
        float brakeCoef = Rate == 0f ? BrakeStaticCoef : BrakeDynamicCoef;
        for (int i = 0; i < WheelCount; i++)
            brakeTorque += Wheels[i].InputBrakeAmount * brakeCoef;

        // ---- engine contribution --------------------------------------------
        float shaftTorque = 0f;
        if (AttachedEngine != null)
        {
            float gearRatio = GearRatio();
            float throttleTerm = gearRatio * AttachedEngine.ThrottleTorque;

            // Reconciliation term: (gearRatio * Rate) is the engine speed implied
            // by the current shaft speed, and CurrentTorque is the engine's actual
            // speed. Their sum is a velocity error, converted to a torque by
            // inertia / dt and reflected through the gear.
            float inertiaTerm = (gearRatio * Rate + AttachedEngine.CurrentTorque)
                                * AttachedEngine.AngInertia * invDt * gearRatio;

            shaftTorque = inertiaTerm + throttleTerm;
        }

        // ---- tire reaction, closing the loop from VehWheel.Update ------------
        for (int i = 0; i < WheelCount; i++)
            shaftTorque -= Wheels[i].LongForceRadScaled;

        // ---- apply brake, with stiction -------------------------------------
        float netTorque;
        bool brakeMayLock = false;

        if (Rate == 0f)
        {
            // Stopped: if the applied torque can't overcome static brake, the
            // shaft stays locked at zero.
            if (shaftTorque < 0f)
            {
                float t = brakeTorque + shaftTorque;
                netTorque = t > 0f ? 0f : t;
            }
            else
            {
                float t = shaftTorque - brakeTorque;
                netTorque = t < 0f ? 0f : t;
            }
        }
        else
        {
            float signedBrake = brakeTorque * Mathf.Sign(Rate);
            // Brake dominates: flag a zero-crossing lock after the integration.
            if (Mathf.Abs(shaftTorque) <= Mathf.Abs(signedBrake))
                brakeMayLock = true;
            netTorque = shaftTorque + signedBrake;
        }

        // ---- reflected engine inertia ----------------------------------------
        float reflectedInertia;
        if (AttachedEngine != null && AttachedTransmission != null)
        {
            float gearRatio = GearRatio();
            reflectedInertia = gearRatio * AttachedEngine.AngInertia * gearRatio + 0.02f;
        }
        else
        {
            reflectedInertia = VehCarSim.Mass * 0.0049999999f;
        }

        // ---- per-wheel step ---------------------------------------------------
        // Called for its SIDE EFFECTS: raycast, tire basis, surface material and
        // suspension force. The returned bound is vestigial in the shipping build.
        for (int i = 0; i < WheelCount; i++)
            Wheels[i].ComputeDwtdw(netTorque, out wheelBounds[i]);

        float driveTorque = -netTorque;

        // ---- differential ------------------------------------------------------
        if (diffRatioMax > 1f)
        {
            float absRate = Mathf.Abs(Rate);
            float maxRatio = absRate < diffRatioHighSpeedLevel
                ? ((diffRatioHighSpeedLevel - absRate) * diffRatioMax
                   + diffRatioMaxHighSpeed * absRate) * dRHSLinv
                : diffRatioMaxHighSpeed;
            float minRatio = 1f / maxRatio;

            if (WheelCount <= 1 || (Rate < 0.001f && Rate > -0.001f))
            {
                DiffRatio = 1f;
            }
            else
            {
                // Torque imbalance across each pair of wheels.
                float imbalance = 0f;
                for (int i = 0; i + 1 < WheelCount; i += 2)
                    imbalance += Wheels[i].LongForceRadScaled - Wheels[i + 1].LongForceRadScaled;

                // NOTE: the original also sums the per-wheel inertia outputs into
                // the denominator here, but those are always zero in the shipping
                // build, so only AngInertia remains.
                float target = imbalance / (AngInertia * Rate) + DiffRatio;
                target = Mathf.Clamp(target, minRatio, maxRatio);

                // Hard 90/10 smoothing - the differential drifts, it doesn't snap.
                DiffRatio = (DiffRatio * 9f + target) * 0.1f;
            }
        }

        // ---- shaft speed solve --------------------------------------------------
        float effectiveInertia = AngInertia;
        float newRate = Rate;
        float remainingTorque = driveTorque;
        float rateBase = Rate;

        int iterations = 0;
        while (true)
        {
            iterations++;
            newRate = remainingTorque / (dt * effectiveInertia + reflectedInertia) * dt + rateBase;

            if (!EnableSpinConstraints)
                break;

            // Tightest bound in the direction the shaft is being driven.
            float bound;
            int boundIndex = 0;
            if (netTorque >= 0f)
            {
                bound = float.PositiveInfinity;
                for (int i = 0; i < WheelCount; i++)
                    if (wheelBounds[i] < bound) { bound = wheelBounds[i]; boundIndex = i; }
            }
            else
            {
                bound = float.NegativeInfinity;
                for (int i = 0; i < WheelCount; i++)
                    if (wheelBounds[i] > bound) { bound = wheelBounds[i]; boundIndex = i; }
            }

            bool satisfied = (netTorque >= 0f || newRate <= bound)
                          && (netTorque <= 0f || newRate >= bound);
            if (satisfied || iterations > 20)
                break;

            // Clamp to the bound, deduct the torque that consumed, drop this
            // wheel out of the search and go round again.
            remainingTorque -= (bound - rateBase) * effectiveInertia;
            rateBase = bound;
            wheelBounds[boundIndex] = Mathf.Sign(netTorque) * -Unbounded;
        }

        float resolvedRate = newRate;

        // Brake lock: if the shaft crossed zero this step under a dominant brake,
        // stop it dead rather than letting it reverse.
        if (brakeMayLock)
        {
            if ((resolvedRate < 0f && Rate > 0f) || (Rate < 0f && resolvedRate > 0f))
                resolvedRate = 0f;
        }

        // ---- engine limits -------------------------------------------------------
        // MaxTorque is the engine's max ANGULAR VELOCITY (see VehEngine's header
        // note on the original's naming), so dividing by the gear ratio gives a
        // max shaft speed. This clamp is dimensionally a speed limit.
        if (AttachedEngine != null && AttachedTransmission != null)
        {
            float gearRatio = GearRatio();
            float sign = Mathf.Sign(resolvedRate);
            float maxShaftSpeed = AttachedEngine.MaxTorque / Mathf.Abs(gearRatio);
            resolvedRate = Mathf.Min(Mathf.Abs(resolvedRate), maxShaftSpeed) * sign;
        }

        Rate = resolvedRate;

        if (AttachedEngine != null && AttachedTransmission != null)
        {
            float gearRatio = GearRatio();

            // Drive the engine speed from the shaft speed. Rate is negative when
            // moving forward, so this comes out positive.
            float engineSpeed = -(gearRatio * Rate);

            if (engineSpeed >= 0f)
            {
                if (engineSpeed > AttachedEngine.MaxTorque)
                {
                    // Rev limiter: pin the engine and back-solve the shaft speed
                    // that corresponds to it.
                    engineSpeed = AttachedEngine.MaxTorque;
                    Rate = -(engineSpeed / gearRatio);
                }
                AttachedEngine.CurrentTorque = engineSpeed;
            }
            else
            {
                // Shaft is trying to drive the engine backwards: stall it.
                Rate = 0f;
            }
        }

        // ---- distribute to the wheels ---------------------------------------------
        float invDiffRatio = 1f / DiffRatio;
        for (int i = 0; i + 1 < WheelCount; i += 2)
        {
            Wheels[i].RotationRate = DiffRatio * Rate;
            Wheels[i + 1].RotationRate = invDiffRatio * Rate;
        }

        // Odd wheel out gets the shaft speed ungeared.
        if ((WheelCount & 1) != 0)
            Wheels[WheelCount - 1].RotationRate = Rate;
    }

    public void Read(TokenFileParser parser)
    {
        AngInertia = parser.Read("AngInertia", AngInertia);
        BrakeDynamicCoef = parser.Read("BrakeDynamicCoef", BrakeDynamicCoef);
        BrakeStaticCoef = parser.Read("BrakeStaticCoef", BrakeStaticCoef);
    }

    public void CopyVars(VehDrivetrain other)
    {
        AngInertia = other.AngInertia;
        BrakeDynamicCoef = other.BrakeDynamicCoef;
        BrakeStaticCoef = other.BrakeStaticCoef;
    }
}