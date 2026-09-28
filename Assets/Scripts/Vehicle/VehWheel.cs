using UnityEngine;

/// <summary>
/// Faithful port of MM2 / AGE vehWheel.
///
/// SIGN CONVENTION (important, and inherited from the original):
///   RotationRate is NEGATIVE when the car is driving forward.
///   Rolling without slip satisfies  RotationRate * Radius + wheelSpaceVelocity.y == 0.
///   Drivetrain Rate follows the same convention; wheels get Rate * DiffRatio.
///
/// WHEEL SPACE (all three axes derived from the CONTACT NORMAL, not the chassis):
///   x = lateral,      along sidewaysDirection
///   y = longitudinal, along -rearwardsDirection
///   z = normal,       along LastSurfaceNormal
///
/// CALL ORDER PER FIXED STEP, for every wheel:
///   1. ComputeDwtdw(appliedTorque, out bound)   - raycast, basis, material, suspension
///   2. drivetrain solves for Rate, assigns RotationRate to each wheel
///   3. Update()                                 - tire forces onto the body
/// Breaking this order breaks the LongForceRadScaled -> Rate feedback loop.
/// </summary>
[System.Serializable]
public class VehWheel
{
    // Sentinel meaning "this wheel imposes no constraint on shaft speed".
    public const float Unbounded = 9.9999998e10f;

    /// <summary>
    /// Relaxation rate for tire deflection while sliding, as a fraction of the
    /// wheel's surface speed per second. File-scope global in the original.
    ///
    /// At 0.1 the deflection relaxes toward the breakaway limit fairly slowly,
    /// which is what gives the tire its relaxation length - force eases in and
    /// out of a slide instead of snapping. Raising it makes breakaway abrupt.
    /// </summary>
    public static float LRelaxCoef = 0.1f;

    /// <summary>
    /// Global friction multiplier for weather. 1 is dry; lower values are rain
    /// and so on. Applied to every wheel on every surface.
    /// </summary>
    public static float WeatherFriction = 1f;

    // ---------------------------------------------------------------- config
    /// <summary>Shared body state. ONE instance per car - NetPush arbitrates
    /// between all four wheels, so they must all point at the same object.</summary>
    [System.NonSerialized] public PhInertialCS ICS;
    [System.NonSerialized] public Transform CarTransform;

    public Rigidbody Body => ICS.Body;

    public Vector3 Center;              // wheel position in chassis space
    public float Radius;
    public float Width;

    public float SteeringLimit;         // 0 => non-steered
    public float SteerAmount;           // current steer angle, radians
    public float CamberLimit;
    public float WobbleAmount;

    public float SuspensionExtent;      // max droop below rest
    public float SuspensionLimit;       // max compression above rest
    public float SuspensionMaxForce;    // spring rate
    public float SuspensionDampForce;   // damper rate
    public float SuspensionRestingPosition; // progression coefficient, NOT a position
    public float NormalLoad;            // static load share

    public float StaticFric = 1.9f;
    public float SlidingFric = 2.0f;
    public float OptimumSlipPercent = 0.14f;
    public float InvSquaredOptimumSlipPercent;

    public float DispLimitLatLoaded;    // lateral tire stiffness  (N/m)
    public float DispLimitLongLoaded;   // longitudinal tire stiffness (N/m)
    public float DampCoefLatLoaded;
    public float DampCoefLongLoaded;

    public float TireDragCoefLat = 0.05f;
    public float TireDragCoefLong = 0.02f;

    public float TireDispLimitLong = 0.075f;
    public float TireDispLimitLat = 0.075f;
    public float TireDampCoefLong = 0.25f;
    public float TireDampCoefLat = 0.25f;

    public int WheelFlags;              // bit 2 (value 4) => no motor contribution

    public LayerMask GroundMask = ~0;

    // ------------------------------------------------------------ live state
    // -------------------------------------------------------------- render pose
    // Written every fixed step by UpdateVisualTransform, grounded or not.
    // CHASSIS SPACE on purpose: assign these to a wheel object parented under the
    // car and it inherits the Rigidbody's interpolation for free. Includes the
    // authored pivot (via Center), so don't offset the renderer separately.
    public Vector3 LocalPosition;
    public Quaternion LocalRotation = Quaternion.identity;

    // Hub before travel and spin: steer only. For brake calipers, suspension
    // arm IK, anything that shouldn't rotate with the wheel.
    public Vector3 LocalHubPosition;
    public Quaternion LocalHubRotation = Quaternion.identity;

    public Vector3 WorldPosition => CarTransform.TransformPoint(LocalPosition);
    public Quaternion WorldRotation => CarTransform.rotation * LocalRotation;
    public Pose WorldPose => new Pose(WorldPosition, WorldRotation);
    public Pose HubWorldPose => new Pose(
        CarTransform.TransformPoint(LocalHubPosition),
        CarTransform.rotation * LocalHubRotation);

    public bool IsGrounded;
    public bool LastGroundedStatus;
    public bool BottomedOut;
    public bool MajorlySlipping;

    public Vector3 LastHitPosition;
    public Vector3 LastSurfaceNormal;
    public Vector3 ContactPoint;

    public Vector3 rearwardsDirection;
    public Vector3 sidewaysDirection;
    public Vector3 wheelSpaceVelocity;

    public float RotationRate;          // rad/s, negative when driving forward
    public float AccumulatedRotation;
    public float CamberAmount;

    public float TargetSuspensionTravel;
    public float SuspensionCompressionRate;
    public float CurrentSuspensionForce;
    public float SuspensionForceCopy;   // normal load N used by the tire model
    public float DampingCoefficient;   // implicit-contact Jacobian term (see note)

    public float MaterialFriction = 1f;
    public LevelPhysMaterial CurrentMaterial;
    public float MaterialDrag;
    public float MaterialDepth;
    public float MaterialHeight;
    public float MaterialWidth;
    public float BumpDisplacement;

    // Persistent brush-tire deflection. MUST survive between steps - this is
    // where the handling character lives.
    public float CurrentTireDispLat;
    public float CurrentTireDispLong;

    public float LatSlipPercent;
    public float LongSlipPercent;
    public float motorAdjustedForwardVelocity;

    public float LatForce;
    public float LongForce;
    public float LongForceRadScaled;    // fed back into the drivetrain as wheel torque
    public float LastSlippage;

