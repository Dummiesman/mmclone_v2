using UnityEngine;

namespace MM2.AI
{
    public class AITrafficCar : AIRailEntity
    {
        public AmbientHornAudio HornAudio => hornAudio;
        public VoiceAudio VoiceAudio => voiceAudio;

        // constants
        private const float SignalDistance = 30f; // start indicating this far before the road end
        private const float CableCarClearance = 0.75f;
        private const float CableCarSampleStep = 1f;

        // lane changing
        private const float LaneChangeChance = 0.35f;       // odds of wanting a lane change on each new road
        private const float LaneChangeWindowStart = 0.25f;  // normalized road progress
        private const float LaneChangeWindowEnd = 0.75f;
        private const float LaneChangeSignalTime = 1f;      // seconds of indicating before moving over
        private const float LaneChangeTime = 2.5f;          // manoeuvre length = speed * this...
        private const float LaneChangeMinLength = 12f;      // ...but never shorter than this (meters)
        private const float LaneChangeFrontGap = 4f;        // meters of clear space needed ahead in the target lane
        private const float LaneChangeRearGap = 4f;         // and behind
        private const float LaneChangeHeadway = 1f;         // extra meters of gap per m/s of closing speed

        // physics / accidents
        public const float KnockOffImpulse = 100f;          // N*s; softer hits just bump off us (AITrafficCarBody caps contacts just above this)
        private const float HitRestitution = 0.15f;         // 0 = we end up moving with whatever hit us, 1 = bouncy

        // avoiding oncoming players
        private const float AvoidLookAhead = 30f;           // meters
        private const float AvoidTimeToImpact = 1.5f;       // seconds
        private const float AvoidMinSpeed = 2.24f;          // m/s (5 mph): slower than this we just sit there, no swerve, no horn
        private const float AvoidMinClosingSpeed = 6f;      // m/s (~13 mph) of speed difference toward the player
        private const float AvoidPlayerHalfWidth = 1f;
        private const float AvoidMargin = 0.5f;

        // rail anchor while knocked off
        private const float LaneHalfWidth = 1.75f;          // a wreck blocks its lane while its centre is within this + our half width of the rail
        private const float AnchorReinsertStep = 0.25f;     // meters the slot must move before we re-sort it in the lane
        private const float AnchorLookStep = 0.01f;

        // Lower rail index is the faster lane (see GetRoadSpeedLimit), so it's assumed to be the leftmost.
        // Flip to +1 if rail indices run the other way.
        private const int RailIndexStepLeft = -1;

        private enum LaneChangeState { None, Signalling, Changing }

        private LaneChangeState laneChangeState = LaneChangeState.None;
        private bool wantsLaneChange;
        private float laneChangeTriggerProgress;
        private int laneChangeOffset;            // rail index delta of the target lane
        private float laneChangeTimer;           // signalling time so far
        private float laneChangeProgress;        // 0..1 through the manoeuvre
        private float laneChangeLength;          // meters of road the manoeuvre takes
        private Vector3 laneChangeStartOffset;   // old rail position - new rail position at the moment we switched
        private float laneChangeYaw;             // radians, relative to the rail
        private float laneChangeCurvature;       // for the steering wheels

        private AIVehicleData vehicleData;

        private AIVehicleModel vehicleModel;
        private AITrafficCarBody body;
        private AmbientVehicleAudio audio;
        private AmbientHornAudio hornAudio;
        private VoiceAudio voiceAudio;

        // goals
        private RailGoal goal;
        public RailGoalRandomDrive DriveGoal { get; private set; }
        public RailGoalAvoidAccident AvoidGoal { get; private set; }
        public RailGoalInAccident AccidentGoal { get; private set; }
        public RailGoalRegainRail RegainGoal { get; private set; }
        public RailGoal CurrentGoal => goal;

        public int AccidentCount { get; private set; }
        public bool IsWrecked { get; private set; }   // gave up on the rail for good

        // Knocked loose since the last Update. The body goes dynamic straight away in the collision callback;
        // the lane/goal side waits for Update rather than touching lanes from inside physics.
        private bool hitPending;
        private float hitForce;

        // steering set by goals that move us off the plain rail pose (swerving, regaining)
        private float steerCurvature;
        private float swerveYaw;

        // Rail anchor: while knocked off and while regaining we keep our slot on the rail (see TrackAnchor).
        private bool anchored;
        private bool anchorInIntersection;   // slot is on the intersection curve rather than a road
        private bool anchorInLane;           // road slot currently registered in the lane (wreck is within it sideways)
        private RoadPositioningInfo anchorRoad;
        private float anchorDistance;        // meters along the road or intersection curve
        private float anchorLateral;         // meters, + is right of the rail

        public override int RoomID => roomId;
        private int roomId;

        public AITrafficCarBody Body => body;
        public AINetwork Network => network;

        public override float FrontBumperDistance
        {
            get => vehicleData.CG.z + (vehicleData.Size.z * 0.5f);
        }

        public override float RearBumperDistance
        {
            get => vehicleData.CG.z - (vehicleData.Size.z * 0.5f);
        }

        public override float LeftSideDistance
        {
            get => vehicleData.CG.x - (vehicleData.Size.x * 0.5f);
        }

        public override float RightSideDistance
        {
            get => vehicleData.CG.x + (vehicleData.Size.x * 0.5f);
        }

        public float HalfWidth => 0.5f * (RightSideDistance - LeftSideDistance);

        private float lastSpeed = 0f;

        // wheels (degrees): X is roll, Y is steering
        private const float MaxSteerAngle = 40f;
        private const float SteerRate = 90f;          // deg/s, keeps steering from snapping at curve entry/exit
        private float wheelXAngle = 0f;
        private float wheelYAngle = 0f;

        public int TurnSignal { get; private set; } /// -1 left, 1 right, 0 off.
        public bool HazardLights => goal != null && goal.HazardLights;

        // What the lights should actually show; use these rather than reading TurnSignal directly.
        public bool LeftIndicatorOn => TurnSignal < 0 || HazardLights;
        public bool RightIndicatorOn => TurnSignal > 0 || HazardLights;
        public float WheelXAngle => wheelXAngle;
        public float WheelYAngle => wheelYAngle;

