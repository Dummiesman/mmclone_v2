namespace MM2.AI
{
    public class NeverStopDevice : TrafficControlDevice
    {
        public NeverStopDevice(IntersectionInstance intersection) : base(intersection)
        {
        }

        public override bool CanEnterIntersection(AIEntity entity)
        {
            return true;
        }
    }
}