    public float InputBrakeAmount;

    /// <summary>Per-wheel brake gains. "Loaded" in the original - these are the
    /// load-scaled values produced by ComputeConstants, not raw authored ones.</summary>
    public float BrakeCoefLoaded = 1f;
    public float HandbrakeCoefLoaded = 1f;

    /// <summary>
    /// Ackermann coefficient. Positive values make the inside wheel turn more
    /// than the outside one; zero gives parallel steering.
    /// </summary>
    public float SteeringOffset;

    // ---- authored parameters that SetNormalLoad derives from ----------------
    public float SuspensionFactor = 1f;      // >= 0.75, progression amount
    public float SuspensionDampCoef;         // damping ratio, not a rate

    public float HandbrakeCoef;
    public float BrakeCoef;

    /// <summary>
    /// vehWheel::SetInputs.
    ///
    /// Three things worth noting, all of them in the original:
    ///  - the steer angle is NEGATED against SteeringLimit
    ///  - an Ackermann correction is applied, signed by which side of the car
    ///    this wheel sits on, so the inner wheel sweeps a tighter arc
    ///  - a wheel with flag 4 set is LOCKED: its brake torque is pinned to
    ///    exactly the maximum the tyre can transmit, so it can never rotate.
    ///    That is also why Update() drops the omega*R term for these wheels.
    /// </summary>
    public void SetInputs(float steer, float brake, float handBrake)
    {
        float angle = (steer * SteeringLimit);

        // Ackermann: sign(Center.x) picks which side of the car this wheel is on.
        SteerAmount = (1f - angle * SteeringOffset * Mathf.Sign(Center.x)) * angle;

        if ((WheelFlags & 4) != 0)
        {
            // Locked wheel: brake torque == the friction limit at this load.
            InputBrakeAmount = StaticFric * SuspensionForceCopy * MaterialFriction * Radius;
        }
        else
        {
            InputBrakeAmount = brake * BrakeCoefLoaded + handBrake * HandbrakeCoefLoaded;
        }
    }

    private float bumpPhase;            // vehWheel::unknown480

    // Per-step output, applied by the car after all wheels have run.
    public Vector3 AccumulatedForce;
    public Vector3 AccumulatedTorque;

    /// <summary>
    /// vehWheel::SetNormalLoad. Derives every "Loaded" quantity from the static
    /// load this wheel carries, so the authored parameters are load-independent
    /// ratios rather than absolute rates.
    ///
    /// The springs and tires are all critically-damped-scaled: each damping rate is
    /// 2 * ratio * sqrt(k * m), the standard 2*zeta*sqrt(km) form. That is why the
    /// authored Coef values are dimensionless and usually near 1.
    /// </summary>
    public void SetNormalLoad(float normalLoad)
    {
        NormalLoad = normalLoad;

        if (SuspensionFactor < 0.75f)
            SuspensionFactor = 0.75f;

        // ---- suspension --------------------------------------------------
        float inv = 1f / ((SuspensionLimit + SuspensionExtent) * SuspensionExtent);

        SuspensionMaxForce = (SuspensionFactor * SuspensionExtent + SuspensionLimit)
                             * inv * normalLoad;

        // Progression coefficient, scaled so that SuspensionFactor == 1 gives a
        // perfectly linear spring.
        SuspensionRestingPosition = (SuspensionFactor - 1f) * inv * normalLoad
                                    / SuspensionMaxForce;

        float sSqrt = Mathf.Sqrt(SuspensionMaxForce * normalLoad);
        SuspensionDampForce = 2f * sSqrt * SuspensionDampCoef;

        // ---- tire ----------------------------------------------------------
        // Effective mass at the contact patch, from the load and gravity.
        float mass = -(normalLoad / Physics.gravity.y);
        float twice = normalLoad + normalLoad;

        DispLimitLongLoaded = twice / TireDispLimitLong;
        DampCoefLongLoaded = 2f * Mathf.Sqrt(DispLimitLongLoaded * mass) * TireDampCoefLong;

        DispLimitLatLoaded = twice / TireDispLimitLat;
        DampCoefLatLoaded = 2f * Mathf.Sqrt(DispLimitLatLoaded * mass) * TireDampCoefLat;

        // ---- brakes ----------------------------------------------------------
        // Maximum torque the tyre can transmit at this load, scaled per channel.
        float maxTorque = StaticFric * Radius * normalLoad;
        BrakeCoefLoaded = maxTorque * BrakeCoef;
        HandbrakeCoefLoaded = maxTorque * HandbrakeCoef;
    }

    /// <summary>
    /// vehWheel::AddNormalLoad. Adjusts the load and re-derives, with a floor of 1
    /// so the sqrt terms can never go imaginary.
    /// </summary>
    public void AddNormalLoad(float delta)
    {
        float load = delta + NormalLoad;
        SetNormalLoad(load < 1f ? 1f : load);
    }

    // =====================================================================
    // ComputeDwtdw - raycast, tire basis, surface material, suspension
    //
    // Called by the drivetrain BEFORE the shaft solve. In the shipping build the
    // returned bound is unused (see VehDrivetrain), so this runs for its side
    // effects: IsGrounded, the contact basis, wheelSpaceVelocity, the surface
    // material terms, and SuspensionForceCopy - which is the normal load every
    // tire force scales off.
    // =====================================================================

