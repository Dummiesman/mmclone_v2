using UnityEngine;

namespace MM2.AI
{
    public class AICableCar : AIRailEntity
    {
        private const float CableCarSpeed = 11f;
        private const float CrossingSpeed = 11f;
        private const float ApproachDecel = 2f;
        private const float PathStep = 1f;

        // Player braking
        private const float PlayerStopTime = 0.75f;                               // seconds to stop from full speed
        private const float PlayerBrakeDecel = CableCarSpeed / PlayerStopTime; // 11 m/s^2
        private const float PlayerLookAhead = 10f;    // ignore players further ahead than this (past front bumper)
        private const float PlayerStopGap = 4f;       // come to rest this far short of the player
        private const float PlayerSideMargin = 1f;    // widen the lane check a bit to account for the player's own width
        private const float PlayerResumeGap = 1.5f;   // once held, player must be this much further than PlayerStopGap to release
        private const float PlayerResumeDelay = 0.75f; // ...and stay clear for this long

        // Audio hysteresis
        private const float AudioStartSpeed = 0.25f; // must exceed this to count as "moving"
        private const float AudioStopSpeed = 0.05f;  // must drop below this to count as "stopped"

        private CableCarAudio audio;
        private AIVehicleData vehicleData;
        private CableCarInstance vehicleModel;

        public override int RoomID => roomId;
        private int roomId;

        public override float FrontBumperDistance => vehicleData.CG.z + (vehicleData.Size.z * 0.5f);
        public override float RearBumperDistance => vehicleData.CG.z - (vehicleData.Size.z * 0.5f);
        public override float LeftSideDistance => vehicleData.CG.x - (vehicleData.Size.x * 0.5f);
        public override float RightSideDistance => vehicleData.CG.x + (vehicleData.Size.x * 0.5f);

        public float HalfWidth => 0.5f * (RightSideDistance - LeftSideDistance);

        // intersection state
        private bool inIntersection = false;
        private IntersectionInstance currentIntersection;
        private float intersectionDistance = 0f;

        public bool InIntersection => inIntersection;

        // player hold state (computed once per frame in Update)
        private float playerSpeedLimit = float.PositiveInfinity;
        private bool heldForPlayer;
        private float playerClearTimer;

        private bool audioMoving;


        /// Does the rest of my path through the intersection (rear bumper onward) come within
        /// sqrt(clearanceSq) of 'point'? XZ only.
        public bool PathComesWithin(Vector3 point, float clearanceSq)
        {
            if (!inIntersection || intersectionCurveEvaluator == null || intersectionCurveLength <= 0f)
                return false;

            for (float d = Mathf.Max(0f, intersectionDistance + RearBumperDistance); ; d += PathStep)
            {
                float t = Mathf.Min(1f, d / intersectionCurveLength);
                Vector3 offset = intersectionCurveEvaluator.Evaluate(t) - point;
                offset.y = 0f;
                if (offset.sqrMagnitude <= clearanceSq)
                    return true;
                if (t >= 1f)
                    return false;
            }
        }

        /// Gap from our front bumper to the nearest player directly in front of us.
        /// PositiveInfinity if nobody is in the way. XZ only.
        private float GetNearestPlayerGap()
        {
            var players = network.VehicleProxies;
            if (players == null || players.Count == 0)
                return float.PositiveInfinity;

            Vector3 fwd = rotation * Vector3.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-6f)
                return float.PositiveInfinity;
            fwd.Normalize();
            Vector3 right = new Vector3(fwd.z, 0f, -fwd.x); // Cross(up, fwd)

            // Measured from our origin, so cover the nose as well as the lookahead.
            float bailDist = FrontBumperDistance + PlayerLookAhead + PlayerSideMargin;
            float bailDistSq = bailDist * bailDist;

            float nearestGap = float.PositiveInfinity;
            foreach (var player in players)
            {
                Vector3 offset = player.Position - position;
                offset.y = 0f;

                // Quick bail: are they far away?
                if (offset.sqrMagnitude > bailDistSq)
                    continue;

                // Inside our side extents?
                float side = Vector3.Dot(offset, right);
                if (side < LeftSideDistance - PlayerSideMargin || side > RightSideDistance + PlayerSideMargin)
                    continue;

                // Ahead of the front bumper (small tolerance so someone right on the nose still counts)?
                float gap = Vector3.Dot(offset, fwd) - FrontBumperDistance;
                if (gap < -PlayerSideMargin || gap > PlayerLookAhead)
                    continue;

                if (gap < nearestGap)
                    nearestGap = gap;
            }

            return nearestGap;
        }

        /// Call once per frame. Returns the max speed allowed by players ahead,
        /// with a hold so a stopped car doesn't twitch when the player shuffles.
        private float UpdatePlayerSpeedLimit()
        {
            float gap = GetNearestPlayerGap();
            float target;

            if (heldForPlayer)
            {
                // Stay stopped until the player has been clearly out of the way for a moment.
                if (gap > PlayerStopGap + PlayerResumeGap)
                {
                    playerClearTimer += Time.deltaTime;
                    if (playerClearTimer >= PlayerResumeDelay)
                        heldForPlayer = false;
                }
                else
                {
                    playerClearTimer = 0f;
                }
            }
            else if (gap <= PlayerStopGap)
            {
                heldForPlayer = true;
                playerClearTimer = 0f;
            }

            if (heldForPlayer)
                target = 0f;
            else if (float.IsPositiveInfinity(gap))
                return float.PositiveInfinity;
            else
                target = Mathf.Sqrt(2f * PlayerBrakeDecel * Mathf.Max(0f, gap - PlayerStopGap)); // v^2 = 2ad

            // Never ask to shed speed faster than the brakes can; if there isn't room, we hit them.
            float minCap = Mathf.Max(0f, speed - PlayerBrakeDecel * Time.deltaTime);
            return Mathf.Max(minCap, target);
        }

