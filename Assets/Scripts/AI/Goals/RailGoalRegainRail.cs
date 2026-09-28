using System.Collections.Generic;
using UnityEngine;

namespace MM2.AI
{
    /// After an accident: drive a Hermite curve from wherever the wreck ended up back onto our own rail,
    /// a little way past our slot, waiting for a gap if needed, then hand back to normal driving.
    /// The car keeps its rail slot the whole time (see AITrafficCar.TrackAnchor), so traffic behind waits.
    public class RailGoalRegainRail : RailGoal
    {
        private const float MaxRegainDistance = 25f;  // further than this sideways from our rail: stay a wreck
        private const float MinJoinAhead = 6f;        // join at least this far past our slot...
        private const float JoinAheadPerLateral = 2f; // ...and more the further off to the side we are
        private const float RoadEndMargin = 5f;       // don't join right at the road end
        private const float RegainSpeed = 4f;         // m/s
        private const float Accel = 3f;
        private const float Brake = 8f;
        private const float HoldDistance = 6f;        // stop this far before the join if it isn't clear
        private const float UprightDot = 0.5f;        // up.y below this counts as flipped
        private const float LevelOutDistance = 2f;    // blend out of whatever tilt the crash left us at
        private const float SettleDistance = 3f;      // blend into the exact rail rotation over the last meters
        private const float MinHeadingDot = 0.25f;    // rail must run within ~75 degrees of our heading
        private const float GroundLookAhead = 1.5f;   // meters between the two ground samples that set our pitch
        private const float GroundProbeUp = 10f;      // ground rays start this far above the curve...
        private const float GroundProbeDown = 30f;    // ...and reach this far below it

        private static readonly RaycastHit[] groundHits = new RaycastHit[16];

        // Reused every regain: the evaluator keeps a reference to the list, so refill it and re-Init.
        private readonly List<HermitePoint> curvePoints = new List<HermitePoint> { default, default };
        private readonly HermiteEvaluator curve;
        private float curveLength;
        private float startGroundOffset;   // car origin height above the ground at each end of the curve,
        private float endGroundOffset;     // blended along it so we land exactly on the rail

        private float targetDistance;
        private float distance;
        private float speed;
        private float yaw;
        private Quaternion startRotation;
        private Quaternion joinRotation;
        private int signal;

        public RailGoalRegainRail(AITrafficCar car) : base(car)
        {
            curve = new HermiteEvaluator(curvePoints);
        }

        public override int? TurnSignalOverride => signal;

        public override void Enter()
        {
            distance = 0f;
            speed = 0f;
            signal = 0;

            if ((car.Rotation * Vector3.up).y < UprightDot || !car.HasAnchor ||
                Mathf.Abs(car.AnchorLateral) > MaxRegainDistance)
            {
                car.GiveUpOnRail(); // on its side/roof, or nowhere near the road any more
                return;
            }

            float lateral = car.AnchorLateral;
            float ahead = Mathf.Max(MinJoinAhead, Mathf.Abs(lateral) * JoinAheadPerLateral);
            if (!car.TryGetRegainTarget(ahead, RoadEndMargin, out targetDistance, out Vector3 joinPos,
                                        out Vector3 joinFwd, out joinRotation))
            {
                car.GiveUpOnRail();
                return;
            }

            // Start from the rigidbody's real pose rather than the interpolated transform, so switching to
            // kinematic doesn't pull the body back to where it was drawn a step ago.
            var rb = car.Body.Rb;
            Vector3 start = rb.position;
            Quaternion startRot = rb.rotation;
            Vector3 forward = Flat(startRot * Vector3.forward);

            // Only regain onto a rail that runs our way and is in front of us. Otherwise we'd have to spin
            // round on the spot (and in an intersection, snap back the other way when the road takes over).
            // Stay in the accident instead; InAccident tries again after its timer, by which time we may have
            // been shoved round.
            Vector3 toJoin = joinPos - start;
            toJoin.y = 0f;
            if (Vector3.Dot(forward, joinFwd) < MinHeadingDot || Vector3.Dot(toJoin, forward) <= 0f)
            {
                car.SetGoal(car.AccidentGoal);
                return;
            }

            car.SetOffRailPose(start, startRot, 0f, 0f);
            car.Body.MakeKinematic();

            // Unit tangents: HermiteEvaluate scales them by the endpoint distance itself.
            curvePoints[0] = new HermitePoint { Position = start, Tangent = forward };
            curvePoints[1] = new HermitePoint { Position = joinPos, Tangent = joinFwd };
            curve.Init(curvePoints);
            curveLength = HermitePoint.Distance(curvePoints[0], curvePoints[1]);

            startGroundOffset = TryGround(start, out Vector3 g0, out _) ? start.y - g0.y : 0f;
            endGroundOffset = TryGround(joinPos, out Vector3 g1, out _) ? joinPos.y - g1.y : 0f;

            startRotation = startRot;
            yaw = Yaw(forward);

            // Indicate toward the rail: right of it means pulling left.
            signal = lateral > 0.5f ? -1 : lateral < -0.5f ? 1 : 0;
        }

