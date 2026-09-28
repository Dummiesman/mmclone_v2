using System.Collections.Generic;
using UnityEngine;

namespace MM2.AI
{
    public struct RailTarget
    {
        public RoadPositioningInfo Info;
        public float Progress;      // normalized along the road
        public Vector3 Position;
        public Vector3 Forward;
    }

    public static class RailQuery
    {
        private const float CoarseStep = 4f;        // meters between samples on the first pass
        private const int RefineIterations = 12;    // ternary search steps around the best sample
        private const float MinHeadingDot = -0.2f;  // skip rails going the other way (U-turns look awful)

        /// Nearest point on any rail of 'railType' within 'maxDistance' (horizontally) that runs roughly
        /// along 'forward'. Runs once per regain, so brute force is fine for now; if it shows up in the
        /// profiler, only enumerate roads in the car's room.
        public static bool FindNearestRail(AINetwork network, RailType railType, Vector3 position, Vector3 forward,
                                           float maxDistance, out RailTarget result)
        {
            result = default;
            float bestSq = maxDistance * maxDistance;
            bool found = false;

            foreach (var info in EnumerateRails(network, railType))
            {
                var road = info.RoadInstance;
                if (road == null)
                    continue;

                float length = road.Road.Length;
                if (length <= 0.01f)
                    continue;

                // Coarse pass.
                int steps = Mathf.Max(1, Mathf.CeilToInt(length / CoarseStep));
                int bestIndex = -1;
                float laneBestSq = float.MaxValue;
                for (int i = 0; i <= steps; i++)
                {
                    float d = DistanceSq(info, i / (float)steps, position);
                    if (d < laneBestSq)
                    {
                        laneBestSq = d;
                        bestIndex = i;
                    }
                }

                if (bestIndex < 0 || laneBestSq > bestSq + CoarseStep * CoarseStep)
                    continue; // nowhere near us

                // Refine between the neighbouring samples.
                float lo = Mathf.Max(0f, (bestIndex - 1) / (float)steps);
                float hi = Mathf.Min(1f, (bestIndex + 1) / (float)steps);
                for (int it = 0; it < RefineIterations; it++)
                {
                    float m1 = lo + (hi - lo) / 3f;
                    float m2 = hi - (hi - lo) / 3f;
                    if (DistanceSq(info, m1, position) < DistanceSq(info, m2, position)) hi = m2;
                    else lo = m1;
                }

                float progress = 0.5f * (lo + hi);
                if (!TryGetRailPose(info, progress, out Vector3 railPos, out Vector3 railFwd))
                    continue;

                float dSq = FlatSq(railPos - position);
                if (dSq >= bestSq)
                    continue;

                Vector3 flatFwd = railFwd;
                flatFwd.y = 0f;
                if (flatFwd.sqrMagnitude < 1e-6f || Vector3.Dot(forward, flatFwd.normalized) < MinHeadingDot)
                    continue;

                bestSq = dSq;
                found = true;
                result = new RailTarget
                {
                    Info = info,
                    Progress = progress,
                    Position = railPos,
                    Forward = railFwd.normalized,
                };
            }

            return found;
        }

        private static float DistanceSq(RoadPositioningInfo info, float progress, Vector3 position)
        {
            return TryGetRailPose(info, progress, out Vector3 p, out _) ? FlatSq(p - position) : float.MaxValue;
        }

        private static float FlatSq(Vector3 v)
        {
            v.y = 0f;
            return v.sqrMagnitude;
        }

        // ---------------------------------------------------------------------------------------------
        // Adapters: wire these two to your road data. Until then they find nothing, so knocked-off cars
        // just stay wrecks instead of regaining the rail. Everything else works without them.
        // ---------------------------------------------------------------------------------------------

        /// Every rail of the given type: one RoadPositioningInfo per (road, side, rail index), built the
        /// same way your spawner builds one before calling SetRoad.
        private static IEnumerable<RoadPositioningInfo> EnumerateRails(AINetwork network, RailType railType)
        {
#warning RailQuery.EnumerateRails is not wired up yet
            // e.g.
            // foreach (var road in network.Roads)
            //     foreach (var side in <both sides>)
            //         for (int rail = 0; rail < road.GetLaneCount(side, railType); rail++)
            //             yield return <RoadPositioningInfo for road, side, rail>;
            yield break;
        }

        /// World position and travel direction at 'progress' along a rail. Must match what
        /// PositionAlongPath(progress) produces for a car placed on that rail.
        public static bool TryGetRailPose(RoadPositioningInfo info, float progress, out Vector3 position, out Vector3 forward)
        {
#warning RailQuery.TryGetRailPose is not wired up yet
            position = default;
            forward = Vector3.forward;
            return false;
        }
    }
}
