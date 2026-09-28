// Port of aiMap::CalcRoute, wired to AINetwork / AINetworkData.
//
// CalcRoute itself is ported 1:1. The helpers it calls that weren't in the decompile
// (PositionToAIMapComp, aiPath::Direction, PredictIntersectionPath) are rebuilt from your data
// and marked APPROXIMATION. They're virtual so you can drop in exact versions later.
//
// Uses AINetwork's CompType / AIComponent and AINetwork.MapComponents.
//
// Usage (after AINetwork.Init):
//   var router = new AIRouter(aiNetwork);
//   router.GetNeighbourRooms = room => /* SDLCity room adjacency */;   // optional
//   List<int> route = router.CalcRoute(car.position, car.forward, target);

using System;
using System.Collections.Generic;
using UnityEngine;

namespace MM2.AI
{
    public class AIRouter
    {
        static bool Same(AIComponent a, AIComponent b) => a.Type == b.Type && a.Id == b.Id;
        static AIComponent MakeComp(CompType type, int id) => new AIComponent { Type = type, Id = id };

        const float Infinity = 9999999f;
        const int NoNode = -1;

        /// <summary>lvlLevel::GetNeighbours. Optional; without it only the room itself is searched.</summary>
        public Func<int, IEnumerable<int>> GetNeighbourRooms;

        readonly AINetwork _net;
        AINetworkData Data => _net.Data;
        int NumIntersections => Data.Intersections.Count;

        // Scratch (routingNodes / routingStuff / v109 in the original)
        readonly float[] _dist;
        readonly int[] _pred;
        readonly bool[] _inOpen;
        readonly int[] _out;
        readonly List<int> _open = new List<int>();
        readonly List<int> _targets = new List<int>();
        readonly List<AIComponent> _src = new List<AIComponent>();
        readonly List<AIComponent> _dst = new List<AIComponent>();
        readonly List<AIComponent> _roomComps = new List<AIComponent>();

        public AIRouter(AINetwork network)
        {
            _net = network;
            int n = NumIntersections;
            _dist = new float[n];
            _pred = new int[n];
            _inOpen = new bool[n];
            _out = new int[n + 2];
        }

        /// <summary>
        /// Intersection ids to drive through. Empty means already there, or no route.
        /// Directional mode (shortestPath = false) starts with [behind, ahead, ...].
        /// Rooms default to SDLCity.FindRoomIdWithWarps when left at -1.
        /// </summary>
        public List<int> CalcRoute(Vector3 srcPos, Vector3 srcForward, Vector3 destPos,
                                   int sourceRoom = -1, int destRoom = -1, bool shortestPath = false)
        {
            int count = CalcRouteInternal(srcPos, srcForward, destPos, sourceRoom, destRoom, shortestPath);
            var result = new List<int>(count);
            for (int i = 0; i < count; i++) result.Add(_out[i]);
            return result;
        }

        public int CalcRoute(in Vector3 srcPos, in Vector3 srcForward, in Vector3 destPosition,
                       int[] outIntersectionIds, out int outIntersectionCount,
                       int sourceRoom = -1, int destRoom = -1, bool shortestPath = false)
        {
            if (outIntersectionIds == null)
                throw new ArgumentNullException(nameof(outIntersectionIds));

            int count = CalcRouteInternal(srcPos, srcForward, destPosition,
                                          sourceRoom, destRoom, shortestPath);

            if (count > outIntersectionIds.Length)
                count = outIntersectionIds.Length;

            for (int i = 0; i < count; i++)
                outIntersectionIds[i] = (short)_out[i];

            outIntersectionCount = (short)count;
            return outIntersectionCount;
        }

