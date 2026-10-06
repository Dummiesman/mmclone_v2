using UnityEngine;

namespace MM2.AI
{
    public abstract class AIRailEntity : AIEntity
    {
        private const float SlowDownBufferMeters = 3f;
        private const bool UseLazyOrient = true;

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
        private bool lastOrientWasSingleProbe = false;
        private int lastOrientFrame = -1;
        private bool orientedThisFrame => Time.frameCount == lastOrientFrame;
        protected bool[] OrientRaycastResults { get; private set; } = { false, false, false };
        protected RaycastHit[] OrientRaycastHits { get; private set; } = new RaycastHit[3];
        protected CachedRaycastPoly CachedRaycastPoly = new CachedRaycastPoly();

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

        public AIRailEntity(AINetwork network) : base(network)
        {
        }

        protected void ComputeIntersectionCurve()
        {
            intersectionCurveEvaluator = null;
            intersectionCurveLength = 0f;
            curveHasBend = false;

            if (NextRoadInfo.RoadInstance == null || RoadInfo.RoadInstance == null)
                return;

            int lastSection = RoadInfo.RoadInstance.Road.NumSections - 1;

            //get road end and next road start
            var roadEnd = RoadInfo.RoadData.GetVertex(RailType, RoadInfo.RailIndex, lastSection);
            var roadEndDir = (roadEnd - RoadInfo.RoadData.GetVertex(RailType, RoadInfo.RailIndex, lastSection - 1)).normalized;

            var nxtRoadData = NextRoadInfo.RoadData;
            var nxtRoadStart = nxtRoadData.GetVertex(RailType, NextRoadInfo.RailIndex, 0);
            var nxtRoadStartDir = (nxtRoadData.GetVertex(RailType, NextRoadInfo.RailIndex, 1) - nxtRoadStart).normalized;

            //flatten direction vectors
            nxtRoadStartDir.y = 0f;
            roadEndDir.y = 0f;

            //create a curve
            intersectionCurve.Points.Clear();
            intersectionCurve.Points.Add(new HermitePoint { Position = roadEnd, Tangent = roadEndDir * 1.05f });
            intersectionCurve.Points.Add(new HermitePoint { Position = nxtRoadStart, Tangent = nxtRoadStartDir * 1.05f });

            intersectionCurveEvaluator = new HermiteEvaluator(intersectionCurve.Points);

            //sample the curve: length, plus curvature at every sample point
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

        /// Fastest we may go at arc position 'pos' on the curve (negative = still on the road before it),
        /// braking comfortably for any tighter part further along. Past the apex this rises again.
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

            //if (nextRoad.AccidentOccurred) TODO
            //return false;

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

            float distance = Mathf.Clamp01(percentage) * road.Length;
            int sectionBase = 0;
            for (int i = road.NumSections - 2; i > 0; i--)
            {
                if (distance >= road.GetSectionDistance(RoadInfo.SideOfRoad, i))
                {
                    sectionBase = i;
                    break;
                }
            }

            float lenBase = road.GetSectionDistance(RoadInfo.SideOfRoad, sectionBase);
            float lenNext = road.GetSectionDistance(RoadInfo.SideOfRoad, sectionBase + 1);
            float lerpAmount = lenNext > lenBase ? Mathf.Clamp01((distance - lenBase) / (lenNext - lenBase)) : 0f;

            var vertexBase = roadData.GetVertex(this.RailType, RoadInfo.RailIndex, sectionBase);
            var vertexNext = roadData.GetVertex(this.RailType, RoadInfo.RailIndex, sectionBase + 1);

            Vector3 percentagePos = Vector3.Lerp(vertexBase, vertexNext, lerpAmount);
            Vector3 directionVec = (vertexNext - vertexBase).normalized;


            position = percentagePos;
            if(UseLazyOrient)
            {
                rotation = Quaternion.LookRotation(directionVec * Direction, Vector3.up);
            }
            else if (road.Flags.HasFlag(PathFlags.Flat))
            {
                OnePointOrient(false);
            }
            else
            {
                ThreePointOrient(false);
            }
        }

        public virtual void GetCorners(out Vector3 frontLeft, out Vector3 frontRight, out Vector3 rearRight)
        {
            frontLeft = Vector3.zero;
            frontRight = Vector3.zero;
            rearRight = Vector3.zero;
        }

        private void OnePointOrientFromCachedPoly()
        {
            position = new Vector3(position.x, CachedRaycastPoly.CalculateHeightForPoint(position), position.z);
        }

        private void ThreePointOrientFromCachedPoly()
        {
            //orient vehicle
            //[0] is FL
            //[1] is FR
            //[2] is RR
            position = new Vector3(position.x, (OrientRaycastHits[1].point.y + OrientRaycastHits[2].point.y) / 2f, position.z);

            var forwardDir = -(OrientRaycastHits[1].point - OrientRaycastHits[2].point).normalized;
            var upDir = Vector3.Cross(forwardDir, -(OrientRaycastHits[1].point - OrientRaycastHits[0].point).normalized);
            rotation = Quaternion.LookRotation(rotation * Vector3.forward, upDir);
        }


