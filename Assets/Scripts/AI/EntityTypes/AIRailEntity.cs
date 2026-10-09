using UnityEngine;

namespace MM2.AI
{
    public abstract class AIRailEntity : AIEntity
    {
        // debug stats
        public static int UncachedProbeCount = 0;
        public static int CheapOrientCount = 0;
        public static int OrientSkipCount = 0;
        // end debug stats

        private const float SlowDownBufferMeters = 3f;

        public override int RoomID => roomId;
        public override Vector3 Position => position;
        public override Quaternion Rotation => rotation;
        public override float Speed => speed;

        protected Vector3 position;
        protected Quaternion rotation;

        public bool OnGround => (lastOrientWasSingleProbe) ? OrientRaycastResults[0] : OrientRaycastResults[0] && OrientRaycastResults[1] && OrientRaycastResults[2];

        // movement stuff
        protected float speed = 0.0f;
        protected float speedLimit = 999.0f;
        protected float accelerationRate = 5.0f;
        private int roomId = -1;

        protected const float StopGapMeters = 1.5f;
        protected const float BrakingRate = 8f;
        private const float CommitSlackMeters = 0.5f;

        // Once set, we no longer stop for the road end (reset when we change road).
        protected bool committedToIntersection = false;

        // raycast stuff
        private const float ProbeUpOffset = 10f;
        private const float ProbeLength = 15f;

        private bool lastOrientWasSingleProbe = false;
        protected bool[] OrientRaycastResults { get; private set; } = { false, false, false };
        protected RaycastHit[] OrientRaycastHits { get; private set; } = new RaycastHit[3];
        private readonly int groundMask;

        // current rail
        private HermiteEvaluator railEvaluator;

        // curve computing
        private const int CurveLengthSamples = 16;
        protected HermiteCurve intersectionCurve = new HermiteCurve();
        protected HermiteEvaluator intersectionCurveEvaluator;
        protected float intersectionCurveLength = 0f;


        // turn speed profile: allowed speed at each curve sample, so cars only slow for the actual bend
        private const float MaxLateralAccel = 6f;     // m/s^2, how hard we're willing to corner (raise for more arcade)
        private const float MinTurnSpeed = 4f;        // floor for very tight curves
        private readonly float[] curveSampleDist = new float[CurveLengthSamples + 1];
        private readonly float[] curveSampleSpeedSq = new float[CurveLengthSamples + 1];
        private readonly float[] curveSampleCurvature = new float[CurveLengthSamples + 1]; // signed, + = right
        protected bool curveHasBend = false;

        public float IntersectionTurnAngle => intersectionTurnAngle;
        protected float intersectionTurnAngle = 0f;

        public AIRailEntity(AINetwork network) : base(network)
        {
            groundMask = LayerMask.GetMask("Default", "BangerStatic");
        }

        private void OrientFromPath()
        {
            float percentage = NormalizedPathProgress;
            var evaluated = railEvaluator.Evaluate(percentage - 0.01f);
            var evaluatedAhead = railEvaluator.Evaluate(percentage + 0.01f);

            var percentagePos = evaluated;
            var directionVec = (evaluatedAhead - evaluated).normalized;

            Vector3 forward = directionVec * Direction;
            if (forward.sqrMagnitude > 1e-6f)
            {
                rotation = Quaternion.LookRotation(forward.normalized, Vector3.up);
            }
        }

        protected void Orient()
        {
            var road = (RoadInfo.RoadInstance != null) ? RoadInfo.RoadInstance.Road : null;
            if(road == null || !road.Flags.HasFlag(PathFlags.Flat))
            {
                // skip if player too far away
                foreach (var proxy in network.VehicleProxies)
                {
                    if (proxy.IsPlayer)
                    {
                        float dist = (proxy.Position - this.Position).ToVec2XZ().sqrMagnitude;
                        if (dist >= 10000.0f)
                        {
                            // too far away for full orient
                            OrientFromPath();
                            OrientSkipCount++;
                            return;
                        }
                    }
                }

                // do orient
                if(road != null) OrientFromPath();
                ThreePointOrient();
            }
            else
            {
                OrientFromPath();
                CheapOrientCount++;
            }
        }