        public override void Update()
        {
            float remaining = curveLength - distance;
            bool clear = car.IsRegainPathClear(targetDistance, RegainSpeed);

            // Creep in, but hold short if someone's in the way.
            float wanted = (!clear && remaining < HoldDistance) ? 0f : RegainSpeed;
            speed = Mathf.MoveTowards(speed, wanted, (wanted < speed ? Brake : Accel) * Time.deltaTime);

            float step = Mathf.Min(speed * Time.deltaTime, remaining);
            distance += step;

            // Distance / length as the curve parameter, same approximation the intersection curves use.
            // The curve only fixes where we go across the ground; the height comes from raycasts so we
            // follow hills and kerbs rather than cutting straight through them.
            float t = curveLength > 0f ? Mathf.Clamp01(distance / curveLength) : 1f;
            float stepT = curveLength > 0f ? Mathf.Min(0.5f, GroundLookAhead / curveLength) : 0.5f;

            Vector3 pos = OnGround(t, out Vector3 normal);
            Vector3 tangent = t + stepT <= 1f
                ? OnGround(t + stepT, out _) - pos
                : pos - OnGround(t - stepT, out _);

            Quaternion rot = tangent.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(tangent, normal) : car.Rotation;
            rot = Quaternion.Slerp(startRotation, rot, Mathf.Clamp01(distance / LevelOutDistance));

            // Arrive in exactly the pose the rail will give us, so handing back to driving doesn't snap.
            float settle = 1f - Mathf.Clamp01((curveLength - distance) / SettleDistance);
            rot = Quaternion.Slerp(rot, joinRotation, settle);

            float newYaw = tangent.sqrMagnitude > 1e-6f ? Yaw(tangent) : yaw;
            float curvature = step > 1e-4f ? Mathf.DeltaAngle(yaw * Mathf.Rad2Deg, newYaw * Mathf.Rad2Deg) * Mathf.Deg2Rad / step : 0f;
            yaw = newYaw;

            car.SetOffRailPose(pos, rot, speed, curvature);
            car.TrackAnchor(); // our slot follows us in, and we take the lane once we're in it

            if (distance >= curveLength - 1e-3f && clear)
            {
                car.ResumeFromAnchor(targetDistance);
                car.SetGoal(car.DriveGoal);
            }
        }

        /// Curve position at t, dropped (or lifted) onto the ground underneath it.
        private Vector3 OnGround(float t, out Vector3 normal)
        {
            Vector3 p = curve.Evaluate(Mathf.Clamp01(t));
            if (TryGround(p, out Vector3 ground, out normal))
                p.y = ground.y + Mathf.Lerp(startGroundOffset, endGroundOffset, t);
            return p;
        }

        /// Highest static surface under 'p' (within the probe range). Anything with a rigidbody, us included,
        /// is ignored: that's cars and props, not ground.
        private static bool TryGround(Vector3 p, out Vector3 point, out Vector3 normal)
        {
            point = p;
            normal = Vector3.up;

            int count = Physics.RaycastNonAlloc(p + Vector3.up * GroundProbeUp, Vector3.down, groundHits,
                GroundProbeUp + GroundProbeDown, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);

            float best = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                var hit = groundHits[i];
                if (hit.rigidbody != null || hit.distance >= best)
                    continue;

                best = hit.distance;
                point = hit.point;
                normal = hit.normal;
            }

            return !float.IsPositiveInfinity(best);
        }

        private static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.forward;
        }

        private static float Yaw(Vector3 dir) => Mathf.Atan2(dir.x, dir.z); // + is to the right
    }
}
