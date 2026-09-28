using UnityEngine;

/// <summary>
/// The parts of AGE's phInertialCS the vehicle code actually needs: point
/// velocity, the filtered variant the tire model is tuned against, and the
/// NetPush positional correction.
///
/// ONE INSTANCE PER BODY. NetPush arbitrates between all four wheels pushing at
/// once - giving each wheel its own accumulator defeats the whole mechanism.
/// </summary>
public class PhInertialCS
{
    public Rigidbody Body;

    /// <summary>Positional correction accumulated this step, applied at the end of it.</summary>
    public Vector3 NetPush;

    /// <summary>What was applied last step. Drives the velocity filter.</summary>
    public Vector3 LastTotalAppliedPush;

    public bool HasContactStiffness;
    public Matrix4x4 StiffnessSum = Mat3.Zero();
    public Matrix4x4 StiffnessMoment = Mat3.Zero();
    public Matrix4x4 StiffnessInertia = Mat3.Zero();
    public Vector3 ContactForce;
    public Vector3 ContactTorque;

    // ---- gizmo snapshots (written at solve time, survive the clear) ----------
    [System.NonSerialized] public Vector3 LastContactForce;
    [System.NonSerialized] public Vector3 LastContactTorque;
    [System.NonSerialized] public Vector3 LastDeltaV;
    [System.NonSerialized] public Vector3 LastDeltaOmega;
    [System.NonSerialized] public bool LastContactSolved;
    [System.NonSerialized] public Vector3 LastAppliedPush;

    public PhInertialCS(Rigidbody body)
    {
        Body = body;
    }

    /// <summary>phInertialCS::GetInertiaMatrix - R * diag(I) * R^T.</summary>
    private Matrix4x4 WorldInertia()
    {
        Quaternion q = Body.rotation * Body.inertiaTensorRotation;
        Matrix4x4 R = Matrix4x4.Rotate(q);
        Vector3 t = Body.inertiaTensor;

        Matrix4x4 D = Mat3.Zero();
        D.m00 = t.x; D.m11 = t.y; D.m22 = t.z;

        return Mat3.Mul3x3(Mat3.Mul3x3(R, D), Mat3.Transpose3x3(R));
    }

    /// <summary>Velocity of a world-space point on the body: v + omega x r.</summary>
    public Vector3 GetLocalVelocity(Vector3 point)
    {
        Vector3 r = point - Body.worldCenterOfMass;
        return Body.velocity + Vector3.Cross(Body.angularVelocity, r);
    }

    /// <summary>
    /// phInertialCS::GetLocalFilteredVelocity2.
    ///
    /// Last step's NetPush moved the body positionally without changing its
    /// velocity, so the raw point velocity still contains the closing speed that
    /// push already resolved. Feeding that to the brush tire model double-counts
    /// it and makes the suspension chatter on rough ground.
    ///
    /// The fix is to remove the velocity component along the push direction,
    /// clamped so a point closing faster than the push can account for only gets
    /// partial compensation.
    /// </summary>
    public Vector3 GetLocalFilteredVelocity2(Vector3 point)
    {
        Vector3 v = GetLocalVelocity(point);

        Vector3 push = LastTotalAppliedPush;
        float pushSq = push.sqrMagnitude;
        if (pushSq <= 0.000099999997f)
            return v;

        float invDt = 1f / Time.fixedDeltaTime;

        float dot = Vector3.Dot(push, v) * invDt;
        float pushVelSq = invDt * invDt * pushSq;

        // Default: add the push velocity outright. Used only when the point is
        // closing along -push faster than that velocity.
        float scale = invDt;

        if (-dot <= pushVelSq)
        {
            // Cancel exactly the component of v along push.
            scale = invDt * -(dot / pushVelSq);
        }

        return v + push * scale;
    }

    private void ClearContact()
    {
        HasContactStiffness = false;
        StiffnessSum = Mat3.Zero();
        StiffnessMoment = Mat3.Zero();
        StiffnessInertia = Mat3.Zero();
        ContactForce = Vector3.zero;
        ContactTorque = Vector3.zero;
    }

