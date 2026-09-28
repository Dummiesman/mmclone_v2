using UnityEngine;
using MM2.AI;
using System.Text;
using System.Linq;


#if UNITY_EDITOR
using UnityEditor;
#endif

namespace MM2
{
    // Move this next to PoliceState / PoliceApprehendState with the rest of the AI enums.
    // Raw values match the original: Update() only handled 0 and 1, anything else did nothing at all.
    public enum RouteRacerState
    {
        Racing = 0,
        Disabled = 1,
    }

    public class AIRouteRacer : MonoBehaviour
    {
        // ------------------------------------------------------------------
        // Statics / config
        // ------------------------------------------------------------------

        // Gizmo toggles
        public static bool GizmoDrawId = true;
        public static bool GizmoDrawRoute = true;
        public static bool GizmoDrawRanges = false;

        // dgPhysManager::sm_OpponentOptimization
        public static bool OpponentOptimization = true;

        private const int GameModeCircuit = 3;              // AIMAP.gameMode == 3 -> lapped race

        private const float FallThroughY = -200.0f;
        private const float PlayerPhysicsRange = 200.0f;    // original compared sqr dist against 40000
        private const float PlayerPhysicsRangeSqr = PlayerPhysicsRange * PlayerPhysicsRange;
        private const float NoPlayerDistSqr = 9999999.0f;   // original's "no player found" seed value

        // ------------------------------------------------------------------
        // AI Network Ref
        // ------------------------------------------------------------------
        private AINetwork network;

        // ------------------------------------------------------------------
        // State
        // ------------------------------------------------------------------
        private AiVehiclePhysics m_VehiclePhysics;

        private int m_ID;

        private RouteRacerState m_RacerState = RouteRacerState.Racing;
        private int m_LastRacerState = -1;

        // The waypoint file gives us N points: [0] is the spawn, [N-1] is the destination,
        // and everything between maps to an intersection id. m_NumIntersectionIds == N - 2.
        private int[] m_IntersectionIds;                    // original: a short[] allocated at 2-byte align
        private int m_NumIntersectionIds;
        private Vector3 m_Destination;

        // debug only
        private float m_DebugClosestPlayerDist;

        // ------------------------------------------------------------------
        // Properties (mirrors AIPoliceOfficer)
        // ------------------------------------------------------------------
        public int ID => m_ID;
        public VehCar Car => m_VehiclePhysics.Car;
        public AiVehiclePhysics VehiclePhysics => m_VehiclePhysics;
        public VehiclePhysicsState State => m_VehiclePhysics.State;
        public RouteRacerState RacerState => m_RacerState;
        public int CurrentLap => m_VehiclePhysics.CurLap;
        public int NumLaps => m_VehiclePhysics.NumLaps;
        public bool IsDamagedOut => m_VehiclePhysics.DamagedOut;
        public Vector3 Destination => m_Destination;
        public int NumIntersections => m_NumIntersectionIds;

        public void SetState(VehiclePhysicsState state) => m_VehiclePhysics.State = state;
        public void SetRacerState(RouteRacerState state) => m_RacerState = state;

        private bool IsCircuitRace => network.GameMode == MMGameMode.Circuit;

        // ------------------------------------------------------------------
        // Init / Reset
        // ------------------------------------------------------------------
        public bool Init(SDLCity level, int id, string directory)
        {
            network = level.AINetwork;

            var ourData = network.AIMap.Opponents[id];
            return Init(level, id, directory, ourData.VehicleBasename, ourData.OpponentFile);
        }

        public bool Init(SDLCity level, int id, string directory, string basename, string waypointFileName)
        {
            network = level.AINetwork;

            m_VehiclePhysics = this.gameObject.AddComponent<AiVehiclePhysics>();
            m_ID = id;


            m_VehiclePhysics.Init(level, id, basename, id % 4, IsCircuitRace);
            return LoadWaypoints(level, directory, waypointFileName);
        }

        public void Reset()
        {
            m_LastRacerState = -1;
            m_RacerState = RouteRacerState.Racing;

            m_VehiclePhysics.Reset();
        }

