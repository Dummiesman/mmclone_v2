namespace MM2.AI
{
    public class AlwaysStopDevice : TrafficControlDevice
    {
        public AlwaysStopDevice(IntersectionInstance intersection) : base(intersection)
        {
        }

        public override bool CanEnterIntersection(AIEntity entity)
        {
            return false;
        }
    }
}