    public void ComputeDwtdw(float appliedTorque, out float spinBound)
    {
        float dt = Time.fixedDeltaTime;

        // ---- wheel transform in world space ----------------------------------
        Quaternion steer = SteeringLimit == 0f
            ? Quaternion.identity
            : Quaternion.AngleAxis(SteerAmount * Mathf.Rad2Deg, Vector3.up);

        Vector3 localCenter = Center;
        if (SteeringLimit != 0f)
        {
            // Steering pivots about the inside edge of the tyre, not its centre.
            float offset = Mathf.Sign(Center.x) * Width * 0.5f;
            localCenter.x -= offset;
            localCenter += steer * (Vector3.right * offset);
        }

        Vector3 origin = CarTransform.TransformPoint(localCenter);
        Quaternion worldRot = CarTransform.rotation * steer;
        Vector3 wheelUp = worldRot * Vector3.up;
        Vector3 wheelRight = worldRot * Vector3.right;

        // ---- suspension raycast -----------------------------------------------
        int doNotHitTheseMask = LayerMask.GetMask("PlayerVehicleBody", "VehicleBody", "Banger");
        int layerMask = ~(doNotHitTheseMask);
        GroundMask = layerMask;

        float above = SuspensionLimit + 0.30000001f;
        float below = SuspensionExtent + Radius;
        float rayLength = above + below;

        Vector3 rayStart = origin + wheelUp * above;
        IsGrounded = Physics.Raycast(rayStart, -wheelUp, out RaycastHit hit, rayLength, GroundMask);
        bool upsideDownOrVertical = false;
        float normalDotWheelUp = 0f;

        if (IsGrounded)
        {
            normalDotWheelUp = Vector3.Dot(hit.normal, wheelUp);

            // Reject near-parallel contacts - the suspension has no leverage.
            if (normalDotWheelUp < 0.02f)
            {
                IsGrounded = false;
            }
            else
            {
                LastHitPosition = hit.point;
                LastSurfaceNormal = hit.normal;
                ContactPoint = hit.point;

                // Basis is built from the CONTACT NORMAL, not the chassis, so the
                // tire frame follows the road on cambered geometry.
                rearwardsDirection = Vector3.Cross(LastSurfaceNormal, wheelRight);

                if (rearwardsDirection.sqrMagnitude < 0.02f)
                {
                    IsGrounded = false;
                }
                else
                {
                    rearwardsDirection.Normalize();
                    sidewaysDirection = Vector3.Cross(rearwardsDirection, LastSurfaceNormal);
                    upsideDownOrVertical = Mathf.Abs(LastSurfaceNormal.y) < 0.001f;
                }
            }
        }

        if (!IsGrounded)
        {
            IsGrounded = false;
            CalcSuspensionForce(-SuspensionExtent, false, 0f);
            SuspensionForceCopy = CurrentSuspensionForce;
            spinBound = Mathf.Sign(appliedTorque) * -1.0e10f;
            return;
        }

        // ---- velocity at the contact point, in wheel space ----------------------
        // Filtered, not raw: last step's NetPush moved the body positionally
        // without touching its velocity, and feeding the raw closing speed to the
        // brush model double-counts what the push already resolved.
        Vector3 pointVelocity = ICS.GetLocalFilteredVelocity2(ContactPoint);

        wheelSpaceVelocity = new Vector3(
            Vector3.Dot(pointVelocity, sidewaysDirection),
            Vector3.Dot(pointVelocity, -rearwardsDirection),
            Vector3.Dot(pointVelocity, LastSurfaceNormal));

        motorAdjustedForwardVelocity = (WheelFlags & 4) != 0
            ? wheelSpaceVelocity.y
            : RotationRate * Radius + wheelSpaceVelocity.y;

        // ---- surface material ----------------------------------------------------
        CurrentMaterial = ResolveMaterial(hit);

        if (CurrentMaterial != null)
        {
            float contactSpeed = Mathf.Sqrt(
                wheelSpaceVelocity.y * wheelSpaceVelocity.y +
                wheelSpaceVelocity.x * wheelSpaceVelocity.x);

            BumpDisplacement = GetBumpDisplacement(contactSpeed);
            MaterialDrag = CurrentMaterial.Drag;
            MaterialFriction = CurrentMaterial.Friction;

            // Depth only accumulates while the tyre is actually spinning up the
            // surface, and eases in rather than snapping - this is the sand sink.
            float targetDepth = MajorlySlipping ? CurrentMaterial.Depth : 0f;
            float depthRate = Mathf.Abs(RotationRate) * Radius * 0.1f;

            if (MaterialDepth < targetDepth)
                MaterialDepth = Mathf.Min(MaterialDepth + dt * depthRate, targetDepth);
            else if (MaterialDepth > targetDepth)
                MaterialDepth = Mathf.Max(MaterialDepth - dt * depthRate, targetDepth);

            MaterialHeight = CurrentMaterial.Height;
            MaterialWidth = CurrentMaterial.Width;
        }
        else
        {
            BumpDisplacement = 0f;
            MaterialDrag = 0f;
            MaterialFriction = 1f;
            MaterialDepth = 0f;
            MaterialHeight = 0f;
            MaterialWidth = 0f;
        }

        if (upsideDownOrVertical)
            MaterialFriction = 0f;

        MaterialFriction *= WeatherFriction;

        // Per-car handling adjustment, applied only on low-grip surfaces.
        if (CarSim != null && MaterialFriction < 1f)
        {
            float h = CarSim.CarFrictionHandling;
            MaterialFriction = h >= 1f
                ? MaterialFriction / h
                : (1f - MaterialFriction) * (1f - h) + MaterialFriction;
        }

        // ---- suspension ------------------------------------------------------------
        float compression = above + Radius
                          - (rayLength * (hit.distance / rayLength)
                             - BumpDisplacement + MaterialDepth);

        CalcSuspensionForce(compression, true, normalDotWheelUp);
        SuspensionForceCopy = CurrentSuspensionForce;

        // ---- spin bound (vestigial in the shipping build) ----------------------------
        // Kept because the geometry is correct and cheap: the envelope of wheel
        // speeds that keep longitudinal slip inside the rising side of the
        // friction curve. Reconstructed; the original's final branch selecting
        // between these four candidates could not be fully resolved.
        float omegaFree = -wheelSpaceVelocity.y / Radius;
        float lo = (1f - OptimumSlipPercent) * omegaFree;
        float hi = (1f + OptimumSlipPercent) * omegaFree;
        float atCurrentSlip =
            (1f - Mathf.Clamp(LongSlipPercent, -OptimumSlipPercent, OptimumSlipPercent)) * omegaFree;

        float near = omegaFree >= 0f ? Mathf.Min(omegaFree, atCurrentSlip)
                                     : Mathf.Max(omegaFree, atCurrentSlip);
        float far = omegaFree >= 0f ? Mathf.Max(omegaFree, atCurrentSlip)
                                    : Mathf.Min(omegaFree, atCurrentSlip);

        if (appliedTorque < 0f)
        {
            if (wheelSpaceVelocity.y > 0f)
                spinBound = far >= RotationRate ? far : (RotationRate <= lo ? lo : Unbounded);
            else
                spinBound = RotationRate <= near ? near : (RotationRate > hi ? Unbounded : hi);
        }
        else
        {
            if (wheelSpaceVelocity.y <= 0f)
                spinBound = far <= RotationRate ? far : (RotationRate >= lo ? lo : -Unbounded);
            else
                spinBound = RotationRate >= near ? near : (RotationRate >= hi ? hi : -Unbounded);
        }
    }