        int CalcRouteInternal(Vector3 srcPos, Vector3 srcForward, Vector3 destPos,
                              int sourceRoom, int destRoom, bool shortestPath)
        {
            int count = 0;
            _open.Clear();
            for (int i = 0; i < NumIntersections; i++)
            {
                _dist[i] = Infinity;
                _inOpen[i] = false;
            }

            _net.PositionToAIMapComp(srcPos, _src, ref sourceRoom);
            _net.PositionToAIMapComp(destPos, _dst, ref destRoom);

            foreach (var s in _src)
                foreach (var d in _dst)
                    if (Same(s, d)) return 0;

            AIComponent src0 = _src[0];
            int start;

            // ---- 1. Seed from the source ----
            switch (src0.Type)
            {
                case CompType.None:
                    start = NoNode;
                    if (!SeedFromSourceRoom(sourceRoom, srcPos, _dst[0])) return 0;
                    break;

                case CompType.Road:
                case CompType.Shortcut:
                    {
                        Road road = Data.GetRoad(src0.Id);
                        int l = road.LeftEndData.IntersectionID;   // lInterPtr: at the last section
                        int r = road.RightEndData.IntersectionID;  // rInterPtr: at section 0

                        if (shortestPath)
                        {
                            float along = CenterDist(road, srcPos); // measured from section 0 (the right end)
                            if (l >= 0) SeedNode(l, road.Length - along);
                            if (r >= 0) SeedNode(r, along);
                            start = NoNode;
                            count = 1; // slot 0 filled in at the end
                        }
                        else
                        {
                            if (Direction(road, srcPos, srcForward)) { _out[0] = r; _out[1] = l; }
                            else { _out[0] = l; _out[1] = r; }
                            if (_out[1] < 0) return 0; // heading into a dead end (original would crash)
                            count = 2;
                            start = _out[1];
                            SeedNeighboursOf(start, _out[0]); // no U-turn
                            _dist[start] = 0f;
                        }
                        break;
                    }

                case CompType.Intersection:
                    if (shortestPath)
                    {
                        _out[0] = src0.Id;
                        count = 1;
                        start = src0.Id;
                    }
                    else
                    {
                        int next = PredictExit(src0.Id, srcForward);
                        if (next < 0) return 0;
                        _out[0] = src0.Id;
                        _out[1] = next;
                        count = 2;
                        start = next;
                    }
                    SeedNeighboursOf(start, count == 2 ? _out[0] : NoNode);
                    _dist[start] = 0f;
                    break;

                default:
                    Debug.LogWarning("Unknown Map Component type: SrcMatrix, AIRouter.CalcRoute");
                    start = NoNode;
                    break;
            }

            for (int i = 0; i < NumIntersections; i++)
                _pred[i] = start;

            // ---- 2. Targets from the destination ----
            _targets.Clear();
            foreach (var d in _dst)
            {
                switch (d.Type)
                {
                    case CompType.None:
                        if (!CollectTargetsFromDestRoom(destRoom, destPos, src0)) return 0;
                        break;
                    case CompType.Road:
                    case CompType.Shortcut:
                        AddTarget(Data.GetRoad(d.Id).RightEndData.IntersectionID, false);
                        AddTarget(Data.GetRoad(d.Id).LeftEndData.IntersectionID, false);
                        break;
                    case CompType.Intersection:
                        AddTarget(d.Id, false);
                        break;
                }
            }

            if (start != NoNode && _targets.Contains(start))
                return count == 1 ? 0 : count;

            // ---- 3. Dijkstra (linear-scan open list, stop at first target) ----
            int hit = -1;
            while (_open.Count > 0)
            {
                int u = NoNode;
                float best = Infinity;
                foreach (int n in _open)
                    if (best > _dist[n]) { best = _dist[n]; u = n; }
                if (u == NoNode) break;

                hit = _targets.IndexOf(u);
                if (hit >= 0) break;

                OpenRemove(u);

                foreach (Road road in Data.Intersections[u].Roads) // already in the game's Paths[] order
                {
                    int other = OtherEnd(road, u);
                    if (other < 0) continue;
                    float nd = road.Length + _dist[u];
                    if (nd < _dist[other])
                    {
                        _dist[other] = nd;
                        OpenAdd(other);
                        _pred[other] = u;
                    }
                }
            }
            if (hit < 0) return 0;

            // ---- 4. Reconstruct after any pre-filled slots ----
            int end = _targets[hit];
            for (int k = end; k != start; k = _pred[k])
                count++;

            int v = end, m = 0;
            while (v != start)
            {
                _out[count - m - 1] = v;
                m++;
                v = _pred[v];
            }
            if (count - m - 1 >= 0)
                _out[count - m - 1] = v;

            // ---- 5. Slot 0 fix-up ----
            if (src0.Type == CompType.Road || src0.Type == CompType.Shortcut)
            {
                Road road = Data.GetRoad(src0.Id);
                int l = road.LeftEndData.IntersectionID, r = road.RightEndData.IntersectionID;
                _out[0] = _out[1] == l ? r : l; // -1 if the road is a dead end
            }
            else if (src0.Type == CompType.Intersection)
            {
                _out[0] = src0.Id;
            }

            return count;
        }

