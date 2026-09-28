using UnityEngine;

/// <summary>
/// Port of MM2 / AGE vehStuck.
///
/// Recovery assist. Armed by <see cref="Impact"/> when the car hits something,
/// then watches for the car failing to make progress. Three recoveries, picked
/// by how the car is stuck:
///
///  - PEGGED (state 2)   nose into a wall, throttle floored, steering held over.
///                       Yaws the body in place about world up so you can work
///                       off the wall. Direct transform write, not a torque.
///  - RIGHTING (state 3) on its side or roof, with Rotation authored. Adds an
///                       upward impulse and a roll impulse about the car's own
///                       forward axis, both scaled by steering input, so the
///                       player rocks it back over. Cuts throttle, holds brake.
///  - SNAP (state 4)     same situation but Rotation is zero. Teleports the car
///                       level - heading preserved, pitch and roll discarded -
///                       and lifts it by Translation.
///
/// TWO DIFFERENT DISTANCE METRICS, both inherited:
///   The "have we escaped" test (top of Update, and state 2) is FULL 3D against
///   MoveThreshSqr. The "are we still near the impact" test in state 1 is
///   HORIZONTAL ONLY - x and z, y discarded - against PosThreshSqr. That is
///   deliberate: dropping off a ledge after an impact should not count as
///   having escaped it, but sinking into the suspension should not either.
///
/// STATE 1 USES !OnGround(), WHICH MEANS ZERO WHEELS DOWN.
/// vehCarSim::OnGround returns a wheel COUNT, so `!OnGround()` is true only when
/// the car is fully airborne or resting on something that isn't its wheels -
/// i.e. on its roof or side. It is not "any wheel lifted".
///
/// FLAGS, matching vehGyro's mapping:
///   bit 0 (0x1) node enabled -> VehSubsystem.Enabled. Impact sets it, Reset
///   clears it, so Update is skipped entirely while idle.
/// </summary>
public class VehStuck : VehSubsystem
{
    public enum StuckState
    {
        Idle = 0,
        Impacted = 1,
        Pegged = 2,
        Righting = 3,
        Snap = 4,
    }

    [Header("Thresholds")]
    /// <summary>Horizontal radius around the impact point that still counts as stuck.</summary>
    public float PosThresh = 1.25f;

    /// <summary>3D distance from the impact point that counts as having escaped.</summary>
    public float MoveThresh = 1.75f;

    /// <summary>How long the car must fail to make progress before recovery arms.</summary>
    public float TimeThresh = 0.3f;

    [Header("Recovery strengths (0 disables)")]
    /// <summary>Yaw rate for the pegged-against-a-wall recovery, rad/s at full lock.</summary>
    public float Turn = 1.57f;

    /// <summary>Roll impulse scale for the righting recovery. Zero selects the snap instead.</summary>
    public float Rotation = 0.39f;

    /// <summary>Lift impulse scale, and the lift distance used by the snap.</summary>
    public float Translation = 0.1f;

    // ------------------------------------------------------------ live state
    public StuckState State = StuckState.Idle;
    public float StuckTime;
    public Vector3 LastImpactPos;

    // Derived by ComputeConstants (StuckCB in the original).
    private float posThreshSqr;
    private float moveThreshSqr;

    private VehCarSim sim;
    private Rigidbody body;

    public override void Init(VehCar car)
    {
        base.Init(car);
        sim = car.VehCarSim;
        body = car.Body;

        ComputeConstants();
        Reset();
    }

    /// <summary>StuckCB. Must run after anything writes the thresholds.</summary>
    public void ComputeConstants()
    {
        posThreshSqr = PosThresh * PosThresh;
        moveThreshSqr = MoveThresh * MoveThresh;
    }

    /// <summary>
    /// vehStuck::Impact. Call from the car's collision handling. Only arms when
    /// idle - an impact during an active recovery is ignored, so a recovery
    /// can't be restarted by the collisions it causes.
    /// </summary>
    public void Impact()
    {
        if (State != StuckState.Idle)
            return;

        LastImpactPos = body.position;
        State = StuckState.Impacted;
    }

    public void Reset()
    {
        State = StuckState.Idle;
        StuckTime = 0f;
    }

    /// <summary>
    /// vehStuck::Pegged. Throttle meaningfully above the AI's cruise level and
    /// steering held past half lock - i.e. the player is actively trying to
    /// drive out of something and not getting anywhere.
    /// </summary>
    private bool Pegged()
    {
        return sim.Engine.AIThrottle * 0.75f < sim.Engine.ThrottleInput
            && Mathf.Abs(sim.SteeringInput) > 0.5f;
    }