        private void UpdateAudio()
        {
            if (!audioMoving && speed > AudioStartSpeed)
            {
                audioMoving = true;
                audio.Play();
                audio.PlayStartBell();
            }
            else if (audioMoving && speed < AudioStopSpeed)
            {
                audioMoving = false;
                audio.Stop();
            }
        }

        private void EnterIntersection(float overshoot)
        {
            if (Speed > 5.0f)
            {
                audio.PlayIntersectionEntryBell();
            }

            // Read this before RoadInfo is cleared, it's derived from it.
            currentIntersection = CurrentDestIntersection;

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

        private void UpdateIntersection()
        {
            // No AI avoidance: hold crossing speed, or less on tight bends / for players.
            float cap = Mathf.Min(CrossingSpeed, GetCurveSpeedLimit(intersectionDistance));
            cap = Mathf.Min(cap, playerSpeedLimit);
            ApplySpeed(GetTargetSpeed(float.PositiveInfinity), cap);

            intersectionDistance += speed * Time.deltaTime;

            if (intersectionCurveEvaluator == null || intersectionDistance >= intersectionCurveLength)
            {
                ExitIntersection(Mathf.Max(0f, intersectionDistance - intersectionCurveLength));
                return;
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
        }

        protected override float GetMaxApproachSpeed()
        {
            float limit = Mathf.Min(speedLimit, playerSpeedLimit);

            if (NextRoadInfo.RoadInstance == null)
                return limit;

            // Brake so we arrive at the intersection at crossing speed: v^2 = v0^2 + 2ad.
            float toRoadEnd = Mathf.Max(0f, RoadInfo.RoadInstance.Road.Length - CurrentPathDistance);
            float approach = Mathf.Sqrt(CrossingSpeed * CrossingSpeed + 2f * ApproachDecel * toRoadEnd);

            if (curveHasBend)
                approach = Mathf.Min(approach, GetCurveSpeedLimit(-toRoadEnd));

            return Mathf.Min(limit, approach);
        }

        protected override float GetStoppingDistanceBeyondRoadEnd()
        {
            // Cable cars don't yield in intersections.
            return float.PositiveInfinity;
        }

        public override void SetRoad(RoadPositioningInfo newRoadInfo)
        {
            // Repositioned mid-crossing: drop out of the intersection cleanly.
            if (inIntersection)
                LeaveIntersectionState();

            base.SetRoad(newRoadInfo);
            speedLimit = CableCarSpeed;
            RefreshNextRoad();

            audio.enabled = true;
        }

        private void UpdateRoom()
        {
            int newRoom = network.Level.FindRoomIdWithWarpsCheckMiss(Position, roomId);
            if (newRoom != roomId)
            {
                roomId = newRoom;
            }
        }

        public override void Reset()
        {
            base.Reset();
            vehicleModel.Reset();
        }

        public override void Update()
        {
            if (vehicleModel.Broken)
            {
                return; // don't do anything, someone crashed into us
            }
            else if (inIntersection)
            {
                playerSpeedLimit = UpdatePlayerSpeedLimit();
                UpdateIntersection();
            }
            else
            {
                // Not placed yet.
                if (RoadInfo.RoadInstance == null)
                    return;

                // Dead end last time we looked, try again.
                if (NextRoadInfo.RoadInstance == null)
                    RefreshNextRoad();

                playerSpeedLimit = UpdatePlayerSpeedLimit();
                base.Update();

                float roadLength = RoadInfo.RoadInstance.Road.Length;
                if (NextRoadInfo.RoadInstance != null && CurrentPathDistance >= roadLength)
                    EnterIntersection(CurrentPathDistance - roadLength);
            }

            UpdateAudio();
            UpdateRoom();
        }

        public override void Activate()
        {
            base.Activate();
            audio.enabled = true;
            UpdateRoom();
        }

        public override void Deactivate()
        {
            base.Deactivate();
            if (inIntersection) LeaveIntersectionState();
            if (RoadInfo.RoadInstance != null) RemoveFromCurrentRoad();
            RoadInfo = RoadPositioningInfo.Null;
            NullNextRoad();

            playerSpeedLimit = float.PositiveInfinity;
            heldForPlayer = false;
            playerClearTimer = 0f;
            audioMoving = false;

            roomId = 0;
            audio.enabled = false;
            vehicleModel.Level.MoveToRoom(vehicleModel, 0);
        }

        public AICableCar(AINetwork network) : base(network)
        {
            const string typeName = "va_cablecar_f";

            var dataMgr = network.VehicleDataManager;
            int dataIndex = dataMgr.AddVehicleDataEntry(typeName);
            this.vehicleData = dataMgr.GetEntry(dataIndex);

            RailType = RailType.Tram;
            AmbientTypeFlagMask = AmbientTypeFlags.Vehicles;
            speedLimit = CableCarSpeed;
            NullNextRoad();

            var vehicleBanger = CableCarInstance.Create(network.Level, this, typeName);
            vehicleBanger.Unbreakable = true; // for now they're breaking instantly, todo

            var vehicleObj = vehicleBanger.gameObject;
            audio = vehicleObj.AddComponent<CableCarAudio>();
            audio.Init();

            vehicleModel = vehicleBanger;
            Deactivate();
        }
    }
}