using UnityEngine;


[System.Serializable]
public class VehAxle
{
    // ---------------------------------------------------------------- authored
    /// <summary>Anti-roll stiffness, normalised by the body's roll inertia.</summary>
    public float TorqueCoef;

    /// <summary>Anti-roll damping ratio. 1 is critical.</summary>
    public float DampCoef;

    /// <summary>
    /// Optional "axle0"/"axle1" pivot from the model. Used only for the visual
    /// geometry; leave null and the half-track is taken from the wheel centres.
    /// </summary>
    public Transform AxlePivot;

    // ------------------------------------------------------------- derived
    public float ScaledTorqueCoef;
    public float ScaledDampCoef;

    /// <summary>1 / lateral distance from the pivot to the left wheel.</summary>
    private float invLateralArm = 1f;

    /// <summary>1 / longitudinal distance from the pivot to the left wheel.</summary>
    private float invLongitudinalArm = 1f;

    // --------------------------------------------------------------- live
    /// <summary>Axle beam roll angle in radians. Positive = left wheel higher.</summary>
    public float RollAngle;

    /// <summary>Mean axle lift, in the original's pivot units.</summary>
    public float LiftAmount;

    /// <summary>Signed travel difference, left minus right, in metres.</summary>
    public float TravelDifference;

    /// <summary>Roll torque applied to the body this step, N.m.</summary>
    public float AppliedTorque;

    [System.NonSerialized] public VehCarSim CarSim;
    [System.NonSerialized] public VehWheel LeftWheel;
    [System.NonSerialized] public VehWheel RightWheel;

    public void Init(VehCarSim carSim, VehWheel left, VehWheel right)
    {
        CarSim = carSim;

        // Order by chassis X so LeftWheel is always the -X side, regardless of how
        // the caller indexes wheels. The ARB torque sign, invLateralArm and the
        // solid-axle camber all depend on this and none of them guard it.
        bool swap = left.Center.x > right.Center.x;
        LeftWheel = swap ? right : left;
        RightWheel = swap ? left : right;

        if (AxlePivot != null && carSim.Transform != null)
        {
            // Wheel centres are in chassis space; bring the pivot into the same
            // space, then measure the arms along the pivot's own axes.
            Vector3 pivotPos = carSim.Transform.InverseTransformPoint(AxlePivot.position);
            Quaternion pivotRot = Quaternion.Inverse(carSim.Transform.rotation) * AxlePivot.rotation;

            Vector3 offset = LeftWheel.Center - pivotPos;

            float lateral = Vector3.Dot(offset, pivotRot * Vector3.right);
            float longitudinal = Vector3.Dot(offset, pivotRot * Vector3.forward);

            invLateralArm = SafeInverse(lateral);
            invLongitudinalArm = SafeInverse(longitudinal);
        }
        else
        {
            // No pivot: assume the axle centre sits midway between the wheels,
            // so the lateral arm is half the track.
            float halfTrack = (LeftWheel.Center.x - RightWheel.Center.x) * 0.5f;
            invLateralArm = SafeInverse(halfTrack);
            invLongitudinalArm = 1f;
        }

        ComputeConstants();
    }

    private static float SafeInverse(float v)
    {
        // The original divides unguarded. A properly authored axle0 pivot always
        // has a non-zero lateral arm, but a missing or centred pivot would give
        // an infinity that then propagates straight into the wheel transforms.
        return Mathf.Abs(v) > 1e-6f ? 1f / v : 1f;
    }

    public void ComputeConstants()
    {
        if (CarSim == null || CarSim.Body == null)
        {
            ScaledTorqueCoef = 0f;
            ScaledDampCoef = 0f;
            return;
        }

        // Roll inertia about the car's longitudinal axis.
        float rollInertia = CarSim.Body.inertiaTensor.z;

        ScaledTorqueCoef = TorqueCoef * rollInertia;
        ScaledDampCoef = 2f * Mathf.Sqrt(Mathf.Max(ScaledTorqueCoef * rollInertia, 0f)) * DampCoef;
    }