        protected void ComputeIntersectionCurve()
        {
            intersectionCurveEvaluator = null;
            intersectionCurveLength = 0f;
            curveHasBend = false;
            intersectionTurnAngle = 0f;

            if (NextRoadInfo.RoadInstance == null || RoadInfo.RoadInstance == null)
                return;

            int lastSection = RoadInfo.RoadInstance.Road.NumSections - 1;

            // get road end and next road start
            var roadEnd = RoadInfo.RoadData.GetVertex(RailType, RoadInfo.RailIndex, lastSection);
            var roadEndDir = (roadEnd - RoadInfo.RoadData.GetVertex(RailType, RoadInfo.RailIndex, lastSection - 1)).normalized;

            var nxtRoadData = NextRoadInfo.RoadData;
            var nxtRoadStart = nxtRoadData.GetVertex(RailType, NextRoadInfo.RailIndex, 0);
            var nxtRoadStartDir = (nxtRoadData.GetVertex(RailType, NextRoadInfo.RailIndex, 1) - nxtRoadStart).normalized;

            // flatten direction vectors
            nxtRoadStartDir.y = 0f;
            roadEndDir.y = 0f;

            //the turn we're actually about to drive, measured off the rails themselves rather than
            //whatever left/right/ahead bucket the next road was picked from
            intersectionTurnAngle = Vector3.SignedAngle(roadEndDir, nxtRoadStartDir, Vector3.up);            

            // create a curve
            intersectionCurve.Points.Clear();
            intersectionCurve.Points.Add(new HermitePoint { Position = roadEnd, Tangent = roadEndDir * 1.05f });
            intersectionCurve.Points.Add(new HermitePoint { Position = nxtRoadStart, Tangent = nxtRoadStartDir * 1.05f });

            intersectionCurveEvaluator = new HermiteEvaluator(intersectionCurve.Points);

            // sample the curve: length, plus curvature at every sample point
            float length = 0f;
            Vector3 prev = intersectionCurveEvaluator.Evaluate(0f);
            Vector3 prevDir = Vector3.zero;
            float prevFlatLen = 0f;
            curveSampleDist[0] = 0f;
            for (int i = 0; i <= CurveLengthSamples; i++)
            {
                curveSampleSpeedSq[i] = float.PositiveInfinity;
                curveSampleCurvature[i] = 0f;
            }

            for (int i = 1; i <= CurveLengthSamples; i++)
            {
                Vector3 p = intersectionCurveEvaluator.Evaluate(i / (float)CurveLengthSamples);
                length += Vector3.Distance(prev, p);
                curveSampleDist[i] = length;

                Vector3 flat = p - prev;
                flat.y = 0f;
                float flatLen = flat.magnitude;
                if (flatLen > 1e-4f)
                {
                    Vector3 dir = flat / flatLen;
                    if (prevFlatLen > 0f)
                    {
                        // heading change around sample i-1, over the distance between segment midpoints
                        float signedAngle = Vector3.SignedAngle(prevDir, dir, Vector3.up) * Mathf.Deg2Rad;
                        float signedCurvature = signedAngle / (0.5f * (flatLen + prevFlatLen));
                        curveSampleCurvature[i - 1] = signedCurvature;
                        float curvature = Mathf.Abs(signedCurvature);
                        if (curvature > 1e-3f)
                        {
                            // v^2 * curvature = lateral acceleration
                            float v = Mathf.Max(MinTurnSpeed, Mathf.Sqrt(MaxLateralAccel / curvature));
                            curveSampleSpeedSq[i - 1] = v * v;
                            curveHasBend = true;
                        }
                    }
                    prevDir = dir;
                    prevFlatLen = flatLen;
                }
                prev = p;
            }
            intersectionCurveLength = length;

            // endpoints have no angle of their own, borrow from their neighbours
            curveSampleSpeedSq[0] = curveSampleSpeedSq[1];
            curveSampleSpeedSq[CurveLengthSamples] = curveSampleSpeedSq[CurveLengthSamples - 1];
            curveSampleCurvature[0] = curveSampleCurvature[1];
            curveSampleCurvature[CurveLengthSamples] = curveSampleCurvature[CurveLengthSamples - 1];
        }

