using UnityEngine;
using MM2.AI;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace MM2
{
    public class AIPoliceOfficer : MonoBehaviour
    {
        // ------------------------------------------------------------------
        // Statics / config
        // ------------------------------------------------------------------
        public static bool EnableRubberBanding = true;

        // Gizmo toggles
        public static bool GizmoDrawId = true;
        public static bool GizmoDrawRanges = true;
        public static bool GizmoDrawFov = true;

        // Raw behaviour ids used by the original flags table
        private const int BehaviorRam = 3;
        private const int BehaviorPush = 4;
        private const int BehaviorBlock = 6;

        private const float DetectionRadius = 75.0f;
        private const float DetectionRadiusSqr = DetectionRadius * DetectionRadius;
        private const float FovHalfAngle = 1.57f;              // radians, ~90 deg each side
        private const float FallThroughY = -200.0f;
        private const int MaxOpponents = 32;
        private const int MaxRouteIntersections = 100;

        // ------------------------------------------------------------------
        // AI Network Ref
        // ------------------------------------------------------------------
        private AINetwork network;

        // ------------------------------------------------------------------
        // Public tunables (were Lua variables)
        // ------------------------------------------------------------------
        public float ChaseRange = 250.0f;
        public bool ChasePlayers = true;
        public bool ChaseOpponents = true;
        public float OpponentChaseChance = 0.5f;
        public float OpponentDetectionRange = 50.0f;

        // ------------------------------------------------------------------
        // State
        // ------------------------------------------------------------------
        private AiVehiclePhysics m_VehiclePhysics;

        private int m_ID;
        private VehCar m_FollowCar;
        private float m_FollowCarDistance;

        private PoliceState m_PoliceState = PoliceState.Idle;
        private int m_LastPoliceState = -1;
        private PoliceApprehendState m_ApprehendState = PoliceApprehendState.Ram;

        private readonly PoliceApprehendState[] m_AllowedBehaviors = new PoliceApprehendState[4];
        private int m_BehaviorCount;

        private readonly bool[] m_OpponentChaseDenyList = new bool[MaxOpponents];

        private readonly int[] m_IntersectionIds = new int[MaxRouteIntersections];
        private int m_NumIntersectionIds;

        private int m_PerpComponentID;
        private CompType m_PerpComponentType;

        // debug only: last point the apprehend behaviours steered at
        private Vector3 m_DebugApprehendTarget;

        // ------------------------------------------------------------------
        // Properties
        // ------------------------------------------------------------------
        public int ID => m_ID;
        public VehCar Car => m_VehiclePhysics.Car;
        public VehCar FollowedCar => m_FollowCar;
        public AiVehiclePhysics VehiclePhysics => m_VehiclePhysics;
        public VehiclePhysicsState State => m_VehiclePhysics.State;
        public PoliceState PoliceState => m_PoliceState;
        public PoliceApprehendState ApprehendState => m_ApprehendState;
        public int CurrentLap => m_VehiclePhysics.CurLap;
        public int NumLaps => m_VehiclePhysics.NumLaps;
        public bool IsDamagedOut => m_VehiclePhysics.DamagedOut;
        public bool InPursuit => m_PoliceState != PoliceState.Idle && m_PoliceState != PoliceState.Incapacitated;

        public void SetState(VehiclePhysicsState state) => m_VehiclePhysics.State = state;
        public void SetPoliceState(PoliceState state) => m_PoliceState = state;

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------
        private static float FlatDistSqr(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return dx * dx + dz * dz;
        }

        private static float FlatDist(Vector3 a, Vector3 b) => Mathf.Sqrt(FlatDistSqr(a, b));

        // ------------------------------------------------------------------
        // Init / Reset
        // ------------------------------------------------------------------
        public bool Init(SDLCity level, int id)
        {
            network = level.AINetwork;

            var raceData = network.AIMap;
            var ourData = raceData.Police[id];

            if(!Init(level, id, ourData.VehicleBasename, ourData.Flags))
            {
                return false;
            }

            var carsim = m_VehiclePhysics.Car.VehCarSim;
            carsim.SetResetPos(ourData.StartPosition);
            carsim.SetResetRotation(-(ourData.StartRotation * Mathf.Deg2Rad) + Mathf.PI);

            /* todo: load this
            ChaseRange = raceData.GetCopChaseRange();
            OpponentDetectionRange = ourData.OppDetectRange;
            OpponentChaseChance = ourData.OppChaseChance;*/
            return true;
        }

        public bool Init(SDLCity level, int id, string basename, int flags)
        {
            // bail out if the car does not exist
            if (VehicleList.GetVehicle(basename) == null)
            {
                return false;
            }

            network = level.AINetwork;
            m_VehiclePhysics = this.gameObject.AddComponent<AiVehiclePhysics>();

            int variant = (level.Name == "sf") ? 0 : 1; // hackhackhack TODO: remove

            m_ID = id;
            m_VehiclePhysics.Init(level, id, basename, variant);
            Car.Stuck.TimeThresh = 0.75f;
            Car.Damage.EnableRepair = false;

            m_BehaviorCount = 0;
            if ((flags & 1) != 0) m_AllowedBehaviors[m_BehaviorCount++] = (PoliceApprehendState)BehaviorBlock;
            // flag 2 -> behaviour 8, invalid (potentially was Barricade)
            if ((flags & 4) != 0) m_AllowedBehaviors[m_BehaviorCount++] = (PoliceApprehendState)BehaviorPush;
            if ((flags & 8) != 0) m_AllowedBehaviors[m_BehaviorCount++] = (PoliceApprehendState)BehaviorRam;

            ChasePlayers = true;
            ChaseOpponents = true;
            ChaseRange = 250.0f;
            OpponentDetectionRange = 50.0f;
            OpponentChaseChance = 0.5f;

            return true;
        }

        public void Reset()
        {
            for (int i = 0; i < m_OpponentChaseDenyList.Length; i++)
                m_OpponentChaseDenyList[i] = false;

            if (m_PoliceState != PoliceState.Idle)
            {
                network.PoliceForce.UnRegisterCop(Car, m_FollowCar);
            }

            m_LastPoliceState = -1;
            m_PoliceState = PoliceState.Idle;
            m_ApprehendState = PoliceApprehendState.Ram;
            m_FollowCar = null; // not in the original, avoids a dangling perp reference

            Car.Audio.DeactivateSiren();
            StopSiren();

            m_VehiclePhysics.Reset();
            m_NumIntersectionIds = 0;
            m_VehiclePhysics.RegisterRoute(m_IntersectionIds, m_NumIntersectionIds,
                Car.VehCarSim.ResetPos, Vector3.zero,
                0, 0.0f, 5.0f, false, true, true, false, true, false,
                1.0f, 2.0f, 0.7f, 75.0f);
        }

        // ------------------------------------------------------------------
        // Update
        // ------------------------------------------------------------------
        void Update()
        {
            if (m_PoliceState != PoliceState.Incapacitated)
            {
                // guard: C++ would crash here if the perp was cleared without resetting state
                if (m_PoliceState != PoliceState.Idle && m_FollowCar == null)
                    m_PoliceState = PoliceState.Idle;

                if (m_PoliceState != PoliceState.Idle)
                {
                    var perpPos = m_FollowCar.transform.position;
                    network.MapComponent(perpPos, out m_PerpComponentID, out m_PerpComponentType, m_FollowCar.CurrentRoom);

                    m_FollowCarDistance = FlatDist(perpPos, Car.transform.position);
                    m_PoliceState = network.PoliceForce.State(Car, m_FollowCar, m_FollowCarDistance);

                    var perpSim = m_FollowCar.VehCarSim;
                    if (perpSim.Transmission.CurrentGear == 0
                        || m_VehiclePhysics.State == VehiclePhysicsState.Backup
                        || m_BehaviorCount == 0
                        || perpSim.Speed < 10.0f)
                    {
                        m_PoliceState = PoliceState.FollowPerp;
                    }

                    if (m_PoliceState == PoliceState.FollowPerp)
                        FollowPerpetrator();
                    else
                        ApprehendPerpetrator();

                    if (m_FollowCarDistance > ChaseRange)
                    {
                        PerpEscapes(false);
                        return;
                    }
                }
                else
                {
                    DetectPerpetrator();
                }

                // did we damage out?
                if (m_VehiclePhysics.DamagedOut)
                {
                    PerpEscapes(true);
                    m_PoliceState = PoliceState.Incapacitated;
                }

                // did we fall in the water?
                if (Car.Splash.enabled)
                {
                    PerpEscapes(false);
                    m_PoliceState = PoliceState.Incapacitated;
                }
            }

            // drive route
            if (m_PoliceState == PoliceState.Apprehend && m_ApprehendState == PoliceApprehendState.BlockWait)
                m_VehiclePhysics.Mirror(m_FollowCar);
            else
                m_VehiclePhysics.DriveRoute();

            // check if we've fallen and can't get up
            if (Car.transform.position.y < FallThroughY)
            {
                Debug.LogWarning($"Police Officer #{m_ID}, Has fallen through the geometry.");
                Reset();
            }
        }

        private void FixedUpdate()
        {
            if (m_PoliceState != PoliceState.Incapacitated && m_PoliceState != PoliceState.Idle)
            {
                if (EnableRubberBanding && m_VehiclePhysics.Throttle == 1.0f
                    && m_VehiclePhysics.Car.Body.velocity.magnitude < 50.0f
                    && m_VehiclePhysics.Car.VehCarSim.OnGround() > 0
                    && m_VehiclePhysics.Car.VehCarSim.Speed > 5.0f)
                {
                    Vector3 velocity = Car.Body.velocity;
                    Vector3 forward = Car.Body.transform.forward;

                    // signed speed along the car's facing direction
                    float forwardSpeed = Vector3.Dot(velocity, forward);

                    // only boost when actually moving forwards
                    if (forwardSpeed > 0.0f)
                    {
                        Car.Body.velocity = velocity + forward * (forwardSpeed * 0.03f);
                    }
                }
            }
        }

        // ------------------------------------------------------------------
        // Pursuit control
        // ------------------------------------------------------------------
        public void CancelPursuit()
        {
            if (m_PoliceState == PoliceState.Idle)
                return;

            network.PoliceForce.UnRegisterCop(Car, m_FollowCar);

            Car.Audio.DeactivateSiren();

            m_PoliceState = PoliceState.Idle;
            m_VehiclePhysics.State = VehiclePhysicsState.Stop;
            m_FollowCar = null;
        }

        public bool ChaseVehicle(VehCar chaseMe)
        {
            if (m_PoliceState == PoliceState.Incapacitated)
                return false;

            var policeForce = network.PoliceForce;
            if (m_PoliceState != PoliceState.Idle)
            {
                // already chasing this car?
                if (policeForce.IsCopChasingPerp(Car, chaseMe))
                    return true;

                // stop chasing the original vehicle first
                CancelPursuit();
            }

            if (!policeForce.RegisterPerp(Car, chaseMe))
                return false;

            m_VehiclePhysics.State = VehiclePhysicsState.Forward;
            m_FollowCar = chaseMe;
            m_FollowCarDistance = Vector3.Distance(Car.transform.position, chaseMe.transform.position);

            network.MapComponent(m_FollowCar.transform.position, out m_PerpComponentID, out m_PerpComponentType, -1);

            m_PoliceState = PoliceState.FollowPerp;
            FollowPerpetrator();
            return true;
        }

        private void ApprehendPerpetrator()
        {
            if ((int)m_PoliceState != m_LastPoliceState)
            {
                m_LastPoliceState = (int)m_PoliceState;
                m_ApprehendState = m_AllowedBehaviors[Random.Range(0, m_BehaviorCount)];
            }

            switch (m_ApprehendState)
            {
                case PoliceApprehendState.Block:
                case PoliceApprehendState.BlockWait:
                    Block();
                    break;
                case PoliceApprehendState.PushLeft:
                case PoliceApprehendState.PushRight:
                    Push();
                    break;
                default:
                    Ram();
                    break;
            }
        }

        // ------------------------------------------------------------------
        // Driving behaviours
        // ------------------------------------------------------------------
        private bool CanReroute()
        {
            var s = m_VehiclePhysics.State;
            return s == VehiclePhysicsState.Forward || s == VehiclePhysicsState.Shortcut;
        }

        private void CalcRouteToPerp(Vector3 target)
        {
            var ours = Car.transform;
            network.Router.CalcRoute(ours.position, ours.forward, target,
                m_IntersectionIds, out m_NumIntersectionIds,
                Car.CurrentRoom, m_FollowCar.CurrentRoom, true);
        }

        /// <summary>
        /// Shared route setup for the apprehend behaviours (Ram / Push / Block).
        /// </summary>
        private void RouteTo(Vector3 target, Vector3 heading, float targetSpeed)
        {
            m_DebugApprehendTarget = target;

            CalcRouteToPerp(target);

            m_VehiclePhysics.RegisterRoute(m_IntersectionIds, m_NumIntersectionIds,
                target, heading,
                0, targetSpeed, 0.0f, false, true, true, false, true, false,
                1.0f, 1.0f, 0.7f, 75.0f);
        }

        public void Ram()
        {
            if (!CanReroute())
                return;

            var perp = m_FollowCar.transform;
            RouteTo(perp.position, perp.forward, m_FollowCar.VehCarSim.Speed + 15.0f);
        }

        public void FollowPerpetrator()
        {
            // entering (or re-entering) follow mode lights us up
            if ((int)m_PoliceState != m_LastPoliceState)
            {
                StartSiren();
                m_LastPoliceState = (int)m_PoliceState;
            }

            if (!CanReroute())
                return;

            var perp = m_FollowCar.transform;
            Vector3 perpPos = perp.position;
            Vector3 perpHeading = perp.forward;

            CalcRouteToPerp(perpPos);

            // match the perp's speed, plus a catch-up term that settles us ~12.5m behind them
            float targetSpeed = m_FollowCar.VehCarSim.Speed + m_FollowCarDistance - 12.5f;

            m_VehiclePhysics.RegisterRoute(m_IntersectionIds, m_NumIntersectionIds,
                perpPos, perpHeading,
                0, targetSpeed, 5.0f, false, true, true, false, true, false,
                1.0f, 2.0f, 0.7f, 75.0f);
        }

        /// <summary>
        /// Drives at a point 3m off one side of the perp. On reaching it, swaps to the other
        /// side, so the cop weaves back and forth across the perp, shoving it as it passes.
        /// </summary>
        public void Push()
        {
            // NOTE: in the original, CalcRoute/RegisterRoute sit outside this check (confirmed after the
            // x87 fix), so in other states it routes to an uninitialised stack target. That's a genuine
            // bug in the original; the port only reroutes in Forward/Shortcut, like Ram/Block/Follow.
            if (!CanReroute())
                return;

            var perp = m_FollowCar.transform;
            Vector3 perpPos = perp.position;
            Vector3 perpSide = perp.right;

            Vector3 target;
            if (m_ApprehendState == PoliceApprehendState.PushLeft)
            {
                target = perpPos - perpSide * 3.0f;
            }
            else if (m_ApprehendState == PoliceApprehendState.PushRight)
            {
                target = perpPos + perpSide * 3.0f;
            }
            else
            {
                Debug.LogError("ERROR: Unknown Apprehend State.");
                return; // original continued with a garbage target
            }

            // reached our side of the perp? switch sides (target is kept for this frame, as in the original)
            Vector3 ourPos = Car.transform.position;
            if (FlatDistSqr(ourPos, target) < 4.0f)
            {
                m_ApprehendState = m_ApprehendState == PoliceApprehendState.PushLeft
                    ? PoliceApprehendState.PushRight
                    : PoliceApprehendState.PushLeft;
            }

            Vector3 perpForward = perp.forward;

            // more than 2m behind the perp -> +25 to catch up, otherwise match its speed
            float aheadDist = Vector3.Dot(perpForward, ourPos - perpPos);
            float targetSpeed = m_FollowCar.VehCarSim.Speed + (aheadDist >= -2.0f ? 0.0f : 25.0f);

            RouteTo(target, perpForward, targetSpeed);
        }

        /// <summary>
        /// Block: come up alongside the perp's rear quarter at +25 speed, then once level with it,
        /// aim 12m ahead at matched speed. Within 3m of that point -> BlockWait (Update mirrors the perp).
        /// BlockWait: if we drop behind the perp, go back to Block.
        /// </summary>
        public void Block()
        {
            if (!CanReroute())
                return;

            var perp = m_FollowCar.transform;
            Vector3 perpPos = perp.position;
            Vector3 perpForward = perp.forward;
            Vector3 perpSide = perp.right;

            Vector3 ourPos = Car.transform.position;
            Vector3 relPos = ourPos - perpPos;
            float aheadDist = Vector3.Dot(perpForward, relPos); // how far in front of the perp we are

            if (m_ApprehendState != PoliceApprehendState.Block)
            {
                // BlockWait
                if (aheadDist < 0.0f)
                    m_ApprehendState = PoliceApprehendState.Block;
                return;
            }

            // Perp extents in its local space, assuming the model faces +Z like its transform:
            // rear bumper is -Min.z (MM2's -Z-forward original used Max.z), left half-width is -Min.x.
            var bound = m_FollowCar.Bound.Collider.sharedMesh.bounds;
            float rearExtent = -bound.min.z;
            float leftExtent = -bound.min.x;

            bool levelWithPerp = aheadDist >= -rearExtent; // past its rear bumper

            Vector3 target;
            if (levelWithPerp)
            {
                target = perpPos + perpForward * 12.0f;
            }
            else
            {
                float lateralDist = Vector3.Dot(relPos, perpSide); // > 0 = we're on its right
                float slotOffset = m_VehiclePhysics.RightSideDistance + leftExtent + 1.0f;

                Vector3 rearBumper = perpPos - perpForward * rearExtent;
                Vector3 rightSlot = rearBumper + perpSide * slotOffset;
                Vector3 leftSlot = rearBumper - perpSide * slotOffset;

                bool useRight = lateralDist > 0.0f;

                // well behind on a road: prefer whichever side is actually on the road
                // NOTE: the decompile reads `m_PerpComponentID == 1` and `Path(m_PerpComponentType)`,
                // which only makes sense with ID/type labels swapped in the IDA struct. Read as road-type check.
                if (aheadDist <= -20.0f && (CompType)m_PerpComponentType == CompType.Road)
                {
                    var path = network.Roads[m_PerpComponentID].Road;
                    var rightOnRoad = path.IsPosOnRoad(rightSlot, m_VehiclePhysics.LeftSideDistance, out _);
                    var leftOnRoad = path.IsPosOnRoad(leftSlot, m_VehiclePhysics.RightSideDistance, out _);

                    // lower = more on the road; ties keep the side we're already on
                    if (rightOnRoad != leftOnRoad)
                        useRight = rightOnRoad < leftOnRoad;
                }

                target = useRight ? rightSlot : leftSlot;
            }

            float targetSpeed = m_FollowCar.VehCarSim.Speed + (levelWithPerp ? 0.0f : 25.0f);
            RouteTo(target, perpForward, targetSpeed);

            if (FlatDistSqr(ourPos, target) < 9.0f)
                m_ApprehendState = PoliceApprehendState.BlockWait;
        }

        public void PerpEscapes(bool playExplosion)
        {
            Car.Audio.DeactivateSiren();
            if(playExplosion)
            {
                Car.Audio.PlayExplosion();
            }
            /*var policeAudio = Car.GetCarAudioContainerPtr().GetPoliceCarAudioPtr();
            if (policeAudio != null)
            {
                if (playExplosion)
                    policeAudio.PlayExplosion();      // explosion + starts the siren damage timer
                else
                    policeAudio.SirenFadingOut = true;

                policeAudio.StopSiren();              // kill the siren immediately
            }*/

            network.PoliceForce.UnRegisterCop(Car, m_FollowCar);

            // m_FollowCar is intentionally left set, matching the original
            m_PoliceState = PoliceState.Idle;
            m_VehiclePhysics.State =VehiclePhysicsState.Stop;
        }

        public void StartSiren()
        {
            var siren = Car.Siren;
            if (siren != null && siren.HasLights)
                siren.Activate();

            Car.Audio.ActivateSiren();
        }

        public void StopSiren()
        {
            var siren = Car.Siren;
            if (siren != null && siren.HasLights)
                siren.Deactivate();

            Car.Audio.DeactivateSiren();
        }

        // ------------------------------------------------------------------
        // Detection
        // ------------------------------------------------------------------
        public bool Fov(VehCar perpCar)
        {
            var ours = Car.transform;
            Vector3 dirToCar = perpCar.transform.position - ours.position;

            float sideDist = Vector3.Dot(ours.right, dirToCar);
            float forwardDist = Vector3.Dot(ours.forward, dirToCar);
            float angle = Mathf.Atan2(sideDist, forwardDist);

            return angle > -FovHalfAngle && angle < FovHalfAngle;
        }

        public bool IsPerpACop(VehCar perpCar)
        {
            return perpCar.Siren != null && perpCar.Siren.HasLights;
        }

        public bool OffRoad(VehCar perpCar)
        {
            var carPosition = perpCar.transform.position;

            int outId = 0; CompType outType = 0;
            network.MapComponent(carPosition, out outId, out outType, perpCar.CurrentRoom);

            var type = (CompType)outType;
            if (type == CompType.Road)
            {
                var path = network.Roads[outId].Road;
                return path.IsPosOnRoad(carPosition, 0.0f, out float _) == RoadPosition.OnSidewalk;
            }

            return type == CompType.None || type == CompType.Shortcut;
        }

        public bool WrongWay(VehCar perpCar)
        {
            var carSim = perpCar.VehCarSim;
            var carTransform = perpCar.transform;
            var carVelocity = carSim.Body.velocity;
            var carPos = carTransform.position;
            var carFwd = carTransform.forward;

            int outId = 0; CompType outType = 0;
            network.MapComponent(carPos, out outId, out outType, perpCar.CurrentRoom);

            if ((CompType)outType != CompType.Road)
                return false;

            var path = network.Roads[outId].Road;

            // two-way alleyways (e.g. around the houses in SF) never count
            if (path.Flags.HasFlag(PathFlags.Alleyway))
                return false;

            int vIndex = path.RoadVertice(carPos, 1);

            float ddot = Vector3.Dot(path.OriZ(vIndex), carFwd);
            float vddot = -Vector3.Dot(carVelocity, carFwd); // > 0 means moving backwards

            int reverseSide = network.AIMap.LeftSidedTraffic ? 0 : 1;
            if (!path.IsOneWay())
            {
                Vector3 localPos = carPos - path.Origin(vIndex);
                int side = Vector3.Dot(localPos, path.OriX(vIndex)) > 0 ? 1 : 0;
                if (side == reverseSide) ddot = -ddot;
            }

            // facing against traffic, or reversing against traffic at >= 10mph
            return ddot > 0 || (vddot > 0.0f && carSim.SpeedInMph >= 10.0f);
        }

        public bool Speeding(VehCar perpCar)
        {
            int cmpId = 0; CompType cmpType = 0;
            network.MapComponent(perpCar.transform.position, out cmpId, out cmpType, perpCar.CurrentRoom);

            var type = (CompType)cmpType;
            if (type != CompType.Road && type != CompType.Shortcut)
                return false;

            var path = network.Roads[cmpId].Road;
            // max AI exceed limit + speed limit + leniency so 0.01mph over isn't speeding
            float speedLimit = 4.0f + path.SpeedLimit + 1.0f;
            if (path.Flags.HasFlag(PathFlags.Freeway))
                speedLimit += (path.HighestLaneCount - 1) * 5.0f;

            return perpCar.VehCarSim.Speed > speedLimit;
        }

        public bool IsPerpBreakingTheLaw(VehCar perpCar)
        {
            if (network.PoliceForce.GetNumChasers(perpCar) > 0)
                return true;

            return !IsPerpACop(perpCar) && (OffRoad(perpCar) || Speeding(perpCar) || WrongWay(perpCar));
        }

        private void DetectPerpetrator()
        {
            m_LastPoliceState = (int)m_PoliceState;

            var ourPos = Car.transform.position;
            var proxies = network.VehicleProxies;

            // look for players
            if (ChasePlayers)
            {
                foreach (var proxy in proxies)
                {
                    if (proxy.IsPlayer)
                    {
                        var playerCar = proxy.Vehicle;
                        float distSqr = (playerCar.transform.position - ourPos).sqrMagnitude;

                        if (distSqr < DetectionRadiusSqr && Fov(playerCar) && IsPerpBreakingTheLaw(playerCar))
                        {
                            if (ChaseVehicle(playerCar))
                                return;
                        }
                    }
                }
            }

            // look for opponents
            if (ChaseOpponents)
            {
                int count = 0;
                while (count < MaxOpponents && count < proxies.Count)
                {
                    var proxy = proxies[count];
                    if (!proxy.IsPlayer)
                    {
                        count++;
                        continue;
                    }
                    if (m_OpponentChaseDenyList[count])
                    {
                        count++;
                        continue;
                    }

                    var opponentCar = proxy.Vehicle;
                    float distSqr = (opponentCar.transform.position - ourPos).sqrMagnitude;

                    if (distSqr < DetectionRadiusSqr && distSqr < (OpponentDetectionRange * OpponentDetectionRange)
                        && Fov(opponentCar) && IsPerpBreakingTheLaw(opponentCar))
                    {
                        if (Random.value <= OpponentChaseChance)
                        {
                            if (ChaseVehicle(opponentCar))
                                return;
                        }
                        else
                        {
                            m_OpponentChaseDenyList[count] = true;
                        }
                    }
                    count++;
                }
            }
        }

        // ------------------------------------------------------------------
        // Debug drawing (Gizmos). Call from OnDrawGizmos / OnDrawGizmosSelected.
        // ------------------------------------------------------------------
        public void DrawGizmos()
        {
            var car = Car;
            if (car == null)
                return;

            Vector3 pos = car.transform.position;
            Vector3 fwd = car.transform.forward;

            if (GizmoDrawRanges)
            {
                if (InPursuit)
                {
                    Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.35f);
                    Gizmos.DrawWireSphere(pos, ChaseRange);
                }
                else if (m_PoliceState == PoliceState.Idle)
                {
                    Gizmos.color = new Color(0.3f, 0.6f, 1f, 0.5f);
                    Gizmos.DrawWireSphere(pos, DetectionRadius);
                    if (ChaseOpponents && OpponentDetectionRange < DetectionRadius)
                    {
                        Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.5f);
                        Gizmos.DrawWireSphere(pos, OpponentDetectionRange);
                    }
                }
            }

#if UNITY_EDITOR
            if (GizmoDrawFov && m_PoliceState == PoliceState.Idle)
            {
                // 180 degree detection cone, flattened to the ground plane
                Vector3 flatFwd = Vector3.ProjectOnPlane(fwd, Vector3.up).normalized;
                Vector3 arcStart = Quaternion.AngleAxis(-FovHalfAngle * Mathf.Rad2Deg, Vector3.up) * flatFwd;
                Handles.color = new Color(0.3f, 0.6f, 1f, 0.08f);
                Handles.DrawSolidArc(pos, Vector3.up, arcStart, FovHalfAngle * 2f * Mathf.Rad2Deg, DetectionRadius);
            }
#endif

            // line to perp
            if (m_FollowCar != null && InPursuit)
            {
                Vector3 perpPos = m_FollowCar.transform.position;
                Gizmos.color = GetStateColor();
                Gizmos.DrawLine(pos + Vector3.up, perpPos + Vector3.up);
                Gizmos.DrawWireCube(perpPos + Vector3.up, new Vector3(2f, 2f, 2f));

                // where Ram / Push / Block is steering
                if (m_PoliceState == PoliceState.Apprehend)
                {
                    Gizmos.color = Color.magenta;
                    Gizmos.DrawWireSphere(m_DebugApprehendTarget + Vector3.up * 0.5f, 0.75f);
                    Gizmos.DrawLine(pos + Vector3.up * 0.5f, m_DebugApprehendTarget + Vector3.up * 0.5f);
                }
            }

            // heading
            Gizmos.color = GetStateColor();
            Gizmos.DrawRay(pos + Vector3.up, fwd * 4f);

            if (GizmoDrawId)
                DrawId();
        }

        public void DrawId()
        {
#if UNITY_EDITOR
            var car = Car;
            if (car == null)
                return;

            string label = $"Cop #{m_ID}\n{m_PoliceState}";
            if (InPursuit)
                label += $" / {m_ApprehendState}\n{m_FollowCarDistance:0.0}m";

            var style = new GUIStyle(EditorStyles.boldLabel) { normal = { textColor = GetStateColor() } };
            Handles.Label(car.transform.position + Vector3.up * 3f, label, style);
#endif
        }

        private Color GetStateColor()
        {
            switch (m_PoliceState)
            {
                case PoliceState.Idle: return Color.cyan;
                case PoliceState.FollowPerp: return Color.yellow;
                case PoliceState.Apprehend: return Color.red;
                case PoliceState.Incapacitated: return Color.gray;
                default: return Color.white;
            }
        }
    }
}