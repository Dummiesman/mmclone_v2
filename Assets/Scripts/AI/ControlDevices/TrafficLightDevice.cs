
using UnityEngine;

namespace MM2.AI
{
    public class TrafficLightDevice : TrafficControlDevice
    {
        private const float longTimer = 5.0f;
        private const float shortTimer = 1.5f;
        private float currentTimer = float.MaxValue;

        public TrafficLightInstance Instance;
        public bool TimerEnabled = true;

        private TrafficLightState _currentState;
        private TrafficLightState nextState;
        public TrafficLightState State
        {
            get
            {
                return _currentState;
            }
            set
            {
                _currentState = value;
                if (Instance != null) Instance.LightState = value;
                SetNextState();
            }
        }

        public TrafficLightDevice(IntersectionInstance intersection) : base(intersection)
        {
        }

        public override void Update()
        {
            if (!TimerEnabled)
                return;

            currentTimer -= Time.deltaTime;
            if (currentTimer <= 0f)
                State = nextState; // setter schedules the following state and timer
        }

        void SetNextState()
        {
            if (State == TrafficLightState.Red)
            {
                // Red lasts as long as the cross traffic's green + yellow, otherwise both directions overlap.
                currentTimer = longTimer + shortTimer;
                nextState = TrafficLightState.Green;
            }
            else if (State == TrafficLightState.Yellow)
            {
                currentTimer = shortTimer;
                nextState = TrafficLightState.Red;
            }
            else if (State == TrafficLightState.Green)
            {
                currentTimer = longTimer;
                nextState = TrafficLightState.Yellow;
            }
        }

        public override bool CanEnterIntersection(AIEntity entity)
        {
            if(Instance != null && Instance.Broken)
            {
                return false;
            }

            if (entity.RailType == RailType.Pedestrian)
            {
                // use the pedestrian light state
                return State != TrafficLightState.Red;
            }

            // other entities
            if (Intersection.IntersectionLightSet.IsFourWay && entity.NextRoadInfo.RoadInstance != null)
            {
                for (int i = 0; i < Intersection.EntitiesInIntersection.Count; i++)
                {
                    var isEntity = Intersection.EntitiesInIntersection[i];
                    if (isEntity.NextRoadInfo.RoadInstance == null || (isEntity.NextRoadInfo.RoadInstance.Id
                                                                       != entity.RoadInfo.RoadInstance.Id))
                        continue;

                    //someone is crossing straight, we must wait for them to turn left
                    if (isEntity.NextRoadInfo.Relation == 0 && entity.NextRoadInfo.Relation < 0)
                    {
                        return false;
                    }
                }
            }

            // no four way logic
            return State != TrafficLightState.Red;
        }
    }
}