        /// <summary>
        /// Finds the ground below this entity and places the entity above it
        /// </summary>
        /// <param name="allowCached">If set to <c>true</c> allow cached.</param>
        public void OnePointOrient(bool allowCached = true)
        {
            //optimization, check cached
            if (allowCached && lastOrientWasSingleProbe && CachedRaycastPoly.IsValid && CachedRaycastPoly.IsValidForPoint(position))
            {
                OnePointOrientFromCachedPoly();
                return;
            }

            // last was valid room?
            if (RoomID <= 0) return;

            //I hate this but whatever, allows var reuse
            lastOrientWasSingleProbe = true;

            //
            Vector3 raycastAddHeight = Vector3.up * 2f;
            var raycastOrigin = position + raycastAddHeight;
            Ray raycastRay = new Ray(raycastOrigin, Vector3.down);

            OrientRaycastResults[0] = Physics.Raycast(raycastRay, out OrientRaycastHits[0], 10f);

            if (!OnGround)
            {
                //Cannot orient this entity, a raycast missed D:
                CachedRaycastPoly.Invalidate();
                return;
            }
            else
            {
                CachedRaycastPoly.Init(OrientRaycastHits[0]);
            }

            OnePointOrientFromCachedPoly();
            lastOrientFrame = Time.frameCount;
        }

        /// <summary>
        /// Orients the entity based on GetCorners() positions
        /// </summary>
        public void ThreePointOrient(bool allowCached = true)
        {
            //optimization, check cached
            if (allowCached && !lastOrientWasSingleProbe && CachedRaycastPoly.IsValid && CachedRaycastPoly.IsValidForPoint(position))
            {
                ThreePointOrientFromCachedPoly();
                return;
            }

            // in a valid room?
            if (RoomID <= 0) return;

            //I hate this but whatever, allows var reuse
            lastOrientWasSingleProbe = false;

            //get corners
            GetCorners(out var cornerFl, out var cornerFr, out var cornerRr);

            //do raycasts
            Vector3 raycastAddHeight = Vector3.up * 2f;
            var raycastOriginFrontLeft = cornerFl + raycastAddHeight;
            var raycastOriginFrontRight = cornerFr + raycastAddHeight;
            var raycastOriginRearRight = cornerRr + raycastAddHeight;

            Ray raycastRayFrontLeft = new Ray(raycastOriginFrontLeft, Vector3.down);
            Ray raycastRayFrontRight = new Ray(raycastOriginFrontRight, Vector3.down);
            Ray raycastRayRearRight = new Ray(raycastOriginRearRight, Vector3.down);

            OrientRaycastResults[0] = Physics.Raycast(raycastRayFrontLeft, out OrientRaycastHits[0], 10f);
            OrientRaycastResults[1] = Physics.Raycast(raycastRayFrontRight, out OrientRaycastHits[1], 10f);
            OrientRaycastResults[2] = Physics.Raycast(raycastRayRearRight, out OrientRaycastHits[2], 10f);


            //TODO: dont generate so much garbage, fix!
            //CachedRaycastPoly = OnGround ? new CachedRaycastPoly(OrientRaycastHits[0]) : null;
            if (!OnGround)
            {
                //Cannot orient this entity, a raycast missed D:
                return;
            }

            ThreePointOrientFromCachedPoly();
            lastOrientFrame = Time.frameCount;
        }

        public void RandomizePositionAlongPath()
        {
            NormalizedPathProgress = network.Random.value * 0.5f;
            PositionAlongPath(NormalizedPathProgress);
        }

        /// Distance my front bumper can still travel before it must be stopped behind the car ahead.
        private float GetStoppingDistanceForFollowing(Lane lane)
        {
            var ahead = lane.NextAhead(NormalizedPathProgress, this);
            if (ahead == null) return float.PositiveInfinity;

            float myFront = CurrentPathDistance + FrontBumperDistance;
            float theirRear = ahead.CurrentPathDistance + ahead.RearBumperDistance;
            return (theirRear - myFront) - StopGapMeters;
        }

        /// Distance my front bumper can still travel before the stop line at the road end.
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

        /// Distance my front bumper can travel before hitting something past the end of this road
        /// (inside the intersection or on the next road). Only asked when the lane ahead is empty.
        protected virtual float GetStoppingDistanceBeyondRoadEnd() => float.PositiveInfinity;

        /// Soft speed cap (e.g. slowing for an upcoming turn). Approached with comfortable braking, never snapped to.
        protected virtual float GetMaxApproachSpeed() => speedLimit;

        /// Hard target is a safety limit and is snapped to; soft target is eased towards.
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
    }
}