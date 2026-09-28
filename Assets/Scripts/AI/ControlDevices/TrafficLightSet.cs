using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace MM2.AI
{
    public class TrafficLightSet
    {
        public bool IsFourWay => Lights.Count == 4;
        public readonly List<TrafficLightDevice> Lights = new List<TrafficLightDevice>();
        private int _lightIndex = 0;

        public void Init()
        {
            Reset();
        }

        public void Reset()
        {
            //special case, traffic will cross both ways
            if (Lights.Count == 4)
            {
                Lights[0].State = TrafficLightState.Green;
                Lights[1].State = TrafficLightState.Red;
                Lights[2].State = TrafficLightState.Green;
                Lights[3].State = TrafficLightState.Red;
            }
            else
            {
                _lightIndex = 0;

                //set all lights to red except one
                for (int i = 1; i < Lights.Count; i++)
                {
                    Lights[i].State = TrafficLightState.Red;
                    Lights[i].TimerEnabled = false;
                }
                Lights[0].State = TrafficLightState.Green;
                Lights[0].TimerEnabled = true;
            }
        }

        public void Update()
        {
            //these lights can manage themeslves
            if (Lights.Count == 4 || Lights.Count <= 1)
                return;

            var light = Lights[_lightIndex];
            if (light.State == TrafficLightState.Red)
            {
                //disable timer
                light.TimerEnabled = false;

                //move to next light
                _lightIndex++;
                if (_lightIndex == Lights.Count)
                    _lightIndex = 0;

                Lights[_lightIndex].TimerEnabled = true;
                Lights[_lightIndex].State = TrafficLightState.Green;
            }
        }

    }
}