using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace MM2.AI
{
    public class VehicleProxyObstacle : EntityObstacle
    {
        private AIVehicleProxy proxy;

        public bool IsPlayer => proxy.IsPlayer;

        public override bool IsCar(VehCar car)
        {
            return car == proxy.Vehicle;
        }

        public VehicleProxyObstacle(AIVehicleProxy proxy) : base(proxy)
        {
            this.proxy = proxy;
        }
    }
}