    public override void Update()
    {
        if (sim == null || body == null) return;

        float dt = Time.deltaTime;
        Vector3 pos = body.position;

        // Escaped the impact point entirely - full 3D.
        if ((LastImpactPos - pos).sqrMagnitude > moveThreshSqr)
            Reset();

        switch (State)
        {
            case StuckState.Impacted:
                UpdateImpacted(dt, pos);
                break;

            case StuckState.Pegged:
                UpdatePegged(dt, pos);
                break;

            case StuckState.Righting:
                UpdateRighting();
                break;

            case StuckState.Snap:
                UpdateSnap();
                break;

            default:
                return;
        }
    }

    private void UpdateImpacted(float dt, Vector3 pos)
    {
        StuckTime += dt;

        // HORIZONTAL distance only - y is discarded here, unlike every other
        // distance test in this file.
        float dx = LastImpactPos.x - pos.x;
        float dz = LastImpactPos.z - pos.z;
        float horizSqr = dx * dx + dz * dz;

        bool nearImpact = horizSqr <= posThreshSqr;
        bool timedOut = StuckTime >= TimeThresh;

        // Not on its wheels at all -> flipped. Righting if Rotation is
        // authored, otherwise the hard snap.
        if (nearImpact
            && sim.OnGround() == 0
            && timedOut
            && (Translation > 0f || Rotation > 0f))
        {
            State = Rotation <= 0f ? StuckState.Snap : StuckState.Righting;
            return;
        }

        // Upright but wedged, and the player is fighting it.
        if (nearImpact && timedOut && Pegged())
        {
            State = StuckState.Pegged;
            return;
        }

        // Note the asymmetry: the transitions above test `>= TimeThresh`, this
        // gives up on `> TimeThresh`. So the step where StuckTime lands exactly
        // on the threshold gets one chance to transition before the reset.
        if ((LastImpactPos - body.position).sqrMagnitude > moveThreshSqr
            || StuckTime > TimeThresh)
        {
            Reset();
        }
    }

    private void UpdatePegged(float dt, Vector3 pos)
    {
        if ((LastImpactPos - pos).sqrMagnitude > moveThreshSqr || !Pegged())
        {
            Reset();
            return;
        }

        // steer * |steer| - sign preserved, magnitude squared, so light
        // corrections get almost nothing.
        float steer = sim.SteeringInput;
        float rate = Mathf.Abs(steer) * Turn * steer;

        // Gear 0 is reverse: yaw the other way so the steering still reads
        // correctly from the driver's point of view.
        if (sim.Transmission.CurrentGear == 0)
            rate = -rate;

        float angle = (dt * rate);

        // Direct transform write about WORLD up, not a torque. Pre-multiply
        // applies the rotation in world space.
        body.rotation = Quaternion.AngleAxis(angle * Mathf.Rad2Deg, Vector3.up)
                        * body.rotation;
    }

    private void UpdateRighting()
    {
        // m11 is the y component of the car's own up axis. Above 0.7 the car is
        // upright enough that it no longer needs help.
        Vector3 up = body.rotation * Vector3.up;

        if (up.y > 0.69999999f)
        {
            Reset();
            return;
        }

        float steer = sim.SteeringInput;
        float mass = body.mass;

        // Lift, always upward: magnitude only.
        body.AddForce(Vector3.up * (Mathf.Abs(steer) * mass * Translation),
                      ForceMode.Impulse);

        // Roll about the car's OWN forward axis, signed by steering, so the
        // player picks which way it flops over.
        Vector3 forward = body.rotation * Vector3.forward;
        body.AddTorque(forward * (steer * mass * Rotation), ForceMode.Impulse);

        sim.Engine.ThrottleInput = 0f;
        sim.BrakeInput = 1f;
    }

    private void UpdateSnap()
    {
        Quaternion rot = body.rotation;
        Vector3 forward = rot * Vector3.forward;

        // Flatten the forward axis onto the horizontal plane and rebuild an
        // upright basis from it. The original does this as
        // right = cross(worldUp, forward); forward = cross(right, worldUp),
        // which is the same thing.
        Vector3 flat = new Vector3(forward.x, 0f, forward.z);

        // Not in the original, which would produce a degenerate basis here.
        // Nose straight up or straight down leaves no heading to preserve.
        if (flat.sqrMagnitude < 1e-6f)
        {
            Vector3 rightAxis = rot * Vector3.right;
            flat = new Vector3(-rightAxis.z, 0f, rightAxis.x);

            if (flat.sqrMagnitude < 1e-6f)
                flat = Vector3.forward;
        }

        body.rotation = Quaternion.LookRotation(flat.normalized, Vector3.up);
        body.position = body.position + Vector3.up * Translation;

        Reset();
    }

    public void Read(TokenFileParser parser)
    {
        Turn = parser.Read("Turn", Turn);
        Rotation = parser.Read("Rotation", Rotation);
        Translation = parser.Read("Translation", Translation);
        TimeThresh = parser.Read("TimeThresh", TimeThresh);
        PosThresh = parser.Read("PosThresh", PosThresh);
        MoveThresh = parser.Read("MoveThresh", MoveThresh);

        // vehStuck::Init calls StuckCB after Load for exactly this reason.
        ComputeConstants();
    }
}