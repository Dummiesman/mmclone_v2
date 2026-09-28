using UnityEngine;

/// <summary>
/// Faithful port of MM2 / AGE vehAero.
///
/// Three separate jobs:
///  - angular damping about each body axis, with constant, linear and quadratic
///    terms. This is what stops the car spinning forever in mid-air and gives it
///    its rotational "weight" in the air.
///  - translational drag, scaled by forward speed
///  - downforce along the body's down axis, scaling with speed squared
///
/// The damping is applied in BODY space and transformed back out, so the three
/// axes can be tuned independently: roll (x), yaw (y), pitch (z).
/// </summary>
[System.Serializable]
public class VehAero
{
    [System.NonSerialized] public VehCarSim CarSim;

    public bool EnableAero = true;

    /// <summary>Constant (Coulomb) angular damping - opposes rotation at a fixed
    /// magnitude regardless of rate.</summary>
    public Vector3 AngCDamp;

    /// <summary>Linear angular damping, proportional to rate.</summary>
    public Vector3 AngVelDamp;

    /// <summary>Quadratic angular damping, proportional to rate squared.</summary>
    public Vector3 AngVel2Damp;

    public float Drag;
    public float Down;

    public void Update()
    {
        if (!EnableAero || CarSim == null) return;

        var body = CarSim.Body;
        var tf = CarSim.Transform;

        float dt = Time.fixedDeltaTime;
        float invDt = 1f / dt;

        // ---- angular velocity in body space ---------------------------------
        Vector3 w = body.angularVelocity;
        Vector3 local = new Vector3(
            Vector3.Dot(tf.right, w),
            Vector3.Dot(tf.up, w),
            Vector3.Dot(tf.forward, w));

        // Constant term: opposes rotation at a fixed magnitude.
        Vector3 damp = new Vector3(
            -(Mathf.Sign(local.x) * AngCDamp.x),
            -(Mathf.Sign(local.y) * AngCDamp.y),
            -(Mathf.Sign(local.z) * AngCDamp.z));

        // Sign() returns 1 for zero in Unity but 0 in the original, so zero it
        // explicitly - otherwise a perfectly still car gets a phantom torque.
        if (local.x == 0f) damp.x = 0f;
        if (local.y == 0f) damp.y = 0f;
        if (local.z == 0f) damp.z = 0f;

        // Linear and quadratic terms.
        damp.x -= local.x * AngVelDamp.x + Mathf.Abs(local.x) * local.x * AngVel2Damp.x;
        damp.y -= local.y * AngVelDamp.y + Mathf.Abs(local.y) * local.y * AngVel2Damp.y;
        damp.z -= local.z * AngVelDamp.z + Mathf.Abs(local.z) * local.z * AngVel2Damp.z;

        // Never damp harder than would bring the axis to a stop this step -
        // otherwise strong damping overshoots into the opposite direction and
        // the car oscillates.
        if (Mathf.Abs(local.x) < Mathf.Abs(damp.x) * dt) damp.x = -(invDt * local.x);
        if (Mathf.Abs(local.y) < Mathf.Abs(damp.y) * dt) damp.y = -(invDt * local.y);
        if (Mathf.Abs(local.z) < Mathf.Abs(damp.z) * dt) damp.z = -(invDt * local.z);

        // Fade the damping out below 1 rad/s so it doesn't fight small motions.
        //
        // NOTE: the original indexes the WORLD angular velocity here, not the
        // body-space value used everywhere else in this block. Ported as-is,
        // since it is load-bearing for the feel, but it is almost certainly
        // unintentional.
        if (Mathf.Abs(w.x) < 1f) damp.x *= Mathf.Abs(w.x);
        if (Mathf.Abs(w.y) < 1f) damp.y *= Mathf.Abs(w.y);
        if (Mathf.Abs(w.z) < 1f) damp.z *= Mathf.Abs(w.z);

        // Scale by the principal moments and rotate back into world space.
        Vector3 inertia = body.inertiaTensor;
        Vector3 scaled = new Vector3(
            damp.x * inertia.x,
            damp.y * inertia.y,
            damp.z * inertia.z);

        Vector3 worldTorque = tf.right * scaled.x + tf.up * scaled.y + tf.forward * scaled.z;
        body.AddTorque(worldTorque, ForceMode.Force);

        // ---- drag --------------------------------------------------------------
        // Speed is the scalar forward speed, so this is quadratic in magnitude
        // while staying aligned with the actual velocity vector.
        float speed = CarSim.Speed;
        body.AddForce(body.velocity * -(speed * Drag), ForceMode.Force);

        // ---- downforce ----------------------------------------------------------
        // Along the body's own up axis, negated - so it presses into the road
        // rather than toward world down, and follows the car through banking.
        body.AddForce(tf.up * -(speed * speed * Down), ForceMode.Force);
    }

    public void Read(TokenFileParser parser)
    {
        AngCDamp = parser.Read("AngCDamp", AngCDamp);
        AngVelDamp = parser.Read("AngVelDamp", AngVelDamp);
        AngVel2Damp = parser.Read("AngVel2Damp", AngVel2Damp);
        Drag = parser.Read("Drag", Drag);
        Down = parser.Read("Down", Down);
    }
}