        private const float BrakeLightDecelerationThreshold = 1.0f;
        public bool TailLightsActive => tailLightsActive;
        private bool tailLightsActive;

        // intersection state
        private bool inIntersection = false;
        private IntersectionInstance currentIntersection;
        private float intersectionDistance = 0f;
        private Lane sourceLane;

        public bool InIntersection => inIntersection;
        public bool IsChangingLanes => laneChangeState != LaneChangeState.None;
        public bool IsOnRoad => RoadInfo.RoadInstance != null;
        public bool IsOnRail => IsOnRoad || inIntersection;
        public bool HasNextRoad => NextRoadInfo.RoadInstance != null;
        public float DistanceToRoadEnd => IsOnRoad ? RoadInfo.RoadInstance.Road.Length - CurrentPathDistance : 0f;

        /// Anything but a car that's already loose can be knocked off (including one creeping back onto the rail).
        private bool CanBeKnockedOff => goal != AccidentGoal;

        /// Pose comes from physics or the regain curve, not the rail.
        public bool IsOffRail => goal == AccidentGoal || goal == RegainGoal;

        private float GetRoadSpeedLimit(RoadPositioningInfo info)
        {
            const float FreewayLaneBonus = 5f;
            float exceedLimit = 0f;

            var road = info.RoadInstance.Road;
            float limit = road.SpeedLimit + exceedLimit;

            if ((road.Flags & PathFlags.Freeway) != 0)
            {
                int lanes = info.RoadData.numLanes; // rNumLanes / lNumLanes for this side
                limit += (lanes - info.RailIndex - 1) * FreewayLaneBonus;
            }

            return limit;
        }

        public override void GetCorners(out Vector3 frontLeft, out Vector3 frontRight, out Vector3 rearRight)
        {
            if (vehicleData != null)
            {
                var data = vehicleData;
                frontLeft = Position + Rotation * data.WheelPositions[0];
                frontRight = Position + Rotation * data.WheelPositions[1];
                rearRight = Position + Rotation * data.WheelPositions[3];
            }
            else
            {
                base.GetCorners(out frontLeft, out frontRight, out rearRight);
            }
        }

        void UpdateTaillights()
        {
            float deceleration = (lastSpeed - Speed) / Time.deltaTime;
            tailLightsActive = (Speed <= 0.5f) || (deceleration > BrakeLightDecelerationThreshold);
        }

        private void UpdateTurnSignal()
        {
            int? goalSignal = goal?.TurnSignalOverride;
            if (goalSignal.HasValue)
            {
                TurnSignal = goalSignal.Value;
                return;
            }

            if (inIntersection)
                return; // keep whatever we had on approach until we're through

            if (laneChangeState != LaneChangeState.None)
            {
                TurnSignal = laneChangeOffset == RailIndexStepLeft ? -1 : 1;
                return;
            }

            if (NextRoadInfo.RoadInstance == null || RoadInfo.RoadInstance == null)
            {
                TurnSignal = 0;
                return;
            }

            float toRoadEnd = RoadInfo.RoadInstance.Road.Length - CurrentPathDistance;
            TurnSignal = toRoadEnd <= SignalDistance ? NextRoadInfo.Relation : 0;
        }

        // ---------------------------------------------------------------------------------------------
        // Goals
        // ---------------------------------------------------------------------------------------------

        public void SetGoal(RailGoal next)
        {
            if (goal == next)
                return;

            goal?.Exit();
            goal = next;
            goal.Enter(); // may switch goal again (e.g. RegainRail giving up straight away)
        }

        /// Normal rail driving: road or intersection. Returns meters travelled this frame.
        public float DriveOnRail(bool allowLaneChanges)
        {
            if (inIntersection)
                return UpdateIntersection();

            return UpdateRoad(allowLaneChanges);
        }

        private bool EntityCanBeThreat(AIEntity entity)
        {
            if (Mathf.Abs(entity.Position.y - position.y) >= 5.0f)
                return false; // below or above
            return Vector3.Distance(entity.Position, position) <= AvoidLookAhead;
        }

        /// Is a player about to hit us head on? Picks the nearest one in our path.
        public bool FindOncomingThreat(out Vector3 threatPosition, out AIEntity threatEntity)
        {
            threatPosition = default;
            threatEntity = null;

            // Stopped or crawling (queued at lights, parked behind a wreck): don't react to anything.
            if (speed < AvoidMinSpeed)
                return false;

            Vector3 forward = rotation * Vector3.forward;
            Vector3 right = rotation * Vector3.right;
            Vector3 ourVelocity = forward * speed;
            float reach = HalfWidth + AvoidPlayerHalfWidth + AvoidMargin;
            float bestAhead = float.PositiveInfinity;

            foreach (var player in network.VehicleProxies)
            {
                if(!EntityCanBeThreat(player)) 
                    continue;

                Vector3 toPlayer = player.Position - position;
                toPlayer.y = 0f;

                float ahead = Vector3.Dot(toPlayer, forward);
                if (ahead < FrontBumperDistance || ahead > AvoidLookAhead || ahead >= bestAhead)
                    continue;

                if (Mathf.Abs(Vector3.Dot(toPlayer, right)) > reach)
                    continue; // not in our path

                // Speed difference along our heading: both moving the same way at similar speeds isn't a threat.
                Vector3 playerVelocity = player.Rotation * Vector3.forward * player.Speed;
                float closing = Vector3.Dot(ourVelocity - playerVelocity, forward);
                if (closing < AvoidMinClosingSpeed || ahead / closing > AvoidTimeToImpact)
                    continue;

                bestAhead = ahead;
                threatPosition = player.Position;
                threatEntity = player;
            }

            return !float.IsPositiveInfinity(bestAhead);
        }

