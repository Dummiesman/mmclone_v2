using UnityEngine;

namespace MM2.AI
{
    /// Knocked off the rail: physics owns the car's pose, but it keeps its slot on the rail so traffic
    /// stops behind it. Once it has come to rest for a few seconds we drive back onto the rail, unless this was the third accident (or regaining failed), in which case the
    /// car stays a wreck until the pool deactivates it.
    public class RailGoalInAccident : RailGoal
    {
        public const int MaxAccidents = 3;

        private const float RestSpeed = 0.5f;     // m/s, below this we count as stopped
        private const float RecoverDelay = 3f;    // seconds at rest before regaining the rail

        private float timer;

        public RailGoalInAccident(AITrafficCar car) : base(car) { }

        public override bool HazardLights => true;

        public override void Enter()
        {
            timer = 0f;
        }

        public override void Update()
        {
            car.SyncFromBody();
            car.TrackAnchor(); // keep our rail slot level with the wreck so traffic queues behind it

            if (car.IsWrecked || car.AccidentCount >= MaxAccidents)
                return;

            // Timer counts from coming to rest, so getting shoved again restarts it.
            if (car.Speed > RestSpeed)
            {
                timer = 0f;
                return;
            }

            timer += Time.deltaTime;
            if (timer >= RecoverDelay)
                car.SetGoal(car.RegainGoal);
        }
    }
}
