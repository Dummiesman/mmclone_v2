using UnityEngine;

namespace MM2.AI
{
    public class EntityObstacle : Obstacle
    {
        public AIEntity Entity => entity;
        private AIEntity entity;

        public override float Radius
        {
            get
            {
                return entity.Radius;
            }
        }

        public override Vector3 Position
        {
            get
            {
                return entity.Position;
            }
        }

        public override bool IsBroken
        {
            get
            {
                return false;
            }
        }

        public override float IsBlockingTarget(Vector3 from, Vector3 to, float pad, float width)
        {
            Vector3 flat = (to - from).Flatten();
            Vector3 dir = flat.normalized;
            Vector3 perp = PerpXZ(dir);
            float segLength = SegLengthXZ(from, to);
            float limit = width * 0.5f + 1.0f;

            var corners = BoxCorners();
            for (int i = 0; i < 4; i++)
                if (Blocks(corners[i], from, dir, perp, segLength, pad, limit, out float along))
                    return along;

            return -1f;
        }

        Vector3[] BoxCorners()
        {
            Vector3 center = entity.Position;
            Vector3 rightAxis = entity.Rotation * Vector3.right;
            Vector3 fwdAxis = entity.Rotation * Vector3.forward;

            Vector3 f = fwdAxis * entity.FrontBumperDistance;
            Vector3 b = fwdAxis * entity.RearBumperDistance;
            Vector3 l = rightAxis * entity.LeftSideDistance;
            Vector3 r = rightAxis * entity.RightSideDistance;

            return new[]
            {
                center + f + l,   // front left
                center + f + r,   // front right
                center + b + l,   // rear left
                center + b + r,   // rear right
            };
        }

        public override void PreAvoid(Vector3 from, Vector3 dir, float pad, out Vector3 left, out Vector3 right)
        {
            var corners = BoxCorners();

            var points = new Vector3[8];
            for (int i = 0; i < 4; i++)
            {
                Vector3 perp = PerpXZ((corners[i] - from).normalized) * pad; // keeps the y term, as the original does
                points[2 * i] = corners[i] + perp;
                points[2 * i + 1] = corners[i] - perp;
            }

            Vector3 d = dir.normalized;
            Vector3 side = PerpXZ(d);

            int minIdx = 0, maxIdx = 0;
            float minAng = 99999f, maxAng = -99999f;
            for (int i = 0; i < 8; i++)
            {
                Vector3 v = points[i] - from;
                float ang = Mathf.Atan2(v.x * side.x + v.z * side.z, v.x * d.x + v.z * d.z);
                if (ang < minAng) { minAng = ang; minIdx = i; }
                if (ang > maxAng) { maxAng = ang; maxIdx = i; }
            }

            left = points[minIdx];
            right = points[maxIdx];
        }

        /// <summary>Which planned path this entity is driving on, and its travel-order vertex.</summary>
        public override int CurrentRoadIdx(Road[] paths, int[] sides, out int vertex)
        {
            vertex = 0;

            var instance = entity.RoadInfo.RoadInstance;
            if (instance == null) return -1;

            for (int i = 0; i < 3; i++)
            {
                var path = paths[i];
                if (path == null || path.Id != instance.Id) continue;

                path.GetInfoAtPoint(entity.Position, out _, out int section, out _);
                if (section < 0) section = 0;
                vertex = sides[i] != 0 ? section : path.NumSections - section - 1;
                return i;
            }
            return -1;
        }


        public EntityObstacle(AIEntity entity)
        {
            this.entity = entity;
        }
    }
}