        // ------------------------------------------------------------------
        // Waypoint loading
        // ------------------------------------------------------------------
        /// <summary>
        /// Reads a CSV waypoint file via MMPositions: x, y, z, heading (degrees) per line.
        /// Point 0 is the spawn point, the last point is the route destination, and every point in
        /// between is resolved to the intersection that owns the cull room it sits in.
        /// </summary>
        private bool LoadWaypoints(SDLCity level, string directory, string filename)
        {
            var waypoints = new MMPositions();
            waypoints.Load(AssetManager.CombinePath("race", directory, filename));

            var points = waypoints.Positions;
            int numPoints = points.Count;

            // original bailed on a missing/unopenable file, leaving the racer with no route at all
            if (numPoints == 0)
            {
                Debug.LogWarning($"Opponent #{m_ID}, Waypoint file '{directory}/{filename}' is missing or empty.");
                return false;
            }

            if (numPoints < 3)
            {
                Debug.LogError($"ERROR: Opp: {m_ID}, Waypoint file only has {numPoints} point(s), needs at least 3.");
                return false;
            }

            m_IntersectionIds = new int[numPoints];

            for (int i = 0; i < numPoints; i++)
            {
                var waypoint = points[i];
                Vector3 pos = waypoint;                     // w is the heading, in degrees
   
                int roomId = level.FindRoomIdWithWarps(pos);
                if (roomId <= 0)
                {
                    // NOTE: the original prints and moves on, leaving this slot of the id array
                    // holding uninitialised heap memory. Nothing downstream re-checks it.
                    Debug.LogError($"ERROR: Point - {i}, Is not in a cull room.");
                    continue;
                }

                if (i == 0)
                {
                    // spawn
                    var carsim = Car.VehCarSim;
                    Car.transform.position = pos;
                    carsim.SetResetPos(pos);
                    carsim.SetResetRotation(-(waypoint.w * Mathf.Deg2Rad) + Mathf.PI);
                }
                else if (i == numPoints - 1)
                {
                    m_Destination = pos;
                }
                else if (FindIntersectionInRoom(roomId, out int intersectionId))
                {
                    m_IntersectionIds[i - 1] = intersectionId;
                }
                else
                {
                    Debug.LogError($"ERROR: Opp: {m_ID}, Point - {i}{pos} Room - {roomId}, Is not a intersection.");
                }
            }

            // drop the spawn point and the destination
            m_NumIntersectionIds = numPoints - 2;
            return true;
        }

        /// <summary>
        /// Returns the id of the FIRST intersection component in the room's component list.
        /// The original made no attempt to pick the nearest one, so a room holding more than one
        /// intersection silently resolves to whichever is listed first.
        /// </summary>
        private bool FindIntersectionInRoom(int roomId, out int intersectionId)
        {
            intersectionId = -1;
            var components = network.GetRoomComponents(roomId);
            foreach(var component in components)
            {
                if(component.Type == CompType.Intersection)
                {
                    intersectionId = component.Id;
                    return true;
                }
            }
            return false;
        }

        // ------------------------------------------------------------------
        // Finished
        // ------------------------------------------------------------------
        public bool Finished()
        {
            const float FinishRadiusSq = 400f; // 20^2
            if (VehiclePhysics.CurLap != VehiclePhysics.NumLaps ||
                VehiclePhysics.WayPtIndex < VehiclePhysics.NumWayPts - 1)
                return false;

            Vector3 pos = Car.transform.position;
            float dx = pos.x - m_Destination.x;
            float dz = pos.z - m_Destination.z;

            return dx * dx + dz * dz < FinishRadiusSq;
        }

        // ------------------------------------------------------------------
        // Update
        // ------------------------------------------------------------------
        void Update()
        {
            switch (m_RacerState)
            {
                case RouteRacerState.Racing:
                    DriveRoute();
                    break;
                case RouteRacerState.Disabled:
                    Disabled();
                    break;
                    // any other value is inert, as in the original
            }

            UpdatePhysicsLod();
        }