        /// Layers a sideways offset (meters, + right) and matching heading over the rail pose DriveOnRail set.
        /// 'slope' is d(offset)/d(distance along the road).
        public void ApplySwerve(float offset, float slope, float travelled)
        {
            Vector3 right = rotation * Vector3.right;
            right.y = 0f;
            if (right.sqrMagnitude > 1e-6f)
                position += right.normalized * offset;

            float yaw = Mathf.Atan(slope);
            rotation = Quaternion.AngleAxis(yaw * Mathf.Rad2Deg, Vector3.up) * rotation;

            steerCurvature = travelled > 1e-4f ? (yaw - swerveYaw) / travelled : 0f;
            swerveYaw = yaw;
        }

        public void ClearSwerve()
        {
            swerveYaw = 0f;
        }

        /// Off the rail and physics-driven: take our pose from the rigidbody.
        public void SyncFromBody()
        {
            // The transform, not rb.position: that's the raw fixed-step pose, the transform is interpolated.
            var t = body.transform;
            position = t.position;
            rotation = t.rotation;
            speed = body.Rb.velocity.magnitude; // linearVelocity on Unity 6
            steerCurvature = 0f;
        }

        /// Off the rail but driving ourselves (regaining): set the pose directly.
        public void SetOffRailPose(Vector3 newPosition, Quaternion newRotation, float newSpeed, float curvature)
        {
            position = newPosition;
            rotation = newRotation;
            speed = newSpeed;
            steerCurvature = curvature;
        }


        /// Couldn't get back on the rail (flipped, nowhere near a road): stay a physics wreck.
        public void GiveUpOnRail()
        {
            IsWrecked = true;
            if (!body.IsDynamic)
                body.MakeDynamic();
            SetGoal(AccidentGoal);
        }

        // ---------------------------------------------------------------------------------------------
        // Rail anchor
        //
        // A knocked-off car keeps its slot on the rail, slid along to the point level with the wreck, so
        // traffic queues behind it instead of plowing through, and so regaining is just driving back to
        // our own rail. The slot is only registered in the lane while the wreck is actually in the lane
        // sideways; knocked onto the pavement, traffic can pass.
        // ---------------------------------------------------------------------------------------------

        public bool HasAnchor => anchored;
        public bool AnchorInLane => anchorInLane;
        public float AnchorLateral => anchorLateral;

        private float AnchorLength => anchorInIntersection ? intersectionCurveLength : anchorRoad.RoadInstance.Road.Length;

        private void CaptureAnchor()
        {
            anchored = false;
            anchorInLane = false;
            anchorLateral = 0f;

            if (inIntersection)
            {
                if (intersectionCurveEvaluator == null || intersectionCurveLength <= 0f)
                    return;

                // Stay in the intersection: cars sharing our path read intersectionDistance and Position.
                anchorInIntersection = true;
                anchorDistance = intersectionDistance;
                anchored = true;
            }
            else if (RoadInfo.RoadInstance != null)
            {
                anchorInIntersection = false;
                anchorRoad = RoadInfo;
                anchorDistance = CurrentPathDistance;
                anchorInLane = true; // we're already in the lane at this spot
                anchored = true;
            }
        }

        /// Rail pose 'distance' meters along the anchor's road or intersection curve.
        private void SampleAnchorPose(float distance, out Vector3 railPos, out Vector3 railFwd, out Quaternion railRot)
        {
            float length = AnchorLength;
            float t = length > 0f ? Mathf.Clamp01(distance / length) : 0f;

            if (anchorInIntersection)
            {
                railPos = intersectionCurveEvaluator.Evaluate(t);
                railFwd = t + AnchorLookStep <= 1f
                    ? intersectionCurveEvaluator.Evaluate(t + AnchorLookStep) - railPos
                    : railPos - intersectionCurveEvaluator.Evaluate(t - AnchorLookStep);
                // Same rotation UpdateIntersection would give us here.
                railRot = railFwd.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(railFwd, Vector3.up) : rotation;
            }
            else
            {
                // Borrow PositionAlongPath to read the rail pose, then put everything back.
                var savedInfo = RoadInfo;
                float savedProgress = NormalizedPathProgress;
                Vector3 savedPos = position;
                Quaternion savedRot = rotation;

                RoadInfo = anchorRoad;
                PositionAlongPath(t);
                railPos = position;
                railFwd = rotation * Vector3.forward;
                railRot = rotation;

                RoadInfo = savedInfo;
                NormalizedPathProgress = savedProgress;
                position = savedPos;
                rotation = savedRot;
            }

            railFwd.y = 0f;
            railFwd = railFwd.sqrMagnitude > 1e-6f ? railFwd.normalized : Vector3.forward;
        }

        /// Slides our rail slot to the point level with where we actually are, carrying it onto the next
        /// road or intersection curve if we've been shoved past the end of this one, and joining or leaving
        /// the lane depending on whether we're still in it sideways. Call after our pose has been updated.
        public void TrackAnchor()
        {
            if (!anchored)
                return;

            Vector3 railPos, railFwd, offset;

            // One pass per segment we get carried into; more than a couple would mean a huge shove.
            for (int segment = 0; segment < 3; segment++)
            {
                float length = AnchorLength;
                float d = anchorDistance;

                // A couple of projection steps is plenty for road curvature.
                for (int i = 0; i < 2; i++)
                {
                    SampleAnchorPose(Mathf.Clamp(d, 0f, length), out railPos, out railFwd, out _);
                    offset = position - railPos;
                    offset.y = 0f;
                    d += Vector3.Dot(offset, railFwd);
                }

                // Pushed off the end (rear-ended into the intersection, say): take the slot with us, so we
                // regain forwards from where we now are instead of trying to reverse back up the road.
                if (d > length && TryAdvanceAnchor(d - length))
                    continue;

                d = Mathf.Clamp(d, 0f, length);

                SampleAnchorPose(d, out railPos, out railFwd, out _);
                offset = position - railPos;
                offset.y = 0f;
                anchorLateral = Vector3.Dot(offset, Vector3.Cross(Vector3.up, railFwd)); // cross(up, fwd) = right

                MoveAnchor(d, Mathf.Abs(anchorLateral) < LaneHalfWidth + HalfWidth);
                return;
            }
        }

