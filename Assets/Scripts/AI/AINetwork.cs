using PSDL;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace MM2.AI
{
    // / <summary>
    // / Structure containing the basic data needed to position an entity on a road
    // / </summary>
    public struct RoadPositioningInfo
    {
        public RoadInstance RoadInstance;
        public RoadSide SideOfRoad;
        public int RailIndex;
        public int Relation; // used for e.g. relation to the next/prev road
        public RoadData RoadData => RoadInstance == null ? null : (SideOfRoad > 0 ? RoadInstance.Road.RightData : RoadInstance.Road.LeftData);

        public static RoadPositioningInfo Null => new RoadPositioningInfo() { RoadInstance = null };
    }

    public class AINetwork : MonoBehaviour
    {
        public SDLCity Level => level;
        private SDLCity level;

        public AINetworkData Data => networkData;
        private AINetworkData networkData;

        public AIMap AIMap => aiMap;
        private AIMap aiMap;

        public AIRouter Router => router;
        private AIRouter router;

        [Header("Gizmos")]
        [SerializeField] private bool drawEntityGizmos = false;
        [SerializeField] private bool drawActiveCullRoadGizmos = false;
        [SerializeField] private Color trafficGizmoColor = new Color(0f, 0.6f, 1f, 0.35f);
        [SerializeField] private Color pedestrianGizmoColor = new Color(1f, 0.8f, 0f, 0.35f);
        [SerializeField] private Color proxyGizmoColor = new Color(1.0f, 0.0f, 0.0f, 0.35f);
        [SerializeField] private Color cableCarGizmoColor = new Color(1.0f, 0.647f, 0.0f, 0.35f);
        [SerializeField] private Color forwardGizmoColor = Color.green;

        // police force
        public PoliceForce PoliceForce => policeForce;
        private PoliceForce policeForce;

        // ai dvehicle data holder
        public AIVehicleDataManager VehicleDataManager => vehicleDataManager;
        private AIVehicleDataManager vehicleDataManager = new AIVehicleDataManager();

        // sim data
        public IReadOnlyList<RoadInstance> Roads => roads;
        public IReadOnlyList<RoadInstance> Shortcuts => shortcuts;
        public IReadOnlyList<IntersectionInstance> Intersections => intersections;

        private readonly List<RoadInstance> roads = new List<RoadInstance>();
        private readonly List<RoadInstance> shortcuts = new List<RoadInstance>();
        private readonly List<IntersectionInstance> intersections = new List<IntersectionInstance>();

        // maps
        private readonly Dictionary<int, List<AIComponent>> componentMap = new Dictionary<int, List<AIComponent>>();

        // race parameters, these should probably live somewhere else
        public MMGameMode GameMode = MMGameMode.Cruise;
        public int NumLaps = 4;

        // statics
        public static bool DrawActiveCullroomBound = true;
        public static bool DrawCapacityDebug = false;

        // entities
        public IReadOnlyList<AIPoliceOfficer> PoliceCars => policeCars;
        public IReadOnlyList<AIRouteRacer> Opponents => opponents;
        public List<AIVehicleProxy> VehicleProxies = new List<AIVehicleProxy>();
        
        private List<AIPoliceOfficer> policeCars = new List<AIPoliceOfficer>();
        private List<AIRouteRacer> opponents = new List<AIRouteRacer>();
        private AITrafficCar[] trafficCarPool = Array.Empty<AITrafficCar>();
        private AIPedestrian[] pedestrianPool = Array.Empty<AIPedestrian>();
        private List<AICableCar> cableCars = new List<AICableCar>();

        // repositioining data
        private const float LaneEndMargin = 5f;   // CenterLength - 5
        private const float MetresPerCar = 8f;    // numCars = length * density / 8

        private static readonly HashSet<int> EmptyRoadSet = new HashSet<int>();

        private readonly HashSet<int> populatedTrafficRoads = new HashSet<int>();
        private readonly HashSet<int> populatedPedRoads = new HashSet<int>();
        
        private readonly List<int> roadScratch = new List<int>();
        private readonly List<Lane> laneScratch = new List<Lane>();
        private readonly List<AIRailEntity> entityScratch = new List<AIRailEntity>();

        private readonly List<int> arrivedRoads = new List<int>();
        private readonly List<Vector2Int> pedSides = new List<Vector2Int>(); // x = start in laneScratch, y = lane count
        private int pedLimit = int.MaxValue;
        private float trafficDensity = 1f;

        // map component
        // map component
        // Component types match the exe: None = 0, Road = 1, Shortcut = 2, Intersection = 3
        public const int MaxMapComponents = 5;   // PositionToAIMapComp output cap

        private static readonly List<AIComponent> EmptyComponentList = new List<AIComponent>();

        // exe: lvlLevel::FindRoomId(pos, roomHint). Swap in a hinted lookup here if SDLCity gets one.
        private int FindRoomId(Vector3 pos, int roomHint)
        {
            return level.FindRoomIdWithWarpsCheckMiss(pos, roomHint);
        }

        public IReadOnlyList<AIComponent> GetRoomComponents(int room)
        {
            return componentMap != null && componentMap.TryGetValue(room, out var list) ? list : EmptyComponentList;
        }

        // exe: aiPath::IsPosOnRoad(path, pos, 0.0, &lateral) < 3
        private bool IsOnPath(int pathId, Vector3 pos)
        {
            return GetRoadInstance(pathId).Road.IsPosOnRoad(pos, 0f, out _) != RoadPosition.OffRoad;
        }

        /// aiMap::CoreMapComponent
        /// wanted road -> first intersection -> first road the position is on.
        /// Shortcuts are never considered. On failure, type = None and id is left untouched.
        public int CoreMapComponent(Vector3 pos, ref int id, out CompType type, int roomHint, int wantedId)
        {
            int room = FindRoomId(pos, roomHint);
            var list = GetRoomComponents(room);

            if (wantedId >= 0)
            {
                foreach (var c in list)
                {
                    if (c.Id == wantedId && c.Type == CompType.Road)
                    {
                        id = c.Id; type = c.Type;
                        return room;
                    }
                }
            }

            foreach (var c in list)
            {
                if (c.Type == CompType.Intersection)
                {
                    id = c.Id; type = c.Type;
                    return room;
                }
            }

            foreach (var c in list)
            {
                if (c.Type == CompType.Road && IsOnPath(c.Id, pos))
                {
                    id = c.Id; type = c.Type;
                    return room;
                }
            }

            type = CompType.None;
            return room;
        }

        /// aiMap::MapComponent (5 args)
        /// First road OR intersection in list order wins, and roads are NOT position-checked here.
        /// Otherwise the first shortcut the position is on. On failure, type = None and id = room.
        public int MapComponent(Vector3 pos, out int id, out CompType type, int roomHint)
        {
            int room = FindRoomId(pos, roomHint);
            var list = GetRoomComponents(room);

            foreach (var c in list)
            {
                if (c.Type == CompType.Road || c.Type == CompType.Intersection)
                {
                    id = c.Id; type = c.Type;
                    return room;
                }
            }

            foreach (var c in list)
            {
                if (c.Type == CompType.Shortcut && IsOnPath(c.Id, pos))
                {
                    id = c.Id; type = c.Type;
                    return room;
                }
            }

            id = room;
            type = CompType.None;
            return room;
        }

        /// aiMap::MapComponent (6 args)
        /// wanted road -> wanted shortcut -> first intersection -> first road/shortcut the position is on.
        /// On failure, type = None and id is left untouched.
        public int MapComponent(Vector3 pos, ref int id, out CompType type, int roomHint, int wantedId)
        {
            int room = FindRoomId(pos, roomHint);
            var list = GetRoomComponents(room);

            if (wantedId >= 0)
            {
                foreach (var c in list)
                {
                    if (c.Id == wantedId && c.Type == CompType.Road)
                    {
                        id = c.Id; type = c.Type;
                        return room;
                    }
                }
                foreach (var c in list)
                {
                    if (c.Id == wantedId && c.Type == CompType.Shortcut)
                    {
                        id = c.Id; type = c.Type;
                        return room;
                    }
                }
            }

            foreach (var c in list)
            {
                if (c.Type == CompType.Intersection)
                {
                    id = c.Id; type = c.Type;
                    return room;
                }
            }

            foreach (var c in list)
            {
                if ((c.Type == CompType.Road || c.Type == CompType.Shortcut) && IsOnPath(c.Id, pos))
                {
                    id = c.Id; type = c.Type;
                    return room;
                }
            }

            type = CompType.None;
            return room;
        }

        /// aiMap::MapComponentType
        /// First intersection or road in list order, shortcuts skipped. Otherwise None with id = room.
        public CompType MapComponentType(int room, out int id)
        {
            foreach (var c in GetRoomComponents(room))
            {
                if (c.Type == CompType.Intersection || c.Type == CompType.Road)
                {
                    id = c.Id;
                    return c.Type;
                }
            }

            id = room;
            return CompType.None;
        }

        /// aiMap::PositionToAIMapComp
        /// Can return several candidates (up to MaxMapComponents); ids/types must hold at least that many.
        /// room is both the lookup hint and the output room. Always returns at least 1.
        public int PositionToAIMapComp(Vector3 pos, int[] ids, CompType[] types, ref int room, int wantedRoadId = -1)
        {
            room = FindRoomId(pos, room);
            var list = GetRoomComponents(room);

            // wanted road or shortcut
            if (wantedRoadId >= 0)
            {
                foreach (var c in list)
                {
                    if (c.Id == wantedRoadId && (c.Type == CompType.Road || c.Type == CompType.Shortcut))
                    {
                        ids[0] = c.Id; types[0] = c.Type;
                        return 1;
                    }
                }
            }

            // empty room
            if (list.Count == 0)
            {
                ids[0] = room; types[0] = CompType.None;
                return 1;
            }

            // a single non-shortcut component is trusted without a position check
            if (list.Count == 1 && list[0].Type != CompType.Shortcut)
            {
                ids[0] = list[0].Id; types[0] = list[0].Type;
                return 1;
            }

            foreach (var c in list)
            {
                if (c.Type == CompType.Intersection)
                {
                    ids[0] = c.Id; types[0] = c.Type;
                    return 1;
                }
            }

            // collect every road/shortcut the position is near
            int found = 0;
            foreach (var c in list)
            {
                if (!IsNearPathSections(GetRoadInstance(c.Id).Road, pos))
                    continue;

                ids[found] = c.Id;
                types[found] = c.Type;
                if (++found >= MaxMapComponents)
                    return found;
            }

            if (found == 0)
            {
                ids[0] = room; types[0] = CompType.None;
                return 1;
            }
            return found;
        }

        public int PositionToAIMapComp(Vector3 pos, List<AIComponent> results, ref int room, int wantedRoadId = -1)
        {
            results.Clear();

            room = FindRoomId(pos, room);
            var list = GetRoomComponents(room);

            // wanted road or shortcut
            if (wantedRoadId >= 0)
            {
                foreach (var c in list)
                {
                    if (c.Id == wantedRoadId && (c.Type == CompType.Road || c.Type == CompType.Shortcut))
                    {
                        AddResult(results, c.Id, c.Type);
                        return results.Count;
                    }
                }
            }

            // empty room
            if (list.Count == 0)
            {
                AddResult(results, room, CompType.None);
                return results.Count;
            }

            // a single non-shortcut component is trusted without a position check
            if (list.Count == 1 && list[0].Type != CompType.Shortcut)
            {
                AddResult(results, list[0].Id, list[0].Type);
                return results.Count;
            }

            foreach (var c in list)
            {
                if (c.Type == CompType.Intersection)
                {
                    AddResult(results, c.Id, c.Type);
                    return results.Count;
                }
            }

            // collect every road/shortcut the position is near
            foreach (var c in list)
            {
                if (!IsNearPathSections(GetRoadInstance(c.Id).Road, pos))
                    continue;

                AddResult(results, c.Id, c.Type);
                if (results.Count >= MaxMapComponents)
                    return results.Count;
            }

            if (results.Count == 0)
                AddResult(results, room, CompType.None);

            return results.Count;
        }

        private static void AddResult(List<AIComponent> results, int id, CompType type)
        {
            results.Add(new AIComponent { Id = id, Type = type });
        }

        // The inline section test from PositionToAIMapComp: sections 1..count-1 (section 0 is skipped),
        // in front of the section plane and within 2 * halfWidth laterally.
        // Plain dot products of two mirrored vectors, so no sign flip is needed after the import.
        private static bool IsNearPathSections(Road road, Vector3 pos)
        {
            for (int v = 1; v < road.NumSections; v++)
            {
                Vector3 o = road.Origin(v);
                float dx = pos.x - o.x;
                float dz = pos.z - o.z;

                Vector3 oz = road.OriZ(v);
                Vector3 ox = road.OriX(v);

                if (dz * oz.z + dx * oz.x > -0.1f &&
                    road.HalfWidth + road.HalfWidth > Mathf.Abs(dz * ox.z + dx * ox.x))
                    return true;
            }
            return false;
        }

        // convenience wrapper, used by InitObstacles
        public bool MapComponent(Vector3 location, out AIComponent component)
        {
            MapComponent(location, out int id, out var type, 0);
            component = new AIComponent(type, id);
            return type != CompType.None;
        }

        // repositioning
        private void AdjustAmbients()
        {
            if (networkData == null) return;
            AdjustTraffic();
            AdjustPedestrians();
        }

        /// Diffs 'populated' against the new cull list: clears roads that left, collects roads that arrived.
        private void SyncRoadSet(HashSet<int> populated, HashSet<int> cullRoads,
                                 RailType railType, AmbientTypeFlags flags, List<int> arrived)
        {
            var next = cullRoads ?? EmptyRoadSet;

            roadScratch.Clear();
            foreach (var id in populated)
                if (!next.Contains(id))
                    roadScratch.Add(id);

            foreach (var id in roadScratch)
            {
                ClearRoad(id, railType, flags);
                populated.Remove(id);
            }

            arrived.Clear();
            foreach (var id in next)
                if (populated.Add(id))
                    arrived.Add(id);
        }

        private void AdjustTraffic()
        {
            if (trafficCarPool == null) return;

            SyncRoadSet(populatedTrafficRoads,
                        activeCullingRoom > 0 ? networkData.CombinedCullRoads[activeCullingRoom - 1] : null,
                        RailType.Vehicle, AmbientTypeFlags.Vehicles, arrivedRoads);

            laneScratch.Clear();
            float totalLength = 0f;
            foreach (var id in arrivedRoads)
            {
                if (RoadHasProxy(id)) continue;

                var road = Roads[id];
                float usable = road.Road.Length - LaneEndMargin;
                if (usable <= 0f) continue;

                for (int s = 0; s < 2; s++)
                {
                    foreach (var lane in road.GetLanes((RoadSide)s, RailType.Vehicle, AmbientTypeFlags.Vehicles))
                    {
                        laneScratch.Add(lane);
                        totalLength += usable;
                    }
                }
            }
            if (laneScratch.Count == 0) return;

            int count = Mathf.FloorToInt(totalLength * trafficDensity / MetresPerCar);
            if (count <= 0) return;
            float spacing = totalLength / (count + 1);

            int poolCursor = 0;
            float next = spacing;
            float laneStart = 0f;
            foreach (var lane in laneScratch)
            {
                float usable = lane.Instance.Road.Length - LaneEndMargin;
                while (next <= laneStart + usable)
                {
                    var car = NextFree(trafficCarPool, ref poolCursor, trafficCarPool.Length);
                    if (car == null) return;

                    PlaceOnLane(car, lane, next - laneStart);
                    next += spacing;
                }
                laneStart += usable;
            }
        }

        private void AdjustPedestrians()
        {
            if (pedestrianPool == null) return;

            SyncRoadSet(populatedPedRoads,
                        activeCullingRoom > 0 ? networkData.PedestrianCullRoads[activeCullingRoom - 1] : null,
                        RailType.Pedestrian, AmbientTypeFlags.Pedestrians, arrivedRoads);

            // One entry per road side that allows peds: left then right, in road order.
            laneScratch.Clear();
            pedSides.Clear();
            foreach (var id in arrivedRoads)
            {
                AddPedSide(Roads[id], RoadSide.Left);
                AddPedSide(Roads[id], RoadSide.Right);
            }
            if (pedSides.Count == 0) return;

            // Deal free peds round-robin, one per side per lap. No budget: the whole free pool goes out,
            // except that a single new road only gets one lap (the a4a == a3c exit in the original).
            bool multiLap = arrivedRoads.Count > 1;
            int poolCursor = 0;
            var ped = NextFree(pedestrianPool, ref poolCursor, pedLimit);

            do
            {
                for (int i = 0; i < pedSides.Count && ped != null; i++)
                {
                    var side = pedSides[i];
                    var lane = laneScratch[side.x + UnityEngine.Random.Range(0, side.y)];

                    // Stand-in for aiPedestrian::Reset, which picks the spot itself.
                    PlaceOnLane(ped, lane, UnityEngine.Random.Range(0f, lane.Instance.Road.Length));
                    ped = NextFree(pedestrianPool, ref poolCursor, pedLimit);
                }
            }
            while (multiLap && ped != null);
        }

        private void AddPedSide(RoadInstance road, RoadSide side)
        {
            int start = laneScratch.Count;
            foreach (var lane in road.GetLanes(side, RailType.Pedestrian, AmbientTypeFlags.Pedestrians))
                laneScratch.Add(lane);

            if (laneScratch.Count > start)
                pedSides.Add(new Vector2Int(start, laneScratch.Count - start));
        }

        private static AIRailEntity NextFree(AIRailEntity[] pool, ref int cursor, int limit)
        {
            int end = Mathf.Min(limit, pool.Length);
            while (cursor < end)
            {
                var e = pool[cursor++];
                if (e != null && !e.Active)
                    return e;
            }
            return null;
        }

        private void ClearRoad(int roadId, RailType railType, AmbientTypeFlags flags)
        {
            var road = Roads[roadId];
            for (int s = 0; s < 2; s++)
            {
                foreach (var lane in road.GetLanes((RoadSide)s, railType, flags))
                {
                    // Copy first: Deactivate removes from lane.Ordered.
                    entityScratch.Clear();
                    for (int i = 0; i < lane.Ordered.Count; i++)
                    {
                        // Only pool entities; proxies stay put.
                        if (lane.Ordered[i] is AITrafficCar car)
                            entityScratch.Add(car);
                        else if (lane.Ordered[i] is AIPedestrian ped)
                            entityScratch.Add(ped);
                    }
                    foreach (var e in entityScratch)
                        e.Deactivate();
                }
            }
        }

        private static void PlaceOnLane(AIRailEntity e, Lane lane, float dist)
        {
            float length = lane.Instance.Road.Length;
            dist = Mathf.Clamp(dist, 0f, length - LaneEndMargin - e.FrontBumperDistance);

            e.NormalizedPathProgress = dist / length; // before SetRoad: Lane.Insert sorts on it
            e.SetRoad(new RoadPositioningInfo
            {
                RoadInstance = lane.Instance,
                SideOfRoad = lane.Side,
                RailIndex = lane.RailIndex
            });
            e.PositionAlongPath(e.NormalizedPathProgress);
            e.Activate();
        }

        /// Entities that have fully driven onto a road outside the bubble go back to the pool.
        /// Cars mid-intersection have no RoadInfo, so they're left until they're past it.
        private static void RetireStrays(AIRailEntity[] pool, HashSet<int> populated)
        {
            if (pool == null) return;
            foreach (var e in pool)
            {
                if (e == null || !e.Active) continue;
                var road = e.RoadInfo.RoadInstance;
                if (road != null && !populated.Contains(road.Id))
                    e.Deactivate();
            }
        }

        private static void DeactivateAll(AIRailEntity[] pool)
        {
            if (pool == null) return;
            foreach (var e in pool)
                if (e != null && e.Active)
                    e.Deactivate();
        }

        private bool RoadHasProxy(int roadId)
        {
            foreach (var proxy in VehicleProxies)
                if (proxy.RoadInfo.RoadInstance != null && proxy.RoadInfo.RoadInstance.Id == roadId)
                    return true;
            return false;
        }

        // culling
        private int activeCullingRoom = -1;

        public bool IntersectionWithinCulling(Intersection intersection, AmbientTypeFlags cullFlags)
        {
            foreach (var road in intersection.Roads)
            {
                if (RoadWithinCulling(road, cullFlags))
                    return true;
            }
            return false;
        }

        public bool IntersectionWithinCulling(int intersectionId, AmbientTypeFlags cullFlags)
        {
            return IntersectionWithinCulling(Intersections[intersectionId].Intersection, cullFlags);
        }

        public bool RoadWithinCulling(Road road, AmbientTypeFlags cullFlags)
        {
            return RoadWithinCulling(road.Id, cullFlags);
        }

        public bool RoadWithinCulling(int roadId, AmbientTypeFlags cullFlags)
        {
            if (activeCullingRoom <= 0)
            {
                // no cull room, everything hides
                return true;
            }

            var cullRoads = (cullFlags & AmbientTypeFlags.Vehicles) != 0 ? networkData.CombinedCullRoads[activeCullingRoom - 1]
                                                                         : networkData.PedestrianCullRoads[activeCullingRoom - 1];
            return cullRoads.Contains(roadId);
        }

        public bool RoadAtEdgeOfCullBubble(int roadID, AmbientTypeFlags cullFlags)
        {
            if (activeCullingRoom <= 0)
            {
                // no cull room, everything hides
                return true;
            }

            var road = networkData.Roads[roadID];
            var is1 = networkData.Intersections[road.RightEndData.IntersectionID];
            int is1count = 0;
            var is2 = networkData.Intersections[road.LeftEndData.IntersectionID];
            int is2count = 0;

            foreach (var rd in is1.Roads)
            {
                if (RoadWithinCulling(rd, cullFlags))
                    is1count++;
            }
            foreach (var rd in is2.Roads)
            {
                if (RoadWithinCulling(rd, cullFlags))
                    is2count++;
            }

            return is1count == 0 || is2count == 0;
        }

        public bool RoadAtEdgeOfCullBubble(Road road, AmbientTypeFlags cullFlags)
        {
            return RoadAtEdgeOfCullBubble(road.Id, cullFlags);
        }

        public bool EntityAtEdgeOfCullBubble(AIEntity entity)
        {
            var cullRoadLists = (entity is AITrafficCar) ? networkData.CombinedCullRoads : networkData.PedestrianCullRoads;
            if (activeCullingRoom <= 0)
                return false;

            // check if any roads at dest intersection are possible
            var cullRoads = cullRoadLists[activeCullingRoom - 1];
            int numAvailableRoads = 0;
            foreach (var endRoad in entity.CurrentDestIntersection.Intersection.Roads)
            {
                if (cullRoads.Contains(endRoad.Id))
                    numAvailableRoads++;
            }
            return numAvailableRoads == 0;
        }

        public bool EntityWithinCullBubble(AIEntity entity)
        {
            if (activeCullingRoom <= 0)
                return true;
            
            // Entities crossing an intersection have no current road; judge them by the road they're heading onto.
            var roadInstance = entity.RoadInfo.RoadInstance ?? entity.NextRoadInfo.RoadInstance;
            if (roadInstance == null)
                return false;

            var cullRoadLists = (entity.RailType != RailType.Pedestrian) ? networkData.CombinedCullRoads 
                                                                         : networkData.PedestrianCullRoads;

            // check if any roads at dest intersection are possible
            var cullRoads = cullRoadLists[activeCullingRoom-1];
            return cullRoads.Contains(roadInstance.Id);
        }

        public static void GetNextRoadInfo(RailType railType, Road road, int intersectionId, out RoadData data, out int railCount, out RoadSide sideNum)
        {
            railCount = -1;
            data = null;
            sideNum = RoadSide.Invalid;

            // special case for looping roads
            if (road.LeftEndData.IntersectionID == road.RightEndData.IntersectionID)
            {
                if (road.LeftData.GetRailCount(railType) > road.RightData.GetRailCount(railType))
                {
                    data = road.LeftData;
                    sideNum = RoadSide.Left;
                }
                else
                {
                    data = road.RightData;
                    sideNum = RoadSide.Right;
                }
            }
            else
            {
                data = (road.LeftEndData.IntersectionID == intersectionId) ? road.LeftData : road.RightData;
                sideNum = (road.LeftEndData.IntersectionID == intersectionId) ? RoadSide.Left : RoadSide.Right;
            }

            if (data != null)
                railCount = data.GetRailCount(railType);
        }

        public RoadPositioningInfo? DetermineNextFreewayRoad(AIEntity entity)
        {
            return DetermineNextFreewayRoad(entity.RoadInfo, entity.RailType, entity.AmbientTypeFlagMask);
        }

        public RoadPositioningInfo? DetermineNextFreewayRoad(RoadPositioningInfo sourceRoad, RailType railType, AmbientTypeFlags typeFlags)
        {
            var intersectionEnd = networkData.Intersections[sourceRoad.SideOfRoad == 0 ? sourceRoad.RoadInstance.Road.RightEndData.IntersectionID
                                                                                                     : sourceRoad.RoadInstance.Road.LeftEndData.IntersectionID];
            int roadIdx = intersectionEnd.Roads.IndexOf(sourceRoad.RoadInstance.Road);

            // take off ramp, or determine or next road like normal
            var offRampRoad = intersectionEnd.Roads[intersectionEnd.Roads.WrapIndex(roadIdx + 1)];
            var offRampSide = (offRampRoad.LeftEndData.IntersectionID == intersectionEnd.Id) ? offRampRoad.LeftData : offRampRoad.RightData;
            bool canTakeOffRamp = offRampSide.GetRailCount(railType) > 0 && (offRampSide.AiTypeFlags & typeFlags) > 0;

            bool shouldTakeOffRamp = (sourceRoad.RailIndex == sourceRoad.RoadData.numLanes - 1) && UnityEngine.Random.value > 0.5f;

            if (shouldTakeOffRamp && canTakeOffRamp)
            {
                // take the off ramp!
                return new RoadPositioningInfo
                {
                    RoadInstance = Roads[offRampRoad.Id],
                    RailIndex = 0,
                    SideOfRoad = (offRampRoad.RightEndData.IntersectionID == intersectionEnd.Id) ? RoadSide.Right : RoadSide.Left,
                    Relation = 1
                };
            }
            else
            {
                // try and go straight because off ramps might merge here
                if (intersectionEnd.Roads.Count == 4)
                {
                    Road forwardRoad = (intersectionEnd.Roads.Count == 4) ? intersectionEnd.Roads[intersectionEnd.Roads.WrapIndex(roadIdx - 2)]
                                                                           : GetRoadAhead(sourceRoad.RoadInstance.Road, intersectionEnd);
                    RoadData forwardRoadSide = null;
                    int forwardRoadRailCount = -1;
                    RoadSide forwardRoadSideId = RoadSide.Invalid;

                    if (forwardRoad != null)
                        GetNextRoadInfo(railType, forwardRoad, intersectionEnd.Id, out forwardRoadSide, out forwardRoadRailCount, out forwardRoadSideId);

                    bool canGoStraight = forwardRoad != null && (forwardRoadSide.AiTypeFlags & typeFlags) > 0 && forwardRoadRailCount > 0 && forwardRoad.Id != sourceRoad.RoadInstance.Id;

                    if (canGoStraight)
                    {
                        // going straight!
                        return new RoadPositioningInfo
                        {
                            RoadInstance = Roads[forwardRoad.Id],
                            SideOfRoad = forwardRoadSideId,
                            RailIndex = (sourceRoad.RailIndex <= forwardRoadRailCount - 1) ? sourceRoad.RailIndex : forwardRoadRailCount - 1,
                            Relation = 0
                        };
                    }
                    else
                    {
                        // otherwise find a new road
                        return DetermineNextNormalRoad(sourceRoad, railType, typeFlags);
                    }
                }
                else
                {
                    // otherwise find a new road
                    return DetermineNextNormalRoad(sourceRoad, railType, typeFlags);
                }
            }
        }

        public RoadPositioningInfo? DetermineNextNormalRoad(AIEntity entity)
        {
            return DetermineNextNormalRoad(entity.RoadInfo, entity.RailType, entity.AmbientTypeFlagMask);
        }

        public RoadPositioningInfo? DetermineNextNormalRoad(RoadPositioningInfo sourceRoad, RailType railType, AmbientTypeFlags typeFlags)
        {
            var intersectionEnd = networkData.Intersections[sourceRoad.SideOfRoad == 0 ? sourceRoad.RoadInstance.Road.RightEndData.IntersectionID
                                                                                                     : sourceRoad.RoadInstance.Road.LeftEndData.IntersectionID];
            var intersectionIndexInList = sourceRoad.SideOfRoad == 0 ? sourceRoad.RoadInstance.Road.RightEndData.IntersectionRoadIndex
                                                                      : sourceRoad.RoadInstance.Road.LeftEndData.IntersectionRoadIndex;
            int roadIdx = intersectionIndexInList < 32 ? intersectionIndexInList : intersectionEnd.Roads.IndexOf(sourceRoad.RoadInstance.Road);

            //  Rule: WTF, we're the only one!
            if (intersectionEnd.Roads.Count <= 1)
            {
                return null;
            }

            //  Rule: One rail 
            //  Try and choose next road randomly
            if (sourceRoad.RoadData.GetRailCount(railType) == 1)
            {
                int nextRoadIdx = -1;

                // see if we're on a freeway on ramp, and choose that if we can
                if (intersectionEnd.Roads.Count == 4)
                {
                    int rightIntersectionIndex = intersectionEnd.Roads.WrapIndex(roadIdx + 1);
                    var rightRoad = intersectionEnd.Roads[rightIntersectionIndex];
                    if ((rightRoad.Flags & PathFlags.Freeway) > 0) // freeway, pick me!
                    {
                        nextRoadIdx = rightIntersectionIndex;
                    }
                }

                // didn't find a freeway, choose randomly
                if (nextRoadIdx < 0)
                {
                    int startRoad = UnityEngine.Random.Range(0, intersectionEnd.Roads.Count);
                    for (int i = 0; i < intersectionEnd.Roads.Count; i++)
                    {
                        int startOffset = (i + startRoad) % intersectionEnd.Roads.Count;

                        // don't choose self
                        if (startOffset == roadIdx)
                            continue;

                        var road = intersectionEnd.Roads[startOffset];
                        var roadData = (road.RightEndData.IntersectionID == intersectionEnd.Id) ? road.RightData : road.LeftData;
                        bool isRoadValid = (roadData.AiTypeFlags & typeFlags) > 0 && roadData.GetRailCount(railType) > 0;

                        if (isRoadValid)
                        {
                            nextRoadIdx = startOffset;
                            break;
                        }
                    }
                }

                // here we go!
                if (nextRoadIdx < 0)
                    return null;

                var nextRoad = intersectionEnd.Roads[nextRoadIdx];

                // get info and checks stuff
                GetNextRoadInfo(railType, nextRoad, intersectionEnd.Id, out RoadData nextRoadSide, out int nextRoadRailCount, out RoadSide nextRoadSideId);

                var nextRoadInfo = new RoadPositioningInfo
                {
                    RoadInstance = Roads[nextRoad.Id],
                    SideOfRoad = nextRoadSideId
                };

                float angleToNext = intersectionEnd.RoadAngles[roadIdx][nextRoadIdx];
                if (angleToNext > 15f)
                {
                    nextRoadInfo.Relation = 1;
                    nextRoadInfo.RailIndex = nextRoadRailCount - 1;
                }
                else if (angleToNext < -15f)
                {
                    nextRoadInfo.Relation = -1;
                    nextRoadInfo.RailIndex = 0;
                }
                else
                {
                    nextRoadInfo.Relation = 0;
                    nextRoadInfo.RailIndex = 0;
                }

                return nextRoadInfo;
            }

            //  Rule: Pick one road from this intersection
            if (intersectionEnd.Roads.Count >= 3)
            {
                // BLA
                Road leftRoad = (intersectionEnd.Roads.Count == 4) ? intersectionEnd.Roads[intersectionEnd.Roads.WrapIndex(roadIdx - 1)]
                                                                   : GetRoadToLeft(sourceRoad.RoadInstance.Road, intersectionEnd);
                RoadData leftRoadSide = null;
                int leftRoadRailCount = -1;
                RoadSide leftRoadSideId = RoadSide.Invalid;

                if (leftRoad != null)
                    GetNextRoadInfo(railType, leftRoad, intersectionEnd.Id, out leftRoadSide, out leftRoadRailCount, out leftRoadSideId);

                Road rightRoad = (intersectionEnd.Roads.Count == 4) ? intersectionEnd.Roads[intersectionEnd.Roads.WrapIndex(roadIdx + 1)]
                                                                    : GetRoadToRight(sourceRoad.RoadInstance.Road, intersectionEnd);
                RoadData rightRoadSide = null;
                int rightRoadRailCount = -1;
                RoadSide rightRoadSideId = RoadSide.Invalid;

                if (rightRoad != null)
                    GetNextRoadInfo(railType, rightRoad, intersectionEnd.Id, out rightRoadSide, out rightRoadRailCount, out rightRoadSideId);

                Road forwardRoad = (intersectionEnd.Roads.Count == 4) ? intersectionEnd.Roads[intersectionEnd.Roads.WrapIndex(roadIdx - 2)]
                                                                      : GetRoadAhead(sourceRoad.RoadInstance.Road, intersectionEnd);
                RoadData forwardRoadSide = null;
                int forwardRoadRailCount = -1;
                RoadSide forwardRoadSideId = RoadSide.Invalid;

                if (forwardRoad != null)
                    GetNextRoadInfo(railType, forwardRoad, intersectionEnd.Id, out forwardRoadSide, out forwardRoadRailCount, out forwardRoadSideId);

                // check if the same road got referenced (e.g. if this is a circular road)
                if ((leftRoad != null && leftRoad == forwardRoad) || (rightRoad != null && rightRoad == forwardRoad))
                {
                    // if so, fix this up so it's a turn / forward if possible
                    if (leftRoad != null && leftRoad == forwardRoad)
                    {
                        leftRoad = null;
                    }
                    if (rightRoad != null && rightRoad == forwardRoad)
                    {
                        forwardRoad = null;
                    }
                }

                // check if we can go any direction
                bool canTurnLeft = leftRoad != null && sourceRoad.RailIndex == 0 && (leftRoadSide.AiTypeFlags & typeFlags) > 0 && leftRoadRailCount > 0 && leftRoad.Id != sourceRoad.RoadInstance.Id;
                bool canTurnRight = rightRoad != null && sourceRoad.RailIndex == sourceRoad.RoadData.GetRailCount(railType) - 1 && (rightRoadSide.AiTypeFlags & typeFlags) > 0 && rightRoadRailCount > 0 && rightRoad.Id != sourceRoad.RoadInstance.Id;
                bool canGoStraight = forwardRoad != null && (forwardRoadSide.AiTypeFlags & typeFlags) > 0 && forwardRoadRailCount > 0 && forwardRoad.Id != sourceRoad.RoadInstance.Id;

                // can't go anywhere! we'll do a couple extra checks just to make sure
                if (!canGoStraight && !canTurnLeft && !canTurnRight)
                {
                    bool canTurnRightFromAnyLane = rightRoad != null
                                                   && (rightRoadSide.AiTypeFlags & typeFlags) > 0
                                                   && rightRoadRailCount > 0
                                                   && rightRoad.Id != sourceRoad.RoadInstance.Id;
                    bool canTurnLeftFromAnyLane = leftRoad != null
                                                   && (leftRoadSide.AiTypeFlags & typeFlags) > 0
                                                   && leftRoadRailCount > 0
                                                   && leftRoad.Id != sourceRoad.RoadInstance.Id;

                    if (!canTurnLeftFromAnyLane && !canTurnRightFromAnyLane)
                    {
                        // Debug.LogError($"ERROR: Could not find a suitable road to turn to from road {sourceRoad.Id}, intersection {intersectionEnd.Id}!");
                        return null;
                    }

                    // edge case, 4 lane road merging onto 2 roads with 2 lanes on each side
                    int myRailCount = sourceRoad.RoadData.GetRailCount(railType);
                    if (leftRoadRailCount + rightRoadRailCount == myRailCount && canTurnRightFromAnyLane && canTurnLeftFromAnyLane)
                    {
                        bool goLeft = sourceRoad.RailIndex < (myRailCount / 2);
                        if (goLeft)
                        {
                            return new RoadPositioningInfo
                            {
                                RoadInstance = Roads[leftRoad.Id],
                                RailIndex = 0,
                                SideOfRoad = leftRoadSideId,
                                Relation = -1
                            };
                        }
                        else
                        {
                            return new RoadPositioningInfo
                            {
                                RoadInstance = Roads[rightRoad.Id],
                                RailIndex = Mathf.Clamp(sourceRoad.RailIndex - (myRailCount / 2), 0, myRailCount - 1),
                                SideOfRoad = rightRoadSideId,
                                Relation = 1
                            };
                        }
                    }
                    else
                    {
                        // just go somewhere then
                        if (canTurnRightFromAnyLane)
                        {
                            return new RoadPositioningInfo
                            {
                                RoadInstance = Roads[rightRoad.Id],
                                RailIndex = Mathf.Clamp(sourceRoad.RailIndex, 0, rightRoadRailCount - 1),
                                SideOfRoad = rightRoadSideId,
                                Relation = 1
                            };
                        }
                        else if (canTurnLeftFromAnyLane)
                        {
                            return new RoadPositioningInfo
                            {
                                RoadInstance = Roads[leftRoad.Id],
                                RailIndex = Mathf.Clamp(sourceRoad.RailIndex, 0, leftRoadRailCount - 1),
                                SideOfRoad = leftRoadSideId,
                                Relation = -1
                            };
                        }
                    }
                }
                else
                {
                    // in some cases, go straight instead of turn, even if we can turn
                    float noTurnChance = UnityEngine.Random.value;
                    if (canGoStraight && (canTurnLeft || canTurnRight))
                    {
                        if (noTurnChance > 0.7f)
                        {
                            canTurnLeft = false;
                            canTurnRight = false;
                        }
                    }

                    // 
                    if (canTurnRight) // right or straight 
                    {
                        return new RoadPositioningInfo
                        {
                            RoadInstance = Roads[rightRoad.Id],
                            RailIndex = rightRoadRailCount - 1,
                            SideOfRoad = rightRoadSideId,
                            Relation = 1
                        };
                    }
                    else if (canTurnLeft) // left or straight
                    {
                        return new RoadPositioningInfo
                        {
                            RoadInstance = Roads[leftRoad.Id],
                            RailIndex = 0,
                            SideOfRoad = leftRoadSideId,
                            Relation = -1
                        };
                    }
                    else if (canGoStraight) // straight
                    {
                        return new RoadPositioningInfo
                        {
                            RoadInstance = Roads[forwardRoad.Id],
                            RailIndex = Mathf.Clamp(sourceRoad.RailIndex, 0, forwardRoadRailCount - 1),
                            SideOfRoad = forwardRoadSideId,
                            Relation = 0
                        };
                    }
                }
            }
            else if (intersectionEnd.Roads.Count == 2)
            {
                // try pass through
                Road forwardRoad = intersectionEnd.Roads[intersectionEnd.Roads.WrapIndex(roadIdx + 1)];
                RoadData forwardRoadSide = null;
                int forwardRoadRailCount = -1;
                RoadSide forwardRoadSideId = RoadSide.Invalid;

                GetNextRoadInfo(railType, forwardRoad, intersectionEnd.Id, out forwardRoadSide, out forwardRoadRailCount, out forwardRoadSideId);

                bool canGoStraight = (forwardRoadSide.AiTypeFlags & typeFlags) > 0 && forwardRoadRailCount > 0 && forwardRoad.Id != sourceRoad.RoadInstance.Id;
                if (!canGoStraight)
                {
                    return null;
                }


                return new RoadPositioningInfo
                {
                    RoadInstance = Roads[forwardRoad.Id],
                    RailIndex = Mathf.Clamp(sourceRoad.RailIndex, 0, forwardRoadSide.GetRailCount(railType) - 1),
                    SideOfRoad = forwardRoadSideId,
                    Relation = 0
                };
            }

            // todo: find out how this can happen :/
            return null;
        }

        // generic
        public RoadPositioningInfo? DetermineNextRoad(RoadPositioningInfo sourceRoad, RailType railType, AmbientTypeFlags typeFlags)
        {
            // determine next road
            var road = sourceRoad.RoadInstance.Road;

            // dead end, nothing to go to
            int destId = sourceRoad.SideOfRoad == 0 ? road.RightEndData.IntersectionID : road.LeftEndData.IntersectionID;
            if (destId < 0 || destId >= networkData.Intersections.Count)
                return null;

            if (railType == RailType.Tram)
            {
                return DetermineNextCableCarRoad(sourceRoad);
            }
            else
            {
                if ((road.Flags & PathFlags.Freeway) > 0)
                {
                    // freeway logic
                    return DetermineNextFreewayRoad(sourceRoad, railType, typeFlags);
                }
                else
                {
                    // normal logic
                    return DetermineNextNormalRoad(sourceRoad, railType, typeFlags);
                }
            }
        }

        public RoadPositioningInfo? DetermineNextRoad(AIEntity entity)
        {
            return DetermineNextRoad(entity.RoadInfo, entity.RailType, entity.AmbientTypeFlagMask);
        }

        // cable car
        private RoadPositioningInfo? TryTurnAroundCableCar(RoadPositioningInfo sourceRoad, Road road)
        {
            int nextSideIndex = sourceRoad.SideOfRoad == 0 ? 1 : 0;
            var nextSideData = nextSideIndex == 0 ? road.LeftData : road.RightData;
            int nextSideRails = nextSideData.GetRailCount(RailType.Tram);

            if (nextSideRails <= 0 || (nextSideData.AiTypeFlags & AmbientTypeFlags.Vehicles) == 0)
                return null;

            return new RoadPositioningInfo
            {
                RoadInstance = sourceRoad.RoadInstance,
                RailIndex = Mathf.Clamp(sourceRoad.RailIndex, 0, nextSideRails - 1),
                SideOfRoad = (RoadSide)nextSideIndex,
                Relation = 0
            };
        }

        // cable car
        public RoadPositioningInfo? DetermineNextCableCarRoad(RoadPositioningInfo sourceRoad)
        {
            var road = sourceRoad.RoadInstance.Road;
            var intersectionEnd = networkData.Intersections[sourceRoad.SideOfRoad == 0 ? road.RightEndData.IntersectionID
                                                                                       : road.LeftEndData.IntersectionID];
            var intersectionIndexInList = sourceRoad.SideOfRoad == 0 ? road.RightEndData.IntersectionRoadIndex
                                                                      : road.LeftEndData.IntersectionRoadIndex;
            int roadIdx = intersectionIndexInList < 32 ? intersectionIndexInList : intersectionEnd.Roads.IndexOf(road);

            //  Rule: we're the only road here - turn around onto the opposite side if it has a cable line
            if (intersectionEnd.Roads.Count <= 1)
            {
                return TryTurnAroundCableCar(sourceRoad, road);
            }

            //  Rule: Pick one road from this intersection
            if (intersectionEnd.Roads.Count >= 3)
            {
                Road leftRoad = (intersectionEnd.Roads.Count == 4) ? intersectionEnd.Roads[intersectionEnd.Roads.WrapIndex(roadIdx - 1)]
                                                                   : GetRoadToLeft(road, intersectionEnd);
                RoadData leftRoadSide = null;
                int leftRoadRailCount = -1;
                RoadSide leftRoadSideId = RoadSide.Invalid;

                if (leftRoad != null)
                    GetNextRoadInfo(RailType.Tram, leftRoad, intersectionEnd.Id, out leftRoadSide, out leftRoadRailCount, out leftRoadSideId);

                Road rightRoad = (intersectionEnd.Roads.Count == 4) ? intersectionEnd.Roads[intersectionEnd.Roads.WrapIndex(roadIdx + 1)]
                                                                    : GetRoadToRight(road, intersectionEnd);
                RoadData rightRoadSide = null;
                int rightRoadRailCount = -1;
                RoadSide rightRoadSideId = RoadSide.Invalid;

                if (rightRoad != null)
                    GetNextRoadInfo(RailType.Tram, rightRoad, intersectionEnd.Id, out rightRoadSide, out rightRoadRailCount, out rightRoadSideId);

                Road forwardRoad = (intersectionEnd.Roads.Count == 4) ? intersectionEnd.Roads[intersectionEnd.Roads.WrapIndex(roadIdx - 2)]
                                                                      : GetRoadAhead(road, intersectionEnd);
                RoadData forwardRoadSide = null;
                int forwardRoadRailCount = -1;
                RoadSide forwardRoadSideId = RoadSide.Invalid;

                if (forwardRoad != null)
                    GetNextRoadInfo(RailType.Tram, forwardRoad, intersectionEnd.Id, out forwardRoadSide, out forwardRoadRailCount, out forwardRoadSideId);

                // check if the same road got referenced (e.g. if this is a circular road)
                if ((leftRoad != null && leftRoad == forwardRoad) || (rightRoad != null && rightRoad == forwardRoad))
                {
                    if (leftRoad != null && leftRoad == forwardRoad)
                    {
                        leftRoad = null;
                    }
                    if (rightRoad != null && rightRoad == forwardRoad)
                    {
                        forwardRoad = null;
                    }
                }

                bool canTurnLeft = leftRoad != null && (leftRoadSide.AiTypeFlags & AmbientTypeFlags.Vehicles) > 0 && leftRoadRailCount > 0 && leftRoad.Id != sourceRoad.RoadInstance.Id;
                bool canTurnRight = rightRoad != null && (rightRoadSide.AiTypeFlags & AmbientTypeFlags.Vehicles) > 0 && rightRoadRailCount > 0 && rightRoad.Id != sourceRoad.RoadInstance.Id;
                bool canGoStraight = forwardRoad != null && (forwardRoadSide.AiTypeFlags & AmbientTypeFlags.Vehicles) > 0 && forwardRoadRailCount > 0 && forwardRoad.Id != sourceRoad.RoadInstance.Id;

                // can't go anywhere else - try turning around on the current road
                if (!canGoStraight && !canTurnLeft && !canTurnRight)
                {
                    return TryTurnAroundCableCar(sourceRoad, road);
                }

                // cable cars will first try to go straight, then right, then left
                if (canGoStraight)
                {
                    return new RoadPositioningInfo
                    {
                        RoadInstance = Roads[forwardRoad.Id],
                        RailIndex = Mathf.Clamp(sourceRoad.RailIndex, 0, forwardRoadRailCount - 1),
                        SideOfRoad = forwardRoadSideId,
                        Relation = 0
                    };
                }
                if (canTurnRight)
                {
                    return new RoadPositioningInfo
                    {
                        RoadInstance = Roads[rightRoad.Id],
                        RailIndex = rightRoadRailCount - 1,
                        SideOfRoad = rightRoadSideId,
                        Relation = 1
                    };
                }
                if (canTurnLeft)
                {
                    return new RoadPositioningInfo
                    {
                        RoadInstance = Roads[leftRoad.Id],
                        RailIndex = 0,
                        SideOfRoad = leftRoadSideId,
                        Relation = -1
                    };
                }
            }
            else if (intersectionEnd.Roads.Count == 2)
            {
                // try pass through
                Road forwardRoad = intersectionEnd.Roads[intersectionEnd.Roads.WrapIndex(roadIdx + 1)];
                RoadData forwardRoadSide = null;
                int forwardRoadRailCount = -1;
                RoadSide forwardRoadSideId = RoadSide.Invalid;

                GetNextRoadInfo(RailType.Tram, forwardRoad, intersectionEnd.Id, out forwardRoadSide, out forwardRoadRailCount, out forwardRoadSideId);

                bool canGoStraight = (forwardRoadSide.AiTypeFlags & AmbientTypeFlags.Vehicles) > 0 && forwardRoadRailCount > 0 && forwardRoad.Id != sourceRoad.RoadInstance.Id;

                // can't pass through - try turning around on the current road
                if (!canGoStraight)
                {
                    return TryTurnAroundCableCar(sourceRoad, road);
                }

                return new RoadPositioningInfo
                {
                    RoadInstance = Roads[forwardRoad.Id],
                    RailIndex = Mathf.Clamp(sourceRoad.RailIndex, 0, forwardRoadSide.GetRailCount(RailType.Tram) - 1),
                    SideOfRoad = forwardRoadSideId,
                    Relation = 0
                };
            }

            Debug.LogError($"DetermineNextCableCarRoad failed from {road.Id}, could not choose from {intersectionEnd.Roads.Count} choices");
            return null;
        }

        public RoadPositioningInfo? DetermineNextCableCarRoad(AIEntity entity)
        {
            return DetermineNextCableCarRoad(entity.RoadInfo);
        }

        public Road GetRoadWithinAngle(float angMin, float angMax, Road refRoad, Intersection refIntersection)
        {
            if (refRoad.LeftEndData.IntersectionID != refIntersection.Id && refRoad.RightEndData.IntersectionID != refIntersection.Id)
            {
                Debug.LogError($"GetRoadWithinAngle: refRoad {refRoad.Id} does not connect to refIntersection {refIntersection.Id}");
                return null;
            }

            var intersectionEnd = refIntersection;
            var intersectionIndexInList = (refRoad.LeftEndData.IntersectionID == refIntersection.Id) ? refRoad.LeftEndData.IntersectionRoadIndex : refRoad.RightEndData.IntersectionRoadIndex;
            int roadIdx = intersectionIndexInList < 32 ? intersectionIndexInList : intersectionEnd.Roads.IndexOf(refRoad);

            if (roadIdx < 0)
            {
                Debug.LogError($"GetRoadWithinAngle: refRoad {refRoad.Id} seemingly connects to refIntersection {refIntersection.Id}, but it can't be found in the roads reference table?");
                return null;
            }

            var angles = refIntersection.RoadAngles[roadIdx];
            int validRoadCount = angles.Count(x => x > angMin && x < angMax);

            int roadChoice = (validRoadCount > 1) ? UnityEngine.Random.Range(0, validRoadCount) : 0;
            int roadChoiceCur = 0;
            for (int i = 0; i < angles.Count; i++)
            {
                float ang = angles[i];
                var road = intersectionEnd.Roads[i];
                if (ang > angMin && ang < angMax)
                {
                    if (roadChoiceCur == roadChoice)
                        return road;
                    roadChoiceCur++;
                }
            }

            return null;
        }

        public Road GetRoadToRight(Road refRoad, Intersection refIntersection)
        {
            return GetRoadWithinAngle(30f, 175f, refRoad, refIntersection);
        }

        public Road GetRoadAhead(Road refRoad, Intersection refIntersection)
        {
            return GetRoadWithinAngle(-30f, 30f, refRoad, refIntersection);
        }

        public Road GetRoadToLeft(Road refRoad, Intersection refIntersection)
        {
            return GetRoadWithinAngle(-175f, -30f, refRoad, refIntersection);
        }

        // pathfinding stuff
        public List<Tuple<int, int>> DetermineCableCarPaths()
        {
            var pathIntersections = new List<Tuple<int, int>>();
            HashSet<int> visitedSrcDstIntersections = new HashSet<int>();

            foreach (var intersectionInstance in Intersections)
            {
                var intersection = intersectionInstance.Intersection;

                // if we've already determined this to be a start/end
                if (visitedSrcDstIntersections.Contains(intersection.Id))
                    continue;

                // find out of this is a cable car source intersection
                int sources = 0;
                Road lastSource = null;

                foreach (var road in intersection.Roads)
                {
                    if (road.LeftData.GetRailCount(RailType.Tram) > 0 || road.RightData.GetRailCount(RailType.Tram) > 0)
                    {
                        sources++;
                        lastSource = road;
                    }
                }

                // source intersection if we only have one source
                if (sources != 1)
                    continue;

                // pathfind until we hit the next intersection!
                RoadSide pathfindSide = lastSource.RightEndData.IntersectionID == intersection.Id ? RoadSide.Right
                                                                                                  : RoadSide.Left;

                RoadPositioningInfo sourceInfo = new RoadPositioningInfo { RoadInstance = Roads[lastSource.Id],
                                                                           RailIndex = 0,
                                                                           SideOfRoad = pathfindSide };
                RoadPositioningInfo? lastPositioningInfo = null;

                while (true)
                {
                    var currentInfo = lastPositioningInfo ?? sourceInfo;
                    var positionInfo = DetermineNextCableCarRoad(currentInfo);

                    // dead end with no way to turn around
                    if (positionInfo == null)
                        break;

                    // turned around onto the same road = end of the line
                    if (positionInfo.Value.RoadInstance.Id == currentInfo.RoadInstance.Id)
                        break;

                    lastPositioningInfo = positionInfo;
                    if (lastPositioningInfo.Value.RoadInstance.Id == sourceInfo.RoadInstance.Id)
                        break;
                }

                // check if an oopsie woopsie happened
                if (lastPositioningInfo != null && lastPositioningInfo.Value.RoadInstance.Id == lastSource.Id)
                {
                    Debug.LogError($"ERROR: Cable car path beginning at {lastSource.Id} is an infinite loop!");
                    continue;
                }

                // we have a start and an end!
                int intersectionEndId;
                if (lastPositioningInfo == null) // this cable car path is one road long
                {
                    intersectionEndId = lastSource.RightEndData.IntersectionID == intersection.Id ? lastSource.LeftEndData.IntersectionID : lastSource.RightEndData.IntersectionID;
                }
                else
                {
                    intersectionEndId = lastPositioningInfo.Value.SideOfRoad == RoadSide.Right ? lastPositioningInfo.Value.RoadInstance.Road.LeftEndData.IntersectionID
                                                                                               : lastPositioningInfo.Value.RoadInstance.Road.RightEndData.IntersectionID;
                }

                // add to our list and hashmap
                visitedSrcDstIntersections.Add(intersectionEndId);
                visitedSrcDstIntersections.Add(intersection.Id);
                pathIntersections.Add(new Tuple<int, int>(intersection.Id, intersectionEndId));
            }

            return pathIntersections;
        }


        // accessors
        public RoadInstance GetRoadInstance(int id)
        {
            if (id < 0) return null;
            if (id < Roads.Count) return id < Roads.Count ? Roads[id] : null;
            id -= Roads.Count;
            return id < Shortcuts.Count ? Shortcuts[id] : null;
        }

        //
        public void Reset()
        {
            // basically setcullindex with a set seed
            foreach(AIEntity entity in trafficCarPool.Cast<AIEntity>().Concat(pedestrianPool))
            {
                if(entity != null)
                {
                    entity.Reset();
                    entity.Deactivate();
                }
            }
            foreach(var entity in cableCars)
            {
                entity.Reset();
            }
            foreach(var cop in policeCars)
            {
                cop.Reset();
            }
            foreach (var racer in opponents)
            {
                racer.Reset();
            }
            foreach (var intersection in intersections)
            {
                intersection.Reset();
            }
            foreach (var road in roads)
            {
                road.Reset();
            }
            foreach (var road in shortcuts)
            {
                road.Reset();
            }
            policeForce.Reset();
            activeCullingRoom = -1;
            populatedPedRoads.Clear();
            populatedTrafficRoads.Clear();
        }

        public void SetTrafficDensity(float density)
        {
            trafficDensity = Mathf.Clamp01(density);
            RefreshAmbients();
        }

        public void SetPedestrianDensity(float density)
        {
            pedLimit = pedestrianPool == null ? 0 : Mathf.FloorToInt(Mathf.Clamp01(density) * pedestrianPool.Length);
            RefreshAmbients();
        }

        private void AddComponentToMap(int room, CompType type, int id)
        {
            if(!componentMap.TryGetValue(room, out var list))
            {
                list = new List<AIComponent>();
                componentMap[room] = list;
            }
            list.Add(new AIComponent(type, id));
        }

        private void CreateComponentMaps()
        {
            // first, intersections, then roads, then shortcuts, same order as the base game.
            // for intersections, the room they state is trusted fully
            foreach(var intersection in Data.Intersections)
            {
                AddComponentToMap(intersection.Room, CompType.Intersection, intersection.Id);
            }

            // for roads and shortcuts, we do it ourselves
            foreach (var road in roads)
                MapRoadToRooms(road.Road, CompType.Road);

            foreach (var shortcut in shortcuts)
                MapRoadToRooms(shortcut.Road, CompType.Shortcut);
        }

        private void MapRoadToRooms(Road road, CompType type)
        {
            // Pass 1: interior section origins only (1 .. count-2), for both roads and shortcuts.
            // No intersection-room check here, unlike the shortcut passes below.
            for (int i = 1; i < road.NumSections - 1; i++)
            {
                int room = level.FindRoomIdWithWarps(road.Origin(i));
                if (!componentMap.TryGetValue(room, out var list) || !ContainsComponent(list, type, road.Id))
                    AddComponentToMap(room, type, road.Id);
            }

            if (type != CompType.Shortcut)
                return;

            // Passes 2 and 3: shortcuts also walk both sidewalk boundaries in 1m steps,
            // right side first, then left, same as the exe.
            int lRoom = GetIntersectionRoom(road.LeftEndData);
            int rRoom = GetIntersectionRoom(road.RightEndData);

            MapBoundaryToRooms(road, road.RightData.SidewalkOuterVertices, lRoom, rRoom);
            MapBoundaryToRooms(road, road.LeftData.SidewalkOuterVertices, lRoom, rRoom);
        }

        private void MapBoundaryToRooms(Road road, Vector3[] verts, int lRoom, int rRoom)
        {
            for (int i = 0; i < road.NumSections - 1; i++)
            {
                float len = road.CenterLength(i, i + 1);

                // integer steps along the segment, t = k / len, endpoint excluded
                for (int k = 0; k < len; k++)
                {
                    Vector3 p = Vector3.LerpUnclamped(verts[i], verts[i + 1], k / len);
                    int room = level.FindRoomIdWithWarps(p);

                    // The exe only runs these checks when the room already has components.
                    // An empty room always gets the shortcut added.
                    if (componentMap.TryGetValue(room, out var list) && list.Count > 0)
                    {
                        // never map a shortcut into either end intersection's room
                        if (room == lRoom || room == rRoom)
                            continue;

                        if (ContainsComponent(list, CompType.Shortcut, road.Id))
                            continue;
                    }

                    AddComponentToMap(room, CompType.Shortcut, road.Id);
                }
            }
        }

        private int GetIntersectionRoom(RoadEnd end)
        {
            // dead ends have IntersectionID == -1; return something no room can match
            if (end.IntersectionID < 0 || end.IntersectionID >= Data.Intersections.Count)
                return int.MinValue;
            return Data.Intersections[end.IntersectionID].Room;
        }

        private static bool ContainsComponent(List<AIComponent> list, CompType type, int id)
        {
            foreach (var c in list)
                if (c.Type == type && c.Id == id)
                    return true;
            return false;
        }

        private void InitPedPool(int size, string[] modelNames)
        {
            var city = SDLCity.Instance;
            if (city == null)
                return;

            // pedpool arg
            var sizeArg = ArgParser.Get("pedpool");
            if (sizeArg != null && sizeArg.Parameters.Count > 0)
            {
                int.TryParse(sizeArg.Parameters[0], out size);
            }
            pedestrianPool = new AIPedestrian[size];

            // instantiate
            if (modelNames == null || modelNames.Length == 0)
            {
                Debug.LogError($"Empty pedestrian model array, not initializing any pedestrians!");
                return;
            }

            for (int i = 0; i < size; i++)
            {
                int modelIndex = modelNames.WrapIndex(i);
                var pedInstance = new GameObject(modelNames[modelIndex]);
                pedInstance.SetActive(false);
                pedInstance.transform.position = new Vector3(0, -9999f, 0); // spawn in the void

                // add to pool
                var pedestrian = new AIPedestrian(this);
                pedestrianPool[i] = pedestrian;
            }
        }

        private void InitTrafficPool(int size, Dictionary<string, float> trafficTypesAndDensity)
        {
            // This is crap, move the AIMap to this class instead
            var city = SDLCity.Instance;
            if (city == null)
                return;

            trafficCarPool = new AITrafficCar[size];

            var trafficTypes = trafficTypesAndDensity;
            var orderedTrafficTypes = trafficTypes.OrderBy((arg) => arg.Value);

            // now init the pool
            for (int i = 0; i < size; i++)
            {
                float spawnVal = UnityEngine.Random.value;
                string spawnType = null;

                foreach (var trafficType in orderedTrafficTypes)
                {
                    if (spawnVal > trafficType.Value)
                        continue;
                    spawnType = trafficType.Key;
                    break;
                }

                if (string.IsNullOrEmpty(spawnType))
                {
                    Debug.LogError($"No spawn candidate for value {spawnVal}. There's now a NULL entry in the traffic pool!!");
                    continue;
                }

                // spawn it
                var trafficCar = new AITrafficCar(this, spawnType);
                trafficCarPool[i] = trafficCar;
            }
        }

        private void InitTrafficLightSet(IntersectionInstance ints)
        {
            var set = new TrafficLightSet();
            ints.IntersectionLightSet = set;
        }

        private void InitControlDevices(string singleTrafLitModel = "sp_traflitsingle_f", string dualTrafLitModel = "sp_traflitdual_f")
        {
            // create control devices
            foreach (var ints in Intersections)
            {
                // create control devices array
                int roadCount = ints.Intersection.Roads.Count;
                ints.ControlDevices = new TrafficControlDevice[roadCount];

                // assign
                for (int i = 0; i < roadCount; i++)
                {
                    var rd = ints.Intersection.Roads[i];
                    var rdSide = rd.LeftEndData.IntersectionID == ints.Id ? rd.LeftData : rd.RightData;
                    var rdEnd = rd.LeftEndData.IntersectionID == ints.Id ? rd.LeftEndData : rd.RightEndData;

                    switch (rdEnd.VehRule)
                    {
                        case VehicleRule.AlwaysStop:
                            ints.ControlDevices[i] = new AlwaysStopDevice(ints);
                            break;
                        case VehicleRule.NeverStop:
                            ints.ControlDevices[i] = new NeverStopDevice(ints);
                            break;
                        case VehicleRule.StopSign:
                            ints.ControlDevices[i] = new StopSignDevice(ints);
                            break;
                        case VehicleRule.TrafficLight:
                            {
                                if (ints.IntersectionLightSet == null)
                                    InitTrafficLightSet(ints);

                                // create instance
                                TrafficLightInstance instance = null;
                                if(rdEnd.TrafficLightOrigin != Vector3.zero)
                                {
                                    string trafLightModel = rdSide.GetRailCount(RailType.Vehicle) <= 1 ? singleTrafLitModel : dualTrafLitModel;
                                    var trafLight = new GameObject("TrafficLight");

                                    var trafLightPosition = rdEnd.TrafficLightOrigin;
                                    var trafLightDirection = (rdEnd.TrafficLightOrientation - rdEnd.TrafficLightOrigin).Flatten();
                                    var trafLightRotation = Quaternion.LookRotation(trafLightDirection, Vector3.up) 
                                                            * Quaternion.Euler(0f, 90f, 0f);

                                    instance = trafLight.AddComponent<TrafficLightInstance>();
                                    instance.Init(level, trafLightModel);
                                    instance.Init(level, trafLightModel, trafLightPosition, trafLightRotation, Vector3.one);
                                    level.MoveToRoom(trafLight, level.FindRoomIdWithWarps(trafLightPosition));
                                }

                                // set control device
                                var device = new TrafficLightDevice(ints);
                                device.Instance = instance;
                                ints.ControlDevices[i] = device;
                                ints.IntersectionLightSet.Lights.Add(device);
                            }
                            break;
                    }
                }
            }

            // init traffic light sets
            foreach (var ints in Intersections)
            {
                if (ints.IntersectionLightSet != null)
                    ints.IntersectionLightSet.Init();
            }
        }

        private void InitCableCarLine(int start, int end)
        {
            // get  intersections
            var startIntersection = networkData.Intersections[start];
            var endIntersection = networkData.Intersections[end];

            // find out what road, and what side we should be on
            var startRoad = startIntersection.Roads.First(x => (x.RightEndData.IntersectionID == start || x.LeftEndData.IntersectionID == start)
                                                            && (x.RightData.GetRailCount(RailType.Tram) > 0 || x.LeftData.GetRailCount(RailType.Tram) > 0));
            var endRoad = endIntersection.Roads.First(x => (x.RightEndData.IntersectionID == end || x.LeftEndData.IntersectionID == end)
                                                            && (x.RightData.GetRailCount(RailType.Tram) > 0 || x.LeftData.GetRailCount(RailType.Tram) > 0));
            var startRoadInstance = Roads[startRoad.Id];
            var endRoadInstance = Roads[endRoad.Id];

            // create our cable cars 
            var cableCarOne = new AICableCar(this);
            var cableCarTwo = new AICableCar(this);
            cableCars.Add(cableCarTwo);
            cableCars.Add(cableCarOne);

            // try and place cable car one
            RoadSide startRoadSide = RoadSide.Invalid;
            if (startRoad.LeftEndData.IntersectionID == start)
            {
                startRoadSide = RoadSide.Left;
            }
            else if (startRoad.RightEndData.IntersectionID == start)
            {
                startRoadSide = RoadSide.Left;
            }

            if (startRoadSide == RoadSide.Invalid)
            {
                Debug.Log($"Could not place cable car at start of line {start}->{end}.");
                cableCarOne.Deactivate();
            }
            else
            {
                cableCarOne.Activate();
                cableCarOne.SetRoad(new RoadPositioningInfo { RoadInstance = startRoadInstance, RailIndex = 0, SideOfRoad = startRoadSide });
                cableCarOne.PositionAlongPath(0f); // position cablecar away from world origin
            }

            // try and place cable car two
            RoadSide endRoadSide = RoadSide.Invalid;
            if (endRoad.LeftEndData.IntersectionID == end)
            {
                endRoadSide = RoadSide.Left;
            }
            else if (endRoad.RightEndData.IntersectionID == end)
            {
                endRoadSide = RoadSide.Left;
            }

            if (endRoadSide == RoadSide.Invalid)
            {
                Debug.Log($"Could not place cable car at end of line {start}->{end}.");
                cableCarTwo.Deactivate();
            }
            else
            {
                cableCarTwo.Activate();
                cableCarTwo.SetRoad(new RoadPositioningInfo { RoadInstance = endRoadInstance, RailIndex = 0, SideOfRoad = endRoadSide });
                cableCarTwo.PositionAlongPath(0f); // position cablecar away from world origin
            }
        }

        private void InitObstacles()
        {
            for (int i = 0; i < level.RoomCount; i++)
            {
                foreach (var instance in level.GetRoom(i+1).Instances)
                {
                    if (instance is not UnhitBangerInstance banger)
                        continue;

                    if (!MapComponent(banger.transform.position, out var component))
                        continue;

                    if (component.Type == CompType.Road || component.Type == CompType.Shortcut)
                    {
                        GetRoadInstance(component.Id).AddObstacle(new BangerObstacle(banger));
                    }
                    else if (component.Type == CompType.Intersection)
                    {
                        intersections[component.Id].AddObstacle(new BangerObstacle(banger));
                    }
                }
            }
        }

        private void InitPolice(AIMap aimap)
        {
            policeForce =  new PoliceForce();

            int numCops = Mathf.RoundToInt(aimap.Police.Count * Mathf.Clamp01(GameState.CopDensity));
            for (int i=0; i < numCops; i++)
            {
                var copCar = new GameObject($"PoliceCar{i}");
                copCar.transform.parent = this.transform;

                var copCmp = copCar.AddComponent<AIPoliceOfficer>();
                bool success = copCmp.Init(level, i);

                if (success)
                {
                    policeCars.Add(copCmp);
                }
                else
                {
                    Destroy(copCar);
                }
            }
        }

        private void InitOpponents(AIMap aimap)
        {
            int numOpps = Mathf.Min(aimap.Opponents.Count, GameState.OpponentCount);
            for (int i = 0; i < numOpps; i++)
            {
                var oppCar = new GameObject($"Opponent{i}");
                oppCar.transform.parent = this.transform;

                var oppCmp = oppCar.AddComponent<AIRouteRacer>();
                oppCmp.Init(level, i, level.Name);
                if(this.GameMode != MMGameMode.Circuit)
                {
                    oppCmp.Car.Damage.EnableRegeneration = false;
                }

                opponents.Add(oppCmp);
            }
        }

        public void Init(SDLCity level, AIMap aimap, MMGameMode gameMode, MMWeather weather, string name)
        {
            this.aiMap = aimap;
            this.level = level;
            this.GameMode = gameMode;

            // Load network data
            string baiPath = AssetManager.CombinePath("city", $"{name}.bai");
            string supBaiPath = AssetManager.CombinePath("city", $"{name}_sup.bai");
            if (AssetManager.Exists(baiPath))
            {
                networkData = new AINetworkData();
                using (var stream = AssetManager.Open(baiPath))
                {
                    using (var reader = new BinaryReader(stream))
                    {
                        networkData.ReadBinary(reader);
                    }
                }
                if (AssetManager.Exists(supBaiPath))
                {
                    using (var stream = AssetManager.Open(supBaiPath))
                    {
                        using (var reader = new BinaryReader(stream))
                        {
                            networkData.ReadShortcutsBinary(reader);
                        }
                    }
                }
            }

            // init things that depend on the bai file
            if (networkData != null)
            {
                // Set speed limits based on aimap, and reverse paths
                if (aimap.LeftSidedTraffic)
                {
                    foreach (var road in networkData.Roads)
                    {
                        road.ReverseDirection();
                    }
                }
                foreach (var road in networkData.Roads)
                {
                    if (road.Flags.HasFlag(PathFlags.Freeway))
                    {
                        road.SpeedLimit = aimap.SpeedLimit + 12.5f;
                    }
                    else
                    {
                        road.SpeedLimit = aimap.SpeedLimit;
                    }
                }

                // Create road and intersection instances
                intersections.Clear();
                roads.Clear();
                shortcuts.Clear();

                foreach (var intersectionData in networkData.Intersections)
                {
                    intersections.Add(new IntersectionInstance(intersectionData));
                }
                foreach (var roadData in networkData.Roads)
                {
                    roads.Add(new RoadInstance(roadData));
                }
                foreach (var roadData in networkData.Shortcuts)
                {
                    shortcuts.Add(new RoadInstance(roadData));
                }

                // create the component map
                CreateComponentMaps();

                // init traffic pool
                InitTrafficPool(100, aimap.TrafficTypesAndDensity);

                // init ped pool
                var pedNames = (weather == MMWeather.Raining) ? aimap.BadWeatherPedModels : aimap.GoodWeatherPedModels;
                InitPedPool(aimap.PedestrianPoolSize, pedNames.ToArray());

                // init cable cars
                var cableCarPaths = DetermineCableCarPaths();
                foreach (var cableCarPath in cableCarPaths)
                {
                    InitCableCarLine(cableCarPath.Item1, cableCarPath.Item2);
                }

                InitControlDevices(aimap.SingleTrafficLightModel, aimap.DualTrafficLightModel);

                // create router
                router = new AIRouter(this);
            }

            // init police
            InitPolice(aiMap);
            InitOpponents(aiMap);

            // init obstacles
            InitObstacles();
        }

        public Vector3 GetRandomSpawn(int randomFactor, bool disallowFreeways = false, bool disallowAlleys = false)
        {
            // no network, spawn above origin
            var defaultSpawn = new Vector3(0.0f, 0.0f, 0.0f);
            if (networkData == null)
            {
                return defaultSpawn;
            }

            // find an intersection to spawn in
            var oldState = UnityEngine.Random.state;
            UnityEngine.Random.InitState((int)(trafficDensity * 10000.0f));

            Vector3 spawn = defaultSpawn;

            int intersectionIdx = (UnityEngine.Random.Range(0, networkData.Intersections.Count) * (1 + randomFactor)) % networkData.Intersections.Count;
            bool foundValidIntersection = false;
            int intersectionFindAttempts = 0;

            while (!foundValidIntersection && intersectionFindAttempts < 512)
            {
                var intersection = networkData.Intersections[intersectionIdx];
                bool isIntersectionValid = true;
                
                foreach (var road in intersection.Roads)
                {
                    if(disallowAlleys && road.Flags.HasFlag(PathFlags.Alleyway))
                    {
                        isIntersectionValid = false;
                    }
                    if (disallowFreeways && road.Flags.HasFlag(PathFlags.Freeway))
                    {
                        isIntersectionValid = false;
                    }
                }

                foundValidIntersection = isIntersectionValid;
                if (isIntersectionValid)
                {
                    spawn = intersection.Center;
                    break;
                }

                intersectionIdx = UnityEngine.Random.Range(0, networkData.Intersections.Count);
                intersectionFindAttempts++;
            }

            UnityEngine.Random.state = oldState;
            return spawn;
        }

        public Vector3 GetRandomSpawn()
        {
            return GetRandomSpawn(0);
        }

        public void SetCullRoom(int index)
        {
            if (index == activeCullingRoom) return;
            activeCullingRoom = index;
            AdjustAmbients();
        }

        public void RefreshAmbients()
        {
            DeactivateAll(trafficCarPool);
            DeactivateAll(pedestrianPool);
            populatedTrafficRoads.Clear();
            populatedPedRoads.Clear();
            AdjustAmbients();
        }

        public Intersection GetNearestIntersection(Vector3 point)
        {
            return GetNearestIntersection(new Vector2(point.x, point.z));
        }

        public Intersection GetNearestIntersection(Vector2 point)
        {
            float dist = float.MaxValue;
            Intersection last = null;

            foreach (var ints in networkData.Intersections)
            {
                float d = (new Vector2(ints.Center.x, ints.Center.z) - point).sqrMagnitude;
                if (d < dist)
                {
                    dist = d;
                    last = ints;
                }
            }

            return last;
        }


        private void Update()
        {
            RetireStrays(trafficCarPool, populatedTrafficRoads);
            RetireStrays(pedestrianPool, populatedPedRoads);

            // update our entities
            // pools can contain null entries (see InitTrafficPool), and one exception here stops everything below
            foreach (var cableCar in cableCars)
                cableCar.Update();
            if (trafficCarPool != null)
                foreach (var car in trafficCarPool)
                    if (car != null && car.Active) car.Update();
            if (pedestrianPool != null)
                foreach (var ped in pedestrianPool)
                    if (ped != null && ped.Active) ped.Update();
            foreach (var proxy in VehicleProxies)
                proxy.Update();

            // update active roads and intersections
            // todo: use culling data and only update active
            foreach (var road in roads)
            {
                if(road.Entities.Count != 0)
                    road.Update();
            }
            foreach (var intersection in intersections)
            {
                intersection.Update();
            }
        }

        private void OnDrawGizmos()
        {
            if (drawActiveCullRoadGizmos && activeCullingRoom > 0)
            {
                var data = networkData.CombinedCullRoads[activeCullingRoom - 1];
                foreach(var roadIndex in data)
                {
                    networkData.Roads[roadIndex].DrawGizmos();
                }
            }
            if (drawEntityGizmos)
            {
                if (trafficCarPool != null)
                    foreach (var car in trafficCarPool)
                        DrawEntityGizmo(car, trafficGizmoColor);

                if (pedestrianPool != null)
                    foreach (var ped in pedestrianPool)
                        DrawEntityGizmo(ped, pedestrianGizmoColor);

                foreach (var proxy in VehicleProxies)
                    DrawEntityGizmo(proxy, proxyGizmoColor);

                foreach (var cableCar in cableCars)
                    DrawEntityGizmo(cableCar, cableCarGizmoColor);
            }
        }

        private void OnDrawGizmosSelected()
        {
            OnDrawGizmos();
        }

        private static bool IsValidRotation(Quaternion q)
        {
            float lengthSq = q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w;
            return !float.IsNaN(lengthSq) && lengthSq > 1e-6f;
        }

        private static bool IsValidPosition(Vector3 v)
        {
            return !float.IsNaN(v.x) && !float.IsNaN(v.y) && !float.IsNaN(v.z);
        }

        private void DrawEntityGizmo(AIEntity entity, Color color)
        {
            if (entity == null) return;
            if (entity.RoomID == 0) return;

            Vector3 position = entity.Position;
            Quaternion rotation = entity.Rotation;

            // Unplaced or broken entity, nothing meaningful to draw.
            if (!IsValidPosition(position) || !IsValidRotation(rotation)) return;

            // TRS wants a unit quaternion; normalize in case of drift.
            rotation = Quaternion.Normalize(rotation);

            float left = entity.LeftSideDistance;
            float right = entity.RightSideDistance;
            float front = entity.FrontBumperDistance;
            float rear = entity.RearBumperDistance;

            Vector3 size = new Vector3(right - left, 2f, front - rear);
            Vector3 localCenter = new Vector3((right + left) * 0.5f, 1f, (front + rear) * 0.5f);

            Matrix4x4 prevMatrix = Gizmos.matrix;
            Gizmos.matrix = Matrix4x4.TRS(position, rotation, Vector3.one);

            Gizmos.color = color;
            Gizmos.DrawCube(localCenter, size);
            Gizmos.color = new Color(color.r, color.g, color.b, 1f);
            Gizmos.DrawWireCube(localCenter, size);

            Gizmos.color = forwardGizmoColor;
            Vector3 nose = new Vector3(0f, size.y * 0.5f, front);
            Gizmos.DrawLine(nose, nose + Vector3.forward * Mathf.Max(0.5f, entity.Speed));

            Gizmos.matrix = prevMatrix;

#if UNITY_EDITOR
            if (entity.RoadInfo.RoadInstance != null)
            {
                UnityEditor.Handles.Label(position + Vector3.up * 2.5f,
                $"Room {entity.RoomID}\n{entity.Speed:F1} m/s\n{entity.RoadInfo.RoadInstance.Id} Rail {entity.RoadInfo.RailIndex} Side {entity.RoadInfo.SideOfRoad}");
            }
            else
            {
                UnityEditor.Handles.Label(position + Vector3.up * 2.5f,
                $"Room {entity.RoomID}\n{entity.Speed:F1} m/s");
            }
#endif
        }
    }
}