        // ================= Seeding / targets (ported) =================

        bool SeedFromSourceRoom(int room, Vector3 p, AIComponent dst0)
        {
            bool seeded = false;

            foreach (int rm in NeighbourRooms(room))
            {
                GetRoomComps(rm, _roomComps);
                foreach (var c in _roomComps)
                {
                    if (c.Type == CompType.Intersection)
                    {
                        SeedNode(c.Id, FlatDist(p, Data.Intersections[c.Id].Center));
                        seeded = true;
                    }
                    if (Same(c, dst0)) return false;
                }
            }
            if (seeded) return true;

            // Fallback: ends of roads in neighbouring rooms (Road only, like the original).
            foreach (int rm in NeighbourRooms(room))
            {
                GetRoomComps(rm, _roomComps);
                foreach (var c in _roomComps)
                {
                    if (c.Type != CompType.Road) continue;
                    if (SeedEnd(Data.GetRoad(c.Id).LeftEndData.IntersectionID, p)) seeded = true;
                    if (SeedEnd(Data.GetRoad(c.Id).RightEndData.IntersectionID, p)) seeded = true;
                }
            }

            // NOT ORIGINAL: nothing nearby (unknown room, no neighbour data). Use the nearest intersection.
            if (!seeded)
            {
                var nearest = _net.GetNearestIntersection(p);
                if (nearest != null) SeedEnd(nearest.Id, p);
            }
            return true;
        }

        bool CollectTargetsFromDestRoom(int room, Vector3 p, AIComponent src0)
        {
            int before = _targets.Count;

            foreach (int rm in NeighbourRooms(room))
            {
                GetRoomComps(rm, _roomComps);
                foreach (var c in _roomComps)
                {
                    if (c.Type == CompType.Road)
                    {
                        AddTarget(Data.GetRoad(c.Id).LeftEndData.IntersectionID, true);
                        AddTarget(Data.GetRoad(c.Id).RightEndData.IntersectionID, true);
                    }
                    if (Same(c, src0)) return false;
                }
            }

            // NOT ORIGINAL: nothing nearby. Use the nearest intersection.
            if (_targets.Count == before)
            {
                var nearest = _net.GetNearestIntersection(p);
                if (nearest != null) AddTarget(nearest.Id, true);
            }
            return true;
        }

        void SeedNeighboursOf(int node, int exclude)
        {
            foreach (Road road in Data.Intersections[node].Roads)
            {
                int other = OtherEnd(road, node);
                if (other < 0) continue;
                if (exclude >= 0 && other == exclude) continue;
                SeedNode(other, road.Length);
            }
        }

        bool SeedEnd(int id, Vector3 p)
        {
            if (id < 0) return false;
            SeedNode(id, FlatDist(p, Data.Intersections[id].Center));
            return true;
        }

        // Overwrites rather than keeping the minimum, like the original.
        void SeedNode(int id, float distance)
        {
            _dist[id] = distance;
            OpenAdd(id);
        }

        void AddTarget(int id, bool unique)
        {
            if (id < 0) return;
            if (unique && _targets.Contains(id)) return;
            _targets.Add(id);
        }

        // Assumes AddRoutingNode pushes to the head and ignores duplicates.
        void OpenAdd(int id)
        {
            if (_inOpen[id]) return;
            _inOpen[id] = true;
            _open.Insert(0, id);
        }

