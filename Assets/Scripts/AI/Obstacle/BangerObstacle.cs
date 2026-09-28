using UnityEngine;

namespace MM2.AI
{
    public class BangerObstacle : Obstacle
    {
        private float yRadius = 0.5f;
        private float breakThreshold = float.MaxValue;
        private UnhitBangerInstance instance;

        public override float Radius
        {
            get
            {
                return Mathf.Min(yRadius, 2.0f);
            }
        }

        public override Vector3 Position
        {
            get
            {
                return instance.transform.position;
            }
        }

        public override bool IsBroken
        {
            get
            {
                return instance.Broken;
            }
        }

        public override float BreakThreshold
        {
            get => breakThreshold;
        }

        public override float IsBlockingTarget(Vector3 from, Vector3 to, float pad, float width)
        {
            Vector3 dir = (to - from).normalized;   // full 3D normalize here, unlike the vehicle version
            Vector3 perp = PerpXZ(dir);
            float segLength = SegLengthXZ(from, to);
            float limit = Radius + width * 0.5f + 1.0f;

            return Blocks(Position, from, dir, perp, segLength, pad, limit, out float along) ? along : -1f;
        }

        public override void PreAvoid(Vector3 from, Vector3 dir, float pad, out Vector3 left, out Vector3 right)
        {
            Vector3 center = Position;
            Vector3 perp = PerpXZ((center - from).normalized);
            float r = Radius + pad;

            // y comes from the obstacle alone; the original adds 0.0 on that axis
            right = new Vector3(center.x + perp.x * r, center.y, center.z + perp.z * r);
            left = new Vector3(center.x - perp.x * r, center.y, center.z - perp.z * r);
        }

        public BangerObstacle(UnhitBangerInstance instance)
        {
            this.instance = instance;
            var data = instance.Data;

            if(data != null)
            {
                // impulse limit is in Ns but BreakThreshold is Ns2
                yRadius = data.YRadius;
                breakThreshold = data.ImpulseLimit * data.ImpulseLimit;
            }
        }
    }
}