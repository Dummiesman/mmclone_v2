namespace MM2.AI
{
    /// A traffic car's current behaviour. Plain class, not a MonoBehaviour: the car is a pooled plain C#
    /// object, so it owns one instance of each goal and swaps between them with AITrafficCar.SetGoal.
    public abstract class RailGoal
    {
        protected readonly AITrafficCar car;

        protected RailGoal(AITrafficCar car)
        {
            this.car = car;
        }

        public virtual void Enter() { }
        public virtual void Exit() { }
        public abstract void Update();

        /// Non-null to take over the indicators (-1 left, 1 right, 0 off).
        public virtual int? TurnSignalOverride => null;

        /// Both indicators flashing together.
        public virtual bool HazardLights => false;
    }
}
