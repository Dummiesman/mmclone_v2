namespace MM2.AI 
{
    public class AIPedestrian : AIRailEntity
    {
        const float PEDESTRIAN_RADIUS = 0.1f; // todo: figure out good pedestrian distance for avoidance

        private const float WalkSpeed = 1.5f;

        public AIPedestrian(AINetwork network) : base(network)
        {
            // Defaults are Vehicle/None, which put pedestrians into vehicle lanes.
            RailType = RailType.Pedestrian;
            AmbientTypeFlagMask = AmbientTypeFlags.Pedestrians;
            speedLimit = WalkSpeed;
        }

        public override int RoomID => 0;
        public override float FrontBumperDistance => PEDESTRIAN_RADIUS;
        public override float RearBumperDistance => -PEDESTRIAN_RADIUS;
        public override float LeftSideDistance => -PEDESTRIAN_RADIUS;
        public override float RightSideDistance => PEDESTRIAN_RADIUS;

        public override void Update()
        {
            // do nothing for now cause something's fucked
        }
    }
}