    [System.NonSerialized] public VehCarSim CarSim;

    /// <summary>
    /// Map the hit surface to its physics material. Wire this to whatever carries
    /// the PSDL element type - the collider identifies the element directly.
    /// </summary>
    private LevelPhysMaterial ResolveMaterial(RaycastHit hit)
    {
        var provider = hit.collider != null ? hit.collider.GetComponent<BoundBase>() : null;
        if (provider == null)
        {
            return LevelMaterialManager.GetDefault();
        }
        return provider.GetMaterial(hit.triangleIndex);
    }

    // =====================================================================
    // Friction curve
    // =====================================================================

    /// <summary>
    /// Parabolic friction curve: mu(s) = mu_s * s * (2*sopt - s) / sopt^2,
    /// peaking exactly at mu_s when s == sopt, then falling off, floored at mu_d.
    /// <paramref name="visual"/> is a 0..1 slip indicator for particles/audio only.
    /// </summary>
    public float ComputeFriction(float slip, out float visual)
    {
        float s = Mathf.Abs(slip);
        float muStatic = StaticFric * MaterialFriction;
        float muSliding = SlidingFric * MaterialFriction;

        float mu = (OptimumSlipPercent + OptimumSlipPercent - s)
                   * InvSquaredOptimumSlipPercent * muStatic * s;

        if (s <= OptimumSlipPercent)
        {
            visual = s * 0.5f / OptimumSlipPercent;
        }
        else if (muSliding >= mu)
        {
            mu = muSliding;
            visual = 1f;
        }
        else
        {
            visual = ((mu - muSliding) * 0.5f + muStatic - mu) / (muStatic - muSliding);
        }

        return Mathf.Max(mu, 0f);
    }

    // =====================================================================
    // Suspension
    // =====================================================================

    /// <param name="compression">travel; ignored when not grounded</param>
    /// <param name="normalDotWheelUp">contact normal . wheel up axis, used for the Jacobian</param>
    public void CalcSuspensionForce(float compression, bool grounded, float normalDotWheelUp)
    {
        float dt = Time.fixedDeltaTime;
        float invDt = 1f / dt;

        float lastTravel = TargetSuspensionTravel;
        BottomedOut = false;

        float droopLimit = -SuspensionExtent;
        bool atDroopLimit;

        if (grounded)
        {
            TargetSuspensionTravel = compression;
            if (compression >= droopLimit)
            {
                atDroopLimit = false;
            }
            else
            {
                TargetSuspensionTravel = droopLimit;
                atDroopLimit = true;
            }
        }
        else
        {
            TargetSuspensionTravel = droopLimit;
            atDroopLimit = true;
        }

        // Compression rate is hard-clamped BEFORE it reaches the damper. This is
        // what stops a kerb strike producing an impulse spike.
        float rate = Mathf.Clamp((TargetSuspensionTravel - lastTravel) * invDt, -10f, 10f);
        SuspensionCompressionRate = rate;

        // Progressive rate: stiffness (and damping) rise with compression.
        float progression = SuspensionRestingPosition * TargetSuspensionTravel + 1f;
        float force = (TargetSuspensionTravel * SuspensionMaxForce + rate * SuspensionDampForce)
                      * progression + NormalLoad;
        CurrentSuspensionForce = force;

        if (force < 0f)
        {
            // Spring can't pull. Zero the force and back-solve travel to where the
            // force would be zero 
            CurrentSuspensionForce = 0f;
            float k = dt * SuspensionMaxForce;
            float relaxed = (lastTravel * SuspensionDampForce - k * SuspensionExtent)
                            / (k + SuspensionDampForce);
            TargetSuspensionTravel = relaxed;
            SuspensionCompressionRate = (relaxed - lastTravel) * invDt;
            DampingCoefficient = 0f;
            return;
        }

        bool didBottomOut = false;
        float bottomOutForce = 0f;
        float bottomOutJacobian = 0f;

        if (TargetSuspensionTravel > SuspensionLimit)
        {
            // Bottoming out: take a real collision impulse along the normal.
            float impulse = CalcCollisionNoFriction(LastSurfaceNormal, wheelSpaceVelocity.z, ContactPoint);
            didBottomOut = true;
            bottomOutForce = invDt * impulse * 0.25f;
            bottomOutJacobian = Mathf.Abs(wheelSpaceVelocity.z) > 1e-9f
                              ? -(bottomOutForce / wheelSpaceVelocity.z)
                              : 0f;

            float push = (TargetSuspensionTravel - SuspensionLimit) * normalDotWheelUp;

            const float slop = 0.005f;
            const float beta = 0.15f;

            float overlap = (TargetSuspensionTravel - SuspensionLimit) * normalDotWheelUp;
            float residual = Mathf.Max(0f, overlap - slop);

            if (residual > 0f)
                ICS.CalcNetPush(LastSurfaceNormal * (residual * beta));

            BottomedOut = true;
            TargetSuspensionTravel = SuspensionLimit;
            RecomputeForceAfterClamp(lastTravel, invDt);
        }
        else if (droopLimit > TargetSuspensionTravel)
        {
            TargetSuspensionTravel = droopLimit;
            RecomputeForceAfterClamp(lastTravel, invDt);
        }
        else if (!atDroopLimit)
        {
            // Normal grounded case: contact stiffness (force per unit displacement)
            // for the original's implicit contact solver. See note in the header.
            DampingCoefficient = Mathf.Abs(normalDotWheelUp) > 1e-9f
                ? dt * SuspensionMaxForce / normalDotWheelUp + progression * SuspensionDampForce
                : 0f;
            return;
        }

        DampingCoefficient = 0f;
        if (didBottomOut)
        {
            CurrentSuspensionForce += bottomOutForce;
            DampingCoefficient = bottomOutJacobian;
        }
    }

