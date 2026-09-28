using UnityEngine;

/// <summary>
/// Port of MM2 / AGE vehGyro.
///
/// The gyro is a pure driver aid - it never touches the tyre model, it just adds
/// torque to the body. Three independent assists:
///
///  - DRIFT      yaw torque proportional to steering * driveshaft speed, so the
///               car rotates into a slide instead of understeering out of it.
///               Only while every wheel is on the ground.
///  - SPIN180    yaw torque while the handbrake is held, used for handbrake
///               turns. Reverse180 is the same thing when rolling backwards.
///  - PITCH/ROLL air control. Only while braking AND airborne - this is what
///               lets you level the car off a jump with the brake held.
///
/// GROUND FRACTION IS INTEGER DIVISION IN THE ORIGINAL.
/// vehGyro::Update computes `vehCarSim::OnGround() / vehCarSim::WheelCount`
/// with two ints, so it is 1 only when all four wheels are down and 0 otherwise.
/// Pitch/roll then keys off `1 - that`, i.e. "not fully planted". That binary
/// behaviour is deliberate - it is what makes air control cut in cleanly the
/// instant the car leaves the ground. Set <see cref="FractionalGroundContact"/>
/// if you want the smoother 0/0.25/0.5/0.75/1 version instead.
///
/// FLAGS, from vehCar::Update:
///   bit 0      (0x00001) node enabled  -> mapped to VehSubsystem.Enabled
///   bit 16     (0x10000) handbrake applied
///   bit 17     (0x20000) drift assist enabled (set by the ctor)
///   bit 18     (0x40000) brake applied
/// </summary>
public class VehGyro : VehSubsystem
{
    public const int FlagHandBrake = 0x10000;
    public const int FlagDrift = 0x20000;
    public const int FlagBrake = 0x40000;

    /// <summary>vehGyro ctor sets the drift bit; vehCar::Update drives the other two.</summary>
    public int Flags = FlagDrift;

    [Header("Assist strengths (0 disables)")]
    public float Drift;
    public float Spin180;
    public float Reverse180;
    public float Pitch;
    public float Roll;

    /// <summary>
    /// Off = faithful (integer division, so the ground term is 0 or 1).
    /// On = wheelsOnGround / 4, which ramps the assists instead of switching them.
    /// </summary>
    public bool FractionalGroundContact = false;

    private VehCarSim sim;
    private Rigidbody body;
    private Transform tf;

    public override void Init(VehCar car)
    {
        base.Init(car);

        var node = AssetManager.OpenNode("tune", "vehicle", $"{car.Basename}.vehGyro");
        if (node != null)
        {
            Read(node);
        }

        sim = car.VehCarSim;
        body = car.Body;
        tf = car.transform;
    }

    private float GroundTerm()
    {
        int onGround = sim.OnGround();
        int wheelCount = sim.WheelCount;
        if (wheelCount <= 0) return 0f;

        return FractionalGroundContact
            ? (float)onGround / wheelCount
            : onGround / wheelCount;   // integer division, as in the original
    }

    public override void Update()
    {
    }

    private void FixedUpdate()
    {
        if (sim == null || body == null) return;

        Vector3 inertia = body.inertiaTensor;
        float ground = GroundTerm();

        // Driveshaft speed. Rate is negative moving forward, so this is positive
        // when the car is going forwards.
        float shaft = -sim.PrimaryDrivetrain.Rate;

        // ---- drift assist -------------------------------------------------
        if ((Flags & FlagDrift) != 0 && Drift > 0f)
        {
            float steer = sim.GetSSSFactor(sim.Speed) * sim.SteeringInput;

            // steer * |steer| keeps the sign but squares the magnitude, so small
            // corrections get almost nothing and full lock gets the lot.
            float yaw = (ground * (Mathf.Abs(steer) * steer) * shaft * (inertia.y * Drift));

            body.AddTorque(tf.up * yaw, ForceMode.Force);
        }

        // ---- handbrake spin -----------------------------------------------
        if ((Flags & FlagHandBrake) != 0 && (Spin180 > 0f || Reverse180 > 0f))
        {
            // Rate >= 0 means stopped or rolling backwards.
            float coef = sim.PrimaryDrivetrain.Rate >= 0f ? Reverse180 : Spin180;

            // Raw steering here, NOT speed-sensitive - the original deliberately
            // skips GetSSSFactor on this one so a handbrake turn keeps its
            // authority at speed.
            float steer = sim.SteeringInput;

            float yaw = (ground * steer * shaft * inertia.y * coef);

            body.AddTorque(tf.up * yaw, ForceMode.Force);
        }

        // ---- air control --------------------------------------------------
        if ((Flags & FlagBrake) != 0 && (Pitch > 0f || Roll > 0f))
        {
            // (1 - ground) is 1 while airborne, 0 while planted.
            float brake = sim.BrakeInput;
            float authority = (1f - ground) * (brake * brake);

            // Pitch torque about the lateral axis, scaled by how nose-up the car
            // already is - a self-levelling term rather than a constant push.
            float pitchTorque = inertia.x * Pitch * authority * tf.forward.y;
            body.AddTorque(tf.right * pitchTorque, ForceMode.Force);

            // Roll torque about the longitudinal axis, scaled by how far the
            // lateral axis has tipped out of horizontal.
            float rollTorque = -(authority * (Roll * inertia.z)) * tf.right.y;
            body.AddTorque(tf.forward * rollTorque, ForceMode.Force);
        }
    }

    public void Reset()
    {
        // vehGyro::Reset only clears the transient input bits; the drift bit and
        // the enable bit survive.
        Flags &= ~(FlagHandBrake | FlagBrake);
    }

    public void Read(TokenFileParser parser)
    {
        Drift = parser.Read("Drift", Drift);
        Spin180 = parser.Read("Spin180", Spin180);
        Reverse180 = parser.Read("Reverse180", Reverse180);
        Pitch = parser.Read("Pitch", Pitch);
        Roll = parser.Read("Roll", Roll);
    }
}