        /// Moves the slot onto the next segment: road -> the intersection curve at its end -> the next road.
        /// False if there's nowhere to go, in which case the slot just clamps to the end of this one.
        private bool TryAdvanceAnchor(float overshoot)
        {
            Vector3 savedPos = position;
            Quaternion savedRot = rotation;

            if (anchorInIntersection)
            {
                var next = NextRoadInfo;
                if (next.RoadInstance == null)
                    return false;

                LeaveIntersectionState();

                anchorInIntersection = false;
                anchorRoad = next;
                anchorDistance = Mathf.Min(overshoot, next.RoadInstance.Road.Length);

                // Join the new road properly, so the limits and the curve off its end are set up for
                // whatever comes next (including being shoved along again).
                NormalizedPathProgress = anchorDistance / next.RoadInstance.Road.Length;
                base.SetRoad(next);
                speedLimit = GetRoadSpeedLimit(next);
                RefreshNextRoad();
                anchorInLane = true; // MoveAnchor drops us out again if we're not in the lane sideways

                position = savedPos; // physics still owns our pose
                rotation = savedRot;
                return true;
            }

            if (NextRoadInfo.RoadInstance == null || intersectionCurveEvaluator == null || intersectionCurveLength <= 0f)
                return false; // dead end: nothing to carry the slot onto

            // Read these before the road goes: they're derived from it.
            var intersection = CurrentDestIntersection;
            var lane = anchorInLane ? anchorRoad.RoadInstance.GetLaneOf(this) : null;

            if (anchorInLane)
                RemoveFromCurrentRoad();
            anchorInLane = false;
            RoadInfo = RoadPositioningInfo.Null;

            currentIntersection = intersection;
            sourceLane = lane;
            currentIntersection?.Enter(this);
            inIntersection = true;

            anchorInIntersection = true;
            anchorDistance = Mathf.Min(overshoot, intersectionCurveLength);
            intersectionDistance = anchorDistance;

            position = savedPos;
            rotation = savedRot;
            return true;
        }

        private void MoveAnchor(float distance, bool wantInLane)
        {
            if (anchorInIntersection)
            {
                // Intersection membership doesn't depend on where we are; others just read these.
                anchorDistance = distance;
                intersectionDistance = distance;
                return;
            }

            if (!wantInLane)
            {
                if (anchorInLane)
                {
                    RemoveFromCurrentRoad();
                    RoadInfo = RoadPositioningInfo.Null;
                    anchorInLane = false;
                }
                anchorDistance = distance;
                return;
            }

            if (anchorInLane && Mathf.Abs(distance - anchorDistance) < AnchorReinsertStep)
                return;

            // (Re)insert at the new spot, so the lane order stays right even if we got shoved past someone.
            if (anchorInLane)
                RemoveFromCurrentRoad();

            Vector3 savedPos = position;
            Quaternion savedRot = rotation;

            anchorDistance = distance;
            NormalizedPathProgress = distance / AnchorLength; // before SetRoad: Lane.Insert sorts on it
            base.SetRoad(anchorRoad);                          // lane membership only, no lane-change roll
            anchorInLane = true;

            position = savedPos; // physics / the regain curve still owns our pose
            rotation = savedRot;
        }

        /// Where to rejoin: 'ahead' meters past our slot, kept clear of the road end.
        public bool TryGetRegainTarget(float ahead, float roadEndMargin, out float targetDistance,
                                       out Vector3 railPos, out Vector3 railFwd, out Quaternion railRot)
        {
            targetDistance = 0f;
            railPos = default;
            railFwd = Vector3.forward;
            railRot = Quaternion.identity;
            if (!anchored)
                return false;

            float length = AnchorLength;
            float max = Mathf.Max(anchorDistance, length - (anchorInIntersection ? 0f : roadEndMargin));
            targetDistance = Mathf.Clamp(anchorDistance + ahead, anchorDistance, max);

            SampleAnchorPose(targetDistance, out railPos, out railFwd, out railRot);
            return true;
        }

        /// Can we drive into our lane at 'targetDistance'?
        public bool IsRegainPathClear(float targetDistance, float mySpeed)
        {
            if (!anchored || anchorInIntersection)
                return true; // cars in the intersection already treat us as on our path

            var lane = anchorRoad.RoadInstance.GetLane(anchorRoad.SideOfRoad, RailType, anchorRoad.RailIndex);
            if (lane == null)
                return true;

            // Already holding our slot: traffic behind is waiting for us, so only mind what's ahead.
            return IsLaneClearAt(lane, targetDistance / AnchorLength, targetDistance, mySpeed, checkBehind: !anchorInLane);
        }

        /// Regain finished at 'targetDistance': back to normal rail driving from there.
        public void ResumeFromAnchor(float targetDistance)
        {
            if (!anchored)
                return;

            anchored = false;

            if (anchorInIntersection)
            {
                intersectionDistance = targetDistance;
                return; // UpdateIntersection takes it from here
            }

            if (anchorInLane)
                RemoveFromCurrentRoad();
            anchorInLane = false;

            float p = targetDistance / anchorRoad.RoadInstance.Road.Length;
            NormalizedPathProgress = p; // before SetRoad: Lane.Insert sorts on it
            SetRoad(anchorRoad);        // full version: speed limit, next road, lane change roll
            PositionAlongPath(p);
        }

        // ---------------------------------------------------------------------------------------------
        // Collisions
        // ---------------------------------------------------------------------------------------------