    private void RecomputeForceAfterClamp(float lastTravel, float invDt)
    {
        float progression = SuspensionRestingPosition * TargetSuspensionTravel + 1f;
        float rate = Mathf.Clamp((TargetSuspensionTravel - lastTravel) * invDt, -10f, 10f);
        SuspensionCompressionRate = rate;
        CurrentSuspensionForce = (TargetSuspensionTravel * SuspensionMaxForce
                                  + rate * SuspensionDampForce) * progression + NormalLoad;
    }

    // =====================================================================
    // Surface roughness
    // =====================================================================

    /// <summary>
    /// Road-roughness oscillator. Advances a phase accumulator at the contact
    /// speed and returns a sine perturbation. The frand() term makes it
    /// stochastic rather than a pure tone.
    /// </summary>
    public float GetBumpDisplacement(float speed)
    {
        if (CurrentMaterial == null || CurrentMaterial.Height == 0f)
            return 0f;

        bumpPhase += (Random.value + 0.61799997f) * Time.fixedDeltaTime * speed;
        bumpPhase = Mathf.Repeat(bumpPhase, CurrentMaterial.Width);

        float result = Mathf.Sin(bumpPhase * 2f * Mathf.PI / CurrentMaterial.Width)
                       * CurrentMaterial.Height;

        return speed < 1f ? result * speed : result;
    }

    /// <summary>Visual-only sag so the wheel mesh sinks under load.</summary>
    public float GetVisualDispVert()
    {
        float max = Radius * 0.30000001f;
        float sag = Radius * 0.050000001f * CurrentSuspensionForce / NormalLoad;
        if (sag < 0f) return 0f;
        return sag <= max ? sag : max;
    }

