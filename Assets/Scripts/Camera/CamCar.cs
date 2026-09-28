using UnityEngine;
using static UnityEngine.GraphicsBuffer;

namespace MM2.Camera
{
    public class camCarCS : camAppCS
    {
        [SerializeField] protected VehCar Car;

        // Parsed as "ReverseOn": 0 = off, 1 = automatic, -1 = forced.
        public int ReverseMode = 1;

        public virtual void SetCar(VehCar car)
        {
            Car = car;
            Target = (car != null) ? car.transform : null;
        }

        public VehCar GetCar()
        {
            return Car;
        }
    }
}