    /// <summary>
    /// phInertialCS::CalcNetPush. Per-direction max rather than accumulation:
    /// aligned pushes raise the existing component up to the new one, opposing
    /// pushes add. Stops four simultaneously bottomed wheels launching the car.
    /// </summary>
    public void CalcNetPush(Vector3 force)
    {
        float lenSq = force.sqrMagnitude;
        if (lenSq <= 0f) return;

        float d = Vector3.Dot(NetPush, force);
        if (d < 0f) { NetPush += force; return; }

        float len = Mathf.Sqrt(lenSq);
        float existing = d / len;
        if (len > existing)
            NetPush += force / len * (len - existing);
    }

    public void AccumulateContact(Vector3 force, Vector3 point, float stiffness, Vector3 normal)
    {
        Vector3 r = point - Body.worldCenterOfMass;

        HasContactStiffness = true;
        ContactForce += force;
        ContactTorque += Vector3.Cross(r, force);

        Matrix4x4 K = Mat3.Outer(normal, stiffness);
        Matrix4x4 rTilde = Mat3.Skew(r);
        Matrix4x4 rK = Mat3.Mul3x3(rTilde, K);

        StiffnessSum = Mat3.Add3x3(StiffnessSum, K);
        StiffnessMoment = Mat3.Add3x3(StiffnessMoment, rK);
        StiffnessInertia = Mat3.Add3x3(StiffnessInertia, Mat3.Mul3x3(rK, Mat3.Transpose3x3(rTilde)));
    }

    /// <summary>
    /// Apply the accumulated push and roll it into LastTotalAppliedPush. Call
    /// once at the END of the fixed step, after every wheel has run.
    ///
    /// This is a positional hack in the original too - the body's position is
    /// displaced directly with no velocity change - so doing the same here is
    /// faithful rather than a shortcut.
    /// </summary>
    public void ApplyNetPush()
    {
        LastTotalAppliedPush = NetPush;

        if (NetPush.sqrMagnitude > 0f)
            Body.MovePosition(Body.position + NetPush);
        NetPush = Vector3.zero;
    }

    public void ApplyContactSolve()
    {
        if (!HasContactStiffness) { ClearContact(); LastContactSolved = false;  return; }

        float h = Time.fixedDeltaTime;
        float invMass = 1f / Body.mass;

        // A = I + h * K_sum / m   (always invertible: identity + PSD)
        Matrix4x4 A = Mat3.Add3x3(Mat3.Identity(), Mat3.Scale3x3(StiffnessSum, h * invMass));
        if (!Mat3.Inverse3x3(A, out Matrix4x4 Ainv)) { ClearContact(); LastContactSolved = false;  return; }

        Matrix4x4 B = StiffnessMoment;

        // M = -h^2/m * (B Ainv B^T) + h * K_inertia + I_world
        Matrix4x4 BAB = Mat3.Mul3x3(Mat3.Mul3x3(B, Ainv), Mat3.Transpose3x3(B));
        Matrix4x4 M = Mat3.Scale3x3(BAB, -(h * h * invMass));
        M = Mat3.Add3x3(M, Mat3.Scale3x3(StiffnessInertia, h));
        M = Mat3.Add3x3(M, WorldInertia());

        // p = ContactForce * h   (the impulse this contact would deliver)
        Vector3 p = ContactForce * h;

        // b = ContactTorque * h  +  (-h/m) * B^T (Ainv p)
        Vector3 b = ContactTorque * h;
        b += Mat3.MulT(B, Mat3.Mul(Ainv, p)) * -(h * invMass);

        Vector3 dOmega = Mat3.SolveSymmetric(M, b);
        Vector3 dV = Mat3.Mul(Ainv, (Mat3.MulT(B, dOmega) * h + p)) * invMass;

        Body.velocity += dV;
        Body.angularVelocity += dOmega;

        LastContactForce = ContactForce;
        LastContactTorque = ContactTorque;
        LastDeltaV = dV;
        LastDeltaOmega = dOmega;
        LastContactSolved = true;


        ClearContact();
    }
}
