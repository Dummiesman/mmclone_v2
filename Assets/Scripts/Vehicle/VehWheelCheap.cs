using UnityEngine;

public class VehWheelCheap : MonoBehaviour
{
    [Header("Setup")]
    [Tooltip("The vehicle body (phInertialCS in the original).")]
    public Rigidbody body;
    [Tooltip("Wheel centre in body space (Unity handedness).")]
    public Vector3 localWheelPosition;
    [Tooltip("Optional visual wheel; positioned from the suspension/deflection state.")]
    public Transform wheelVisual;
    [Tooltip("Layers the wheel can stand on. The body's own colliders are ignored regardless.")]
    public LayerMask groundMask = ~0;

    [Header("aiVehicleData")]
    public float Radius = 0.3f;
    public float Spring = 50000f;
    public float Damping = 5000f;
    public float RubberSpring = 40000f;
    public float RubberDamp = 2000f;
    public float Limit = 0.07f;

    // --- Runtime state (original field names in comments) ---
    public float Compression { get; private set; }
    public float LateralDeflection { get; private set; }
    public float LongitudinalDeflection { get; private set; }
    public bool Grounded { get; private set; }
    public bool BottomedOut { get; private set; }
    public RaycastHit Hit => hit;
    public Matrix4x4 WorldMatrix { get; private set; } = Matrix4x4.identity;
    public Vector3 VisualLocalPosition { get; private set; }

    float quarterGravity;
    RaycastHit hit;
    readonly RaycastHit[] hitBuffer = new RaycastHit[8];

    void Awake()
    {
        if (body == null) body = GetComponentInParent<Rigidbody>();
        Init(body, localWheelPosition);
    }

    public void Init(Rigidbody rb, Vector3 localPos)
    {
        body = rb;
        localWheelPosition = localPos;

        quarterGravity = -body.mass * Physics.gravity.y * 0.25f;

        ResetState();
        VisualLocalPosition = localWheelPosition;
        WorldMatrix = Matrix4x4.TRS(body.position + body.rotation * VisualLocalPosition, body.rotation, Vector3.one);
    }

    public void ResetState()
    {
        Compression = 0f;
        LongitudinalDeflection = 0f;
        LateralDeflection = 0f;
        Grounded = false;
        BottomedOut = false;
    }

    void FixedUpdate()
    {
        Step(Time.fixedDeltaTime);
    }