        /// From AITrafficCarBody.OnCollisionEnter.
        public void OnBodyCollision(Collision collision)
        {
            if (!CanBeKnockedOff || hitPending || body.IsDynamic)
                return;

            // Any rigidbody can knock us loose (player, wrecks, props). Wrecks keep their rail slot, so traffic
            // queues behind them rather than driving into them, which keeps chain reactions from snowballing.
            var other = collision.rigidbody;
            if (other == null)
                return;

            // Capped just above the threshold by AITrafficCarBody's contact modifier, so a hard hit reads as
            // about KnockOffImpulse and a soft one as whatever it actually was.
            if (collision.impulse.magnitude < KnockOffImpulse)
                return;

            var rb = body.Rb;
            float myMass = rb.mass;
            float otherMass = other.mass;
            float totalMass = myMass + otherMass;
            if (totalMass <= 0f)
                return;

            float reducedMass = (myMass * otherMass) / totalMass;

            // Average the contacts: a flat panel hit gives several, and one arbitrary corner point would spin us.
            int count = collision.contactCount;
            Vector3 point = Vector3.zero;
            Vector3 normal = Vector3.zero;
            for (int i = 0; i < count; i++)
            {
                var c = collision.GetContact(i);
                point += c.point;
                normal += c.normal;
            }
            point /= count;
            normal = normal.sqrMagnitude > 1e-6f ? normal.normalized : Vector3.up;

            // We're still kinematic, so rb.velocity reads zero: our velocity is the rail pose's.
            // Theirs comes off the rigidbody, at the contact point so a spinning wreck reads right.
            Vector3 myVelocity = (rotation * Vector3.forward) * speed;
            Vector3 relativeVelocity = myVelocity - other.GetPointVelocity(point);

            Vector3 impulse = relativeVelocity * reducedMass;

            // The shove we should have taken. ContactPoint.normal points at us, but flip it if the averaging
            // or a weird contact set turned it around, so it always pushes us away from them.
            if (Vector3.Dot(relativeVelocity, normal) > 0f)
                normal = -normal;
            float closing = -Vector3.Dot(relativeVelocity, normal);
            Vector3 hitImpulse = normal * ((1f + HitRestitution) * reducedMass * closing);

            body.MakeDynamic();
            body.DepenetrateFrom(collision.collider);

            hitPending = true;
            hitForce = Mathf.Abs(impulse.x) + Mathf.Abs(impulse.y) + Mathf.Abs(impulse.z);
        }

        private void ProcessPendingHit()
        {
            if (!hitPending)
                return;

            hitPending = false;
            if (CanBeKnockedOff)
                KnockOffRail();
        }

        private void KnockOffRail()
        {
            if (voiceAudio != null)
            { 
                voiceAudio.PlayCollisionReaction(hitForce);
            }

            // The body is where we are now. Take its pose here, not a frame later, so nothing reading
            // Position (wheels, lights, audio) sees a jump halfway through a goal.
            SyncFromBody();

            // Keep our slot on the rail so traffic queues behind the wreck instead of plowing through it.
            ResetLaneChange();
            ClearSwerve();
            TurnSignal = 0;
            if (!anchored)
                CaptureAnchor(); // already anchored if we were hit mid-regain

            AccidentCount++;
            SetGoal(AccidentGoal);
        }

        private void DetachFromRail()
        {
            ResetLaneChange();
            ClearSwerve();
            if (inIntersection) LeaveIntersectionState();
            if (RoadInfo.RoadInstance != null) RemoveFromCurrentRoad();
            RoadInfo = RoadPositioningInfo.Null;
            NullNextRoad();
            TurnSignal = 0;
            anchored = false;
            anchorInLane = false;
        }

        // ---------------------------------------------------------------------------------------------
        // Lane changing
        // ---------------------------------------------------------------------------------------------

        private void ResetLaneChange()
        {
            laneChangeState = LaneChangeState.None;
            wantsLaneChange = false;
            laneChangeOffset = 0;
            laneChangeTimer = 0f;
            laneChangeProgress = 0f;
            laneChangeStartOffset = Vector3.zero;
            laneChangeYaw = 0f;
            laneChangeCurvature = 0f;
        }

        /// Called whenever we get a new road: decide whether we'll try a lane change on it, and where.
        private void RollLaneChange()
        {
            ResetLaneChange();

            var info = RoadInfo;
            if (info.RoadInstance == null)
                return;

            if (info.RoadInstance.GetLaneCount(info.SideOfRoad, RailType) < 2)
                return;

            wantsLaneChange = network.Random.value < LaneChangeChance;
            laneChangeTriggerProgress = network.Random.Range(LaneChangeWindowStart, LaneChangeWindowEnd);
        }

        /// Is there room for us in 'lane' with our front/rear at pathDistance + bumper offsets?
        public bool IsLaneClearAt(Lane lane, float progress, float pathDistance, float mySpeed, bool checkBehind = true)
        {
            float myFront = pathDistance + FrontBumperDistance;
            float myRear = pathDistance + RearBumperDistance;

            // First car at or ahead of us. If it's alongside, its rear is behind our front and the gap goes negative.
            var ahead = lane.NextAhead(progress, this);
            if (ahead != null)
            {
                float gap = (ahead.CurrentPathDistance + ahead.RearBumperDistance) - myFront;
                float closing = Mathf.Max(0f, mySpeed - ahead.Speed);
                if (gap < LaneChangeFrontGap + closing * LaneChangeHeadway)
                    return false;
            }

            // First car behind us, with extra room if it's coming up faster than we're going.
            var behind = checkBehind ? lane.NextBehind(progress, this) : null;
            if (behind != null)
            {
                float gap = myRear - (behind.CurrentPathDistance + behind.FrontBumperDistance);
                float closing = Mathf.Max(0f, behind.Speed - mySpeed);
                if (gap < LaneChangeRearGap + closing * LaneChangeHeadway)
                    return false;
            }

            return true;
        }

        /// Is there room to slot into 'lane' alongside our current position?
        private bool IsLaneClearForChange(Lane lane)
        {
            return IsLaneClearAt(lane, NormalizedPathProgress, CurrentPathDistance, Speed);
        }

        /// Picks a clear neighbouring lane (randomly if both sides are clear). False if neither is.
        private bool TryPickLaneChangeTarget(out int offset)
        {
            offset = 0;

            var road = RoadInfo.RoadInstance;
            var current = road.GetLaneOf(this);
            if (current == null)
                return false;

            var left = road.GetNeighbourLane(current, RailIndexStepLeft);
            var right = road.GetNeighbourLane(current, -RailIndexStepLeft);
            bool leftOk = left != null && IsLaneClearForChange(left);
            bool rightOk = right != null && IsLaneClearForChange(right);

            if (leftOk && rightOk)
                offset = network.Random.value < 0.5f ? RailIndexStepLeft : -RailIndexStepLeft;
            else if (leftOk)
                offset = RailIndexStepLeft;
            else if (rightOk)
                offset = -RailIndexStepLeft;

            return offset != 0;
        }

