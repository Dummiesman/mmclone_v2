using UnityEngine;

namespace MM2.AI
{
    /// Normal traffic: follow the rail, change lanes, take random turns at intersections.
    /// Hands off to AvoidAccident when a player is about to hit us head on.
    public class RailGoalRandomDrive : RailGoal
    {
        public RailGoalRandomDrive(AITrafficCar car) : base(car) { }

        public override void Update()
        {
            car.DriveOnRail(true);

            if (car.FindOncomingThreat(out Vector3 threat, out var threatEntity))
                car.AvoidGoal.TryBegin(threatEntity);
        }
    }
}