        void OpenRemove(int id)
        {
            if (!_inOpen[id]) return;
            _inOpen[id] = false;
            _open.Remove(id);
        }

        // Original: other = rInter; if (inter == rInter) other = lInter;
        static int OtherEnd(Road road, int inter)
        {
            return road.RightEndData.IntersectionID == inter
                ? road.LeftEndData.IntersectionID
                : road.RightEndData.IntersectionID;
        }

        // ================= Rooms =================

        IEnumerable<int> NeighbourRooms(int room)
        {
            if (room < 0) return Array.Empty<int>();
            return GetNeighbourRooms != null ? GetNeighbourRooms(room) : new[] { room };
        }

        // Stand-in for ComponentMap[room]: the room's intersection, then its roads.
        void GetRoomComps(int room, List<AIComponent> outComps)
        {
            outComps.Clear();
            outComps.AddRange(_net.GetRoomComponents(room));
        }

        // ================= Helpers rebuilt from your data =================

        /// <summary>aiPath::CenterDist: distance along the centreline from section 0.</summary>
        protected virtual float CenterDist(Road road, Vector3 p)
        {
            return ProjectOntoRoad(road, p, out _);
        }

        /// <summary>
        /// APPROXIMATION of aiPath::Direction. True = heading along the section order,
        /// towards the left end (where right-side traffic goes).
        /// </summary>
        protected virtual bool Direction(Road road, Vector3 p, Vector3 forward)
        {
            if (road.NumSections < 2) return true;
            ProjectOntoRoad(road, p, out int seg);
            var pts = road.SectionCenterCurve.Points;
            Vector2 tangent = XZ(pts[seg + 1].Position) - XZ(pts[seg].Position);
            return Vector2.Dot(XZ(forward), tangent) >= 0f;
        }

        /// <summary>APPROXIMATION of PredictIntersectionPath: the exit best aligned with the heading.</summary>
        protected virtual int PredictExit(int inter, Vector3 forward)
        {
            int best = NoNode;
            float bestDot = float.NegativeInfinity;
            Vector2 f = XZ(forward);

            foreach (Road road in Data.Intersections[inter].Roads)
            {
                int other = OtherEnd(road, inter);
                if (other < 0 || road.NumSections < 2) continue;

                var pts = road.SectionCenterCurve.Points;
                int n = road.NumSections;
                Vector2 dir = road.RightEndData.IntersectionID == inter
                    ? XZ(pts[1].Position) - XZ(pts[0].Position)          // leaves from section 0
                    : XZ(pts[n - 2].Position) - XZ(pts[n - 1].Position); // leaves from the last section

                float dot = Vector2.Dot(f, dir.normalized);
                if (dot > bestDot) { bestDot = dot; best = other; }
            }
            return best;
        }

        // Nearest point on the centreline (XZ). Returns distance along from section 0.
        static float ProjectOntoRoad(Road road, Vector3 p, out int segment)
        {
            segment = 0;
            if (road.NumSections < 2) return 0f;

            var pts = road.SectionCenterCurve.Points;
            Vector2 q = XZ(p);
            float bestDist = float.MaxValue, bestAlong = 0f;

            for (int i = 0; i < road.NumSections - 1; i++)
            {
                Vector2 a = XZ(pts[i].Position), ab = XZ(pts[i + 1].Position) - a;
                float len2 = ab.sqrMagnitude;
                float t = len2 > 0f ? Mathf.Clamp01(Vector2.Dot(q - a, ab) / len2) : 0f;
                float d = (a + ab * t - q).sqrMagnitude;
                if (d < bestDist)
                {
                    bestDist = d;
                    segment = i;
                    bestAlong = Mathf.Lerp(road.SectionCenterDistances[i], road.SectionCenterDistances[i + 1], t);
                }
            }
            return bestAlong;
        }

        static Vector2 XZ(Vector3 v) => new Vector2(v.x, v.z);
        static float FlatDist(Vector3 a, Vector3 b) => Vector2.Distance(XZ(a), XZ(b));
    }
}