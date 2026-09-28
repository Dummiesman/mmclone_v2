using System.Collections.Generic;
using UnityEngine;

namespace MM2.AI
{
    public class StopSignDevice : TrafficControlDevice
    {
        public bool InDepthCheck = false;

        public StopSignDevice(IntersectionInstance intersection) : base(intersection)
        {
        }

        private const float MinWaitTime = 1f;
        private const float StoppedSpeed = 0.1f;

        private readonly Dictionary<AIEntity, float> waitTimes = new Dictionary<AIEntity, float>();
        private readonly List<AIEntity> waitKeys = new List<AIEntity>();

        public override bool CanEnterIntersection(AIEntity entity)
        {
            if (!waitTimes.TryGetValue(entity, out float waitTime))
            {
                waitTimes[entity] = 0f;
            }

            if (InDepthCheck)
            {
                foreach (var cd in Intersection.ControlDevices)
                {
                    if (cd == this)
                        continue;
                    if (cd.CanEnterIntersection(entity))
                        return false;
                }
            }

            //enforce a min wait time
            if (waitTime >= MinWaitTime && Intersection.NumVehiclesInIntersection == 0)
            {
                waitTimes.Remove(entity);
                return true;
            }
            else
            {
                return false;
            }
        }

        public override void Update()
        {
            base.Update();

            // Don't write to the dictionary while enumerating it, that throws.
            waitKeys.Clear();
            waitKeys.AddRange(waitTimes.Keys);
            foreach (var entity in waitKeys)
            {
                // Entity moved on, was repositioned, or is now heading somewhere else.
                if (entity.CurrentDestIntersection != Intersection)
                {
                    waitTimes.Remove(entity);
                    continue;
                }

                // Only count time actually spent stopped.
                if (entity.Speed <= StoppedSpeed)
                    waitTimes[entity] += Time.deltaTime;
            }
        }
    }
}