    // =====================================================================
    // Main tire update
    // =====================================================================
    public void Update(Vector3 lastBodyVelocity, Vector3 lastBodyAngularVelocity)
    {
        ComputeConstants();

        float dt = Time.fixedDeltaTime;
        float invDt = 1f / dt;

        AccumulatedForce = Vector3.zero;
        AccumulatedTorque = Vector3.zero;

        if (!IsGrounded)
        {
            LastGroundedStatus = false;
            LastSlippage = 0f;
            MajorlySlipping = false;
            CurrentTireDispLat = 0f;
            CurrentTireDispLong = 0f;
            LongForceRadScaled = 0f;
            LatForce = 0f;
            LongForce = 0f;
            UpdateVisualTransform(dt);
            return;
        }

        LastGroundedStatus = true;

        if (DampingCoefficient > 0f)
        {
            Vector3 predicted = TargetSuspensionTravel >= SuspensionLimit
                ? Vector3.zero
                : LastSurfaceNormal * (SuspensionMaxForce * SuspensionCompressionRate * dt);

            ICS.AccumulateContact(predicted, ContactPoint, DampingCoefficient, LastSurfaceNormal);
        }

        float wheelSurfaceSpeed = RotationRate * Radius;

        // ---- slip ratios --------------------------------------------------
        if (wheelSpaceVelocity.x == 0f)
        {
            LatSlipPercent = 0f;
        }
        else
        {
            float absLong = Mathf.Abs(wheelSpaceVelocity.y);
            LatSlipPercent = Mathf.Abs(wheelSpaceVelocity.x) > absLong
                ? Mathf.Sign(wheelSpaceVelocity.x)
                : wheelSpaceVelocity.x / absLong;
        }

        motorAdjustedForwardVelocity = (WheelFlags & 4) != 0
            ? wheelSpaceVelocity.y
            : wheelSurfaceSpeed + wheelSpaceVelocity.y;

        if (motorAdjustedForwardVelocity == 0f)
        {
            LongSlipPercent = 0f;
        }
        else
        {
            float absLong = Mathf.Abs(wheelSpaceVelocity.y);
            LongSlipPercent = absLong < Mathf.Abs(motorAdjustedForwardVelocity)
                ? Mathf.Sign(motorAdjustedForwardVelocity)
                : motorAdjustedForwardVelocity / absLong;
        }

        // ---- deflection limits at breakaway --------------------------------
        float longDisp = Mathf.Sign(motorAdjustedForwardVelocity) * SuspensionForceCopy / DispLimitLongLoaded;
        float latDisp = Mathf.Sign(wheelSpaceVelocity.x) * SuspensionForceCopy / DispLimitLatLoaded;

        float posDeltaX = dt * wheelSpaceVelocity.x;
        float posDeltaY = dt * motorAdjustedForwardVelocity;

        float muStatic = StaticFric * MaterialFriction;

        // ---- longitudinal friction ----------------------------------------
        float longFriction = muStatic;
        float longFrictionDisp = muStatic * longDisp;
        float longVisual = 0f;
        bool longSkipped = false;

        if (Mathf.Abs(LongSlipPercent) > OptimumSlipPercent)
        {
            // If this step's deflection increment would carry us across the
            // breakaway point anyway, skip the curve evaluation.
            if (posDeltaY <= 0f)
            {
                if (CurrentTireDispLong > longFrictionDisp
                    && longFrictionDisp - CurrentTireDispLong <= posDeltaY)
                    longSkipped = true;
            }
            else
            {
                if (CurrentTireDispLong < longFrictionDisp
                    && longFrictionDisp - CurrentTireDispLong >= posDeltaY)
                    longSkipped = true;
            }
        }

        if (!longSkipped)
        {
            longFriction = ComputeFriction(LongSlipPercent, out longVisual);
            longFrictionDisp = longFriction * longDisp;
        }

        // ---- lateral friction ----------------------------------------------
        float latFriction = muStatic;
        float latFrictionDisp = muStatic * latDisp;
        float latVisual = 0f;
        bool latSkipped = false;

        if (Mathf.Abs(LatSlipPercent) > OptimumSlipPercent)
        {
            if (posDeltaX <= 0f)
            {
                if (CurrentTireDispLat > latFrictionDisp
                    && latFrictionDisp - CurrentTireDispLat <= posDeltaX)
                    latSkipped = true;
            }
            else
            {
                if (CurrentTireDispLat < latFrictionDisp
                    && latFrictionDisp - CurrentTireDispLat >= posDeltaX)
                    latSkipped = true;
            }
        }

        if (!latSkipped)
        {
            latFriction = ComputeFriction(LatSlipPercent, out latVisual);
            latFrictionDisp = latFriction * latDisp;
        }

        // ---- the dominant axis governs both --------------------------------
        float dominantSlip;
        bool longDominates = true;

        float absLongSlip = Mathf.Abs(LongSlipPercent);
        if (absLongSlip >= LatSlipPercent)
        {
            float negLat = -LatSlipPercent;
            if (negLat <= absLongSlip)
            {
                dominantSlip = absLongSlip;
            }
            else
            {
                dominantSlip = negLat;
                longDominates = false;
            }
        }
        else
        {
            dominantSlip = LatSlipPercent;
            longDominates = false;
        }

        float effectiveMu;
        if (dominantSlip < OptimumSlipPercent)
        {
            effectiveMu = muStatic;
        }
        else if (longDominates)
        {
            effectiveMu = longFriction;
            if (latFriction > longFriction)
                latFrictionDisp = longFriction * latDisp;
        }
        else
        {
            effectiveMu = latFriction;
            if (longFriction > latFriction)
                longFrictionDisp = latFriction * longDisp;
        }

        // ---- brush deflection integration ----------------------------------
        // Stuck: deflection accumulates at the slip velocity and the rate feeds
        // the damper. Sliding: deflection relaxes toward the breakaway limit at
        // |omega*R| * LRelaxCoef per second and the damping term is dropped.
        float relax = Mathf.Abs(wheelSurfaceSpeed) * dt * LRelaxCoef;

        float longRateForDamper;
        bool longStuck;
        float longNext = posDeltaY + CurrentTireDispLong;

        if (posDeltaY >= 0f)
        {
            if (longFrictionDisp >= longNext)
            {
                CurrentTireDispLong = longNext;
                longRateForDamper = posDeltaY;
                longStuck = true;
            }
            else
            {
                float relaxed = CurrentTireDispLong - relax;
                if (longFrictionDisp > relaxed) relaxed = longFrictionDisp;
                CurrentTireDispLong = relaxed;
                longRateForDamper = 0f;
                longStuck = false;
            }
        }
        else
        {
            if (longFrictionDisp <= longNext)
            {
                CurrentTireDispLong = longNext;
                longRateForDamper = posDeltaY;
                longStuck = true;
            }
            else
            {
                float relaxed = relax + CurrentTireDispLong;
                if (longFrictionDisp < relaxed) relaxed = longFrictionDisp;
                CurrentTireDispLong = relaxed;
                longRateForDamper = 0f;
                longStuck = false;
            }
        }

        float latRateForDamper;
        bool latStuck;
        float latNext = posDeltaX + CurrentTireDispLat;

        if (posDeltaX < 0f)
        {
            if (latFrictionDisp <= latNext)
            {
                CurrentTireDispLat = latNext;
                latRateForDamper = posDeltaX;
                latStuck = true;
            }
            else
            {
                float relaxed = relax + CurrentTireDispLat;
                if (latFrictionDisp < relaxed) relaxed = latFrictionDisp;
                CurrentTireDispLat = relaxed;
                latRateForDamper = 0f;
                latStuck = false;
            }
        }
        else
        {
            if (latFrictionDisp < latNext)
            {
                float relaxed = CurrentTireDispLat - relax;
                if (latFrictionDisp > relaxed) relaxed = latFrictionDisp;
                CurrentTireDispLat = relaxed;
                latRateForDamper = 0f;
                latStuck = false;
            }
            else
            {
                CurrentTireDispLat = latNext;
                latRateForDamper = posDeltaX;
                latStuck = true;
            }
        }

        // ---- spring-damper force from deflection ---------------------------
        // The invDt cancels against the dt inside posDelta, so these terms are
        // timestep-independent.
        LatForce = -(CurrentTireDispLat * DispLimitLatLoaded)
                   - latRateForDamper * invDt * DampCoefLatLoaded;
        LongForce = -(DispLimitLongLoaded * CurrentTireDispLong)
                    - longRateForDamper * invDt * DampCoefLongLoaded;

        // ---- friction circle clamp ------------------------------------------
        // Two different clamps, and collapsing them into one changes the feel.
        float forceSq = LatForce * LatForce + LongForce * LongForce;
        float maxForce = effectiveMu * SuspensionForceCopy;
        float maxForceSq = maxForce * maxForce;

        if (forceSq > maxForceSq)
        {
            bool longClean = longStuck || Mathf.Abs(LongSlipPercent) <= OptimumSlipPercent;
            bool latClean = latStuck || Mathf.Abs(LatSlipPercent) <= OptimumSlipPercent;

            if (longClean && latClean)
            {
                // Both stuck: scale the force vector down proportionally.
                float scale = Mathf.Sqrt(maxForceSq / forceSq);
                LatForce *= scale;
                LongForce *= scale;
            }
            else
            {
                // Either sliding: pure Coulomb, opposing the slip velocity vector.
                float slipSq = motorAdjustedForwardVelocity * motorAdjustedForwardVelocity
                             + wheelSpaceVelocity.x * wheelSpaceVelocity.x;
                if (slipSq > 1e-12f)
                {
                    float scale = Mathf.Sqrt(maxForceSq / slipSq);
                    LatForce = -scale * wheelSpaceVelocity.x;
                    LongForce = -scale * motorAdjustedForwardVelocity;
                }
            }
        }

        // ---- slippage readout for effects ------------------------------------
        if (longStuck)
            LastSlippage = latStuck ? 0f : latVisual;
        else if (latStuck)
            LastSlippage = longVisual;
        else
            LastSlippage = Mathf.Max(longVisual, latVisual);

        MajorlySlipping = LastSlippage > 0.5f;

        // Reaction torque back into the drivetrain solve.
        LongForceRadScaled = LongForce * Radius;

        // ---- rolling drag ------------------------------------------------------
        float latDrag = -(Mathf.Abs(wheelSpaceVelocity.x) * TireDragCoefLat * SuspensionForceCopy
                          * wheelSpaceVelocity.x * MaterialDrag);

        // MaterialDepth scales longitudinal drag only - this is the sand/grass sink.
        float longDrag = Mathf.Abs(wheelSpaceVelocity.y) * TireDragCoefLong * SuspensionForceCopy
                         * MaterialDrag * wheelSpaceVelocity.y * (MaterialDepth + 1f);

        // ---- accumulate onto the body -------------------------------------------
        Vector3 force = LastSurfaceNormal * CurrentSuspensionForce;
        force += sidewaysDirection * LatForce;
        force += rearwardsDirection * (-LongForce);
        force += sidewaysDirection * latDrag;
        force += rearwardsDirection * longDrag;

        Vector3 r = ContactPoint - Body.worldCenterOfMass;

        AccumulatedForce += force;
        AccumulatedTorque += Vector3.Cross(r, force);

        UpdateVisualTransform(dt);
    }