        protected float GetCurveCurvature(float pos)
        {
            if (intersectionCurveLength <= 0f)
                return 0f;

            for (int i = 0; i < CurveLengthSamples; i++)
            {
                if (pos <= curveSampleDist[i + 1])
                {
                    float segLen = curveSampleDist[i + 1] - curveSampleDist[i];
                    float t = segLen > 1e-4f ? Mathf.Clamp01((pos - curveSampleDist[i]) / segLen) : 0f;
                    return Mathf.Lerp(curveSampleCurvature[i], curveSampleCurvature[i + 1], t);
                }
            }
            return curveSampleCurvature[CurveLengthSamples];
        }

        // / Fastest we may go at arc position 'pos' on the curve (negative = still on the road before it),
        // / braking comfortably for any tighter part further along. Past the apex this rises again.
        protected float GetCurveSpeedLimit(float pos)
        {
            if (!curveHasBend)
                return float.PositiveInfinity;

            float bestSq = float.PositiveInfinity;
            for (int i = 0; i <= CurveLengthSamples; i++)
            {
                // skip samples fully behind us (keep the one whose segment we're on)
                if (i < CurveLengthSamples && curveSampleDist[i + 1] <= pos)
                    continue;

                float sq = curveSampleSpeedSq[i];
                if (float.IsPositiveInfinity(sq))
                    continue;

                float ahead = Mathf.Max(0f, curveSampleDist[i] - pos);
                bestSq = Mathf.Min(bestSq, sq + 2f * BrakingRate * ahead);
            }
            return Mathf.Sqrt(bestSq);
        }

        protected virtual bool AllowedThroughIntersection()
        {
            var nextRoad = NextRoadInfo.RoadInstance;
            if (nextRoad == null)
                return false;

            // if (nextRoad.AccidentOccurred) TODO
            // return false;

            var destIntersection = CurrentDestIntersection;
            var controlDevices = destIntersection?.ControlDevices;
            if (controlDevices == null)
                return false;

            var intersectionInfo = RoadInfo.SideOfRoad == RoadSide.Left
                ? RoadInfo.RoadInstance.Road.RightEndData
                : RoadInfo.RoadInstance.Road.LeftEndData;

            var indexInList = intersectionInfo.IntersectionRoadIndex < 32
                ? intersectionInfo.IntersectionRoadIndex
                : destIntersection.Intersection.Roads.IndexOf(RoadInfo.RoadInstance.Road);

            if (indexInList < 0 || indexInList >= controlDevices.Count)
                return false;

            // Is there room on the next road, or is the last entity still cramming the entrance?
            var nextLane = nextRoad.GetLane(NextRoadInfo.SideOfRoad, RailType, NextRoadInfo.RailIndex);
            if (nextLane == null)
                return false;

            // Entry point is wherever we'd join the lane, in that lane's direction of travel.
            float nextRoadLength = nextRoad.Road.Length;
            float entryDistance = nextLane.Direction > 0 ? 0f : nextRoadLength;

            // Space already promised to entities crossing the intersection towards the same lane.
            float reserved = 0f;
            var crossing = destIntersection.EntitiesInIntersection;
            for (int i = 0; i < crossing.Count; i++)
            {
                var other = crossing[i];
                if (other == this) continue;
                var otherNext = other.NextRoadInfo;
                if (otherNext.RoadInstance != nextRoad || otherNext.SideOfRoad != NextRoadInfo.SideOfRoad || otherNext.RailIndex != NextRoadInfo.RailIndex)
                    continue;
                reserved += (other.FrontBumperDistance - other.RearBumperDistance) + SlowDownBufferMeters;
            }

            var last = nextLane.NextAhead(entryDistance / nextRoadLength, this);
            if (last != null || reserved > 0f)
            {
                float gap = last != null
                    ? (last.CurrentPathDistance + last.RearBumperDistance - entryDistance) * nextLane.Direction
                    : nextRoadLength;
                if (gap < reserved + (FrontBumperDistance - RearBumperDistance) + SlowDownBufferMeters)
                    return false;
            }

            // Ask the control device last: some devices (stop signs) consume state when they say yes.
            return controlDevices[indexInList].CanEnterIntersection(this);
        }