    public void Update()
    {
        if (LeftWheel == null || RightWheel == null || CarSim == null) return;
        ComputeConstants();

        // Visual wheel height: suspension travel less the tyre's own squash.
        float leftPos = LeftWheel.TargetSuspensionTravel - LeftWheel.GetVisualDispVert();
        float rightPos = RightWheel.TargetSuspensionTravel - RightWheel.GetVisualDispVert();

        TravelDifference = leftPos - rightPos;

        RollAngle = TravelDifference * invLateralArm * 0.5f;
        LiftAmount = (leftPos + rightPos) * invLongitudinalArm * 0.5f;

        // ---- anti-roll bar --------------------------------------------------
        if (ScaledTorqueCoef != 0f)
        {
            // Rate term uses the raw compression rates, not the difference of the
            // visual positions - the tyre squash is not part of the bar.
            float rateDiff = LeftWheel.SuspensionCompressionRate
                           - RightWheel.SuspensionCompressionRate;

            AppliedTorque = -(rateDiff * ScaledDampCoef + TravelDifference * ScaledTorqueCoef);

            CarSim.Body.AddTorque(CarSim.Transform.forward * AppliedTorque, ForceMode.Force);
        }
        else
        {
            AppliedTorque = 0f;
        }

        // ---- camber ----------------------------------------------------------
        // A wheel with CamberLimit < 0 does not set its own camber; it inherits
        // the axle beam's roll. That is why the original's default CamberLimit is
        // -1 and not 0 - a solid axle is the default, independent suspension is
        // the opt-in.
        if (LeftWheel.CamberLimit < 0f || RightWheel.CamberLimit < 0f)
        {
            LeftWheel.CamberAmount = RollAngle;
            RightWheel.CamberAmount = RollAngle;
        }
        // Otherwise each wheel already wrote its own CamberAmount in Update().
    }

    public void Reset()
    {
        RollAngle = 0f;
        LiftAmount = 0f;
        TravelDifference = 0f;
        AppliedTorque = 0f;
    }

    public void Read(TokenFileParser parser)
    {
        TorqueCoef = parser.Read("TorqueCoef", TorqueCoef);
        DampCoef = parser.Read("DampCoef", DampCoef);
    }

    public void CopyVars(VehAxle other)
    {
        TorqueCoef = other.TorqueCoef;
        DampCoef = other.DampCoef;
        ComputeConstants();
    }

    public void DrawGizmos()
    {
        float GizmoTorqueScale = 0.0005f;
        float GizmoMaxLength = 1.5f;
        if (LeftWheel == null || RightWheel == null || CarSim == null || CarSim.Transform == null)
            return;

        Transform t = CarSim.Transform;
        Vector3 l = t.TransformPoint(LeftWheel.Center);
        Vector3 r = t.TransformPoint(RightWheel.Center);
        Vector3 mid = (l + r) * 0.5f;

        bool active = Mathf.Abs(AppliedTorque) > 1f;

        Gizmos.color = active ? Color.magenta : Color.gray;
        Gizmos.DrawLine(l, r);

        if (!active) return;

        // Signed length along the axis the torque is actually applied about.
        float len = Mathf.Clamp(AppliedTorque * GizmoTorqueScale, -GizmoMaxLength, GizmoMaxLength);
        float head = Mathf.Min(0.12f, Mathf.Abs(len) * 0.3f);

        // 1. The vector as handed to the rigidbody: forward * AppliedTorque.
        Gizmos.color = Color.cyan;
        DrawArrow(mid, mid + t.forward * len, head);

        // 2. Rotation sense. Unity is left-handed, so +Z torque carries +X toward
        //    +Y - a positive AppliedTorque lifts the car's right-hand side.
        float radius = (l - r).magnitude * 0.35f;
        DrawRollArc(mid, t.right, t.up, radius, AppliedTorque > 0f, head);

        // 3. The same thing as the equivalent force couple at the wheels, which
        //    is usually the easier read when you're chasing an ARB sign error.
        Gizmos.color = Color.yellow;
        DrawArrow(r, r + t.up * len * 0.5f, head);
        DrawArrow(l, l - t.up * len * 0.5f, head);
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

    private static void DrawRollArc(Vector3 center, Vector3 x, Vector3 y,
                                float radius, bool positive, float headSize)
    {
        const int Segments = 24;
        float start = positive ? 20f : 160f;
        float end = positive ? 160f : 20f;

        Vector3 prev = center + x * radius;
        for (int i = 0; i <= Segments; i++)
        {
            float ang = Mathf.Deg2Rad * Mathf.Lerp(start, end, i / (float)Segments);
            Vector3 p = center + (x * Mathf.Cos(ang) + y * Mathf.Sin(ang)) * radius;

            if (i == Segments) DrawArrow(prev, p, headSize);
            else if (i > 0) Gizmos.DrawLine(prev, p);

            prev = p;
        }
    }
}