        private void DriveRoute()
        {
            if ((int)m_RacerState != m_LastRacerState)
            {
                var d = network.AIMap.Opponents[m_ID];

                // The two branches of the original (gameMode == 3 vs not) are identical apart from
                // the lap count; everything else the decompile shows is just register churn.
                int numLaps = IsCircuitRace ? network.NumLaps : 1;

                m_VehiclePhysics.RegisterRoute(m_IntersectionIds, m_NumIntersectionIds,
                    m_Destination, Vector3.zero,            // destination heading is memset to 0
                    numLaps, 0.0f, 0.0f,
                    d.UnknownFlag, d.AvoidTraffic, d.AvoidProps, d.AvoidPlayers, d.AvoidOpponents, d.BadPathfinding,
                    d.ThrottleAmount, d.TurnSpeedMultiplier, d.BrakingThreshold, d.LookAheadDistance);

                // let the opponent pull away from the lights it starts at
                // network.AIMap.Intersection(m_IntersectionIds[0]).StopSources(true);

                m_LastRacerState = (int)m_RacerState;
            }

            // original passed an extra arg pair here (aiVehiclePhysics::DriveRoute(phys, a2, 1));
            // a2 is clobbered above by the lastState store, so it carries nothing meaningful.
            m_VehiclePhysics.DriveRoute();

            // check if we've fallen and can't get up
            if (Car.transform.position.y < FallThroughY)
            {
                Debug.LogWarning($"Opponent #{m_ID}, Has fallen through the geometry.");
                // NOTE: the cops Reset() here, opponents just switch themselves off for good.
                m_RacerState = RouteRacerState.Disabled;
            }
        }

        private void Disabled()
        {
            if ((int)m_RacerState == m_LastRacerState)
                return;

            m_VehiclePhysics.State = VehiclePhysicsState.Stop;  // raw state 3 - verify against the enum
            m_LastRacerState = (int)m_RacerState;
        }

        /// <summary>
        /// Opponents near a player get full collision physics; everything else is demoted so the
        /// sim can cheap out on cars nobody can see.
        /// </summary>
        private void UpdatePhysicsLod()
        {
            float closestSqr = NoPlayerDistSqr;

            var ourPos = Car.transform.position;
            foreach (var proxy in network.VehicleProxies)
            {
                if (!proxy.IsPlayer)
                    continue;

                // full 3D distance here, not the flat one the cops use
                float distSqr = (proxy.Vehicle.transform.position - ourPos).sqrMagnitude;
                if (distSqr < closestSqr)
                    closestSqr = distSqr;
            }

            m_DebugClosestPlayerDist = Mathf.Sqrt(closestSqr);

            bool nearPlayer = closestSqr < PlayerPhysicsRangeSqr;

            // CollideOtherInstances | Flag4 | CollideTerrain | Flag1 == 0x1B.
            // Far away with opponent optimisation on, Flag4 is dropped (0x13).
            int moverType = nearPlayer ? 3 : 2;
            int moverFlags = (!nearPlayer && OpponentOptimization) ? 0x13 : 0x1B;

            // TODO: dgPhysManager
            /*dgPhysManager.Instance.DeclareMover(Car.Model, moverType, moverFlags);*/
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
                Gizmos.color = new Color(0.4f, 1f, 0.5f, 0.35f);
                Gizmos.DrawWireSphere(pos, PlayerPhysicsRange);
            }

            if (GizmoDrawRoute && m_NumIntersectionIds > 0)
            {
                Gizmos.color = GetStateColor();
                Gizmos.DrawLine(pos + Vector3.up, m_Destination + Vector3.up);
                Gizmos.DrawWireCube(m_Destination + Vector3.up, new Vector3(2f, 2f, 2f));
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

            string label = $"Opp #{m_ID}\n{m_RacerState} / {m_VehiclePhysics.State}";
            label += $"\nLap {CurrentLap}/{NumLaps}  {m_DebugClosestPlayerDist:0.0}m";

            var style = new GUIStyle(EditorStyles.boldLabel) { normal = { textColor = GetStateColor() } };
            Handles.Label(car.transform.position + Vector3.up * 3f, label, style);
#endif
        }

        private Color GetStateColor()
        {
            switch (m_RacerState)
            {
                case RouteRacerState.Racing: return IsDamagedOut ? Color.gray : Color.green;
                case RouteRacerState.Disabled: return Color.gray;
                default: return Color.white;
            }
        }
    }
}