        public override void SetRoad(RoadPositioningInfo newRoadInfo)
        {
            if(newRoadInfo.RoadInstance != null)
            {
                var curve = newRoadInfo.RoadData.GetCurve(RailType, newRoadInfo.RailIndex);
                if (curve != null)
                {
                    railEvaluator = new HermiteEvaluator(curve);
                }
                else
                {
                    railEvaluator = null;
                }
            }
            else
            {
                railEvaluator = null;
            }
            committedToIntersection = false;
            base.SetRoad(newRoadInfo);
        }

        public virtual void PositionAlongPath(float percentage)
        {
            var roadData = RoadInfo.RoadData;
            if (roadData == null)
                return;
            var road = RoadInfo.RoadInstance.Road;
            if (road.NumSections < 2)
                return;
            
            var evaluated = railEvaluator.Evaluate(percentage);
            position = evaluated;

            Orient();
        }

        // / <summary>
        // / Get entity corners in world space
        // / </summary>
        public virtual void GetCorners(out Vector3 frontLeft, out Vector3 frontRight, out Vector3 rearRight)
        {
            Vector3 forward = rotation * Vector3.forward;
            Vector3 right = rotation * Vector3.right;

            Vector3 front = forward * FrontBumperDistance;
            Vector3 rear = forward * RearBumperDistance;
            Vector3 left = right * LeftSideDistance;
            Vector3 starboard = right * RightSideDistance;

            frontLeft = position + front + left;
            frontRight = position + front + starboard;
            rearRight = position + rear + starboard;
        }

        private bool Probe(Vector3 point, out RaycastHit hit)
        {
            var ray = new Ray(point + Vector3.up * ProbeUpOffset, Vector3.down);
            return Physics.Raycast(ray, out hit, ProbeLength, groundMask);
        }

        public void OnePointOrient()
        {
            if (RoomID <= 0) return;

            lastOrientWasSingleProbe = true;

            OrientRaycastResults[0] = Probe(position, out OrientRaycastHits[0]);
            UncachedProbeCount++;

            if (!OnGround)
                return;

            var upDir = OrientRaycastHits[0].normal;
            if (upDir.y < 0f)
                upDir = -upDir;

            position = new Vector3(position.x, OrientRaycastHits[0].point.y, position.z);

            var forwardDir = Vector3.ProjectOnPlane(rotation * Vector3.forward, upDir);
            if (forwardDir.sqrMagnitude < 1e-6f)
                forwardDir = Vector3.ProjectOnPlane(rotation * Vector3.up, upDir);

            rotation = Quaternion.LookRotation(forwardDir.normalized, upDir);
        }

        public void ThreePointOrient()
        {
            if (RoomID <= 0) return;

            // [0] is FL, [1] is FR, [2] is RR
            GetCorners(out var cornerFl, out var cornerFr, out var cornerRr);

            lastOrientWasSingleProbe = false;

            OrientRaycastResults[0] = Probe(cornerFl, out OrientRaycastHits[0]);
            OrientRaycastResults[1] = Probe(cornerFr, out OrientRaycastHits[1]);
            OrientRaycastResults[2] = Probe(cornerRr, out OrientRaycastHits[2]);
            UncachedProbeCount += 3;

            if (!OnGround)
                return;                                   // a probe missed, keep last pose

            var pFl = OrientRaycastHits[0].point;
            var pFr = OrientRaycastHits[1].point;
            var pRr = OrientRaycastHits[2].point;

            var groundForward = pFr - pRr;   // RR -> FR, right side: pitch
            var groundRight = pFr - pFl;   // FL -> FR, front edge: roll
            var upDir = Vector3.Cross(groundForward, groundRight);

            if (upDir.sqrMagnitude < 1e-8f)
                return;                                   // degenerate triangle, keep last orientation
            upDir.Normalize();
            if (upDir.y < 0f)
                upDir = -upDir;                           // probes hit a back face

            // sit on the plane the three contacts define, sampled under the body centre
            if (upDir.y > 1e-4f)
            {
                var y = pFl.y - (upDir.x * (position.x - pFl.x)
                               + upDir.z * (position.z - pFl.z)) / upDir.y;
                position = new Vector3(position.x, y, position.z);
            }

            // keep the heading the rail gave us, but tilt it into the ground plane
            var forwardDir = Vector3.ProjectOnPlane(rotation * Vector3.forward, upDir);
            if (forwardDir.sqrMagnitude < 1e-6f)
                forwardDir = Vector3.ProjectOnPlane(rotation * Vector3.up, upDir);

            rotation = Quaternion.LookRotation(forwardDir.normalized, upDir);
        }