        /// Moves us onto the target rail right away (so traffic in that lane sees us and follows us),
        /// then visually slides us over from where we were.
        private void BeginLaneChange(Lane target)
        {
            Vector3 oldPosition = position;
            float progress = NormalizedPathProgress;

            var info = RoadInfo;
            info.RailIndex = target.RailIndex;

            RemoveFromCurrentRoad();
            // Progress before SetRoad: Lane.Insert sorts on it.
            NormalizedPathProgress = progress;
            base.SetRoad(info); // base only: the override would re-roll the lane change
            PositionAlongPath(progress);

            speedLimit = GetRoadSpeedLimit(info); // freeway lanes have different limits
            RefreshNextRoad();                     // where we can go next depends on the lane

            laneChangeStartOffset = oldPosition - position;
            laneChangeLength = Mathf.Max(LaneChangeMinLength, Speed * LaneChangeTime);
            laneChangeProgress = 0f;
            laneChangeYaw = 0f;
            laneChangeCurvature = 0f;
            wantsLaneChange = false;
            laneChangeState = LaneChangeState.Changing;

            ApplyLaneChangeOffset(0f);
        }

        /// Layers the remaining sideways offset and the matching heading on top of the rail pose base.Update set.
        private void ApplyLaneChangeOffset(float travelled)
        {
            float t = laneChangeProgress;
            float remaining = 1f - t * t * (3f - 2f * t); // smoothstep, so we ease in and out of the move
            position += laneChangeStartOffset * remaining;

            // Heading follows the slope of the sideways move: d(offset)/ds = -offset * 6t(1 - t) / length.
            Vector3 lateral = laneChangeStartOffset;
            lateral.y = 0f;
            Vector3 railForward = rotation * Vector3.forward;
            Vector3 dir = railForward - lateral * (6f * t * (1f - t) / laneChangeLength);

            float yawDeg = Vector3.SignedAngle(railForward, dir, Vector3.up);
            rotation = Quaternion.AngleAxis(yawDeg, Vector3.up) * rotation;

            float yaw = yawDeg * Mathf.Deg2Rad;
            laneChangeCurvature = travelled > 1e-4f ? (yaw - laneChangeYaw) / travelled : 0f;
            laneChangeYaw = yaw;
        }

        private void UpdateLaneChange(float travelled)
        {
            switch (laneChangeState)
            {
                case LaneChangeState.None:
                    {
                        if (!wantsLaneChange)
                            return;

                        float p = NormalizedPathProgress;
                        if (p < laneChangeTriggerProgress)
                            return;

                        if (p > LaneChangeWindowEnd)
                        {
                            wantsLaneChange = false; // never found a gap, stay put on this road
                            return;
                        }

                        // Only start indicating if there's actually room; otherwise keep looking while in the window.
                        if (TryPickLaneChangeTarget(out laneChangeOffset))
                        {
                            laneChangeTimer = 0f;
                            laneChangeState = LaneChangeState.Signalling;
                        }
                        return;
                    }

                case LaneChangeState.Signalling:
                    {
                        laneChangeTimer += Time.deltaTime;
                        if (laneChangeTimer < LaneChangeSignalTime)
                            return;

                        var road = RoadInfo.RoadInstance;
                        var current = road.GetLaneOf(this);
                        var target = current != null ? road.GetNeighbourLane(current, laneChangeOffset) : null;

                        // Re-check: someone may have moved in while we were indicating.
                        if (target != null && IsLaneClearForChange(target))
                        {
                            BeginLaneChange(target);
                        }
                        else if (target == null || NormalizedPathProgress > LaneChangeWindowEnd)
                        {
                            ResetLaneChange(); // give up
                        }
                        // else keep indicating until the gap opens
                        return;
                    }

                case LaneChangeState.Changing:
                    {
                        laneChangeProgress += travelled / laneChangeLength;
                        if (laneChangeProgress >= 1f)
                        {
                            ResetLaneChange(); // base.Update already has us on the new rail, back to normal driving
                            return;
                        }

                        ApplyLaneChangeOffset(travelled);
                        return;
                    }
            }
        }

        // ---------------------------------------------------------------------------------------------

        private void EnterIntersection(float overshoot)
        {
            // Short road and we ran out of room mid-change: just finish it, the curve takes over from here.
            ResetLaneChange();

            // Read this before RoadInfo is cleared, it's derived from it.
            currentIntersection = CurrentDestIntersection;
            sourceLane = RoadInfo.RoadInstance.GetLaneOf(this);

            RemoveFromCurrentRoad();
            RoadInfo = RoadPositioningInfo.Null;

            currentIntersection?.Enter(this);
            inIntersection = true;
            intersectionDistance = overshoot;
        }

        private void LeaveIntersectionState()
        {
            currentIntersection?.Leave(this);
            currentIntersection = null;
            sourceLane = null;
            inIntersection = false;
        }

        private void ExitIntersection(float overshoot)
        {
            var next = NextRoadInfo;
            LeaveIntersectionState();

            // Progress before SetRoad: Lane.Insert sorts on it.
            NormalizedPathProgress = Mathf.Clamp01(overshoot / next.RoadInstance.Road.Length);
            SetRoad(next);
            PositionAlongPath(NormalizedPathProgress);
        }

        private float GetCableCarYieldDistance(AICableCar cable, float myEntryPos)
        {
            if (intersectionCurveEvaluator == null || intersectionCurveLength <= 0f)
                return float.PositiveInfinity;

            float myFront = myEntryPos + FrontBumperDistance;
            float clearance = 0.5f * (RightSideDistance - LeftSideDistance) + cable.HalfWidth + CableCarClearance;
            float clearanceSq = clearance * clearance;

            // Already on its path: stopping would leave us in the way, so keep going and clear out.
            if (cable.PathComesWithin(Position, clearanceSq))
                return float.PositiveInfinity;

            for (float d = Mathf.Max(0f, myFront); d <= intersectionCurveLength; d += CableCarSampleStep)
            {
                Vector3 p = intersectionCurveEvaluator.Evaluate(d / intersectionCurveLength);
                if (cable.PathComesWithin(p, clearanceSq))
                    return d - myFront - StopGapMeters;
            }

            return float.PositiveInfinity;
        }