    /// <summary>
    /// Steered hub transform in world space, matching the WheelMatrix the original
    /// builds at the top of ComputeDwtdw. Steering pivots about the inside edge of
    /// the tyre, not its centre, so the contact patch scrubs the way it should.
    /// Does NOT include suspension travel.
    /// </summary>
    public void GetSteeredHub(out Vector3 origin, out Quaternion rotation)
    {
        Quaternion steer = SteeringLimit == 0f
            ? Quaternion.identity
            : Quaternion.AngleAxis(SteerAmount * Mathf.Rad2Deg, Vector3.up);

        Vector3 localCenter = Center;
        if (SteeringLimit != 0f)
        {
            float offset = 0.0f;
            if (Center.x != 0.0f)
            {
                offset = Mathf.Sign(Center.x) * Width * 0.5f;
            }
            localCenter.x -= offset;
            localCenter += steer * (Vector3.right * offset);
        }

        origin = CarTransform.TransformPoint(localCenter);
        rotation = CarTransform.rotation * steer;
    }

    private void UpdateVisualTransform(float dt)
    {
        if (CamberLimit > 0f && Center.x != 0.0f)
            CamberAmount = Mathf.Sign(Center.x) * TargetSuspensionTravel * CamberLimit;
        else
            CamberAmount = 0.0f;

        // The original lets this grow without bound. After a few minutes at speed
        // the float loses enough mantissa that the spin visibly quantises.
        // Wrapping is invisible on a solid of revolution.
        AccumulatedRotation = Mathf.Repeat(
            AccumulatedRotation + dt * RotationRate, 2f * Mathf.PI);

        Quaternion steer = SteeringLimit == 0f
            ? Quaternion.identity
            : Quaternion.AngleAxis(SteerAmount * Mathf.Rad2Deg, Vector3.up);

        // Steering pivots about the inside edge of the tyre, not its centre, so
        // the contact patch scrubs the way it should.
        Vector3 localHub = Center;
        if (SteeringLimit != 0f)
        {
            float offset = 0.0f;
            if(Center.x != 0.0f)
                offset = Mathf.Sign(Center.x) * Width * 0.5f;
            localHub.x -= offset;
            localHub += steer * (Vector3.right * offset);
        }

        LocalHubPosition = localHub;
        LocalHubRotation = steer;

        // Original: m30 += (TargetSuspensionTravel - GetVisualDispVert()) * m1x,
        // i.e. translate along the hub's own up axis.
        float visualTravel = TargetSuspensionTravel - GetVisualDispVert();
        LocalPosition = localHub + (steer * Vector3.up) * visualTravel;

        // Camber tilts the axle about the hub's forward axis, the wheel spins
        // about that tilted axle, and wobble goes last about the spun forward
        // axis - which is what Matrix34::Rotate(&m, &m.m20, w) does.
        Quaternion rot = steer;

        if (CamberLimit > 0f && CamberAmount != 0f)
            rot *= Quaternion.AngleAxis(CamberAmount * Mathf.Rad2Deg, Vector3.forward);

        rot *= Quaternion.AngleAxis(-AccumulatedRotation * Mathf.Rad2Deg, Vector3.right);
        if (WobbleAmount != 0f)
            rot *= Quaternion.AngleAxis(WobbleAmount * Mathf.Rad2Deg, Vector3.forward);

        LocalRotation = rot;
    }


    private float InverseEffectiveMass(Vector3 normal, Vector3 point)
    {
        Vector3 r = point - Body.worldCenterOfMass;
        Vector3 rCrossN = Vector3.Cross(r, normal);

        Quaternion toPrincipal = Body.rotation * Body.inertiaTensorRotation;
        Vector3 local = Quaternion.Inverse(toPrincipal) * rCrossN;
        Vector3 t = Body.inertiaTensor;
        local = new Vector3(
            t.x > 1e-9f ? local.x / t.x : 0f,
            t.y > 1e-9f ? local.y / t.y : 0f,
            t.z > 1e-9f ? local.z / t.z : 0f);

        return 1f / Body.mass
             + Vector3.Dot(Vector3.Cross(toPrincipal * local, r), normal);
    }

    private float CalcCollisionNoFriction(Vector3 normal, float normalVelocity, Vector3 point)
    {
        if (normalVelocity >= 0f) return 0f;
        float invEff = InverseEffectiveMass(normal, point);
        return invEff <= 1e-9f ? 0f : -normalVelocity / invEff;
    }

    public void ComputeConstants()
    {
        float normalLoad;
        InvSquaredOptimumSlipPercent = 1.0f / (OptimumSlipPercent * OptimumSlipPercent);
        float z = Center.z;
        if (CarSim != null)
        {
            float v3 = Mathf.Abs(z);
            normalLoad = Mathf.Abs(z - CarSim.CenterOfGravity.z) / (v3 + v3) * -(Physics.gravity.y * CarSim.Mass) * 0.5f;
        }
        else
        {
            float v4 = Mathf.Abs(z);
            normalLoad = -(Body.mass * Physics.gravity.y) * v4 * 0.5f / (v4 + v4);
        }
        SetNormalLoad(normalLoad);
    }

    public void CopyVars(VehWheel other)
    {
        SuspensionDampCoef = other.SuspensionDampCoef;
        SuspensionLimit = other.SuspensionLimit;
        SuspensionExtent = other.SuspensionExtent;
        SuspensionFactor = other.SuspensionFactor;
        SteeringLimit = other.SteeringLimit;
        CamberLimit = other.CamberLimit;
        SteeringOffset = other.SteeringOffset;
        BrakeCoef = other.BrakeCoef;
        TireDispLimitLong = other.TireDispLimitLong;
        TireDampCoefLong = other.TireDampCoefLong;
        TireDragCoefLong = other.TireDragCoefLong;
        TireDispLimitLat = other.TireDispLimitLat;
        TireDampCoefLat = other.TireDampCoefLat;
        TireDragCoefLat = other.TireDragCoefLat;
        OptimumSlipPercent = other.OptimumSlipPercent;
        StaticFric = other.StaticFric;
        SlidingFric = other.SlidingFric;

        ComputeConstants();
    }