        public void RandomizePositionAlongPath()
        {
            NormalizedPathProgress = network.Random.value * 0.5f;
            PositionAlongPath(NormalizedPathProgress);
        }

        // / Distance my front bumper can still travel before it must be stopped behind the car ahead.
        private float GetStoppingDistanceForFollowing(Lane lane)
        {
            var ahead = lane.NextAhead(NormalizedPathProgress, this);
            if (ahead == null) return float.PositiveInfinity;

            float myFront = CurrentPathDistance + FrontBumperDistance;
            float theirRear = ahead.CurrentPathDistance + ahead.RearBumperDistance;
            return (theirRear - myFront) - StopGapMeters;
        }

        // / Distance my front bumper can still travel before the stop line at the road end.
        private float GetStoppingDistanceForRoadEnd()
        {
            if (committedToIntersection) return float.PositiveInfinity;

            float stopLine = RoadInfo.RoadInstance.Road.Length - SlowDownBufferMeters;
            float myFront = CurrentPathDistance + FrontBumperDistance;
            float distanceToStopLine = stopLine - myFront;

            if (!AllowedThroughIntersection())
                return distanceToStopLine;

            // Cleared through. Commit once we're too close to stop comfortably, so a permission
            // that flickers off (stop sign consumed, light turns yellow/red) can't freeze us past the line.
            float brakingDistance = (speed * speed) / (2f * BrakingRate);
            if (distanceToStopLine <= brakingDistance + CommitSlackMeters)
                committedToIntersection = true;

            return float.PositiveInfinity;
        }

        // / Distance my front bumper can travel before hitting something past the end of this road
        // / (inside the intersection or on the next road). Only asked when the lane ahead is empty.
        protected virtual float GetStoppingDistanceBeyondRoadEnd() => float.PositiveInfinity;

        // / Soft speed cap (e.g. slowing for an upcoming turn). Approached with comfortable braking, never snapped to.
        protected virtual float GetMaxApproachSpeed() => speedLimit;

        // / Hard target is a safety limit and is snapped to; soft target is eased towards.
        protected void ApplySpeed(float hardTarget, float softTarget)
        {
            float target = Mathf.Min(hardTarget, softTarget);
            if (hardTarget < speed)
                speed = hardTarget;
            else if (target < speed)
                speed = Mathf.MoveTowards(speed, target, BrakingRate * Time.deltaTime);
            else
                speed = Mathf.MoveTowards(speed, target, accelerationRate * Time.deltaTime);
        }

        protected float GetTargetSpeed(float stoppingDistance)
        {
            if (stoppingDistance <= 0f) return 0f;
            if (float.IsPositiveInfinity(stoppingDistance)) return speedLimit;
            return Mathf.Min(speedLimit, Mathf.Sqrt(2f * BrakingRate * stoppingDistance));
        }

        protected void RefreshNextRoad()
        {
            var next = network.DetermineNextRoad(this);

            // Only accept a destination we can actually drive onto, otherwise we'd wait at the line forever.
            if (next.HasValue && next.Value.RoadInstance != null &&
                next.Value.RoadInstance.GetLane(next.Value.SideOfRoad, RailType, next.Value.RailIndex) != null)
            {
                NextRoadInfo = next.Value;
                ComputeIntersectionCurve();
            }
            else
            {
                NullNextRoad();
                intersectionCurveEvaluator = null;
            }
        }

