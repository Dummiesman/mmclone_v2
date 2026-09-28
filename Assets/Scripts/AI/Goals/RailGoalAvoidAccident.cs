using UnityEngine;

namespace MM2.AI
{
    /// Swerves around an oncoming player and rejoins the rail further along.
    ///
    /// The car stays in its lane logically the whole time (so traffic behind still sees and follows it)
    /// and just layers a sideways offset over the rail pose: out and back in over 'length' meters,
    /// which is the curve-around-and-rejoin-further-up shape without ever leaving the rail.
    public class RailGoalAvoidAccident : RailGoal
    {
        private const float SwerveWidth = 2.5f;       // meters sideways at the widest point
        private const float SwerveTime = 1.2f;        // swerve length = speed * this...
        private const float MinSwerveLength = 10f;    // ...but never shorter than this
        private const float Cooldown = 1f;            // seconds before we'll swerve again

        private float length;
        private float amplitude;   // signed: + right, - left
        private float travelled;
        private float lastEndTime = -100f;

        public RailGoalAvoidAccident(AITrafficCar car) : base(car) { }

        /// Starts a swerve away from 'threatPosition' if there's room for one. False if not.
        public bool TryBegin(Vector3 threatPosition)
        {
            if (Time.time - lastEndTime < Cooldown)
                return false;

            if (!car.IsOnRoad || car.InIntersection || car.IsChangingLanes)
                return false;

            float len = Mathf.Max(MinSwerveLength, car.Speed * SwerveTime);
            if (car.DistanceToRoadEnd < len)
                return false; // would run into the intersection mid-swerve

            // Dodge away from the side the threat is on; dead centre goes right, toward the kerb.
            float lateral = Vector3.Dot(threatPosition - car.Position, car.Rotation * Vector3.right);
            amplitude = lateral > 0.25f ? -SwerveWidth : SwerveWidth;
            length = len;
            travelled = 0f;

            car.SetGoal(this);
            return true;
        }

        public override void Enter()
        {
            car.PlayHorn();
        }

        public override void Update()
        {
            float moved = car.DriveOnRail(false);

            if (!car.IsOnRoad || car.InIntersection)
            {
                car.SetGoal(car.DriveGoal);
                return;
            }

            travelled += moved;
            if (travelled >= length)
            {
                car.SetGoal(car.DriveGoal); // offset is back to zero here, so this is seamless
                return;
            }

            // Raised cosine: zero offset and zero slope at both ends.
            float w = 2f * Mathf.PI * (travelled / length);
            float offset = amplitude * 0.5f * (1f - Mathf.Cos(w));
            float slope = amplitude * Mathf.PI / length * Mathf.Sin(w);

            car.ApplySwerve(offset, slope, moved);
        }

        public override void Exit()
        {
            car.ClearSwerve();
            lastEndTime = Time.time;
        }
    }
}
