namespace MM2.AI
{
    public abstract class TrafficControlDevice
    {
        public readonly IntersectionInstance Intersection;

        public abstract bool CanEnterIntersection(AIEntity entity);
        public virtual void Update() { }
        public virtual void Reset() { }

        private TrafficControlDevice() { }
        public TrafficControlDevice(IntersectionInstance intersection)
        {
            Intersection = intersection;
        }
    }
}