    public void Step(float dt)
    {
        float invDt = 1f / dt;

        Quaternion rot = body.rotation;
        Vector3 origin = body.position;
        Vector3 right = rot * Vector3.right;
        Vector3 up = rot * Vector3.up;
        Vector3 fwd = rot * Vector3.forward;

        // Segment runs from (radius + limit) above the wheel centre to the same distance below.
        Vector3 wheelPos = origin + rot * localWheelPosition;
        float reach = Radius + Limit;
        Vector3 start = wheelPos + up * reach;
        float segLength = 2f * reach;

        Grounded = CastSegment(start, -up, segLength, out hit);

        if (!Grounded || hit.normal.sqrMagnitude == 0f)
        {
            Compression = -Limit;
            LateralDeflection = 0f;
            LongitudinalDeflection = 0f;
            // Note: original leaves BottomedOut (dword_124) untouched here.
        }
        else
        {
            Vector3 n = hit.normal;

            // Contact point velocity (v + w x r), projected onto the ground plane.
            Vector3 contactVel = body.GetPointVelocity(hit.point);
            contactVel -= n * Vector3.Dot(contactVel, n);

            // --- Suspension ---
            float normalizedDistance = hit.distance / segLength;
            BottomedOut = normalizedDistance < 0.1f;

            float prevCompression = Compression;
            // 0 when the ground is exactly at the bottom of the wheel at rest height.
            Compression = 2f * Radius + Limit - hit.distance;
            float compressionVel = Mathf.Clamp((Compression - prevCompression) * invDt, -3f, 3f);

            float suspForce = Compression * Spring + compressionVel * Damping + quarterGravity;
            if (suspForce < 0f) suspForce = 0f;

            float normalLoad = Vector3.Dot(n, up) * suspForce;
            Vector3 force = n * normalLoad;

            // --- Rubber (tire) friction ---
            float latSpeed = Vector3.Dot(contactVel, right);
            float longSpeed = Vector3.Dot(contactVel, fwd);

            // Max deflection before the tire slides = friction force limit / rubber stiffness.
            float maxDeflection = normalLoad * VehWheel.WeatherFriction * 0.4f / RubberSpring;
            float latTarget = Sign0(latSpeed) * maxDeflection;
            float longTarget = Sign0(longSpeed) * maxDeflection;

            float prevLat = LateralDeflection;
            float prevLong = LongitudinalDeflection;

            // Deflection follows the contact patch travel this step, capped at the friction limit.
            LateralDeflection = Track(prevLat, latSpeed * dt, latTarget);
            LongitudinalDeflection = Track(prevLong, longSpeed * dt, longTarget);

            float latForce = -RubberSpring * LateralDeflection
                             - (LateralDeflection - prevLat) * invDt * RubberDamp;
            float longForce = -RubberSpring * LongitudinalDeflection
                              - (LongitudinalDeflection - prevLong) * invDt * RubberDamp;

            force += right * latForce + fwd * longForce;

            // Force + (contact - origin) x force torque, as in the original.
            body.AddForceAtPosition(force, hit.point, ForceMode.Force);
        }

        // --- Visual wheel placement (dword_158 / WorldMatrix) ---
        VisualLocalPosition = new Vector3(
            localWheelPosition.x - LateralDeflection * 0.2f,
            localWheelPosition.y + Compression,
            localWheelPosition.z + LongitudinalDeflection * 0.3f);

        WorldMatrix = Matrix4x4.TRS(origin + rot * VisualLocalPosition, rot, Vector3.one);
    }

    void LateUpdate()
    {
        // Use the (possibly interpolated) transform so the visual doesn't jitter.
        if (wheelVisual == null || body == null) return;
        Transform t = body.transform;
        wheelVisual.SetPositionAndRotation(t.position + t.rotation * VisualLocalPosition, t.rotation);
    }

    /// <summary>
    /// Clamp target into the range swept by [current, current + step].
    /// Matches the two branchy blocks around LABEL_30 / LABEL_38.
    /// </summary>
    static float Track(float current, float step, float target)
    {
        float a = current, b = current + step;
        return Mathf.Clamp(target, Mathf.Min(a, b), Mathf.Max(a, b));
    }

    /// <summary>Sign that returns 0 for 0 (Mathf.Sign returns 1).</summary>
    static float Sign0(float v) => v > 0f ? 1f : (v < 0f ? -1f : 0f);

    /// <summary>dgPhysManager::Collide on the segment, ignoring the body's own colliders.</summary>
    bool CastSegment(Vector3 start, Vector3 dir, float length, out RaycastHit best)
    {
        int doNotHitTheseMask = LayerMask.GetMask("PlayerVehicleBody", "VehicleBody", "Banger");
        int layerMask = ~(doNotHitTheseMask);
        groundMask = layerMask;
        int count = Physics.RaycastNonAlloc(start, dir, hitBuffer, length, groundMask, QueryTriggerInteraction.Ignore);
        best = default;
        float bestDist = float.MaxValue;
        bool found = false;
        for (int i = 0; i < count; i++)
        {
            if (hitBuffer[i].rigidbody == body) continue;
            if (hitBuffer[i].distance < bestDist)
            {
                bestDist = hitBuffer[i].distance;
                best = hitBuffer[i];
                found = true;
            }
        }
        return found;
    }

    void OnDrawGizmosSelected()
    {
        if (body == null) return;
        Vector3 up = body.rotation * Vector3.up;
        Vector3 wheelPos = body.position + body.rotation * localWheelPosition;
        float reach = Radius + Limit;
        Gizmos.color = Grounded ? Color.green : Color.red;
        Gizmos.DrawLine(wheelPos + up * reach, wheelPos - up * reach);
        Gizmos.DrawWireSphere(WorldMatrix.GetColumn(3), Radius);
    }
}