        public override void Update()
        {
            base.Update();

            if (RoadInfo.RoadInstance == null)
            {
                return;
            }

            var lane = RoadInfo.RoadInstance.GetLaneOf(this);
            if (lane == null)
            {
                Debug.LogError($"I'm lost!!! {RoadInfo.RoadInstance.Id}/{RoadInfo.SideOfRoad}/{RoadInfo.RailIndex}");
                return;
            }

            // Nobody ahead in this lane: keep looking through the intersection, otherwise the car in front
            // "vanishes" the moment it leaves the road and we floor it straight into its back.
            float followDistance = GetStoppingDistanceForFollowing(lane);
            if (float.IsPositiveInfinity(followDistance))
                followDistance = GetStoppingDistanceBeyondRoadEnd();

            float stoppingDistance = Mathf.Min(followDistance, GetStoppingDistanceForRoadEnd());
            ApplySpeed(GetTargetSpeed(stoppingDistance), GetMaxApproachSpeed());

            // Hard guarantee: never move past the stop point, whatever the frame time.
            float step = Mathf.Min(speed * Time.deltaTime, Mathf.Max(0f, stoppingDistance));
            CurrentPathDistance += step;
            PositionAlongPath(NormalizedPathProgress);

            // room
            if (RoadInfo.RoadInstance == null)
            {
                roomId = 0;
            }
            else
            {
                int newRoom = network.Level.FindRoomIdWithWarpsCheckMiss(Position, roomId);
                if (newRoom != roomId)
                {
                    roomId = newRoom;
                }
            }
        }

        public override void DrawGizmos()
        {
            base.DrawGizmos();

            // these match what OnePointOrient/ThreePointOrient actually use (not ProbeUpOffset/ProbeLength, which are dead)
            const float ProbeHeight = 10f;
            const float ProbeMaxDist = 20f;
            const float MarkerRadius = 0.15f;

            bool single = lastOrientWasSingleProbe;
            int probeCount = single ? 1 : 3;

            // probe origins, same order as OrientRaycastHits: [0]=FL, [1]=FR, [2]=RR (or [0]=centre when single)
            var probeOrigin = new Vector3[3];
            if (single)
                probeOrigin[0] = position;
            else
                GetCorners(out probeOrigin[0], out probeOrigin[1], out probeOrigin[2]);

            // mode tint: cyan = one point (flat road), amber = three point
            Color modeColor = single ? new Color(0f, 0.9f, 1f) : new Color(1f, 0.85f, 0f);

            for (int i = 0; i < probeCount; i++)
            {
                Vector3 start = probeOrigin[i] + Vector3.up * ProbeHeight;
                bool hit = OrientRaycastResults[i];

                // the ray itself: green to the contact point, red for the full miss length
                Gizmos.color = hit ? Color.green : Color.red;
                Gizmos.DrawLine(start, hit ? OrientRaycastHits[i].point : start + Vector3.down * ProbeMaxDist);

                if (hit)
                {
                    Gizmos.DrawSphere(OrientRaycastHits[i].point, MarkerRadius);
                    Gizmos.color = new Color(0f, 1f, 0f, 0.45f);
                    Gizmos.DrawLine(OrientRaycastHits[i].point, OrientRaycastHits[i].point + OrientRaycastHits[i].normal);
                }
                else
                {
                    // cross at the origin so a dead probe is readable even with no ground under it
                    DrawGizmoCross(probeOrigin[i], 0.35f);
                }

                // marks which probes are in play this frame
                Gizmos.color = modeColor;
                Gizmos.DrawWireCube(probeOrigin[i], Vector3.one * (MarkerRadius * 2f));
            }

            // the plane three point orient actually solved from
            if (!single && OrientRaycastResults[0] && OrientRaycastResults[1] && OrientRaycastResults[2])
            {
                Gizmos.color = modeColor;
                Gizmos.DrawLine(OrientRaycastHits[0].point, OrientRaycastHits[1].point);
                Gizmos.DrawLine(OrientRaycastHits[1].point, OrientRaycastHits[2].point);
                Gizmos.DrawLine(OrientRaycastHits[2].point, OrientRaycastHits[0].point);
            }

            // overall validity + the up vector we ended up with
            Gizmos.color = OnGround ? Color.green : Color.red;
            Gizmos.DrawWireSphere(position, 0.25f);
            Gizmos.DrawLine(position, position + rotation * Vector3.up * 1.5f);
        }

        private static void DrawGizmoCross(Vector3 p, float size)
        {
            Gizmos.DrawLine(p + Vector3.left * size, p + Vector3.right * size);
            Gizmos.DrawLine(p + Vector3.forward * size, p + Vector3.back * size);
            Gizmos.DrawLine(p + Vector3.down * size, p + Vector3.up * size);
        }
    }
}