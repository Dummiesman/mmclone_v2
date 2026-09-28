using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MM2.AI
{
    /// <summary>aiStuck - a member of aiVehiclePhysics in MM2. Replace with the port when you have it.</summary>
    public interface IAiStuck
    {
        int State { get; set; }   // 2 = wiggle out
        void Update();
        void Reset();
    }

    public class NullAiStuck : IAiStuck
    {
        public int State { get; set; }
        public void Update() { }
        public void Reset() { }
    }

    /// <summary>aiRouteNode, 0x24 bytes.</summary>
    [Serializable]
    public struct AiRouteNode
    {
        public Obstacle Blocker;       // 0x00  obstacle this node was placed to avoid, if any
        public Vector3 Position;       // 0x04
        public float Angle;            // 0x10  running heading-change cost (DetermineBestRoute minimises it)
        public float Distance;         // 0x14  running path length; 9999 = end-of-route sentinel
        public short PathIndex;        // 0x18  index into SomePaths[]
        public short RoadVertexIndex;  // 0x1A  travel-order vertex
        public short Kind;             // 0x1C  0 road, 1 turn arc, 2 obstacle avoid, 3 blocked (sets RouteBlocked)
        public short StartPathIndex;   // 0x1E  turn code (InSharpTurn - 1), also used as a SomePaths index
        public short BlockType;        // 0x20  from IsTargetBlocked: 0 player, 1 traffic, 2 opponent, 5 prop
        public short RoadPos;          // 0x22  RoadPosition; EnumRoutes stamps 1 (OnRoad), 2 (OnSidewalk) sets RouteUsesSidewalk

        public void Reset() => this = default;
    }

    /// <summary>One output of CalcObstacleAvoidPoints; SaveTarget copies it onto a route node.</summary>
    public struct AvoidPoint
    {
        public Vector3 Pos;      // a5[k]
        public Obstacle Target;  // a6[k] - the obstacle being avoided
        public short RoadPos;    // v33[2k] -> node.RoadPos
        public short Kind;       // v32[2k] -> node.Kind
    }

    public partial class AiVehiclePhysics : MonoBehaviour
    {
        // ---------------- constants ----------------
        const float GripScale = 1.2f;     // grip scale: corner speed = sqrt(radius * 1.2 * 19.8)
        const float TurnHeadingThreshold = 0.7f;     // a heading change past this counts as a turn (rad)
        const float MaxAvoidCandidateAngle = 1.5700001f; // ~pi/2: max |angle| for an avoid candidate (rad)
        const float SharpTurnAngle = 0.2f;     // road-end turn angle below which InSharpTurn looks at the next path
        const float Gravity2 = 19.799999f;
        static float K => GripScale * Gravity2;

        /// <summary>+1 if you kept MM2 handedness, -1 if you flipped Z on import. Applied to 2D cross products only.</summary>
        public const float Handedness = -1f;

        const int InvalidId = 0xFFFF;
        const int InfiniteLaps = 0xFFFF;
        const int MaxRoutes = 25;
        const int MaxActiveRoutes = 10;   // ContinueCheck stops enumerating past this
        const int NodesPerRoute = 40;
        const int AvoidPointsPerNode = 8; // TODO: real buffer size from CalcObstacleAvoidPoints

        // ---------------- tuning ----------------
        public int Id;
        public float MaxThrottle = 1f;
        public float CornerSpeedMultiplier = 1f;
        public float CornerBrakingThreshold = 0.1f;
        public float FinishRadius;
        public float TargetSpeed;

        // ---------------- wiring (assign in Awake from your components) ----------------
        public VehCar Car;
        public AINetwork Net;
        public IAiStuck AiStuck = new NullAiStuck();

        // ---------------- state ----------------
        public VehiclePhysicsState State = VehiclePhysicsState.Forward;
        public VehiclePhysicsState LastState = (VehiclePhysicsState)(-1);

        // route
        public int[] RouteIntersectionIds = Array.Empty<int>();
        public int NumWayPts;
        public int WayPtIndex;
        public int CurLap;
        public int NumLaps;
        public Vector3 RouteEndPos;
        public Vector3 RouteEndOri;
        public int EndRoadId;              // "SomeIntersectionID" - it's passed to aiMap::Path, so it's a road id
        public int EndMode;                // unknown38496: component type the route ends on (1/2 road or shortcut, 3 intersection, 0 none)
        public bool PastLastWayPt;         // unknown38500

        // map location
        public int CurMapCompIdx;
        public CompType CurMapCompType;
        public int RoomId;

        // paths ahead: [0] current, [1] next, [2] after, [3] cache of last [0]
        public readonly Road[] Paths = new Road[4];
        public readonly int[] PathsSides = new int[4];   // 1 = travelling toward LInter
        public int PathEndIndex;                            // SomePathsUnkIndex

        // road-end turns between Paths[i] and [i+1]
        public readonly float[] TurnAngle = new float[2];      
        public readonly float[] TurnDir = new float[2];        
        public readonly Vector3[] TurnPos = new Vector3[2];    
        public readonly Vector3[] TurnCenter = new Vector3[2];
        public readonly Vector3[] TurnStartDir = new Vector3[2];
        public readonly Vector3[] TurnEndDir = new Vector3[2]; 
        public readonly float[] TurnSetback = new float[2];    
        public readonly float[] TurnRadius = new float[2];     

        public Vector3 RegisterRouteOriginalPos1;
        public Vector3 RegisterRouteOriginalPos2;

        // Route nodes. CandidateRoutes sits directly after ScratchRoute in memory (0x2D4 + 40*0x24 = 0x874),
        // so row -1 of CandidateRoutes is ScratchRoute - see RouteNodeAt.
        public readonly AiRouteNode[] ScratchRoute = new AiRouteNode[NodesPerRoute];                 // the route EnumRoutes is building
        public readonly AiRouteNode[,] CandidateRoutes = new AiRouteNode[MaxRoutes, NodesPerRoute];  // committed routes to choose from
        // three contiguous int[25] arrays at 38168 / 38268 / 38368
        public readonly int[] RouteBlocked = new int[MaxRoutes];       // 38168: a node has Kind 3 (drives through an obstacle); skipped in pass 1
        public readonly int[] RouteUsesSidewalk = new int[MaxRoutes];  // 38268: a node has RoadPos 2 (on the sidewalk); preferred when Reckless
        public readonly int[] RouteNodeCount = new int[MaxRoutes];     // 38368: node count of each candidate route
        public int CurrentRouteNode;       // index of the chosen candidate route, or -1
        public int ActiveRouteNodes;       // number of candidate routes committed this tick
        public int TCPO;
        public int BackupTicks;
        public float LookAheadDistance;
        public float RoadLateralOffset;

        public bool UnkFlag, AvoidTraffic, AvoidProps, AvoidPlayers, AvoidOpponents, Reckless;

        // vehicle dimensions: positive distances from the car's origin, set by Init from the bound mesh
        public float LeftSideDistance, RightSideDistance, FrontBumperDistance, RearBumperDistance;
        public int LegacyVehicleID;

        // One avoid-point buffer per node depth. In MM2 this was a stack local in EnumRoutes; a single shared
        // buffer gets overwritten by the recursion before the outer loop has used all its points.
        readonly AvoidPoint[][] avoidPoints = CreateAvoidBuffers();

        static AvoidPoint[][] CreateAvoidBuffers()
        {
            var a = new AvoidPoint[NodesPerRoute][];
            for (int i = 0; i < a.Length; i++) a[i] = new AvoidPoint[AvoidPointsPerNode];
            return a;
        }

        // outputs
        public Vector3 TargetPt;
        public float Steering, Throttle, Brake;

        public bool DamagedOut => damagedOut;
        bool damagedOut;
        float damagedOutStart;

        public enum AiDriveMode { Off, Route, Mirror, Follow }

        [Header("Drive mode")]
        public AiDriveMode Mode = AiDriveMode.Off;
        public VehCar FollowTarget;
        public float FollowGap = 15f;

        /// <summary>MM2 uses 1.33 * 1.428; drop toward 1.0 if the car weaves.</summary>
        public float FollowSteerGain = 1.33f;

        void FixedUpdate()
        {
            if (Car == null) return;

            switch (Mode)
            {
                case AiDriveMode.Route:
                    DriveRoute();
                    break;
                case AiDriveMode.Mirror:
                    if (FollowTarget != null) Mirror(FollowTarget);
                    break;
                case AiDriveMode.Follow:
                    if (FollowTarget != null) Follow(FollowTarget, FollowGap);
                    break;
            }
        }

        // ---- pose ----
        // Body.interpolation is on, so transform.* is the rendered pose and lags the physics
        // state inside FixedUpdate. Steering off that lag makes the car hunt, so read the body.

        public Vector3 CarPos => Car.Body.position;
        public Vector3 CarRight => Car.Body.rotation * Vector3.right;
        public Vector3 CarForward => Car.Body.rotation * Vector3.forward;
        public Vector3 CarUp => Car.Body.rotation * Vector3.up;

        // ---- vehCar / vehCarSim bits the AI needs that aren't plain fields ----

        /// <summary>MM2 gear 0 is reverse; the transmission here exposes the two directions.</summary>
        bool InReverse => Car.VehCarSim.Transmission.CurrentGear == 0;   // TODO: confirm the reverse gear index

        void SetForwardGear() => Car.VehCarSim.Transmission.SetForward();
        void SetReverseGear() => Car.VehCarSim.Transmission.SetReverse(); // TODO: add if VehTransmission lacks it

        /// <summary>Matrix34::Rotate about the car's up axis; Backup snaps out the last of the heading error.</summary>
        void RotateYaw(float radians)
        {
            Quaternion yaw = Quaternion.AngleAxis(radians * Mathf.Rad2Deg, CarUp);
            Car.Body.MoveRotation(yaw * Car.Body.rotation);
        }

        /// <summary>carModel Flags &amp; 0x8000 in MM2: friction handling 2.0 instead of 1.0.</summary>
        bool HighFrictionHandling => false;   // TODO: source from VehicleModel

        // =====================================================================
        //  Init
        // =====================================================================

        /// <summary>
        /// Binds the car and takes its dimensions from the bound mesh. All four are positive distances
        /// from the car's origin: the solver adds Left + Right for the car's width and Front + Rear for
        /// its length. Assumes the bound collider sits on the car root with no offset or scale.
        /// Call RegisterRoute before setting Mode = Route.
        /// </summary>
        public void Init(VehCar car, AINetwork net = null)
        {
            Car = car;
            if (net != null) Net = net;
            if (Net == null) Debug.LogWarning("AiVehiclePhysics.Init: no AINetwork; RegisterRoute and Route mode will fail.");

            LastState = (VehiclePhysicsState)(-1);

            var collider = car != null && car.Bound != null ? car.Bound.Collider : null;
            var mesh = collider != null ? collider.sharedMesh : null;
            if (mesh == null)
            {
                Debug.LogWarning("AiVehiclePhysics.Init: no bound mesh; leaving the car dimensions as they are.");
                return;
            }

            Bounds b = mesh.bounds;
            FrontBumperDistance = b.max.z;
            RearBumperDistance = -b.min.z;
            LeftSideDistance = -b.min.x;
            RightSideDistance = b.max.x;
        }

        public void Init(SDLCity level, int id, string basename, int variant)
        {
            Id = id;
            var car = this.gameObject.AddComponent<VehCar>();
            car.Init(level, basename, variant, vehCarType.Opponent);
            Init(car, level.AINetwork);
        }

        public void Init(SDLCity level, int id, string basename, int variant, bool canRepair)
        {
            Init(level, id, basename, variant);
            Car.Damage.EnableRepair = canRepair;
        }

        // =====================================================================
        //  Network access (aiMap equivalents)
        // =====================================================================

        readonly List<AIComponent> mapScratch = new List<AIComponent>();

        Intersection Inter(int id) =>
            (id < 0 || id == InvalidId || id >= Net.Data.Intersections.Count) ? null : Net.Data.Intersections[id];

        /// <summary>aiMap::Path. Roads and shortcuts share one id range: shortcut ids start at Roads.Count.</summary>
        Road Path(int id) => Net.Data.GetRoad(id);

        /// <summary>
        /// Runtime instance (obstacles, traffic lanes) for a path, road or shortcut.
        /// Null if the network has no instance for it.
        /// </summary>
        RoadInstance InstanceOf(Road p) => p == null ? null : Net.GetRoadInstance(p.Id);

        /// <summary>aiMap::DetRdSegBetweenInts. side 1 = travelling toward the road's left end.</summary>
        Road DetRdSegBetweenInts(Intersection a, Intersection b, out int side)
        {
            side = 0;
            if (a == null || b == null) return null;

            for (int k = 0; k < PathCount(a); k++)
            {
                var road = RoadRef(a, k);
                int l = road.LeftEndData.IntersectionID;
                int r = road.RightEndData.IntersectionID;
                if (l == b.Id && r == a.Id) { side = 1; return road; }
                if (r == b.Id && l == a.Id) { side = 0; return road; }
            }
            return null;
        }

        /// <summary>
        /// aiPath rUnk2[2k] / [2k+1] with k = rNumSidewalks + rNumLanes - 1, doubled by the caller.
        /// TODO: confirm against rUnk2 in the exe.
        /// </summary>
        static float RoadEdgeValue(Road r)
        {
            var d = r.RightData;
            int k = (d.hasSidewalk ? 1 : 0) + d.numLanes - 1;
            int idx = k <= 0 ? 2 * k + 1 : 2 * k;
            return d.UnknownFloats[Mathf.Clamp(idx, 0, d.UnknownFloats.Length - 1)];
        }

        // =====================================================================
        //  Helpers
        // =====================================================================

        int WayPtId(int i) => (i < 0 || i >= NumWayPts) ? InvalidId : (ushort)RouteIntersectionIds[i];
        Intersection WayPtInter(int i) => Inter(WayPtId(i));

        /// <summary>Paths at an intersection: shortcuts first, then roads, indexed as one list. 0 for null.</summary>
        static int PathCount(Intersection inter) =>
            inter == null ? 0 : (inter.Shortcuts?.Count ?? 0) + inter.Roads.Count;

        static Road RoadRef(Intersection inter, int k)
        {
            if (inter == null || k < 0) return null;
            int s = inter.Shortcuts?.Count ?? 0;
            if (k < s) return inter.Shortcuts[k];
            k -= s;
            return k < inter.Roads.Count ? inter.Roads[k] : null;
        }

        static float DotXZ(Vector3 a, Vector3 b) => a.x * b.x + a.z * b.z;
        static float DistSqXZ(Vector3 a, Vector3 b) { float dx = a.x - b.x, dz = a.z - b.z; return dx * dx + dz * dz; }

        static float BrakeFor(float speed, float targetSpeed, float dist) =>
            speed > targetSpeed ? (speed - targetSpeed) / (dist / speed * K) : 0f;

        void ApplyBrake(float b)
        {
            Throttle = 0f;
            Brake = Mathf.Clamp01(b);
            Car.Body.angularVelocity *= 0.85f; // per-tick in MM2; scale if your tick rate differs
        }

        /// <summary>
        /// CandidateRoutes[r, i], where row -1 is ScratchRoute: the original's flat layout puts the
        /// scratch array directly before the candidates, so an unset CurrentRouteNode reads it.
        /// </summary>
        ref AiRouteNode RouteNodeAt(int r, int i)
        {
            if (r < 0) return ref ScratchRoute[i];
            return ref CandidateRoutes[r, i];
        }

        /// <summary>
        /// RouteNodeCount[r], where r == -1 reads the int before it in the flat layout: the three
        /// int[25] arrays are contiguous, so RouteNodeCount[-1] is RouteUsesSidewalk[24].
        /// </summary>
        int RouteNodeCountAt(int r) => r >= 0 ? RouteNodeCount[r] : RouteUsesSidewalk[MaxRoutes - 1];

        /// <summary>Flip if positive SteeringInput turns your sim left rather than right.</summary>
        public bool InvertSteering = false;

        [Header("Steering smoothing - debugging aid, all zero = faithful to MM2")]
        /// <summary>Yaw-rate feedback. 0 = faithful to MM2. Raise until the wheel stops chattering.</summary>
        public float SteerDamping = 0f;
        /// <summary>Max steering change per second. 0 = unlimited, as in MM2.</summary>
        public float SteerRateLimit = 0f;
        /// <summary>Demands smaller than this go to zero, so the car stops hunting on straights.</summary>
        public float SteerDeadzone = 0f;

        float lastSteerOutput;

        /// <summary>
        /// Turns a raw steering demand into the value written to the sim: subtracts the yaw the car
        /// is already carrying, drops tiny demands, then limits how fast the wheel may move.
        /// With SteerDamping and SteerRateLimit both 0 this returns the demand unchanged.
        /// </summary>
        float SmoothSteering(float demand)
        {
            if (SteerDamping > 0f)
            {
                float yawRate = Car.Body.angularVelocity.y;      // rad/s, + is toward Right in Unity
                demand -= (InvertSteering ? -yawRate : yawRate) * SteerDamping;
            }

            demand = Mathf.Clamp(demand, -1f, 1f);
            if (Mathf.Abs(demand) < SteerDeadzone) demand = 0f;

            if (SteerRateLimit > 0f)
                demand = Mathf.MoveTowards(lastSteerOutput, demand, SteerRateLimit * Time.fixedDeltaTime);

            lastSteerOutput = demand;
            return demand;
        }

        float SteerTo(Vector3 d, float gain)
        {
            float a = Mathf.Atan2(DotXZ(d, CarRight), DotXZ(d, CarForward)) * gain;
            return InvertSteering ? -a : a;
        }

        // =====================================================================
        //  DriveRoute
        // =====================================================================

        public void DriveRoute()
        {
            var sim = Car.VehCarSim;
            sim.Engine.AIThrottle = MaxThrottle;

            if (Car.Damage.CurrentDamage < Car.Damage.MaxDamage)
            {
                switch (State)
                {
                    case VehiclePhysicsState.Forward:
                        if (LastState != State) { InitForward(); LastState = State; }
                        Forward();
                        break;
                    case VehiclePhysicsState.Backup:
                        if (LastState != State) { InitBackup(); LastState = State; }
                        Backup();
                        break;
                    case VehiclePhysicsState.Shortcut:
                        if (LastState != State) { InitShortcut(); LastState = State; }
                        Shortcut();
                        break;
                    case VehiclePhysicsState.Stop:
                        Stop();
                        break;
                    default:
                        Debug.LogWarning("Unknown state: aiVehiclePhysics.");
                        break;
                }
            }
            else if(Car.Damage.DamagePercentage >= 1.0f)
            {
                if (!damagedOut) 
                {
                    damagedOutStart = Time.time;
                    damagedOut = true; 
                }
                sim.SteeringInput = 0f;
                sim.Engine.ThrottleInput = 0f;
                sim.BrakeInput = 0f;
                Car.Body.velocity *= 0.95f;
            }
            else if(damagedOut && Car.Damage.DamagePercentage < 1.0f)
            {
                damagedOut = false;
            }
        }

        // =====================================================================
        //  Forward
        // =====================================================================

        void Forward()
        {
            var sim = Car.VehCarSim;

            if (!PreDriveChecks()) return;

            // road hint for map lookup: the segment we're expected to be on
            int roadHint = -1;
            if (WayPtIndex >= NumWayPts)
            {
                if (EndMode == 1 || EndMode == 2)
                    roadHint = EndRoadId;
            }
            else
            {
                var prev = WayPtIndex != 0 ? WayPtInter(WayPtIndex - 1) : WayPtInter(NumWayPts - 1);
                var cur = WayPtInter(WayPtIndex);
                var seg = DetRdSegBetweenInts(prev, cur, out _);
                if (seg != null) roadHint = seg.Id;
            }


            RoomId = Net.MapComponent(CarPos, ref CurMapCompIdx, out CurMapCompType, RoomId, roadHint);
            SolveRoadTargetPoint();
            if (State != VehiclePhysicsState.Forward) return; // NOT from MM2: the guard switched us to Shortcut

            // overshot the finish while facing along the end orientation -> brake and steer away
            Vector3 pos = CarPos, fwd = CarForward;
            bool overshot = RouteNodeAt(CurrentRouteNode, 1).Distance == 9999f
                            && DistSqXZ(pos, RouteEndPos) < 625f
                            && DotXZ(fwd, RouteEndOri) > 0f
                            && DotXZ(RouteEndPos - pos, fwd) < 0f;

            Vector3 d;
            if (overshot)
            {
                Brake = 1f;
                Throttle = 0f;
                d = pos - TargetPt;
            }
            else
            {
                d = TargetPt - pos;
            }

            float raw = SteerTo(d, 1.33f * 1.428f);
            Steering = Mathf.Clamp(raw, -1f, 1f);

            sim.HandBrakeInput = (Car.VehCarSim.Speed > 30f && (raw < -1f || raw > 1f)) ? 1f : 0f;
            sim.SteeringInput = SmoothSteering(Steering);
            sim.BrakeInput = Brake;
            sim.Engine.ThrottleInput = Throttle;
            sim.CarFrictionHandling = HighFrictionHandling ? 2f : 1f;
        }

        // =====================================================================
        //  Init / Stop
        // =====================================================================

        void InitForward()
        {
            var sim = Car.VehCarSim;
            Steering = 0f;
            sim.SteeringInput = 0f;
            sim.Engine.ThrottleInput = 0f;
            sim.BrakeInput = 0f;
            AiStuck.State = 0;
            Car.Stuck.Reset();

            TargetPt = CarPos;
            ActiveRouteNodes = 0;
            CurrentRouteNode = -1; // reads of the chosen route go through RouteNodeAt, which maps -1 to ScratchRoute
            TCPO = 0;
            Array.Clear(RouteBlocked, 0, MaxRoutes);
            Array.Clear(RouteUsesSidewalk, 0, MaxRoutes);
            Array.Clear(RouteNodeCount, 0, MaxRoutes);

            if (InReverse) SetForwardGear();   // MM2 gear 0 is reverse
        }

        void InitBackup()
        {
            BackupTicks = 0;
            TargetPt = RouteNodeAt(CurrentRouteNode, 1).Position;   // -1 reads ScratchRoute[1], as in MM2
            if (!InReverse) SetReverseGear();
        }

        void Stop()
        {
            var sim = Car.VehCarSim;
            TargetPt = RouteEndPos;
            Steering = Mathf.Clamp(SteerTo(RouteEndPos - CarPos, 1.33f), -1f, 1f);
            sim.SteeringInput = SmoothSteering(Steering);
            Throttle = 0f;
            Brake = 1f;
            sim.Engine.ThrottleInput = 0f;
            sim.BrakeInput = 1f;
            RouteNodeCount[0] = 0;
            ActiveRouteNodes = 0;
        }

        // =====================================================================
        //  Mirror - shadow another car: match its heading and sit off its pace
        // =====================================================================

        /// <summary>
        /// aiVehiclePhysics::Mirror. Steers to line up with the other car's heading rather than
        /// its position, and brakes only once we're more than 3 m/s faster than it.
        /// </summary>
        public void Mirror(VehCar other)
        {
            var sim = Car.VehCarSim;
            var otherSim = other.VehCarSim;

            TargetPt = other.transform.position;

            float target = otherSim.Speed - 3f;     // sit a little off their pace
            if (target >= sim.Speed)
            {
                Throttle = 0.5f;
                Brake = 0f;
            }
            else
            {
                float br = (sim.Speed - target) / K;
                if (br <= 0.30000001f)
                {
                    Throttle = 0f;
                    Brake = 0f;
                }
                else
                {
                    Throttle = 0f;
                    Brake = Mathf.Clamp01(br);
                    Car.Body.angularVelocity *= 0.85f;
                }
            }

            // align with their heading, not toward them
            Steering = Mathf.Clamp(SteerTo(other.transform.forward, 1.33f * 1.428f), -1f, 1f);

            sim.SteeringInput = SmoothSteering(Steering);
            sim.BrakeInput = Brake;
            sim.Engine.ThrottleInput = Throttle;
        }

        // =====================================================================
        //  Follow - NOT from MM2. A test harness for the steering and speed plumbing.
        // =====================================================================

        /// <summary>
        /// Drives at another car and holds a gap behind it. Same steering and braking maths as
        /// Forward, but the target point is just the other car instead of a route node.
        /// </summary>
        public void Follow(VehCar other, float gap = 15f)
        {
            var sim = Car.VehCarSim;

            Vector3 pos = CarPos;
            TargetPt = other.transform.position;

            Vector3 d = TargetPt - pos;
            float dist = Mathf.Sqrt(DistSqXZ(pos, TargetPt));

            // Steering. When the target is behind us atan2 swings between +pi and -pi, which
            // flips the steer every frame, so hold full lock toward whichever side it's on.
            float ahead = DotXZ(d, CarForward);
            float lateral = DotXZ(d, CarRight);

            if (ahead > 0f)
            {
                Steering = Mathf.Clamp(SteerTo(d, FollowSteerGain), -1f, 1f);
            }
            else
            {
                float lock_ = lateral >= 0f ? 1f : -1f;
                Steering = InvertSteering ? -lock_ : lock_;
            }

            // speed: close the gap, then match their pace
            float targetSpeed = dist > gap
                ? other.VehCarSim.Speed + 5f
                : Mathf.Max(other.VehCarSim.Speed - 3f, 0f);

            if (dist < gap * 0.5f) targetSpeed = 0f;   // too close, stop

            if (sim.Speed < targetSpeed)
            {
                Throttle = MaxThrottle;
                Brake = 0f;
            }
            else
            {
                float br = (sim.Speed - targetSpeed) / K;
                if (br <= CornerBrakingThreshold)
                {
                    Throttle = 0f;
                    Brake = 0f;
                }
                else
                {
                    Throttle = 0f;
                    Brake = Mathf.Clamp01(br);
                }
            }

            sim.SteeringInput = SmoothSteering(Steering);
            sim.BrakeInput = Brake;
            sim.Engine.ThrottleInput = Throttle;
            sim.HandBrakeInput = 0f;    // no handbrake while testing
        }

        // =====================================================================
        //  Target point solving
        // =====================================================================

        void SolveRoadTargetPoint()
        {
            PastLastWayPt = WayPtIndex >= NumWayPts;
            if (PlanRoute() != 0) return; // PlanRoute always returns 0 in the original

            // NOT from MM2. Reset leaves SomePaths empty while State is already Forward, and a
            // RegisterRoute that found no segments can too. Drive at the next waypoint until the
            // map lookup puts us back on a road.
            if (Paths[0] == null)
            {
                State = VehiclePhysicsState.Shortcut;
                return;
            }

            var p0 = Paths[0];
            if (Paths[3] != p0)
            {
                RegisterRouteOriginalPos1 = CarPos;
                RegisterRouteOriginalPos2 = CarPos;
                Paths[3] = p0;
                InitRoadTurns();
                CalcRoadTurns();
            }

            CalcRoute();
            TargetPt = RouteNodeAt(CurrentRouteNode, 1).Position;
            CalcSpeed();
        }

        void SolveShortcutTargetPoint()
        {
            if (WayPtIndex >= NumWayPts)
            {
                if (FinishRadius == 0f)
                {
                    TargetPt = RouteEndPos;
                }
                else
                {
                    Vector3 pos = CarPos;
                    float dist = Mathf.Sqrt(DistSqXZ(pos, RouteEndPos));
                    TargetPt = dist >= FinishRadius
                        ? pos + (RouteEndPos - pos) * ((dist - FinishRadius) / dist)
                        : pos;
                }
            }
            else
            {
                TargetPt = WayPtInter(WayPtIndex).Center;

                if (CurMapCompType == CompType.Intersection && CurMapCompIdx == WayPtId(WayPtIndex))
                {
                    int w = WayPtIndex;
                    Paths[0] = DetRdSegBetweenInts(WayPtInter(w), WayPtInter(w + 1), out PathsSides[0]);
                    Paths[1] = DetRdSegBetweenInts(WayPtInter(w + 1), WayPtInter(w + 2), out PathsSides[1]);
                    Paths[2] = DetRdSegBetweenInts(WayPtInter(w + 2), WayPtInter(w + 3), out PathsSides[2]);
                    WayPtIndex++;
                    InitRoadTurns();
                    State = VehiclePhysicsState.Forward;

                    if (WayPtIndex == NumWayPts && (CurLap < NumLaps || NumLaps == InfiniteLaps))
                    {
                        WayPtIndex = 0; // note: PlanRoute wraps to 1, this wraps to 0
                        CurLap++;
                    }
                }
            }

            CurrentRouteNode = 0;
            RouteNodeCount[0] = 0;
            CandidateRoutes[0, 0].PathIndex = 0;
            CandidateRoutes[0, 0].StartPathIndex = 0;
            TargetPt.y += 1f;
            CalcSpeed();
        }

        // =====================================================================
        //  Speed
        // =====================================================================

        void CalcSpeed()
        {
            int r = CurrentRouteNode;
            if (RouteNodeCountAt(r) <= 2) { CalcRoadSpeed(); return; }

            Vector3 a = RouteNodeAt(r, 1).Position - RouteNodeAt(r, 0).Position;
            Vector3 b = RouteNodeAt(r, 2).Position - RouteNodeAt(r, 1).Position;
            float ang = Vector3.Angle(a, b) * Mathf.Deg2Rad; // TODO: verify against MM2 Vector3::Angle

            if (ang <= TurnHeadingThreshold) { CalcRoadSpeed(); return; }

            float cornerSpeed = Mathf.Sqrt(Mathf.Tan((3.14f - Mathf.Abs(ang)) * 0.5f) * 10f * K) * CornerSpeedMultiplier;
            float br = BrakeFor(Car.VehCarSim.Speed, cornerSpeed, RouteNodeAt(r, 1).Distance);

            if (br <= CornerBrakingThreshold) { CalcRoadSpeed(); return; }

            ApplyBrake(br);
        }

        void CalcRoadSpeed()
        {
            var sim = Car.VehCarSim;
            Brake = 0f;
            Throttle = sim.Engine.AIThrottle;

            float endDistSq = DistSqXZ(CarPos, RouteEndPos);

            // ---- approaching the finish ----
            if (endDistSq < 5000f && WayPtIndex >= NumWayPts && CurLap >= NumLaps)
            {
                float dist = Mathf.Sqrt(endDistSq) - FinishRadius;
                float b = BrakeFor(Car.VehCarSim.Speed, TargetSpeed, dist);
                if (dist * 0.014f < b) ApplyBrake(b);
                if (dist < 2.5f && TargetSpeed <= 0f) { Throttle = 0f; Brake = 1f; }
                return;
            }

            ref AiRouteNode node0 = ref RouteNodeAt(CurrentRouteNode, 0);

            // ---- A: sharp turns inside the upcoming paths ----
            bool braked = false;
            for (int pi = node0.StartPathIndex; pi < 3 && !braked; pi++)
            {
                var path = Paths[pi];
                if (path == null || path.RoadTurns.Count <= 0) continue;
                int side = PathsSides[pi];

                for (int t = 0; t < path.RoadTurns.Count; t++)
                {
                    int vert = path.RoadTurns[t].Vertex;
                    float cornerSpeed = Mathf.Sqrt(path.RoadTurns[t].Radius * K) * CornerSpeedMultiplier;
                    Vector3 ip = path.RoadTurns[t].Intersection;
                    float dx = node0.Position.x - ip.x;
                    float dz = node0.Position.z - ip.z;

                    float dist;
                    if (side != 0)
                    {
                        Vector3 oz = path.OriZ(vert);
                        dist = dz * oz.z + dx * oz.x - path.RoadTurns[t].Setback;
                    }
                    else
                    {
                        int off = 1;
                        if (vert + 2 < path.NumSections)
                        {
                            Vector3 seg = path.Origin(vert + 1) - path.Origin(vert);
                            Vector3 ox = path.OriX(vert), oz = path.OriZ(vert);
                            float a = Mathf.Atan2(-ox.x * seg.x - ox.z * seg.z, -oz.x * seg.x - oz.z * seg.z);
                            if (path.RoadTurns[t].Angle != a) off = 2; // exact float compare, as original
                        }
                        Vector3 ozo = path.OriZ(vert + off);
                        dist = -ozo.x * dx - ozo.z * dz - path.RoadTurns[t].Setback;
                    }

                    float br = BrakeFor(Car.VehCarSim.Speed, cornerSpeed, dist);
                    if (br > CornerBrakingThreshold)
                    {
                        ApplyBrake(br);
                        braked = true;
                        break;
                    }
                }
            }

            // ---- B: turns at road ends (between SomePaths[0]/[1], then [1]/[2]) ----
            if (Paths[0] != null
                && Mathf.Abs(TurnAngle[0]) > TurnHeadingThreshold
                && CheckDistance(0) > DistSqXZ(CarPos, TurnPos[0]))
            {
                float cs = Mathf.Sqrt(K * TurnRadius[0]) * CornerSpeedMultiplier;
                if (Paths[1] != null && (Paths[1].Flags & PathFlags.Alleyway) != 0) cs *= 0.5f;

                float br = 0f;
                if (cs < Car.VehCarSim.Speed)
                {
                    float dx = node0.Position.x - TurnPos[0].x;
                    float dz = node0.Position.z - TurnPos[0].z;
                    var p0 = Paths[0];
                    float dist = PathsSides[0] != 0
                        ? dz * p0.OriZ(p0.NumSections - 1).z + dx * p0.OriZ(p0.NumSections - 1).x - TurnSetback[0]
                        : -p0.OriZ(0).z * dz - p0.OriZ(0).x * dx - TurnSetback[0];
                    br = (Car.VehCarSim.Speed - cs) / (dist / Car.VehCarSim.Speed * K);
                }

                if (br > CornerBrakingThreshold && br > Brake) ApplyBrake(br);
            }
            else if (Paths[1] != null
                     && Mathf.Abs(TurnAngle[1]) > TurnHeadingThreshold
                     && CheckDistance(1) > DistSqXZ(CarPos, TurnPos[1]))
            {
                float cs = Mathf.Sqrt(K * TurnRadius[1]) * CornerSpeedMultiplier;
                if (Paths[2] != null && (Paths[2].Flags & PathFlags.Alleyway) != 0) cs *= 0.5f;

                float br = 0f;
                if (cs < Car.VehCarSim.Speed)
                {
                    float dist = Mathf.Sqrt(DistSqXZ(node0.Position, TurnPos[1])) - TurnSetback[1];
                    br = (Car.VehCarSim.Speed - cs) / (dist / Car.VehCarSim.Speed * K);
                }

                if (br > CornerBrakingThreshold && br > Brake) ApplyBrake(br);
            }
        }

        // =====================================================================
        //  Road turns
        // =====================================================================

        void InitRoadTurns()
        {
            for (int i = 0; i < 2; i++)
            {
                var a = Paths[i];
                var b = Paths[i + 1];
                if (a == null || b == null)
                {
                    TurnAngle[i] = 0f;
                    TurnDir[i] = 0f;
                    continue;
                }

                // direction we leave along path b
                Vector3 dirB = PathsSides[i + 1] != 0
                    ? b.Origin(1) - b.Origin(0)
                    : b.Origin(b.NumSections - 2) - b.Origin(b.NumSections - 1);

                float y, x;
                if (PathsSides[i] != 0)
                {
                    y = -DotXZ(dirB, a.OriX(a.NumSections - 1));
                    x = -DotXZ(dirB, a.OriZ(a.NumSections - 1));
                }
                else
                {
                    y = DotXZ(dirB, a.OriX(0));
                    x = DotXZ(dirB, a.OriZ(0));
                }

                float ang = Mathf.Atan2(y, x);
                TurnAngle[i] = ang;
                TurnDir[i] = ang >= TurnHeadingThreshold ? 1f : (ang > -TurnHeadingThreshold ? 0f : -1f);
            }
        }

        /// <summary>
        /// 0 = not in a turn, 1..2 = in road-end turn (pathIdx+1), >=3 = in sharp turn (turnIdx+3).
        /// nodeIdx indexes ScratchRoute (the 40-node array at 0x2D4).
        /// </summary>
        public int InSharpTurn(int nodeIdx)
        {
            ref AiRouteNode node = ref ScratchRoute[nodeIdx];

            // first sharp turn at/after this node's vertex on its path
            int turn = -1;
            {
                int pi = node.PathIndex;
                var path = Paths[pi];
                for (int v = node.RoadVertexIndex; v < path.NumSections; v++)
                {
                    turn = path.IsSharpTurn(v, PathsSides[pi]);
                    if (turn > -1) break;
                }
                if (turn <= -1) turn = -1; // original used an uninitialised local here
            }

            for (int pi = node.PathIndex; pi < 3; pi++)
            {
                var path = Paths[pi];

                if (path != null && turn > -1)
                {
                    int side = PathsSides[pi];
                    for (int t = turn; t < path.RoadTurns.Count; t++)
                    {
                        float r = path.RoadTurns[t].Radius + 15f;
                        Vector3 c = path.RoadTurns[t].Center;
                        float dx = node.Position.x - c.x;
                        float dz = node.Position.z - c.z;
                        if (dx * dx + dz * dz >= r * r) continue;

                        Vector3 s = path.RoadTurns[t].StartDir;
                        Vector3 e = path.RoadTurns[t].EndDir;
                        float dir = path.SharpTurnDir(t, (RoadSide)side) * Handedness;
                        float startCross = dir * (s.x * dz - s.z * dx);
                        float endCross = dir * (e.z * dx - e.x * dz);

                        float first = side == 1 ? startCross : endCross;
                        float second = side == 1 ? endCross : startCross;

                        if (first >= -0.01f && second >= 0.01f)
                        {
                            node.PathIndex = (short)pi;
                            return t + 3;
                        }
                    }
                    turn = 0;
                }

                bool checkEnd = pi == 0
                                || (pi == 1 && (node.StartPathIndex == 1 || Mathf.Abs(TurnAngle[0]) < SharpTurnAngle));

                if (checkEnd && TurnDir[pi] != 0f)
                {
                    float dx = node.Position.x - TurnCenter[pi].x;
                    float dz = node.Position.z - TurnCenter[pi].z;
                    float r = TurnRadius[pi] + 15f;
                    if (r * r > dx * dx + dz * dz)
                    {
                        float dir = TurnDir[pi] * Handedness;
                        Vector3 s = TurnStartDir[pi], e = TurnEndDir[pi];
                        if (dir * (s.x * dz - s.z * dx) >= -0.01f && dir * (e.z * dx - e.x * dz) >= 0.01f)
                            return pi + 1;
                    }
                }
            }
            return 0;
        }

        // =====================================================================
        //  Route planning
        // =====================================================================

        int PlanRoute()
        {
            if (WayPtIndex < NumWayPts
                && CurMapCompType == CompType.Intersection
                && CurMapCompIdx == WayPtId(WayPtIndex))
            {
                if (WayPtIndex > 0)
                {
                    var seg = DetRdSegBetweenInts(WayPtInter(WayPtIndex - 1), WayPtInter(WayPtIndex), out _);
                    if (Paths[0] != seg) LocateWayPtFromRoad(seg);
                }

                WayPtIndex++;
                CheckForShortcut();

                if (WayPtIndex == NumWayPts && (CurLap < NumLaps || NumLaps == InfiniteLaps))
                {
                    WayPtIndex = 1;
                    CurLap++;
                }
            }

            if ((int)CurMapCompType <= 0) return 0;

            if ((int)CurMapCompType <= 2)   // road or shortcut
            {
                LocateWayPtFromRoad(Path(CurMapCompIdx));
                return 0;
            }

            if (CurMapCompType != CompType.Intersection || PathEndIndex >= 3 || EndMode != 1)
                return 0;

            AssignEndRoad(Inter(CurMapCompIdx));
            return 0;
        }

        bool AssignEndRoad(Intersection inter)
        {
            if (inter == null) return false;
            var endRoad = Path(EndRoadId);
            for (int k = 0; k < PathCount(inter); k++)
            {
                var p = RoadRef(inter, k);
                if (p != endRoad) continue;
                Paths[PathEndIndex] = p;
                PathsSides[PathEndIndex] = p.RightEndData.IntersectionID == inter.Id ? 1 : 0;
                return true;
            }
            return false;
        }

        int LocateWayPtFromRoad(Road road)
        {
            if (road == null) return 0; // original would crash

            // past the last waypoint: just drive this road in whatever direction we face
            if (WayPtIndex >= NumWayPts)
            {
                Paths[0] = road;
                Paths[1] = null;
                Paths[2] = null;
                int v = Mathf.Clamp(road.RoadVertice(CarPos, 1), 0, road.NumSections - 1);
                PathsSides[0] = DotXZ(road.OriZ(v), -CarForward) >= 0f ? 1 : 0;
                return 1;
            }

            int nextId = WayPtId(WayPtIndex + 1);
            if (road.LeftEndData.IntersectionID == nextId
                || road.RightEndData.IntersectionID == nextId
                || (WayPtIndex == NumWayPts - 1 && EndRoadId == CurMapCompIdx))
            {
                WayPtIndex++;
            }

            int wp = WayPtIndex;
            int wpId = WayPtId(wp);

            if (road.LeftEndData.IntersectionID == wpId) { SetupPathsFromRoad(road, 1); return 1; }
            if (road.RightEndData.IntersectionID == wpId) { SetupPathsFromRoad(road, 0); return 1; }

            // waypoint is one road further out
            if (TryNeighbour(road, Inter(road.LeftEndData.IntersectionID), 1, wpId)
                || TryNeighbour(road, Inter(road.RightEndData.IntersectionID), 0, wpId))
            {
                Paths[2] = (wp + 1) >= NumWayPts
                    ? null
                    : DetRdSegBetweenInts(WayPtInter(wp), WayPtInter(wp + 1), out PathsSides[2]);
                return 1;
            }

            Debug.Log($"ERROR: I'm Lost!! {Id}");
            var comp = mapScratch.Count > 0 ? mapScratch[0] : default;
Debug.Log($"LOST {Id}: compType={CurMapCompType} compIdx={CurMapCompIdx} " +
          $"road.Id={road.Id} ends=({road.LeftEndData.IntersectionID},{road.RightEndData.IntersectionID}) " +
          $"wp={WayPtId(WayPtIndex)} next={WayPtId(WayPtIndex + 1)} roadsCount={Net.Data.Roads.Count} " +
          $"candidates=[{string.Join(",", mapScratch.Select(c => $"{c.Type}:{c.Id}"))}]");
            return 0;
        }

        /// <summary>A null intersection (dangling end id) has PathCount 0, so this just returns false.</summary>
        bool TryNeighbour(Road road, Intersection inter, int side0, int wpId)
        {
            for (int k = 0; k < PathCount(inter); k++)
            {
                var p = RoadRef(inter, k);
                if (p == null) continue;

                int side1;
                if (p.LeftEndData.IntersectionID == wpId) side1 = 1;
                else if (p.RightEndData.IntersectionID == wpId) side1 = 0;
                else continue;

                PathsSides[0] = side0;
                Paths[0] = road;
                Paths[1] = p;
                PathsSides[1] = side1;
                return true;
            }
            return false;
        }

        void SetupPathsFromRoad(Road road, int side)
        {
            Paths[0] = road;
            PathsSides[0] = side;
            PathEndIndex = 0;

            int wp = WayPtIndex;
            int n = NumWayPts;
            bool finalLap = CurLap >= NumLaps && NumLaps != InfiniteLaps;

            Intersection a = WayPtInter(wp), b, c;

            if (wp == n - 1)
            {
                if (finalLap) { b = null; c = null; PathEndIndex = 1; }
                else
                {
                    // wrap: assumes ids[0] == ids[n-1] on circuits, so skip to 1 and 2
                    b = Inter(n > 1 ? (ushort)RouteIntersectionIds[1] : InvalidId);
                    c = Inter(n > 2 ? (ushort)RouteIntersectionIds[2] : InvalidId);
                    PathEndIndex = 3;
                }
            }
            else if (wp == n - 2)
            {
                b = WayPtInter(wp + 1);
                if (finalLap) { c = null; PathEndIndex = 2; }
                else
                {
                    c = Inter(n > 1 ? (ushort)RouteIntersectionIds[1] : InvalidId);
                    PathEndIndex = 3;
                }
            }
            else
            {
                b = WayPtInter(wp + 1);
                c = WayPtInter(wp + 2);
                PathEndIndex = 3;
            }

            Paths[1] = DetRdSegBetweenInts(a, b, out PathsSides[1]);
            Paths[2] = DetRdSegBetweenInts(b, c, out PathsSides[2]);

            if ((EndMode == 1 || EndMode == 2) && PathEndIndex > 0 && PathEndIndex < 3)
                AssignEndRoad(Inter((ushort)RouteIntersectionIds[n - 1]));
        }

        // =====================================================================
        //  Shared Forward/Shortcut preamble
        // =====================================================================

        /// <summary>Returns false if this tick was consumed (inactive or stuck handling).</summary>
        bool PreDriveChecks()
        {
            var sim = Car.VehCarSim;

            if (!Car.IsDrivable)
            {
                // AI not active yet: roll forward once on the ground
                if (sim.Wheels[0].LastGroundedStatus)
                {
                    sim.SteeringInput = 0f;
                    sim.BrakeInput = 0f;
                    sim.Engine.ThrottleInput = 1f;
                }
                return false;
            }

            AiStuck.Update();

            if (Car.Stuck.State == VehStuck.StuckState.Pegged)
            {
                PlanRoute();
                State = VehiclePhysicsState.Backup;
                Car.Body.velocity = Vector3.zero;
                Car.Body.angularVelocity = Vector3.zero;
                return false;
            }

            if (AiStuck.State == 2)
            {
                sim.SteeringInput = 1f;
                sim.BrakeInput = 0f;
                sim.Engine.ThrottleInput = 1f;
                Car.Stuck.State = 0;
                return false;
            }

            return true;
        }

        // =====================================================================
        //  CalcRoute
        // =====================================================================

        void CalcRoute()
        {
            ref AiRouteNode n0 = ref ScratchRoute[0];

            n0.Position = CarPos;
            n0.Position.y += 1f;
            n0.Angle = 0f;
            n0.Distance = 0f;
            n0.StartPathIndex = 0;
            n0.PathIndex = 0;

            // NOT from MM2: no current path means nothing to walk. Leave no routes and make node 1
            // the car itself, so RouteNodeAt(-1, 1) gives a zero steer instead of a stale point.
            var p0 = Paths[0];
            if (p0 == null)
            {
                n0.RoadVertexIndex = 0;
                ScratchRoute[1] = n0;
                ActiveRouteNodes = 0;
                CurrentRouteNode = -1;
                return;
            }

            // which path/vertex is the car on (walk forward if past the end of a path)
            n0.RoadVertexIndex = (short)p0.RoadVertice(CarPos, PathsSides[0]);

            if (n0.RoadVertexIndex == p0.NumSections)
            {
                var p1 = Paths[1];
                if (p1 == null)
                {
                    n0.RoadVertexIndex = (short)(p0.NumSections - 1);
                }
                else
                {
                    n0.PathIndex = 1;
                    n0.RoadVertexIndex = (short)p1.RoadVertice(CarPos, PathsSides[1]);

                    if (n0.RoadVertexIndex == p1.NumSections)
                    {
                        var p2 = Paths[2];
                        if (p2 != null)
                        {
                            n0.PathIndex = 2;
                            n0.RoadVertexIndex = (short)p2.RoadVertice(CarPos, PathsSides[2]);
                        }
                        else
                        {
                            n0.RoadVertexIndex = (short)(p1.NumSections - 1);
                        }
                    }
                }
            }

            ActiveRouteNodes = 0;
            TCPO = 0;
            Array.Clear(RouteBlocked, 0, MaxRoutes);  // RouteNodeCount is NOT cleared here
            Array.Clear(RouteUsesSidewalk, 0, MaxRoutes);

            if (WayPtIndex >= NumWayPts || InSharpTurn(0) != 0)
            {
                n0.Kind = 1;
            }
            else
            {
                n0.Kind = 0;
                RegisterRouteOriginalPos1 = CarPos;
            }
            RegisterRouteOriginalPos2 = RegisterRouteOriginalPos1;

            CalcRoadTurns();
            EnumRoutes(1);          // first arg in the decompile is FPU junk (n0.Position.y)
            DetermineBestRoute();
        }

        // =====================================================================
        //  CalcRoadTurns - fits an arc into the corner between SomePaths[i] and [i+1]
        // =====================================================================

        void CalcRoadTurns()
        {
            for (int i = 0; i < 2; i++)
            {
                if (TurnDir[i] == 0f || Paths[i] == null || Paths[i + 1] == null)
                    continue;

                float inset = CalcTurnIntersection(i);   // also sets TurnPos[i]
                float half = (3.14f - Mathf.Abs(TurnAngle[i])) * 0.5f;
                float radius = inset / (1f - Mathf.Sin(half));
                float setback = Mathf.Cos(half) * radius;
                TurnRadius[i] = radius;
                TurnSetback[i] = setback;

                // path-end basis, facing out of the path in travel direction
                var path = Paths[i];
                Vector3 ox, oz;
                if (PathsSides[i] != 0)
                {
                    ox = path.OriX(path.NumSections - 1);
                    oz = path.OriZ(path.NumSections - 1);
                }
                else
                {
                    ox = -path.OriX(0);
                    oz = -path.OriZ(0);
                }

                float dir = TurnDir[i];
                TurnCenter[i] = TurnPos[i] + oz * setback - ox * ((radius - inset) * dir);

                // radial vectors from the arc centre to the arc start / end, flattened
                Vector3 start = ox * (radius * dir);
                start.y = 0f;
                TurnStartDir[i] = start.normalized;

                float a = TurnAngle[i] * dir;
                Vector3 end = ox * (Mathf.Cos(a) * radius * dir) - oz * (Mathf.Sin(a) * radius);
                end.y = 0f;
                TurnEndDir[i] = end.normalized;
            }
        }

        float CheckDistance(int i)
        {
            float d = TurnSetback[i] + LookAheadDistance;
            return d * d;
        }

        void CheckForShortcut() { } // empty in the retail build

        // =====================================================================
        //  Shortcut state - off the network: drive straight at the next waypoint
        // =====================================================================

        void InitShortcut()
        {
            ActiveRouteNodes = 0;
            CurrentRouteNode = -1;
            TCPO = 0;
            AiStuck.State = 0;
            Car.Stuck.Reset();
            Array.Clear(RouteBlocked, 0, MaxRoutes);
            Array.Clear(RouteUsesSidewalk, 0, MaxRoutes);
            Array.Clear(RouteNodeCount, 0, MaxRoutes);

            if (InReverse) SetForwardGear();
        }

        void Shortcut()
        {
            if (!PreDriveChecks()) return;

            var sim = Car.VehCarSim;

            RoomId = Net.MapComponent(CarPos, out CurMapCompIdx, out CurMapCompType, RoomId);
            SolveShortcutTargetPoint();

            Steering = Mathf.Clamp(SteerTo(TargetPt - CarPos, 1.33f), -0.75f, 0.75f);
            sim.SteeringInput = SmoothSteering(Steering);
            sim.BrakeInput = Brake;
            sim.Engine.ThrottleInput = Throttle;
        }

        // =====================================================================
        //  Backup
        // =====================================================================

        void Backup()
        {
            var sim = Car.VehCarSim;
            Vector3 d = TargetPt - CarPos;   // full 3D here, unlike the other steer calcs
            float a = Mathf.Atan2(Vector3.Dot(d, CarRight), Vector3.Dot(d, CarForward));

            if (a > 0.1f || a < -0.1f)
            {
                sim.SteeringInput = Mathf.Clamp(a * -2.8571429f, -1f, 1f); // reversing: opposite lock

                if (BackupTicks <= 65) // frame count at MM2's tick rate
                {
                    sim.Engine.ThrottleInput = 0.85f;
                    sim.BrakeInput = 0f;
                    BackupTicks++;
                }
                else
                {
                    FinishedBackingUp();
                }
            }
            else
            {
                sim.SteeringInput = 0f;
                RotateYaw(a); // snap out the remaining heading error
                FinishedBackingUp();
            }
        }

        void FinishedBackingUp()
        {
            State = Paths[0] != null ? VehiclePhysicsState.Forward : VehiclePhysicsState.Shortcut;
            Car.Stuck.Reset();
            var sim = Car.VehCarSim;
            Car.Body.velocity *= 0.25f;
            Car.Body.angularVelocity *= 0.25f;
            sim.Engine.ThrottleInput = 0f;
            sim.BrakeInput = 1f;
        }

        // =====================================================================
        //  RegisterRoute
        // =====================================================================

        public void RegisterRoute(
            int[] intersectionIds, int numIntersections,
            Vector3 endPosition, Vector3 endOrientation,
            int numLaps, float targetSpeed, float finishRadius,
            bool unkFlag, bool avoidTraffic, bool avoidProps, bool avoidPlayers, bool avoidOpponents,
            bool reckless, float maxThrottle, float cornerSpeedMultiplier,
            float cornerBrakingThreshold, float lookAheadDistance)
        {
            UnkFlag = unkFlag;
            AvoidTraffic = avoidTraffic;
            AvoidProps = avoidProps;
            AvoidPlayers = avoidPlayers;
            AvoidOpponents = avoidOpponents;
            Reckless = reckless; // prefer routes that mount the sidewalk to get round obstacles
            MaxThrottle = maxThrottle;
            LookAheadDistance = lookAheadDistance;
            NumLaps = numLaps;
            CornerBrakingThreshold = cornerBrakingThreshold;
            CornerSpeedMultiplier = cornerSpeedMultiplier;
            NumWayPts = numIntersections;
            RouteIntersectionIds = intersectionIds.ToArray(); // make a copy. TODO: use a list
            CurLap = 1; // laps are 1-based

            Vector3 pos = CarPos;
            Paths[3] = null;
            RegisterRouteOriginalPos1 = pos;
            RegisterRouteOriginalPos2 = pos;
            TargetPt = pos;
            CurrentRouteNode = -1;
            Paths[0] = Paths[1] = Paths[2] = null;
            TurnAngle[0] = TurnAngle[1] = 0f;
            ScratchRoute[0].RoadVertexIndex = 0;
            ScratchRoute[0].PathIndex = 0;
            RouteEndPos = endPosition;
            RouteEndOri = endOrientation;
            TargetSpeed = targetSpeed;
            FinishRadius = finishRadius;

            DestMapComponent(RouteEndPos, out EndRoadId, out EndMode);
            RoomId = Net.MapComponent(CarPos, out CurMapCompIdx, out CurMapCompType, 0);

            switch (CurMapCompType)
            {
                case CompType.None: // off the road network: start in Shortcut
                    WayPtIndex = 0;
                    PathEndIndex = SetupInitialSegments();
                    if (State != VehiclePhysicsState.Backup) State = VehiclePhysicsState.Shortcut;
                    RoadLateralOffset = Paths[0] != null
                        ? PathEndLateralOffset(Paths[0], PathsSides[0], pos)
                        : 0f;
                    return; // no CheckForShortcut on this path

                case CompType.Road:
                case CompType.Shortcut:
                    {
                        WayPtIndex = 0;
                        PathsSides[0] = 1;
                        Paths[0] = Path(CurMapCompIdx);
                        PathEndIndex = 0;
                        if (NumWayPts >= 2)
                        {
                            var own = Paths[0];
                            PathEndIndex = SetupInitialSegments(); // replaces SomePaths[0]
                            // NOT from MM2: waypoints 0 and 1 share no path, so keep the one we're on
                            if (Paths[0] == null) { Paths[0] = own; PathsSides[0] = 1; }
                            WayPtIndex = 1;
                        }

                        var p0 = Paths[0];
                        if (p0 == null)
                        {
                            // NOT from MM2: nothing to drive on; head for the next waypoint instead
                            if (State != VehiclePhysicsState.Backup) State = VehiclePhysicsState.Shortcut;
                            RoadLateralOffset = 0f;
                            return;
                        }

                        State = VehiclePhysicsState.Forward;

                        int v = Mathf.Clamp(p0.RoadVertice(pos, PathsSides[0]), 0, p0.NumSections - 1);
                        if (PathsSides[0] != 0)
                        {
                            RoadLateralOffset = DotXZ(p0.Origin(v) - pos, p0.OriX(v));
                        }
                        else
                        {
                            int vi = p0.NumSections - v - 1;
                            RoadLateralOffset = -DotXZ(p0.Origin(vi) - pos, p0.OriX(vi));
                        }
                        CheckForShortcut();
                        return;
                    }

                case CompType.Intersection:
                    {
                        PathEndIndex = 0;
                        WayPtIndex = 0;
                        var inter = Inter(CurMapCompIdx);
                        // prefer a real road as the starting path; original crashes on an empty intersection
                        var first = inter == null ? null
                                  : inter.Roads.Count > 0 ? inter.Roads[0]
                                  : RoadRef(inter, 0);
                        if (first == null) return;
                        Paths[0] = first;
                        PathsSides[0] = first.LeftEndData.IntersectionID == inter.Id ? 1 : 0;
                        if (NumWayPts >= 2)
                        {
                            PathEndIndex = SetupInitialSegments();
                            if (Paths[0] == null) { Paths[0] = first; PathsSides[0] = first.LeftEndData.IntersectionID == inter.Id ? 1 : 0; } // NOT from MM2
                            WayPtIndex = 1;
                        }
                        State = VehiclePhysicsState.Forward;
                        RoadLateralOffset = PathEndLateralOffset(Paths[0], PathsSides[0], pos);
                        CheckForShortcut();
                        return;
                    }
            }
        }

        /// <summary>Segments between waypoints 0-1, 1-2, 2-3 (as many as exist). Returns how many.</summary>
        int SetupInitialSegments()
        {
            int n = Mathf.Clamp(NumWayPts - 1, 0, 3);
            for (int k = 0; k < n; k++)
                Paths[k] = DetRdSegBetweenInts(WayPtInter(k), WayPtInter(k + 1), out PathsSides[k]);
            return n;
        }

        static float PathEndLateralOffset(Road p, int side, Vector3 pos)
        {
            if (side != 0)
                return DotXZ(p.Origin(0) - pos, p.OriX(0));
            int last = p.NumSections - 1;
            return -DotXZ(p.Origin(last) - pos, p.OriX(last));
        }

        // =====================================================================
        //  CalcTurnIntersection - corner point of the inside boundaries, returns arc inset
        // =====================================================================

        float CalcTurnIntersection(int i)
        {
            var p = Paths[i];
            var q = Paths[i + 1];
            int c1 = p.NumSections, c2 = q.NumSections;
            int s1 = PathsSides[i], s2 = PathsSides[i + 1];
            float off1 = CalcCurrentRdOffset(i);
            float off2 = CalcNextRdOffset(i);

            // boundary line of each road on the inside of the corner, pushed inward by the offsets
            Vector3 p1, d1, p2, d2;
            float y;
            if (TurnDir[i] > 0f)
            {
                if (s1 != 0) { p1 = p.RightData.SidewalkInnerVertices[c1 - 1] + p.OriX(c1 - 1) * off1; d1 = -p.OriZ(c1 - 1); }
                else { p1 = p.LeftData.SidewalkInnerVertices[0] - p.OriX(0) * off1; d1 = -p.OriZ(0); }

                if (s2 != 0) { p2 = q.RightData.SidewalkInnerVertices[0] + q.OriX(0) * off2; d2 = q.OriZ(0); y = q.RightData.SidewalkInnerVertices[0].y; }
                else { p2 = q.LeftData.SidewalkInnerVertices[c2 - 1] - q.OriX(c2 - 1) * off2; d2 = q.OriZ(c2 - 1); y = q.LeftData.SidewalkInnerVertices[c2 - 1].y; }
            }
            else
            {
                if (s1 != 0) { p1 = p.LeftData.SidewalkInnerVertices[c1 - 1] - p.OriX(c1 - 1) * off1; d1 = -p.OriZ(c1 - 1); }
                else { p1 = p.RightData.SidewalkInnerVertices[0] + p.OriX(0) * off1; d1 = -p.OriZ(0); }

                if (s2 != 0)
                {
                    p2 = q.LeftData.SidewalkInnerVertices[0] - q.OriX(0) * off2;
                    // decompile copies v116 into v121 BEFORE writing v116.z, which would make d2.z = off2 * OriX.z.
                    // Almost certainly a decompiler ordering artifact; verify in the disassembly.
                    d2 = -q.OriZ(0);
                    y = q.LeftData.SidewalkInnerVertices[0].y;
                }
                else
                {
                    p2 = q.RightData.SidewalkInnerVertices[c2 - 1] + q.OriX(c2 - 1) * off2;
                    d2 = q.OriZ(c2 - 1);
                    y = q.RightData.SidewalkInnerVertices[c2 - 1].y;
                }
            }

            // intersect the two lines in XZ (sign of d1/d2 doesn't matter)
            float t = (p2.z * d1.x - p1.z * d1.x - p2.x * d1.z + p1.x * d1.z) / (d2.x * d1.z - d2.z * d1.x);
            TurnPos[i] = new Vector3(p2.x + d2.x * t, y, p2.z + d2.z * t);

            // how far the car sits toward the outside of the corner (+1 if still far away)
            float far = 0f;
            float pad = TurnSetback[i] + 12f; // previous setback
            if (pad * pad < DistSqXZ(RegisterRouteOriginalPos2, TurnPos[i])) far = 1f;

            Vector3 d = RegisterRouteOriginalPos2 - TurnPos[i];
            float inset = (s1 == 1 ? DotXZ(d, p.OriX(c1 - 1)) : -DotXZ(d, p.OriX(0))) * TurnDir[i] + far;

            float w1 = CalcCurrentMaxWidthAdjustment(i);
            float w2 = CalcNextMaxWidthAdjustment(i);
            float minInset = LeftSideDistance + RightSideDistance;

            float cap1 = Mathf.Clamp(2f * RoadEdgeValue(p) - RightSideDistance - off1 - w1, minInset, 9999f);
            if (inset < minInset) inset = minInset;
            else if (inset > cap1) inset = cap1;

            float cap2 = Mathf.Clamp(2f * RoadEdgeValue(q) - RightSideDistance - off2 - w2, minInset, 9999f);
            return inset >= minInset ? Mathf.Min(inset, cap2) : minInset;
        }

        // =====================================================================
        //  EnumRoutes - builds node n from node n-1, branches around obstacles
        // =====================================================================

        void EnumRoutes(int n)
        {
            if (n > NodesPerRoute - 1) return;

            Vector3 prevPos = ScratchRoute[n - 1].Position;
            int prevPath = ScratchRoute[n - 1].PathIndex;

            if (!PastLastWayPt)
            {
                Paths[prevPath].CalcRoadTurns(RegisterRouteOriginalPos2, PathsSides[prevPath]);

                int t = InSharpTurn(n - 1);
                if (t != 0)
                {
                    ScratchRoute[n - 1].StartPathIndex = (short)(t - 1);
                    CalcSharpTurnTarget(ref n, n - 1, prevPos);
                }
                else
                {
                    RegisterRouteOriginalPos2 = ScratchRoute[n - 1].Position;
                    CalcRoadTarget(n, prevPos);
                    t = InSharpTurn(n);
                    if (t != 0)
                    {
                        ScratchRoute[n].StartPathIndex = (short)(t - 1);
                        CalcSharpTurnTarget(ref n, n, prevPos);
                    }
                }
            }
            else
            {
                var path = Paths[prevPath];
                if (path == null)
                {
                    SetTargetPtToDestination(n);
                }
                else
                {
                    path.CalcRoadTurns(RegisterRouteOriginalPos2, PathsSides[prevPath]);

                    int t = InSharpTurn(n - 1);
                    if (t != 0)
                    {
                        ScratchRoute[n - 1].StartPathIndex = (short)(t - 1);
                        if (CalcSharpTurnTarget(ref n, n - 1, prevPos))
                        {
                            ScratchRoute[n].StartPathIndex++;
                            RegisterRouteOriginalPos2 = ScratchRoute[n].Position;
                            var cur = Paths[ScratchRoute[n].PathIndex];
                            if (cur != null && cur.IsPosOnRoad(ScratchRoute[n].Position, 2f, out _) >= RoadPosition.OnSidewalk)
                                CalcDestinationTarget(n, ref prevPos);
                        }
                    }
                    else
                    {
                        RegisterRouteOriginalPos2 = ScratchRoute[n - 1].Position;
                        CalcDestinationTarget(n, ref prevPos);
                        t = InSharpTurn(n);
                        if (t != 0)
                        {
                            ScratchRoute[n].StartPathIndex = (short)(t - 1);
                            CalcSharpTurnTarget(ref n, n, prevPos);
                        }
                    }
                }
            }

            // ---- obstacle check on segment n-1 -> n ----
            ScratchRoute[n].RoadPos = 1; // OnRoad
            ref AiRouteNode prev = ref ScratchRoute[n - 1];
            ref AiRouteNode node = ref ScratchRoute[n];

            float lookPad = (RearBumperDistance + FrontBumperDistance) * 2f;
            var blocker = IsTargetBlocked(
                prev.Position, node.Position,
                prev.RoadVertexIndex, prev.PathIndex,
                node.RoadVertexIndex + 3, node.PathIndex,
                lookPad, out int blockType);

            if (blocker != null)
            {
                Vector3 bp = blocker.Position;
                if (blockType != 2 || (WayPtIndex > 2 && prev.Kind != 1))
                {
                    if (LookAheadDistance * LookAheadDistance > DistSqXZ(prev.Position, bp))
                    {
                        // this depth's own buffer: SaveTarget recurses, and deeper levels must not overwrite it
                        var buf = avoidPoints[n];
                        Array.Clear(buf, 0, buf.Length);
                        int count = CalcObstacleAvoidPoints(blocker, n, LegacyVehicleID != 3, buf);
                        for (int k = 0; k < count; k++)
                            SaveTarget(n, buf[k], blockType);
                        return;
                    }
                }
            }

            ContinueCheck(n);
        }

        // =====================================================================
        //  DetermineBestRoute - cheapest route by the Angle (cost) of its last node
        // =====================================================================

        int DetermineBestRoute()
        {
            CurrentRouteNode = -1;
            if (Reckless) CurrentRouteNode = CheapestRoute(0);
            if (CurrentRouteNode < 0) CurrentRouteNode = CheapestRoute(1);
            if (CurrentRouteNode < 0) CurrentRouteNode = CheapestRoute(2);
            return CurrentRouteNode;
        }

        /// <summary>filter 0: uses the sidewalk, 1: not blocked, 2: any.</summary>
        int CheapestRoute(int filter)
        {
            int best = -1;
            float bestCost = 99999f;
            for (int r = 0; r < ActiveRouteNodes; r++)
            {
                if (filter == 0 && RouteUsesSidewalk[r] == 0) continue;
                if (filter == 1 && RouteBlocked[r] != 0) continue;
                float cost = RouteCost(r);
                if (bestCost > cost) { bestCost = cost; best = r; }
            }
            return best;
        }

        float RouteCost(int r)
        {
            int k = RouteNodeCount[r] - 1;
            if (k >= 0) return CandidateRoutes[r, k].Angle;
            // count 0 reads one node back in the flat layout (ScratchRoute then CandidateRoutes)
            return r > 0 ? CandidateRoutes[r - 1, NodesPerRoute - 1].Angle : ScratchRoute[NodesPerRoute - 1].Angle;
        }

        // =====================================================================
        //  Road offsets - keep the turn line clear of ambient traffic
        // =====================================================================

        /// <summary>
        /// How far the ambient cars on one end of a path reach in from one sidewalk edge, plus 1.25 margin (0 if none).
        /// The axis points inward: the right edge uses +OriX and the left edge uses -OriX.
        /// </summary>
        float TrafficIntrusion(Road p, bool atEnd, bool rightVehicles, bool rightEdge)
        {
            var instance = InstanceOf(p);
            if (instance == null) return 0f; // no instance, so no ambient traffic to keep clear of

            int idx = atEnd ? p.NumSections - 1 : 0;
            Vector3 edge = rightEdge ? p.RightData.SidewalkInnerVertices[idx] : p.LeftData.SidewalkInnerVertices[idx];
            Vector3 inward = rightEdge ? p.OriX(idx) : -p.OriX(idx);

            // MM2's rVehicles / lVehicles lists are the vehicle lanes of that road side
            float max = 0f;
            foreach (var lane in instance.GetLanes(rightVehicles ? RoadSide.Right : RoadSide.Left, RailType.Vehicle))
            {
                for (int i = 0; i < lane.Ordered.Count; i++)
                {
                    float d = DotXZ(lane.Ordered[i].Position - edge, inward) + 1.25f;
                    if (d > max) max = d;
                }
            }
            return max;
        }

        // For the current path, "onEnd" (side == 1) picks the end vertex and the rVehicles list.
        // For the next path, "fromStart" (side == 1) picks vertex 0 and the lVehicles list.
        // RdOffset and MaxWidthAdjustment run the same scan, but for opposite turn directions.

        float CalcCurrentRdOffset(int i)
        {
            bool turnPos = TurnDir[i] > 0f;
            bool onEnd = PathsSides[i] == 1;
            var p = Paths[i];

            if (Net.AIMap.LeftSidedTraffic)
                return turnPos ? RightSideDistance : TrafficIntrusion(p, onEnd, onEnd, !onEnd) + LeftSideDistance;

            return turnPos ? TrafficIntrusion(p, onEnd, onEnd, onEnd) + RightSideDistance : LeftSideDistance;
        }

        float CalcNextRdOffset(int i)
        {
            bool turnPos = TurnDir[i] > 0f;
            bool fromStart = PathsSides[i + 1] == 1;
            var q = Paths[i + 1];

            if (Net.AIMap.LeftSidedTraffic)
                return turnPos ? TrafficIntrusion(q, !fromStart, !fromStart, fromStart) + RightSideDistance : LeftSideDistance;

            return turnPos ? RightSideDistance : TrafficIntrusion(q, !fromStart, !fromStart, !fromStart) + LeftSideDistance;
        }

        float CalcCurrentMaxWidthAdjustment(int i)
        {
            bool turnPos = TurnDir[i] > 0f;
            bool onEnd = PathsSides[i] == 1;
            var p = Paths[i];

            if (Net.AIMap.LeftSidedTraffic)
                return turnPos ? TrafficIntrusion(p, onEnd, onEnd, !onEnd) : 0f;

            return turnPos ? 0f : TrafficIntrusion(p, onEnd, onEnd, onEnd);
        }

        float CalcNextMaxWidthAdjustment(int i)
        {
            bool turnPos = TurnDir[i] > 0f;
            bool fromStart = PathsSides[i + 1] == 1;
            var q = Paths[i + 1];

            if (Net.AIMap.LeftSidedTraffic)
                return turnPos ? 0f : TrafficIntrusion(q, !fromStart, !fromStart, fromStart);

            return turnPos ? TrafficIntrusion(q, !fromStart, !fromStart, !fromStart) : 0f;
        }

        // =====================================================================
        //  DestMapComponent - what the route's end position sits on
        // =====================================================================

        /// <summary>
        /// endMode = component type (3 intersection, 1/2 road or shortcut) with endRoadId = its id,
        /// or endMode 0 with endRoadId = the room id if nothing matched.
        /// </summary>
        void DestMapComponent(Vector3 pos, out int endRoadId, out int endMode)
        {
            var room = Net.Level.FindRoomIdWithWarps(pos);
            var components = Net.GetRoomComponents(room);

            // an intersection in the room wins outright
            foreach (var c in components)
            {
                if (c.Type != CompType.Intersection) continue;
                endMode = (int)c.Type;
                endRoadId = c.Id;
                return;
            }

            // otherwise a road/shortcut the position is on that touches the last waypoint
            foreach (var c in components)
            {
                if (c.Type != CompType.Road && c.Type != CompType.Shortcut) continue;

                var road = Path(c.Id);
                if (road == null || road.IsPosOnRoad(pos, 0f, out _) == RoadPosition.OffRoad) continue;

                if (NumWayPts == 0)
                {
                    endMode = (int)c.Type;
                    endRoadId = c.Id;
                    return;
                }

                int lastId = WayPtId(NumWayPts - 1);
                if (road.LeftEndData.IntersectionID == lastId || road.RightEndData.IntersectionID == lastId)
                {
                    endMode = (int)c.Type;
                    endRoadId = c.Id;
                    return;
                }
            }

            endMode = 0;
            endRoadId = room;
        }

        // =====================================================================
        //  Shared helpers for target calculation
        // =====================================================================

        static float AngleRad(Vector3 a, Vector3 b) => Vector3.Angle(a, b) * Mathf.Deg2Rad; // TODO: verify vs MM2 Vector3::Angle

        static Vector3 SafeNormalize(Vector3 v)
        {
            float m = v.sqrMagnitude;
            return m == 0f ? Vector3.zero : v / Mathf.Sqrt(m);
        }

        // TurnAngle[2] / TurnPos[2] don't exist; the original reads straight on into the next field.
        float TurnAngleAt(int i) => i < 2 ? TurnAngle[i] : TurnDir[i - 2];
        Vector3 TurnPosAt(int i) => i < 2 ? TurnPos[i] : TurnCenter[i - 2];

        static int RawVert(Road p, int side, int travelVert) => side != 0 ? travelVert : p.NumSections - travelVert - 1;

        /// <summary>origin-or-edge + normalize(origin - dirRef) * signedDist</summary>
        static Vector3 Inset(Vector3 origin, Vector3 edge, Vector3 dirRef, bool fromCenter, float signedDist) =>
            (fromCenter ? origin : edge) + SafeNormalize(origin - dirRef) * signedDist;

        // Travel-left / travel-right boundary points at a vertex, pushed in by the car's half-width + 1.
        // fromCenter is used on divided roads (the centre line acts as the boundary on your half).
        Vector3 LeftBoundPoint(Road p, int side, int raw, bool fromCenter) => side != 0
            ? Inset(p.Origin(raw), p.LeftData.SidewalkInnerVertices[raw], p.RightData.SidewalkInnerVertices[raw], fromCenter, -(LeftSideDistance + 1f))
            : Inset(p.Origin(raw), p.RightData.SidewalkInnerVertices[raw], p.RightData.SidewalkInnerVertices[raw], fromCenter, LeftSideDistance + 1f);

        Vector3 RightBoundPoint(Road p, int side, int raw, bool fromCenter) => side != 0
            ? Inset(p.Origin(raw), p.RightData.SidewalkInnerVertices[raw], p.RightData.SidewalkInnerVertices[raw], fromCenter, RightSideDistance + 1f)
            : Inset(p.Origin(raw), p.LeftData.SidewalkInnerVertices[raw], p.RightData.SidewalkInnerVertices[raw], fromCenter, -(RightSideDistance + 1f));

        // The exit variants normalise against the other sidewalk in one case each (as the original does).
        Vector3 RightExitPoint(Road p, int side, int raw, bool fromCenter) => side != 0
            ? RightBoundPoint(p, side, raw, fromCenter)
            : Inset(p.Origin(raw), p.LeftData.SidewalkInnerVertices[raw], p.LeftData.SidewalkInnerVertices[raw], fromCenter, RightSideDistance + 1f);

        Vector3 LeftExitPoint(Road p, int side, int raw, bool fromCenter) => side != 0
            ? Inset(p.Origin(raw), p.LeftData.SidewalkInnerVertices[raw], p.LeftData.SidewalkInnerVertices[raw], fromCenter, LeftSideDistance + 1f)
            : LeftBoundPoint(p, side, raw, fromCenter);

        /// <summary>atan2(lateral, forward) of d in the travel frame of path fp at travel vertex fv.</summary>
        static float FrameAngle(Vector3 d, Road fp, int fside, int fv, bool clampFwd, float latSnap)
        {
            float lat, fwd;
            if (fside != 0)
            {
                lat = -DotXZ(d, fp.OriX(fv));
                fwd = -DotXZ(d, fp.OriZ(fv));
            }
            else
            {
                int k = fp.NumSections - fv;
                if (k == fp.NumSections) k--;
                lat = DotXZ(d, fp.OriX(k));
                fwd = DotXZ(d, fp.OriZ(k));
            }
            if (!float.IsNaN(latSnap) && lat > -0.01f && lat < 0.0099999998f) lat = latSnap;
            if (clampFwd && fwd < 1f) fwd = 1f;
            return Mathf.Atan2(lat, fwd);
        }

        // =====================================================================
        //  CalcSharpTurnTarget - lays arc nodes around a turn. May advance n.
        //  Returns true once the car is past the first half of the arc.
        // =====================================================================

        bool CalcSharpTurnTarget(ref int n, int src, Vector3 prevPos) // prevPos unused in the original too
        {
            int code = ScratchRoute[src].StartPathIndex;
            return code < 2 ? RoadEndTurnTarget(ref n, src, code) : InPathSharpTurnTarget(ref n, src, code - 2);
        }

        /// <summary>Arc between SomePaths[k] and SomePaths[k+1] (built by CalcRoadTurns).</summary>
        bool RoadEndTurnTarget(ref int n, int src, int k)
        {
            var path = Paths[k];
            Vector3 X, Z;
            if (PathsSides[k] != 0)
            {
                X = path.OriX(path.NumSections - 1);
                Z = path.OriZ(path.NumSections - 1);
            }
            else
            {
                // decompile copies X before negating its z (same pattern as CalcTurnIntersection); assumed artifact
                X = -path.OriX(0);
                Z = -path.OriZ(0);
            }

            float dir = TurnDir[k];
            float radius = TurnRadius[k];
            Vector3 c = TurnCenter[k];
            Vector3 start = c + X * (radius * dir);

            // angle of the previous node around the centre, measured from the arc start
            Vector3 pp = ScratchRoute[n - 1].Position;
            float dx = pp.x - c.x, dz = pp.z - c.z;
            Vector3 s = TurnStartDir[k];
            float ang = Mathf.Atan2(Handedness * (s.x * dz - s.z * dx), s.z * dz + s.x * dx);

            ScratchRoute[n].StartPathIndex = (short)k;
            if (dir < 0f) ang = -ang;

            if (ang < -0.1f)
            {
                // not in the turn yet: aim at the arc start
                ref AiRouteNode nd = ref ScratchRoute[n];
                nd.Position = start;
                SaveTurnTarget(n, 1);
                nd.PathIndex = nd.StartPathIndex;
                nd.RoadVertexIndex = (short)(Paths[nd.PathIndex].NumSections - 1);
                nd.Kind = 0;
                return false;
            }

            float arc = dir * TurnAngle[k];
            int segs = (int)(radius * arc * 0.1f);
            segs += segs % 2;
            if (segs < 2) segs = 2;
            float step = arc / segs;

            for (int i = 1; i < segs + 1; i++)
            {
                float a = i * step;
                if (a - step * 0.5f <= ang) continue; // already past this arc point

                ref AiRouteNode nd = ref ScratchRoute[n];
                nd.Position = c - Z * (Mathf.Sin(a) * radius) + X * (Mathf.Cos(a) * radius * dir);
                SaveTurnTarget(n, 0);

                if (arc * 0.5f <= a)
                {
                    int np = k + 1;
                    nd.PathIndex = (short)np;
                    nd.RoadVertexIndex = (short)Paths[np].RoadVertice(nd.Position, PathsSides[np]);
                    if (nd.RoadVertexIndex >= Paths[np].NumSections)
                    {
                        nd.PathIndex++;
                        nd.RoadVertexIndex = 0;
                    }
                    if (np >= PathEndIndex) PastLastWayPt = true;
                }
                else
                {
                    nd.PathIndex = (short)k;
                    nd.RoadVertexIndex = (short)(Paths[k].NumSections - 1);
                }

                nd.StartPathIndex = (short)k;

                if (2 * i > segs && Paths[k + 1].IsPosOnRoad(nd.Position, 0f, out _) != RoadPosition.OffRoad)
                {
                    nd.StartPathIndex++;
                    RegisterRouteOriginalPos2 = nd.Position;
                    return true;
                }

                if (i < segs)
                {
                    if (n >= NodesPerRoute - 1) return true; // original has no guard and spills into CandidateRoutes
                    n++;
                }
            }
            return true;
        }

        /// <summary>Sharp turn t inside a single path (aiPath::SharpTurn*).</summary>
        bool InPathSharpTurnTarget(ref int n, int src, int t)
        {
            int pi = ScratchRoute[src].PathIndex;
            int side = PathsSides[pi];
            var path = Paths[pi];

            int vtx = path.RoadTurns[t].Vertex;
            float dir = path.SharpTurnDir(t, (RoadSide)side);
            float radius = path.RoadTurns[t].Radius;
            float angle = path.RoadTurns[t].Angle;

            int segs = (int)(angle * radius * dir * 0.1f);
            ScratchRoute[n].StartPathIndex = (short)pi;
            segs += segs % 2;
            if (segs < 2) segs = 2;

            float total = angle * dir;
            float step = total / segs;

            int ai = side != 0 ? vtx : path.NumSections - vtx - 1;
            Vector3 X = path.OriX(ai);
            Vector3 Z = path.OriZ(ai);
            Vector3 c = path.RoadTurns[t].Center;

            Vector3 ArcPoint(float a) => new Vector3(
                c.x - Z.x * Mathf.Sin(a) * radius + X.x * Mathf.Cos(a) * radius * dir,
                c.y,
                c.z - Z.z * Mathf.Sin(a) * radius + X.z * Mathf.Cos(a) * radius * dir);

            Vector3 start = side != 0 ? ArcPoint(0f) : ArcPoint(segs * step);

            Vector3 u = SafeNormalize(start - c);
            Vector3 pp = ScratchRoute[n - 1].Position;
            float dx = pp.x - c.x, dz = pp.z - c.z;
            float ang = Mathf.Atan2(Handedness * (u.x * dz - u.z * dx), dx * u.x + dz * u.z);

            if ((dir < 0f && side != 0) || (dir > 0f && side == 0))
                ang = -ang;

            if (ang < -0.1f)
            {
                ref AiRouteNode nd = ref ScratchRoute[n];
                nd.Position = start;
                nd.RoadVertexIndex = (short)path.RoadVertice(nd.Position, side);
                nd.PathIndex = ScratchRoute[src].PathIndex;
                SaveTurnTarget(n, 1);
                return false;
            }

            float half = step * 0.5f;
            for (int i = 1; i < segs + 1; i++)
            {
                if (i * step - half <= ang) continue;

                int idx = side != 0 ? i : segs - i;
                ref AiRouteNode nd = ref ScratchRoute[n];
                nd.Position = ArcPoint(idx * step);
                nd.PathIndex = (short)pi;
                nd.RoadVertexIndex = (short)path.RoadVertice(nd.Position, side);
                if (nd.RoadVertexIndex >= path.NumSections)
                {
                    nd.PathIndex++;
                    nd.RoadVertexIndex = 0;
                }
                SaveTurnTarget(n, 0);
                return i > 1f + segs * 0.5f;
            }

            // every arc point is behind us. Only a vertex index is written (to node n-1, via the AiStuck alias).
            ScratchRoute[n - 1].RoadVertexIndex = (short)path.GetPointSectionIndex(ArcPoint(total));
            return true;
        }

        // =====================================================================
        //  CalcRoadTarget - funnel / string-pulling along the road edges
        // =====================================================================

        void CalcRoadTarget(int n, Vector3 prevPosArg)
        {
            ref AiRouteNode prev = ref ScratchRoute[n - 1];
            ref AiRouteNode node = ref ScratchRoute[n];
            Vector3 prevPos = prev.Position; // original overwrites the argument with this
            float r1 = RightSideDistance + 1f;
            float l1 = LeftSideDistance + 1f;
            const float NaN = float.NaN;

            int pp = prev.PathIndex;
            var ppath = Paths[pp];
            // NOT from MM2: producers can leave RoadVertice's "past the end" value (NumSections) here
            int pv = Mathf.Clamp(prev.RoadVertexIndex, 0, ppath.NumSections - 1);
            int pside = PathsSides[pp];

            // ---- start vertex ----
            int vert = pv;
            int pi = pp;
            int count = Paths[pi].NumSections;

            if (n > 1 && ScratchRoute[n - 2].Kind != 1 && ++vert >= count)
            {
                if (Paths[pi + 1] != null)
                {
                    vert = 0;
                    pi++;
                    count = Paths[pi].NumSections;
                }
                else
                {
                    vert--;
                }
            }

            int axisIdx = count - vert;           // "a2a": side-0 axis index, reused later even after pi changes
            if (axisIdx == count) axisIdx--;

            var path = Paths[pi];
            int side = PathsSides[pi];
            int raw = RawVert(path, side, vert);

            // which half of the road the previous node is on (only matters for divided roads)
            float lateralSign = side != 0
                ? -DotXZ(prevPos - path.Origin(raw), path.OriX(raw))
                : DotXZ(prevPos - path.Origin(raw), path.OriX(raw));

            bool center = (path.Flags & PathFlags.Divided) != 0;

            // ---- initial funnel ----
            float angL = FrameAngle(LeftBoundPoint(path, side, raw, center && lateralSign >= 0f) - prevPos, path, side, vert, n == 1, NaN);
            float angR = FrameAngle(RightBoundPoint(path, side, raw, center && lateralSign < 0f) - prevPos, path, side, vert, n == 1, NaN);

            int bestLVert = vert, bestRVert = vert;   // v619 / v620
            int bestLPath = pi, bestRPath = pi;       // v615 / v613

            vert++;
            if (vert == path.NumSections)
            {
                if (Paths[pi + 1] != null)
                {
                    if (Mathf.Abs(TurnAngleAt(pi)) > TurnHeadingThreshold)
                    {
                        node.Position = TurnPosAt(pi);
                        vert--;
                        goto Finish;
                    }
                    if (pi + 1 == PathEndIndex)
                    {
                        vert--;
                    }
                    else
                    {
                        vert = 0;
                        pi++;
                        path = Paths[pi];
                    }
                }
                else
                {
                    vert--;
                }
            }

            // ---- provisional target: far end of the path at the car's lateral fraction ----
            {
                count = path.NumSections;
                side = PathsSides[pi];
                float maxOff = path.HalfWidth - RightSideDistance - 1f;
                float off;

                if (side == 0)
                {
                    int ax = Mathf.Min(axisIdx, count - 1); // original doesn't clamp
                    float rawOff = -DotXZ(path.Origin(count - vert - 1) - prevPos, path.OriX(ax));
                    if (n != 1) off = Mathf.Clamp(rawOff, -maxOff, maxOff);
                    else
                    {
                        if (vert > 0) RoadLateralOffset = Mathf.Clamp(rawOff, -maxOff, maxOff);
                        off = RoadLateralOffset;
                    }
                    Vector3 o = path.Origin(0);
                    Vector3 edge = path.LeftData.SidewalkInnerVertices[0] - path.OriX(0) * r1;
                    node.Position = o + (edge - o) * (off / maxOff);
                }
                else
                {
                    float rawOff = DotXZ(path.Origin(vert) - prevPos, path.OriX(vert));
                    if (n != 1) off = Mathf.Clamp(rawOff, -maxOff, maxOff);
                    else
                    {
                        if (vert > 0) RoadLateralOffset = Mathf.Clamp(rawOff, -maxOff, maxOff);
                        off = RoadLateralOffset;
                    }
                    int e = count - 1;
                    Vector3 o = path.Origin(e);
                    Vector3 edge = path.RightData.SidewalkInnerVertices[e] + path.OriX(vert) * r1;
                    node.Position = o + (edge - o) * (off / maxOff);
                }
            }

            if (vert >= path.NumSections) goto Exhausted;

            // ---- walk vertices, narrowing the funnel ----
            for (; ; )
            {
                path = Paths[pi];
                side = PathsSides[pi];
                raw = RawVert(path, side, vert);
                center = (path.Flags & PathFlags.Divided) != 0;

                // left edge
                float a = FrameAngle(LeftBoundPoint(path, side, raw, center && lateralSign >= 0f) - prevPos, ppath, pside, pv, true, -1f);
                if (a > angR)
                {
                    // left edge swung past the right bound: aim at the right constraint
                    var bp = Paths[bestRPath];
                    int bs = PathsSides[bestRPath];
                    node.Position = RightExitPoint(bp, bs, RawVert(bp, bs, bestRVert), (bp.Flags & PathFlags.Divided) != 0 && lateralSign < 0f);
                    if (side != 0 && bs != 0 && bestRVert == bp.NumSections - 1 && TurnAngleAt(bestRPath) > TurnHeadingThreshold)
                        node.Position = TurnPosAt(bestRPath);
                    vert = bestRVert;
                    pi = bestRPath;
                    goto Finish;
                }
                if (a > angL - 0.001f)
                {
                    angL = a;
                    bestLVert = vert;
                    bestLPath = pi;
                }

                // right edge
                float b = FrameAngle(RightBoundPoint(path, side, raw, center && lateralSign < 0f) - prevPos, ppath, pside, pv, true, 0f);
                if (b < angL)
                {
                    // right edge swung past the left bound: aim at the left constraint
                    var bp = Paths[bestLPath];
                    int bs = PathsSides[bestLPath];
                    node.Position = LeftExitPoint(bp, bs, RawVert(bp, bs, bestLVert), (bp.Flags & PathFlags.Divided) != 0 && lateralSign >= 0f);
                    if (side != 0 && bs != 0 && bestLVert == bp.NumSections - 1 && TurnAngleAt(bestLPath) < -TurnHeadingThreshold)
                        node.Position = TurnPosAt(bestLPath);
                    vert = bestLVert;
                    pi = bestLPath;
                    goto Finish;
                }
                if (b < angR + 0.001f)
                {
                    angR = b;
                    bestRVert = vert;
                    bestRPath = pi;
                }

                // end of this path: take the corner, or continue onto the next path
                if (vert == path.NumSections - 1)
                {
                    if (pi < 2)
                    {
                        bool allHere = vert == bestLVert && bestLPath == pi && vert == bestRVert && bestRPath == pi;
                        if (allHere && (TurnAngle[pi] < -TurnHeadingThreshold || TurnAngle[pi] > TurnHeadingThreshold))
                        {
                            node.Position = TurnPos[pi];
                            goto Finish;
                        }
                    }

                    if (Vector3.Distance(prevPos, node.Position) + prev.Distance >= LookAheadDistance || pi >= 2)
                        goto Exhausted;

                    if (pi + 1 >= PathEndIndex)
                    {
                        if (EndMode == 3)
                        {
                            SetTargetPtToDestination(n);
                            return;
                        }
                        PastLastWayPt = true;
                        goto Exhausted;
                    }

                    if (Paths[pi + 1] == null) goto Exhausted;

                    vert = -1;
                    pi++;
                    path = Paths[pi];
                    int c = path.NumSections;
                    float mo = path.HalfWidth - RightSideDistance - 1f;

                    // carry the provisional target's lateral fraction onto the new path's far end
                    if (PathsSides[pi] != 0)
                    {
                        float t = Mathf.Clamp(Vector3.Dot(path.Origin(0) - node.Position, path.OriX(0)), -mo, mo) / mo;
                        Vector3 edge = path.RightData.SidewalkInnerVertices[c - 1] + path.OriX(c - 1) * r1;
                        node.Position = Vector3.LerpUnclamped(path.Origin(c - 1), edge, t);
                    }
                    else
                    {
                        float t = Mathf.Clamp(Vector3.Dot(path.Origin(c - 1) - node.Position, -path.OriX(c - 1)), -mo, mo) / mo;
                        Vector3 edge = path.LeftData.SidewalkInnerVertices[0] - path.OriX(0) * r1;
                        node.Position = Vector3.LerpUnclamped(path.Origin(0), edge, t);
                    }
                }

                if (++vert >= Paths[pi].NumSections) goto Exhausted;
            }

            Exhausted:
            // ran out of road (or distance) without the funnel closing: pick a constraint that isn't at the path end
            {
                var rp = Paths[bestRPath];
                var lp = Paths[bestLPath];
                int rs = PathsSides[bestRPath];
                int ls = PathsSides[bestLPath];
                bool rBeforeEnd = bestRPath < pi || bestRVert < rp.NumSections - 1;
                bool lBeforeEnd = bestLPath < pi || bestLVert < lp.NumSections - 1;

                if (rs == 0 && rBeforeEnd)
                {
                    int idx = rp.NumSections - bestRVert - 1;
                    if ((rp.Flags & PathFlags.Divided) != 0 && lateralSign < 0f)
                        node.Position = Inset(rp.Origin(idx), rp.Origin(idx), rp.RightData.SidewalkInnerVertices[idx], true, -r1);
                    else
                        node.Position = rp.LeftData.SidewalkInnerVertices[idx] + CarriedRightOffset(ppath, pside, Mathf.Max(pv, 1), prevPos);
                    vert = bestRVert;
                    pi = bestRPath;
                    PastLastWayPt = false;
                    goto Finish;
                }

                if (ls == 0 && lBeforeEnd)
                {
                    int idx = lp.NumSections - bestLVert - 1;
                    if ((lp.Flags & PathFlags.Divided) != 0 && lateralSign > 0f)
                        node.Position = Inset(lp.Origin(idx), lp.Origin(idx), lp.RightData.SidewalkInnerVertices[idx], true, r1);
                    else
                        node.Position = lp.LeftData.SidewalkInnerVertices[idx] + CarriedRightOffset(ppath, pside, Mathf.Max(pv, 1), prevPos); // uses right-side logic, as original
                    vert = bestLVert;
                    pi = bestLPath;
                    PastLastWayPt = false;
                    goto Finish;
                }

                if (rs != 0 && rBeforeEnd)
                {
                    if ((rp.Flags & PathFlags.Divided) != 0 && lateralSign >= 0f)
                    {
                        node.Position = Inset(rp.Origin(bestRVert), rp.Origin(bestRVert), rp.RightData.SidewalkInnerVertices[bestRVert], true, r1);
                    }
                    else
                    {
                        float lat = Mathf.Max(PrevDistFromRightEdge(ppath, pside, pv, prevPos), RightSideDistance);
                        node.Position = rp.RightData.SidewalkInnerVertices[bestRVert] + rp.OriX(bestRVert) * lat;
                    }
                    vert = bestRVert;
                    pi = bestRPath;
                    PastLastWayPt = false;
                    goto Finish;
                }

                if (ls == 0 || !lBeforeEnd) goto Finish;

                if ((lp.Flags & PathFlags.Divided) != 0 && lateralSign < 0f)
                {
                    node.Position = lp.Origin(bestLVert) - lp.OriX(bestLVert) * l1;
                }
                else
                {
                    float lat = Mathf.Max(PrevDistFromLeftEdge(ppath, pside, pv, prevPos), LeftSideDistance);
                    node.Position = lp.LeftData.SidewalkInnerVertices[bestLVert] - lp.OriX(bestLVert) * lat;
                }
                vert = bestLVert;
                pi = bestLPath;
                PastLastWayPt = false;
            }

            Finish:
            node.Kind = 0;
            node.RoadVertexIndex = (short)vert;
            node.PathIndex = (short)pi;
            node.StartPathIndex = (short)pi;
            node.Position.y += 1f;
            node.Distance = Vector3.Distance(prev.Position, node.Position) + prev.Distance;

            if (n == 1)
                node.Angle = AngleRad(CarForward, node.Position - CarPos); // TODO: confirm the sub_567CA0 axis is forward
            else
                node.Angle = AngleRad(prev.Position - ScratchRoute[n - 2].Position, node.Position - prev.Position) + prev.Angle;
        }

        /// <summary>Previous node's offset in from its right edge, as a vector along that path's inward axis.</summary>
        static Vector3 CarriedRightOffset(Road p, int side, int v, Vector3 pos)
        {
            if (side != 0)
            {
                Vector3 ax = p.OriX(v);
                return ax * Vector3.Dot(pos - p.RightData.SidewalkInnerVertices[v], ax);
            }
            int k = p.NumSections - v;
            Vector3 axis = -p.OriX(k);
            return axis * Vector3.Dot(pos - p.LeftData.SidewalkInnerVertices[k - 1], axis); // index k vs k-1 mismatch is original
        }

        static float PrevDistFromRightEdge(Road p, int side, int v, Vector3 pos)
        {
            if (side != 0) return Vector3.Dot(pos - p.RightData.SidewalkInnerVertices[v], p.OriX(v));
            int k = p.NumSections - v - 1;
            return Vector3.Dot(pos - p.LeftData.SidewalkInnerVertices[k], -p.OriX(k));
        }

        static float PrevDistFromLeftEdge(Road p, int side, int v, Vector3 pos)
        {
            if (side != 0) return Vector3.Dot(pos - p.LeftData.SidewalkInnerVertices[v], -p.OriX(v));
            int k = p.NumSections - v - 1;
            return Vector3.Dot(pos - p.RightData.SidewalkInnerVertices[k], p.OriX(k));
        }

        // =====================================================================
        //  SaveTarget / ContinueCheck - commit a node, then recurse or finish
        // =====================================================================

        /// <summary>Writes an obstacle-avoidance point as node n and continues the enumeration.</summary>
        void SaveTarget(int n, in AvoidPoint point, int blockType)
        {
            ref AiRouteNode prev = ref ScratchRoute[n - 1];
            ref AiRouteNode node = ref ScratchRoute[n];

            // the obstacle reports which of the three planned paths (and which vertex) it sits on
            int pi = point.Target.CurrentRoadIdx(Paths, PathsSides, out int vert);

            if (pi < 0)
            {
                // not on the planned route at all: keep the previous node's placement
                pi = prev.PathIndex;
                vert = prev.RoadVertexIndex;
            }
            else if (Paths[pi] == null)
            {
                pi--;                                   // fall back to the previous path's last vertex
                if (pi < 0 || Paths[pi] == null)
                {
                    pi = prev.PathIndex;
                    vert = prev.RoadVertexIndex;
                }
                else
                {
                    vert = Paths[pi].NumSections - 1;
                }
            }

            var path = Paths[pi];
            vert = Mathf.Clamp(vert, 0, path.NumSections - 1);

            node.Kind = point.Kind;
            node.BlockType = (short)blockType;
            node.Blocker = point.Target;
            node.RoadPos = point.RoadPos;

            node.Position = point.Pos;
            node.Position.y += 1f;
            node.Distance = Mathf.Sqrt(DistSqXZ(prev.Position, node.Position)) + prev.Distance; // XZ here, unlike CalcRoadTarget

            if (n == 1)
                node.Angle = AngleRad(CarForward, node.Position - CarPos);
            else
                node.Angle = AngleRad(prev.Position - ScratchRoute[n - 2].Position, node.Position - prev.Position) + prev.Angle;

            node.RoadVertexIndex = (short)vert;
            node.PathIndex = (short)pi;

            ContinueCheck(n);
        }

        /// <summary>Either walks on to node n+1, or stores the scratch nodes as a finished candidate route.</summary>
        void ContinueCheck(int n)
        {
            ref AiRouteNode node = ref ScratchRoute[n];
            int pi = node.PathIndex;
            var path = Paths[pi];

            // still short of the look-ahead distance and out of planned road: keep going toward the destination
            bool atPlanEnd = path != null
                && (pi == PathEndIndex
                    || (node.RoadVertexIndex >= path.NumSections - 1 && pi + 1 <= 2 && Paths[pi + 1] == null));

            if (node.Distance < LookAheadDistance && atPlanEnd && ActiveRouteNodes < MaxActiveRoutes)
            {
                PastLastWayPt = true;
                EnumRoutes(n + 1);
                return;
            }

            var last = Paths[2];
            bool finished = node.Distance >= LookAheadDistance
                            || last == null
                            || (node.PathIndex == 2 && node.RoadVertexIndex == last.NumSections)
                            || ActiveRouteNodes >= MaxActiveRoutes;

            if (!finished)
            {
                EnumRoutes(n + 1);
                return;
            }

            // commit ScratchRoute[0..n] as candidate route ActiveRouteNodes
            for (int i = 0; i <= n; i++)
            {
                CandidateRoutes[ActiveRouteNodes, i] = ScratchRoute[i];
                if (ScratchRoute[i].RoadPos == 2) RouteUsesSidewalk[ActiveRouteNodes] = 1; // OnSidewalk
                if (ScratchRoute[i].Kind == 3) RouteBlocked[ActiveRouteNodes] = 1;         // drives through an obstacle
            }
            RouteNodeCount[ActiveRouteNodes] = n + 1;
            ActiveRouteNodes++;
        }

        // =====================================================================
        //  SaveTurnTarget
        // =====================================================================

        /// <summary>Running heading-change cost on node n (the route cost DetermineBestRoute minimises).</summary>
        void AccumulateAngleCost(int n)
        {
            ref AiRouteNode node = ref ScratchRoute[n];
            if (n == 1)
                node.Angle = AngleRad(CarForward, node.Position - CarPos);
            else
                node.Angle = AngleRad(ScratchRoute[n - 1].Position - ScratchRoute[n - 2].Position,
                                      node.Position - ScratchRoute[n - 1].Position) + ScratchRoute[n - 1].Angle;
        }

        /// <summary>Marks node n as a turn node and updates its distance; cost only when addCost is set.</summary>
        void SaveTurnTarget(int n, int addCost)
        {
            ref AiRouteNode prev = ref ScratchRoute[n - 1];
            ref AiRouteNode node = ref ScratchRoute[n];

            node.Kind = 1;
            node.Position.y += 1f;
            node.Distance = Mathf.Sqrt(DistSqXZ(prev.Position, node.Position)) + prev.Distance;

            if (addCost != 0) AccumulateAngleCost(n);
        }

        // =====================================================================
        //  IsTargetBlocked - nearest obstacle on the segment from -> to
        // =====================================================================

        /// <summary>
        /// Nearest obstacle blocking the segment from -> to. fromVert/toVert only decide how far along
        /// the path chain to look: MM2 kept per-section obstacle lists, RoadInstance keeps one per road.
        /// </summary>
        Obstacle IsTargetBlocked(Vector3 from, Vector3 to, int fromVert, int fromPath, int toVert, int toPath, float pad, out int blockType)
        {
            float width = LeftSideDistance + RightSideDistance;

            Obstacle found = null;
            float bestDist = 99999f;
            int foundType = 0;

            void Consider(IReadOnlyList<Obstacle> list)
            {
                if (list == null) return;
                for (int i = 0; i < list.Count; i++)
                {
                    var o = list[i];
                    int type;

                    if (o is BangerObstacle)
                    {
                        // props light enough to shove aside are driven through, not avoided
                        if (!AvoidProps || o.BreakThreshold <= 250000f) continue;
                        type = 5;
                    }
                    else if (o is VehicleProxyObstacle proxy)
                    {
                        if (o.IsCar(Car)) continue;   // never avoid ourselves

                        if (proxy.IsPlayer)
                        {
                            if (!AvoidPlayers) continue;
                            type = 0;
                        }
                        else
                        {
                            // opponents are ignored until we're a few waypoints into the race
                            if (!AvoidOpponents || WayPtIndex <= 2 || o.RacerId == Id) continue;
                            type = 2;
                        }
                    }
                    else
                    {
                        if (!AvoidTraffic) continue;
                        type = 1;
                    }

                    float d = o.IsBlockingTarget(from, to, pad, width);
                    if (d > -1f && d < bestDist) { bestDist = d; found = o; foundType = type; }
                }
            }

            int endPath = toPath;
            var endRoad = Paths[toPath];
            if (endRoad != null && toVert > endRoad.NumSections) endPath = toPath + 1;

            for (int pi = fromPath; pi <= endPath && found == null; pi++)
            {
                var path = Paths[pi];
                if (path == null) continue;

                // the intersection this path is entered from, then the road or shortcut itself
                int interId = PathsSides[pi] == 1 ? path.RightEndData.IntersectionID : path.LeftEndData.IntersectionID;
                if (interId >= 0 && interId < Net.Intersections.Count)
                    Consider(Net.Intersections[interId].Obstacles);

                Consider(InstanceOf(path)?.Obstacles);
            }

            blockType = foundType; // 0 player, 1 traffic, 2 opponent, 5 prop
            return found;
        }

        // =====================================================================
        //  CalcObstacleAvoidPoints - two candidate ways past an obstacle
        // =====================================================================

        int CalcObstacleAvoidPoints(Obstacle obstacle, int n, bool weirdAvoidFlag, AvoidPoint[] outPoints)
        {
            ref AiRouteNode prev = ref ScratchRoute[n - 1];
            ref AiRouteNode node = ref ScratchRoute[n];

            // already turning: keep the target we have
            if (prev.Kind == 1)
            {
                outPoints[0].Pos = node.Position;
                outPoints[0].Target = obstacle;
                outPoints[0].Kind = 1;
                outPoints[0].RoadPos = node.RoadPos;   // keep EnumRoutes' OnRoad stamp (MM2 left this unwritten)
                return 1;
            }

            Vector3 dir, from;
            if (n == 1)
            {
                dir = CarForward;
                from = ScratchRoute[0].Position;
            }
            else
            {
                dir = prev.Position - ScratchRoute[n - 2].Position;
                from = prev.Position;
            }
            obstacle.PreAvoid(from, dir, RightSideDistance + 2f, out Vector3 candA, out Vector3 candB);

            var path = Paths[prev.PathIndex];
            int side = PathsSides[prev.PathIndex];
            int pv = prev.RoadVertexIndex;
            int written = 0;

            // candidate A
            Vector3 d = candA - prev.Position;
            float ang;
            if (n == 1)
                ang = Mathf.Atan2(DotXZ(d, CarRight), DotXZ(d, CarForward));
            else if (side != 0)
                ang = Mathf.Atan2(-DotXZ(d, path.OriX(pv)), -DotXZ(d, path.OriZ(pv)));
            else
            {
                int k = path.NumSections - pv - 1;
                ang = Mathf.Atan2(DotXZ(d, path.OriX(k)), DotXZ(d, path.OriZ(k)));
            }
            if (ang > -MaxAvoidCandidateAngle && ang < MaxAvoidCandidateAngle)
                EnumTargets(candA, obstacle, n, prev.PathIndex, pv, -1, weirdAvoidFlag, 0, outPoints, ref written);

            // candidate B - the side branches are swapped here relative to A, as in the original
            d = candB - prev.Position;
            if (n == 1)
                ang = Mathf.Atan2(DotXZ(d, CarRight), DotXZ(d, CarForward));
            else if (side != 0)
            {
                int k = path.NumSections - pv - 1;
                ang = Mathf.Atan2(-DotXZ(d, path.OriX(k)), -DotXZ(d, path.OriZ(k)));
            }
            else
            {
                ang = Mathf.Atan2(DotXZ(d, path.OriX(pv)), DotXZ(d, path.OriZ(pv)));
            }
            if (ang > -MaxAvoidCandidateAngle && ang < MaxAvoidCandidateAngle)
                EnumTargets(candB, obstacle, n, prev.PathIndex, pv, 1, weirdAvoidFlag, 0, outPoints, ref written);

            if (written == 0)
            {
                // nothing usable: drive at the blocked target and mark the route (Kind 3 -> RouteBlocked)
                outPoints[0].Pos = node.Position;
                outPoints[0].Target = obstacle;
                outPoints[0].Kind = 3;
                outPoints[0].RoadPos = node.RoadPos;   // keep EnumRoutes' OnRoad stamp (MM2 left this unwritten)
                return 1;
            }
            return written;
        }

        // =====================================================================
        //  SetTargetPtToDestination - aim straight at the route end
        // =====================================================================

        void SetTargetPtToDestination(int n)
        {
            ref AiRouteNode prev = ref ScratchRoute[n - 1];
            ref AiRouteNode node = ref ScratchRoute[n];

            if (FinishRadius == 0f)
            {
                node.Position = RouteEndPos;
            }
            else
            {
                float dist = Mathf.Sqrt(DistSqXZ(prev.Position, RouteEndPos));
                node.Position = dist >= FinishRadius
                    ? prev.Position + (RouteEndPos - prev.Position) * ((dist - FinishRadius) / dist)
                    : prev.Position;
            }

            node.Position.y += 1f;

            // carry the previous node's road placement forward unchanged
            node.Kind = prev.Kind;
            node.RoadVertexIndex = prev.RoadVertexIndex;
            node.PathIndex = prev.PathIndex;
            node.StartPathIndex = prev.StartPathIndex;
            node.Distance = 9999f;              // the end-of-route sentinel Forward() tests for

            AccumulateAngleCost(n);
        }

        // =====================================================================
        //  EnumTargets - validate an avoid point, or recurse around the next obstacle
        // =====================================================================

        const int MaxAvoidDepth = 3;   // dword_5D8CF8 - TODO: real value from the exe

        int EnumTargets(Vector3 target, Obstacle obstacle, int n, int pathIdx, int vertIdx, int which,
                        bool weirdAvoidFlag, int depth, AvoidPoint[] outPoints, ref int written)
        {
            if (++depth == MaxAvoidDepth) return written;
            if (written >= outPoints.Length) return written; // NOT from MM2: keep within the buffer

            // the obstacle has to be on one of the planned paths, and the target on that road
            int pi = obstacle.CurrentRoadIdx(Paths, PathsSides, out int obstacleVert);
            if (pi < 0) return written;

            var path = Paths[pi];
            if (path == null) return written;

            var onRoad = path.IsPosOnRoad(target, RightSideDistance, out _);
            if (!(onRoad == RoadPosition.OnRoad || (weirdAvoidFlag && onRoad == RoadPosition.OnSidewalk))) return written;

            Vector3 from = ScratchRoute[n - 1].Position;
            float pad = (RearBumperDistance + FrontBumperDistance) * 2f;

            var blocker = IsTargetBlocked(from, target, vertIdx, pathIdx, obstacleVert + 2, pi, pad, out int blockType);

            if (blocker == null || blockType == 2)
            {
                // clear (or only an opponent in the way): take it
                outPoints[written].Pos = target;
                outPoints[written].Target = obstacle;
                outPoints[written].RoadPos = (short)onRoad;
                outPoints[written].Kind = 2;
                written++;
                return written;
            }

            // something else is in the way: ask it for its own two ways past
            Vector3 dir = n == 1 ? CarForward : from - ScratchRoute[n - 2].Position;
            Vector3 preFrom = n == 1 ? ScratchRoute[0].Position : from;
            blocker.PreAvoid(preFrom, dir, RightSideDistance + 2f, out Vector3 candA, out Vector3 candB);

            // if the two obstacles are far apart, the far-side candidate is worth taking directly
            if (DistSqXZ(obstacle.Position, blocker.Position) > 225f && written < outPoints.Length)
            {
                Vector3 other = which == -1 ? candB : candA;
                float ang = AvoidAngle(other - from, pathIdx, vertIdx);
                if (ang > -MaxAvoidCandidateAngle && ang < MaxAvoidCandidateAngle
                    && DistSqXZ(from, blocker.Position) < LookAheadDistance * LookAheadDistance
                    && blockType == 1)
                {
                    outPoints[written].Pos = other;
                    outPoints[written].Target = blocker;
                    outPoints[written].RoadPos = (short)onRoad;
                    outPoints[written].Kind = 2;
                    written++;
                }
            }

            // then keep going around on the same side
            Vector3 next = which == -1 ? candA : candB;
            float nextAng = AvoidAngle(next - from, pathIdx, vertIdx);
            if (nextAng > -MaxAvoidCandidateAngle && nextAng < MaxAvoidCandidateAngle)
                EnumTargets(next, blocker, n, pathIdx, vertIdx, which, weirdAvoidFlag, depth, outPoints, ref written);

            return written;
        }

        /// <summary>Angle of an avoid candidate in the frame of path pathIdx at vertex vertIdx.</summary>
        float AvoidAngle(Vector3 d, int pathIdx, int vertIdx)
        {
            var p = Paths[pathIdx];
            if (PathsSides[pathIdx] != 0)
                return Mathf.Atan2(-DotXZ(d, p.OriX(vertIdx)), -DotXZ(d, p.OriZ(vertIdx)));

            int k = p.NumSections - vertIdx - 1;
            return Mathf.Atan2(DotXZ(d, p.OriX(k)), DotXZ(d, p.OriZ(k)));
        }

        // =====================================================================
        //  CalcDestinationTarget - the funnel again, once past the last waypoint
        // =====================================================================

        void CalcDestinationTarget(int n, ref Vector3 from)
        {
            ref AiRouteNode prev = ref ScratchRoute[n - 1];
            ref AiRouteNode node = ref ScratchRoute[n];
            float l1 = LeftSideDistance + 1f;
            float r1 = RightSideDistance + 1f;

            int pp = prev.PathIndex;
            var ppath = Paths[pp];
            // NOT from MM2: producers can leave RoadVertice's "past the end" value (NumSections) here
            int pv = Mathf.Clamp(prev.RoadVertexIndex, 0, ppath.NumSections - 1);
            int pside = PathsSides[pp];

            int vert = pv, pi = pp;
            int count = Paths[pi].NumSections;

            if (n > 1 && ScratchRoute[n - 2].Kind != 1)
            {
                vert++;
                if (vert == count)
                {
                    if (Paths[pi + 1] != null) { vert = 0; pi++; count = Paths[pi].NumSections; }
                    else vert--;
                }
            }

            // only routes that end on a road get a funnel; anything else drives at the end point
            if (EndMode != 1 && EndMode != 2)
            {
                if (EndMode == 0 || EndMode == 3) SetTargetPtToDestination(n);
                return;
            }

            int axisIdx = count - vert;
            if (axisIdx == count) axisIdx--;

            from = prev.Position;

            var path = Paths[pi];
            int side = PathsSides[pi];
            bool divided = (path.Flags & PathFlags.Divided) != 0;

            // ---- initial funnel, in this path's own frame ----
            float angL, angR;
            {
                Vector3 lp, rp;
                if (side != 0)
                {
                    // note: the left point uses LeftSideDistance raw here, but +1 inside the loop
                    lp = (divided ? path.Origin(vert) : path.LeftData.SidewalkInnerVertices[vert])
                         - path.OriX(vert) * (divided ? l1 : LeftSideDistance);
                    rp = path.RightData.SidewalkInnerVertices[vert] + path.OriX(vert) * RightSideDistance;

                    Vector3 d = lp - from;
                    float lat = -DotXZ(d, path.OriX(vert));
                    if (lat > -0.01f && lat < 0.0099999998f) lat = -1f;
                    angL = Mathf.Atan2(lat, -DotXZ(d, path.OriZ(vert)));

                    d = rp - from;
                    lat = -DotXZ(d, path.OriX(vert));
                    if (lat > -0.01f && lat < 0.0099999998f) lat = 1f;
                    angR = Mathf.Atan2(lat, -DotXZ(d, path.OriZ(vert)));
                }
                else
                {
                    int idx = count - vert - 1;
                    Vector3 edge = path.RightData.SidewalkInnerVertices[idx];
                    Vector3 dir = SafeNormalize(path.Origin(idx) - edge);

                    lp = edge + dir * l1;
                    Vector3 d = lp - from;
                    float lat = DotXZ(d, path.OriX(axisIdx));
                    float fwd = DotXZ(d, path.OriZ(axisIdx));
                    if (lat > 0f && n == 1) { from = ScratchRoute[0].Position + path.OriX(axisIdx) * lat; lat = 0f; }
                    if (fwd < 1f && n == 1) fwd = 1f;
                    angL = Mathf.Atan2(lat, fwd);

                    rp = path.LeftData.SidewalkInnerVertices[idx] - dir * r1;
                    d = rp - from;
                    lat = DotXZ(d, path.OriX(axisIdx));
                    fwd = DotXZ(d, path.OriZ(axisIdx));
                    if (lat < 0f && n == 1) { from = ScratchRoute[0].Position - path.OriX(axisIdx) * lat; lat = 0f; }
                    if (fwd < 1f && n == 1) fwd = 1f;
                    angR = Mathf.Atan2(lat, fwd);
                }
            }

            int bestLVert = vert, bestRVert = vert;
            int bestLPath = pi, bestRPath = pi;
            bool found = false;

            vert++;
            if (vert == path.NumSections)
            {
                if (Paths[pi + 1] != null) { vert = 0; pi++; path = Paths[pi]; }
                else vert--;
            }

            // ---- walk vertices, narrowing ----
            while (vert < Paths[pi].NumSections)
            {
                path = Paths[pi];
                side = PathsSides[pi];
                divided = (path.Flags & PathFlags.Divided) != 0;
                int raw = side != 0 ? vert : path.NumSections - vert - 1;

                float a = DestFrameAngle(DestLeftPoint(path, side, raw, divided, l1) - from, ppath, pside, pv, -1f, side == 0);
                if (a > angR)
                {
                    node.Position = DestExitRight(bestRPath, bestRVert, side, n, pp, pv, r1, out vert);
                    pi = bestRPath;
                    found = true;
                    break;
                }
                if (a > angL - 0.001f) { angL = a; bestLVert = vert; bestLPath = pi; }

                float b = DestFrameAngle(DestRightPoint(path, side, raw, divided, r1) - from, ppath, pside, pv, 1f, side == 0);
                if (b < angL)
                {
                    node.Position = DestExitLeft(bestLPath, bestLVert, side, n, pp, pv, l1, out vert);
                    pi = bestLPath;
                    found = true;
                    break;
                }
                if (b < angR + 0.001f) { angR = b; bestRVert = vert; bestRPath = pi; }

                vert++;
            }

            if (!found)
            {
                vert = Mathf.Min(bestLVert, bestRVert);
                node.Position = DestFallback(bestLPath, bestLVert, bestRPath, bestRVert, prev.Position, r1, ref pi, ref vert);
            }

            // once we're at or past the vertex the destination sits on, just drive at it
            if (vert >= Paths[pi].RoadVertice(RouteEndPos, PathsSides[pi]))
            {
                SetTargetPtToDestination(n);
                return;
            }

            node.Kind = 0;
            node.RoadVertexIndex = (short)vert;
            node.PathIndex = (short)pi;
            node.StartPathIndex = (short)pi;
            node.Position.y += 1f;
            node.Distance = Mathf.Sqrt(DistSqXZ(prev.Position, node.Position)) + prev.Distance;
            AccumulateAngleCost(n);
        }

        static float DestFrameAngle(Vector3 d, Road fp, int fside, int fv, float snap, bool clampFwd)
        {
            float lat, fwd;
            if (fside != 0)
            {
                lat = -DotXZ(d, fp.OriX(fv));
                fwd = -DotXZ(d, fp.OriZ(fv));
            }
            else
            {
                int k = fp.NumSections - fv - 1;
                lat = DotXZ(d, fp.OriX(k));
                fwd = DotXZ(d, fp.OriZ(k));
            }
            if (lat > -0.01f && lat < 0.0099999998f) lat = snap;
            if (clampFwd && fwd < 1f) fwd = 1f;
            return Mathf.Atan2(lat, fwd);
        }

        Vector3 DestLeftPoint(Road p, int side, int raw, bool divided, float l1)
        {
            if (side != 0)
                return (divided ? p.Origin(raw) : p.LeftData.SidewalkInnerVertices[raw]) - p.OriX(raw) * l1;

            Vector3 edge = p.RightData.SidewalkInnerVertices[raw];
            return edge + SafeNormalize(p.Origin(raw) - edge) * l1;
        }

        Vector3 DestRightPoint(Road p, int side, int raw, bool divided, float r1)
        {
            if (side != 0)
                return p.RightData.SidewalkInnerVertices[raw] + p.OriX(raw) * r1;

            Vector3 edge = p.RightData.SidewalkInnerVertices[raw];
            return p.LeftData.SidewalkInnerVertices[raw] - SafeNormalize(p.Origin(raw) - edge) * r1;
        }

        /// <summary>Target at the right-hand constraint; nudges the vertex on if it's the one we're stood at.</summary>
        Vector3 DestExitRight(int bestPath, int bestVert, int curSide, int n, int pp, int pv, float r1, out int vert)
        {
            var p = Paths[bestPath];
            int v = bestVert;
            if (n > 1 && bestPath == pp && bestVert == pv)
                v = Mathf.Min(v + 1, p.NumSections - 1);
            vert = v;

            bool divided = (p.Flags & PathFlags.Divided) != 0;
            if (curSide != 0)
                return (divided ? p.Origin(v) : p.RightData.SidewalkInnerVertices[v]) + p.OriX(v) * r1;

            int idx = p.NumSections - v - 1;
            Vector3 edge = PathsSides[bestPath] != 0 ? p.RightData.SidewalkInnerVertices[idx] : p.LeftData.SidewalkInnerVertices[idx];
            return edge + SafeNormalize(p.Origin(idx) - edge) * r1;
        }

        /// <summary>Target at the left-hand constraint.</summary>
        Vector3 DestExitLeft(int bestPath, int bestVert, int curSide, int n, int pp, int pv, float l1, out int vert)
        {
            var p = Paths[bestPath];
            int v = bestVert;
            if (n > 1 && bestPath == pp && bestVert == pv)
                v = Mathf.Min(v + 1, p.NumSections - 1);
            vert = v;

            bool divided = (p.Flags & PathFlags.Divided) != 0;
            if (curSide != 0)
                return (divided ? p.Origin(v) : p.LeftData.SidewalkInnerVertices[v]) - p.OriX(v) * l1;

            int idx = p.NumSections - v - 1;
            Vector3 edge = p.RightData.SidewalkInnerVertices[idx];
            return edge + SafeNormalize(p.Origin(idx) - edge) * l1;
        }

        /// <summary>
        /// Funnel never closed: place the target by the road end's lateral offset, scaled across
        /// the remaining width. Mirrors the four cases in the original (best-right then best-left,
        /// side 0 then side 1).
        /// </summary>
        Vector3 DestFallback(int lPath, int lVert, int rPath, int rVert, Vector3 prevPos, float r1, ref int pi, ref int vert)
        {
            // best-right constraint first
            var p = Paths[rPath];
            if (PathsSides[rPath] == 0 && rVert < p.NumSections - 1)
            {
                int last = p.NumSections - 1;
                float max = p.HalfWidth - RightSideDistance - 1f;
                float lateral = Mathf.Clamp(-DotXZ(p.Origin(last) - prevPos, p.OriX(last)), -max, max);
                int idx = p.NumSections - rVert - 1;
                float spread = (p.HalfWidth - RightSideDistance - 1f) * (1f - lateral / max);
                pi = rPath;
                vert = rVert;
                return p.LeftData.SidewalkInnerVertices[idx] - p.OriX(idx) * (r1 + spread);
            }

            p = Paths[lPath];
            if (PathsSides[lPath] == 0 && lVert < p.NumSections - 1)
            {
                int last = p.NumSections - 1;
                float max = p.HalfWidth - RightSideDistance - 1f;
                float lateral = Mathf.Clamp(-DotXZ(p.Origin(last) - prevPos, p.OriX(last)), -max, max);
                int idx = p.NumSections - lVert - 1;
                float spread = (p.HalfWidth - LeftSideDistance - 1f) * (lateral / max + 1f);
                pi = lPath;
                vert = lVert;
                return p.RightData.SidewalkInnerVertices[idx] + p.OriX(idx) * (r1 + spread);
            }

            p = Paths[rPath];
            if (PathsSides[rPath] != 0 && rVert < p.NumSections - 1)
            {
                float max = p.HalfWidth - RightSideDistance - 1f;
                float lateral = Mathf.Clamp(DotXZ(p.Origin(0) - prevPos, p.OriX(0)), -max, max);
                float spread = (p.HalfWidth - RightSideDistance - 1f) * (1f - lateral / max);
                pi = rPath;
                vert = rVert;
                return p.RightData.SidewalkInnerVertices[rVert] + p.OriX(rVert) * (r1 + spread);
            }

            p = Paths[lPath];
            if (PathsSides[lPath] != 0 && lVert < p.NumSections - 1)
            {
                float max = p.HalfWidth - LeftSideDistance - 1f;
                float lateral = Mathf.Clamp(DotXZ(p.Origin(0) - prevPos, p.OriX(0)), -max, max);
                pi = lPath;
                vert = lVert;

                if ((p.Flags & PathFlags.Divided) != 0)
                    return p.Origin(lVert) - p.OriX(lVert) * (LeftSideDistance + 1f);

                float spread = (p.HalfWidth - LeftSideDistance - 1f) * (1f - lateral / max);
                return p.LeftData.SidewalkInnerVertices[lVert] - p.OriX(lVert) * (LeftSideDistance + 1f + spread);
            }

            return ScratchRoute[vert].Position; // nothing applied; keep whatever we had
        }

        // =====================================================================
        //  Reset
        // =====================================================================

        /// <summary>
        /// aiVehiclePhysics::Reset. Puts the AI back into a fresh Forward state. LastState = -1
        /// forces InitForward on the next tick, which clears the route counts and flags.
        /// Call RegisterRoute again before driving: the placeholder waypoint state leaves no paths.
        /// </summary>
        public void Reset()
        {
            LastState = (VehiclePhysicsState)(-1);
            State = VehiclePhysicsState.Forward;   // 0
            damagedOut = false;

            Car.Reset();                            // vehCar::Reset - TODO: confirm VehCar has an equivalent

            PastLastWayPt = false;                  // LOWORD(unknown38500)
            WayPtIndex = 1;
            CurLap = 1;
            CurMapCompIdx = 1;
            NumWayPts = 1;
            CurMapCompType = CompType.Intersection; // 3
            TCPO = 0;
            CurrentRouteNode = -1;

            Brake = 0f;
            Throttle = 0f;
            Steering = 0f;

            // SomePaths[3] (the "last [0]" cache) is left alone, as in the original
            Paths[0] = null;
            Paths[1] = null;
            Paths[2] = null;

            for (int i = 0; i < NodesPerRoute; i++)
                ScratchRoute[i].Reset();

            for (int r = 0; r < MaxRoutes; r++)
                for (int i = 0; i < NodesPerRoute; i++)
                    CandidateRoutes[r, i].Reset();

            AiStuck?.Reset();

            // this->Timer.StartTime: the only timer this port has is the damaged-out one
            damagedOutStart = Time.time;

            // aiVehicle::Reset - base-class reset. TODO: call it once aiVehicle is ported
        }

        // =====================================================================
        //  Gizmos
        // =====================================================================

        [Header("Gizmos")]
        public bool DrawRouteGizmos = true;
        public bool DrawCandidateRoutes = true;
        public bool DrawPlannedPaths = true;
        public bool DrawRoadTurns = true;

        public Color ChosenRouteColor = Color.green;
        public Color CandidateRouteColor = new Color(1f, 1f, 1f, 0.25f);
        public Color RejectedRouteColor = new Color(1f, 0.3f, 0.2f, 0.35f);
        public Color PreferredRouteColor = new Color(0.3f, 0.6f, 1f, 0.45f);

        void OnDrawGizmos()
        {
            if (!DrawRouteGizmos || Car == null) return;

            Vector3 carPos = CarPos;

            // ---- the three planned paths ahead ----
            if (DrawPlannedPaths)
            {
                for (int i = 0; i < 3; i++)
                {
                    var path = Paths[i];
                    if (path == null) continue;

                    // fades out along the plan: current path brightest
                    Gizmos.color = new Color(1f, 0.85f, 0.2f, 0.7f - i * 0.2f);
                    for (int v = 0; v < path.NumSections - 1; v++)
                        Gizmos.DrawLine(path.Origin(v) + Vector3.up, path.Origin(v + 1) + Vector3.up);

                    if (!DrawRoadTurns || path.RoadTurns == null) continue;

                    foreach (var t in path.RoadTurns)
                    {
                        Gizmos.color = t.Dir < 0f ? Color.cyan : Color.yellow;
                        Gizmos.DrawWireSphere(t.Intersection, 0.6f);          // apex
                        if (t.Radius > 0f)
                        {
                            Gizmos.DrawWireSphere(t.Center, 0.4f);            // arc centre
                            Gizmos.DrawLine(t.Center, t.Center + t.StartDir * t.Radius);
                            Gizmos.DrawLine(t.Center, t.Center + t.EndDir * t.Radius);
                        }
                    }
                }

                // the road-end turns between the planned paths
                for (int i = 0; i < 2; i++)
                {
                    if (TurnDir[i] == 0f) continue;
                    Gizmos.color = new Color(1f, 0.5f, 0f, 0.8f);
                    Gizmos.DrawWireSphere(TurnPos[i], 0.8f);
                    if (TurnRadius[i] > 0f)
                    {
                        Gizmos.DrawWireSphere(TurnCenter[i], 0.4f);
                        Gizmos.DrawLine(TurnCenter[i], TurnCenter[i] + TurnStartDir[i] * TurnRadius[i]);
                        Gizmos.DrawLine(TurnCenter[i], TurnCenter[i] + TurnEndDir[i] * TurnRadius[i]);
                    }
                }
            }

            // ---- every route DetermineBestRoute had to choose from ----
            if (DrawCandidateRoutes)
            {
                for (int r = 0; r < ActiveRouteNodes && r < MaxRoutes; r++)
                {
                    if (r == CurrentRouteNode) continue;   // drawn last, on top
                    DrawRoute(r, RouteColor(r), carPos);
                }
            }

            // ---- the one it picked ----
            if (CurrentRouteNode >= 0 && CurrentRouteNode < ActiveRouteNodes)
                DrawRoute(CurrentRouteNode, ChosenRouteColor, carPos);

            // ---- where the car is actually steering ----
            Gizmos.color = Color.magenta;
            Gizmos.DrawSphere(TargetPt, 0.35f);
            Gizmos.DrawLine(carPos + Vector3.up * 0.5f, TargetPt);

#if UNITY_EDITOR
            if (ActiveRouteNodes > 0)
            {
                string label = $"{State}  routes {ActiveRouteNodes}  chosen {CurrentRouteNode}";
                if (CurrentRouteNode >= 0 && CurrentRouteNode < ActiveRouteNodes)
                    label += $"  cost {RouteCost(CurrentRouteNode):F2}";
                UnityEditor.Handles.Label(carPos + Vector3.up * 4f, label);
            }
#endif
        }

        Color RouteColor(int r)
        {
            if (RouteBlocked[r] != 0) return RejectedRouteColor;       // a node drives through an obstacle
            if (RouteUsesSidewalk[r] != 0) return PreferredRouteColor; // preferred when Reckless
            return CandidateRouteColor;
        }

        void DrawRoute(int r, Color color, Vector3 carPos)
        {
            int count = RouteNodeCount[r];
            if (count <= 0) return;

            Gizmos.color = color;
            Vector3 previous = carPos + Vector3.up * 0.5f;

            for (int k = 0; k < count; k++)
            {
                Vector3 p = CandidateRoutes[r, k].Position;
                Gizmos.DrawLine(previous, p);
                Gizmos.DrawWireSphere(p, 0.2f);

                // nodes placed to dodge something get a marker
                if (CandidateRoutes[r, k].Blocker != null)
                {
                    Gizmos.DrawWireCube(p, Vector3.one * 0.5f);
                    Gizmos.DrawLine(p, CandidateRoutes[r, k].Blocker.Position);
                }

                previous = p;
            }
        }
    }
}