    public void Read(TokenFileParser parser)
    {
        SuspensionExtent = parser.Read("SuspensionExtent", SuspensionExtent);
        SuspensionLimit = parser.Read("SuspensionLimit", SuspensionLimit);
        SuspensionFactor = parser.Read("SuspensionFactor", SuspensionFactor);
        SuspensionDampCoef = parser.Read("SuspensionDampCoef", SuspensionDampCoef);
        SteeringLimit = parser.Read("SteeringLimit", SteeringLimit);
        SteeringOffset = parser.Read("SteeringOffset", SteeringOffset);
        BrakeCoef = parser.Read("BrakeCoef", BrakeCoef);
        HandbrakeCoef = parser.Read("HandbrakeCoef", HandbrakeCoef);
        CamberLimit = parser.Read("CamberLimit", CamberLimit);

        float WobbleLimit = 0.00f; // unused for now
        parser.Read("WobbleLimit", WobbleLimit);

        TireDispLimitLong = parser.Read("TireDispLimitLong", TireDispLimitLong);
        TireDampCoefLong = parser.Read("TireDampCoefLong", TireDampCoefLong);
        TireDragCoefLong = parser.Read("TireDragCoefLong", TireDragCoefLong);
        TireDispLimitLat = parser.Read("TireDispLimitLat", TireDispLimitLat);
        TireDampCoefLat = parser.Read("TireDampCoefLat", TireDampCoefLat);
        TireDragCoefLat = parser.Read("TireDragCoefLat", TireDragCoefLat);
        OptimumSlipPercent = parser.Read("OptimumSlipPercent", OptimumSlipPercent);
        StaticFric = parser.Read("StaticFric", StaticFric);
        SlidingFric = parser.Read("SlidingFric", SlidingFric);
    }

    public void DrawGizmos()
    {
        Vector3 localCenter = Center;

        Quaternion steer = SteeringLimit == 0f
            ? Quaternion.identity
            : Quaternion.AngleAxis(SteerAmount * Mathf.Rad2Deg, Vector3.up);

        if (SteeringLimit != 0f)
        {
            float offset = Mathf.Sign(Center.x) * Width * 0.5f;
            localCenter.x -= offset;
            localCenter += steer * (Vector3.right * offset);
        }

        Vector3 origin = CarTransform.TransformPoint(localCenter);
        Quaternion worldRot = CarTransform.rotation * steer;

        Vector3 wheelUp = worldRot * Vector3.up;
        Vector3 wheelRight = worldRot * Vector3.right;
        Vector3 wheelForward = worldRot * Vector3.forward;

        Vector3 currentPosition =
            origin + wheelUp * TargetSuspensionTravel;

        // ------------------------------------------------------------
        // Wheel
        // ------------------------------------------------------------

        Gizmos.color = Color.white;

        const int segments = 32;

        float halfWidth = Width * 0.5f;

        Vector3 leftCenter =
            currentPosition - wheelRight * halfWidth;

        Vector3 rightCenter =
            currentPosition + wheelRight * halfWidth;

        Vector3 previousLeft =
            leftCenter + wheelUp * Radius;

        Vector3 previousRight =
            rightCenter + wheelUp * Radius;

        for (int i = 1; i <= segments; i++)
        {
            float angle =
                (i / (float)segments) * Mathf.PI * 2.0f;

            Vector3 radial =
                wheelUp * Mathf.Cos(angle) * Radius +
                wheelForward * Mathf.Sin(angle) * Radius;

            Vector3 pointLeft =
                leftCenter + radial;

            Vector3 pointRight =
                rightCenter + radial;

            // Left and right wheel faces
            Gizmos.DrawLine(previousLeft, pointLeft);
            Gizmos.DrawLine(previousRight, pointRight);

            // Tire sidewall
            Gizmos.DrawLine(pointLeft, pointRight);

            previousLeft = pointLeft;
            previousRight = pointRight;
        }

        // ------------------------------------------------------------
        // Suspension travel
        // ------------------------------------------------------------

        Gizmos.color = Color.gray;

        Vector3 droopPosition =
            origin + wheelUp * -SuspensionExtent;

        Vector3 compressionPosition =
            origin + wheelUp * SuspensionLimit;

        Gizmos.DrawLine(droopPosition, compressionPosition);

        // Actual wheel width
        Gizmos.DrawLine(
            origin - wheelRight * halfWidth,
            origin + wheelRight * halfWidth);

        // Wheel center line
        Gizmos.DrawLine(
            currentPosition - wheelRight * halfWidth,
            currentPosition + wheelRight * halfWidth);

        Gizmos.DrawLine(origin, currentPosition);

        // ------------------------------------------------------------
        // Forces
        // ------------------------------------------------------------

        if (!IsGrounded)
            return;

        const float forceScale = 0.0025f;

        Vector3 suspensionForce =
            LastSurfaceNormal * CurrentSuspensionForce;

        Vector3 lateralForce =
            sidewaysDirection * LatForce;

        Vector3 longitudinalForce =
            rearwardsDirection * -LongForce;

        Vector3 totalForce =
            suspensionForce +
            lateralForce +
            longitudinalForce;

        // Suspension force - yellow
        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(
            ContactPoint,
            ContactPoint + suspensionForce * forceScale);

        // Lateral tire force - blue
        Gizmos.color = Color.blue;
        Gizmos.DrawLine(
            ContactPoint,
            ContactPoint + lateralForce * forceScale);

        // Longitudinal tire force - red
        Gizmos.color = Color.red;
        Gizmos.DrawLine(
            ContactPoint,
            ContactPoint + longitudinalForce * forceScale);

        // Total force - white
        Gizmos.color = Color.white;
        Gizmos.DrawLine(
            ContactPoint,
            ContactPoint + totalForce * forceScale);

        // Contact
        Gizmos.color = Color.green;
        Gizmos.DrawSphere(LastHitPosition, 0.05f);

        // Debug shit
        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(
            origin,
            origin + wheelUp * 0.5f);
    }

    public VehWheel()
    {
    }
}