        /// Returns meters travelled along the road this frame.
        private float UpdateRoad(bool allowLaneChanges)
        {
            // Not placed yet.
            if (RoadInfo.RoadInstance == null)
                return 0f;

            // Dead end last time we looked, try again.
            if (NextRoadInfo.RoadInstance == null)
                RefreshNextRoad();

            float distanceBefore = CurrentPathDistance;
            base.Update();
            float travelled = Mathf.Max(0f, CurrentPathDistance - distanceBefore);

            // A manoeuvre already under way always finishes; new ones only start when allowed.
            if (allowLaneChanges || laneChangeState == LaneChangeState.Changing)
                UpdateLaneChange(travelled);

            float roadLength = RoadInfo.RoadInstance.Road.Length;
            if (NextRoadInfo.RoadInstance != null && CurrentPathDistance >= roadLength)
            {
                EnterIntersection(CurrentPathDistance - roadLength);
            }

            return travelled;
        }

        /// Returns meters travelled along the intersection curve this frame.
        private float UpdateIntersection()
        {
            // Follow whatever is ahead: cars still crossing on a shared path, or the tail of the lane we're joining.
            float maxTravel = GetStoppingDistanceThroughIntersection(currentIntersection, sourceLane,
                intersectionDistance, intersectionCurveLength - intersectionDistance);

            ApplySpeed(GetTargetSpeed(maxTravel), GetCurveSpeedLimit(intersectionDistance));

            float step = Mathf.Min(speed * Time.deltaTime, Mathf.Max(0f, maxTravel));
            intersectionDistance += step;

            if (intersectionCurveEvaluator == null || intersectionDistance >= intersectionCurveLength)
            {
                ExitIntersection(Mathf.Max(0f, intersectionDistance - intersectionCurveLength));
                return step;
            }

            const float LookStep = 0.01f;
            float t = intersectionDistance / intersectionCurveLength;

            Vector3 pos = intersectionCurveEvaluator.Evaluate(t);
            Vector3 dir = t + LookStep <= 1f
                ? intersectionCurveEvaluator.Evaluate(t + LookStep) - pos
                : pos - intersectionCurveEvaluator.Evaluate(t - LookStep);

            position = pos;
            if (dir.sqrMagnitude > 1e-6f)
                rotation = Quaternion.LookRotation(dir, Vector3.up);

            return step;
        }

        /// Has 'other' (which left my lane ahead of me) moved off my path? Once our curves split
        /// sideways by more than our combined half widths it's no longer in the way.
        private bool HasDivergedFromMyPath(AITrafficCar other)
        {
            if (intersectionCurveEvaluator == null || intersectionCurveLength <= 0f)
                return false;

            float t = Mathf.Clamp01(other.intersectionDistance / intersectionCurveLength);
            Vector3 offset = other.Position - intersectionCurveEvaluator.Evaluate(t);
            offset.y = 0f;

            return offset.magnitude > HalfWidth + other.HalfWidth + 0.5f;
        }

        private void UpdateWheels(float distanceRolled)
        {
            if (vehicleData == null)
                return;

            // Roll: arc length / radius.
            float radius = vehicleData.WheelPositions[0].y;
            if (radius > 0.01f)
            {
                wheelXAngle += (distanceRolled / radius) * Mathf.Rad2Deg;
                wheelXAngle = Mathf.Repeat(wheelXAngle, 360f);
            }

            // Steer: bicycle model, angle = atan(wheelbase * curvature). Straight on roads unless something bends us.
            float curvature;
            if (IsOffRail)
                curvature = steerCurvature; // physics or the regain curve, whatever the rail underneath says
            else if (inIntersection)
                curvature = GetCurveCurvature(intersectionDistance) + steerCurvature; // curve, plus any swerve on top
            else if (laneChangeState == LaneChangeState.Changing)
                curvature = laneChangeCurvature;
            else
                curvature = steerCurvature; // swerving; zero otherwise

            float targetSteer = 0f;
            if (curvature != 0f)
            {
                float wheelbase = Mathf.Abs(vehicleData.WheelPositions[0].z - vehicleData.WheelPositions[2].z);
                targetSteer = Mathf.Clamp(Mathf.Atan(wheelbase * curvature) * Mathf.Rad2Deg, -MaxSteerAngle, MaxSteerAngle);
            }
            wheelYAngle = Mathf.MoveTowards(wheelYAngle, targetSteer, SteerRate * Time.deltaTime);
        }

        /// Distance my front bumper can travel before it must stop behind something on my path through the
        /// intersection. Positions use two frames: "entry" (0 = road end, grows into the intersection) for cars
        /// that left my lane, and "exit" (distance still to go until the next road) for cars merging into my lane.
        private float GetStoppingDistanceThroughIntersection(IntersectionInstance intersection, Lane mySourceLane,
                                                             float myEntryPos, float myExitDist)
        {
            float best = float.PositiveInfinity;
            float myFront = FrontBumperDistance;

            // Tail of the lane we're joining.
            var nextLane = NextRoadInfo.RoadInstance?.GetLane(NextRoadInfo.SideOfRoad, RailType, NextRoadInfo.RailIndex);
            var blocker = nextLane?.NextAhead(0f, this);
            if (blocker != null)
            {
                float theirRear = blocker.CurrentPathDistance + blocker.RearBumperDistance;
                best = myExitDist + theirRear - myFront - StopGapMeters;
            }

            if (intersection == null)
                return best;

            var crossing = intersection.EntitiesInIntersection;
            for (int i = 0; i < crossing.Count; i++)
            {
                if (crossing[i] is AICableCar cable)
                {
                    best = Mathf.Min(best, GetCableCarYieldDistance(cable, myEntryPos));
                    continue;
                }

                var other = crossing[i] as AITrafficCar;
                if (other == null || other == this)
                    continue;

                // Left from my lane ahead of me: we share the start of the path, even if we turn different ways.
                if (mySourceLane != null && other.sourceLane == mySourceLane &&
                    (other.intersectionDistance > myEntryPos || (other.intersectionDistance == myEntryPos && other.ID < ID)) &&
                    (SameLane(other.NextRoadInfo, NextRoadInfo) || !HasDivergedFromMyPath(other)))
                {
                    float gap = (other.intersectionDistance + other.RearBumperDistance) - (myEntryPos + myFront) - StopGapMeters;
                    best = Mathf.Min(best, gap);
                }

                // Joining my destination lane closer to the exit than me: we share the end of the path.
                if (SameLane(other.NextRoadInfo, NextRoadInfo))
                {
                    float otherExitDist = other.intersectionCurveLength - other.intersectionDistance;
                    if (otherExitDist < myExitDist || (otherExitDist == myExitDist && other.ID < ID))
                    {
                        float gap = (myExitDist - otherExitDist) + other.RearBumperDistance - myFront - StopGapMeters;
                        best = Mathf.Min(best, gap);
                    }
                }
            }

            return best;
        }

