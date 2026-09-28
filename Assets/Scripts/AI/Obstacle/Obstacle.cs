using UnityEngine;

namespace MM2.AI
{
    public abstract class Obstacle
    {
        // Helpers
        protected static bool Blocks(Vector3 point, Vector3 from, Vector3 dir, Vector3 perp,
                     float segLength, float pad, float limit, out float along)
        {
            Vector3 v = point - from;
            along = v.x * dir.x + v.z * dir.z;
            float lateral = v.x * perp.x + v.z * perp.z;
            float ang = Mathf.Atan2(lateral, along);

            return lateral > -limit && lateral < limit
                && along > 0f && along < segLength + pad
                && ang > -0.7f && ang < 0.7f;
        }

        /// <summary>(-z, y, x) in MM2 coordinates; the 2D cross flips if Z was mirrored on import.</summary>
        protected static Vector3 PerpXZ(Vector3 v)
        {
            const float h = AiVehiclePhysics.Handedness;
            return new Vector3(-v.z * h, v.y, v.x * h);
        }

        protected static float SegLengthXZ(Vector3 from, Vector3 to)
        {
            float dx = from.x - to.x, dz = from.z - to.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        // Obstacle
        public abstract float Radius { get; }
        public abstract Vector3 Position { get; }
        public abstract bool IsBroken { get; }
        public virtual float BreakThreshold => float.PositiveInfinity;

        /// <summary>True when this obstacle is the given car, so a car never avoids itself.</summary>
        public virtual bool IsCar(VehCar car) => false;

        /// <summary>Opponent racer id (aiRouteRacer rr4); -1 for anything else.</summary>
        public virtual int RacerId => -1;

        /// <summary>
        /// How far along from->to this obstacle blocks a corridor of the given width
        /// </summary>
        public abstract float IsBlockingTarget(Vector3 from, Vector3 to, float pad, float width);

        /// <summary>
        /// vtable+0x1C. The two points to aim at to get past this obstacle, coming from 'from'
        /// and heading along 'dir'. Left is the lower-angle point, right the higher.
        /// </summary>
        public abstract void PreAvoid(Vector3 from, Vector3 dir, float pad, out Vector3 left, out Vector3 right);

        public virtual int CurrentRoadIdx(Road[] paths, int[] sides, out int vertex)
        {
            vertex = 0;

            // no road of its own (props, anything static): locate by position instead
            Vector3 pos = Position;
            for (int i = 0; i < 3; i++)
            {
                var path = paths[i];
                if (path == null) continue;

                path.GetInfoAtPoint(pos, out _, out int section, out _);
                if (section < 0) continue;

                vertex = sides[i] != 0 ? section : path.NumSections - section - 1;
                return i;
            }
            return -1;
        }
    }
}