        protected override float GetStoppingDistanceBeyondRoadEnd()
        {
            if (NextRoadInfo.RoadInstance == null || intersectionCurveEvaluator == null)
                return float.PositiveInfinity;

            float toRoadEnd = RoadInfo.RoadInstance.Road.Length - CurrentPathDistance;
            return GetStoppingDistanceThroughIntersection(CurrentDestIntersection, RoadInfo.RoadInstance.GetLaneOf(this),
                -toRoadEnd, toRoadEnd + intersectionCurveLength);
        }

        protected override float GetMaxApproachSpeed()
        {
            if (NextRoadInfo.RoadInstance == null || !curveHasBend)
                return speedLimit;

            // Arrive at the curve no faster than its bends allow.
            float toRoadEnd = Mathf.Max(0f, RoadInfo.RoadInstance.Road.Length - CurrentPathDistance);
            return Mathf.Min(speedLimit, GetCurveSpeedLimit(-toRoadEnd));
        }

        public override void RemoveFromCurrentRoad()
        {
            base.RemoveFromCurrentRoad();
        }

        public override void SetRoad(RoadPositioningInfo newRoadInfo)
        {
            // Repositioned mid-crossing: drop out of the intersection cleanly.
            if (inIntersection)
                LeaveIntersectionState();

            base.SetRoad(newRoadInfo);
            speedLimit = GetRoadSpeedLimit(newRoadInfo);
            RefreshNextRoad();
            RollLaneChange();

            audio.enabled = true;
        }

        private void UpdateRoom()
        {
            int newRoom = network.Level.FindRoomIdWithWarpsCheckMiss(Position, roomId);
            if (newRoom != roomId)
                roomId = newRoom;
        }

        public override void Update()
        {
            steerCurvature = 0f; // goals that steer us set it again below

            // Before measuring startPosition: a knock-off moves us from the rail pose onto the body's.
            ProcessPendingHit();
            Vector3 startPosition = position;

            goal.Update();

            // Distance along our heading: sliding sideways doesn't roll the wheels, going backwards rolls them back.
            UpdateWheels(Vector3.Dot(position - startPosition, rotation * Vector3.forward));

            UpdateTaillights();
            UpdateTurnSignal();
            UpdateRoom();

            lastSpeed = Speed;
        }

        public override void Activate()
        {
            base.Activate();
            audio.enabled = true;
            hornAudio.enabled = true;
            body.SetCollisionEnabled(true);
            vehicleModel.gameObject.SetActive(true);
            UpdateRoom();
        }

        public override void Deactivate()
        {
            base.Deactivate();
            DetachFromRail();

            SetGoal(DriveGoal);
            AccidentCount = 0;
            IsWrecked = false;
            hitPending = false;
            steerCurvature = 0f;
            body.ResetForPool();
            voiceAudio.Reset();
            
            roomId = 0;
            lastSpeed = 0f;
            audio.enabled = false;
            hornAudio.StopAllSounds();
            hornAudio.enabled = false;
          
            vehicleModel.gameObject.SetActive(false);
        }

        public AITrafficCar(AINetwork network, string typeName) : base(network)
        {
            var dataMgr = network.VehicleDataManager;
            int dataIndex = dataMgr.AddVehicleDataEntry(typeName);
            this.vehicleData = dataMgr.GetEntry(dataIndex);

            RailType = RailType.Vehicle;
            AmbientTypeFlagMask = AmbientTypeFlags.Vehicles;
            NullNextRoad();

            // Create vehicle model
            var vehicleObj = new GameObject($"TrafficCar_{typeName}");
            vehicleObj.transform.parent = network.transform;

            vehicleModel = vehicleObj.AddComponent<AIVehicleModel>();
            vehicleModel.Init(network.Level, typeName);
            vehicleModel.Init(this);
            vehicleModel.Load(typeName);

            body = vehicleObj.AddComponent<AITrafficCarBody>();
            body.Init(this, vehicleData, vehicleData.Mass);

            // Audio objects
            var audioObj = new GameObject("Audio");
            audioObj.transform.SetParent(vehicleObj.transform, false);
            audio = audioObj.AddComponent<AmbientVehicleAudio>();
            audio.Init(typeName, this);

            var hornAudioObj = new GameObject("HornAudio");
            hornAudioObj.transform.SetParent(vehicleObj.transform, false);
            hornAudio = hornAudioObj.AddComponent<AmbientHornAudio>();
            hornAudio.Init(typeName);

            var voiceAudioObj = new GameObject("VoiceAudio");
            voiceAudioObj.transform.SetParent(vehicleObj.transform, false);
            voiceAudio = voiceAudioObj.AddComponent<VoiceAudio>();
            voiceAudio.InitVehicle(network.Level.Name, typeName);
            voiceAudio.SetEntity(this);

            DriveGoal = new RailGoalRandomDrive(this);
            AvoidGoal = new RailGoalAvoidAccident(this);
            AccidentGoal = new RailGoalInAccident(this);
            RegainGoal = new RailGoalRegainRail(this);

            Deactivate(); // also puts us in DriveGoal
        }
    }
}