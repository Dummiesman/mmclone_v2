using MM2.AI;
using UnityEngine;

/// <summary>
/// AIVehicleProxy is a wrapper around a VehCar
/// </summary>
public class AIVehicleProxy : AIEntity
{
    public VehCar Vehicle { get; private set; }

    public override int RoomID => Vehicle.Model.RoomID;

    public override float Speed => Vehicle.VehCarSim.Speed;
    public override Vector3 Position => Vehicle.Model.transform.position;
    public override Quaternion Rotation => Vehicle.Model.transform.rotation;

    private Bounds colliderBounds => Vehicle.Bound.Collider.sharedMesh.bounds;
    public override float FrontBumperDistance => colliderBounds.center.z + (colliderBounds.size.z / 2.0f);
    public override float RearBumperDistance => colliderBounds.center.z - (colliderBounds.size.z / 2.0f);
    public override float LeftSideDistance => colliderBounds.center.x - (colliderBounds.size.x / 2.0f);
    public override float RightSideDistance => colliderBounds.center.x + (colliderBounds.size.x / 2.0f);

    public bool IsPlayer => (Vehicle.Type == vehCarType.Player);


    private int lastRoomId = -1;
    private int lastIntersection = -1;
    private int lastRoadId = -1;

    public AIVehicleProxy(AINetwork network, VehCar vehicle) : base(network)
    {
        Init(vehicle);
    }

    public void Init(VehCar vehicle)
    {
        this.Vehicle = vehicle;

        //set rail type and flag mask
        RailType = RailType.Vehicle;
        AmbientTypeFlagMask = AmbientTypeFlags.Vehicles;
    }

    /// <summary>
    /// Normalized progress of a point along a rail, using the same mapping AIRailEntity.PositionAlongPath
    /// uses in reverse: sectionIndex is side-local, vertices come from the side's own data.
    /// </summary>
    private static float ComputeNormalizedProgress(Road road, RoadSide side, RoadData roadData, int railIndex, int sectionIndex, Vector3 point)
    {
        if (road.Length <= 0f || sectionIndex < 0)
            return 0f;
        if (sectionIndex >= road.NumSections - 1)
            return 1f;

        var a = roadData.GetVehicleVertex(railIndex, sectionIndex).ToVec2XZ();
        var b = roadData.GetVehicleVertex(railIndex, sectionIndex + 1).ToVec2XZ();
        var ab = b - a;

        float t = 0f;
        float sqrLen = ab.sqrMagnitude;
        if (sqrLen > 1e-6f)
            t = Mathf.Clamp01(Vector2.Dot(point.ToVec2XZ() - a, ab) / sqrLen);

        float distance = Mathf.Lerp(road.GetSectionDistance(side, sectionIndex), road.GetSectionDistance(side, sectionIndex + 1), t);
        return Mathf.Clamp01(distance / road.Length);
    }

    private void UpdateOnRoad()
    {
        if (RoadInfo.RoadInstance == null)
            return;

        var roadInstance = RoadInfo.RoadInstance;
        var road = roadInstance.Road;
        var position = Vehicle.Model.transform.position;

        road.GetSidedInfoAtPoint(position, out int railIndex, out int sectionIndex, out var side);
        if (railIndex < 0 || sectionIndex < 0 || side == RoadSide.Invalid)
            return; // off the rails this frame, keep the last known placement

        // Use the side we're actually on now, not the one stored in RoadInfo.
        var roadData = side == RoadSide.Right ? road.RightData : road.LeftData;
        NormalizedPathProgress = ComputeNormalizedProgress(road, side, roadData, railIndex, sectionIndex, position);

        if (side != RoadInfo.SideOfRoad || railIndex != RoadInfo.RailIndex)
        {
            var targetLane = roadInstance.GetLane(side, RailType, railIndex);
            if (targetLane == null)
                return;

            var roadInfo = RoadInfo;
            roadInfo.RailIndex = railIndex;
            roadInfo.SideOfRoad = side;
            RoadInfo = roadInfo;

            // Progress is already updated, so Lane.Insert places us correctly.
            roadInstance.MoveToLane(this, targetLane);
        }
    }

    private void RemoveFromPreviousRoad()
    {
        if (lastRoadId < 0)
            return;

        var roadInstance = network.GetRoadInstance(lastRoadId);
        roadInstance.RemoveEntity(this);
        lastRoadId = -1;
    }

    private void RemoveFromPreviousIntersection()
    {
        if (lastIntersection < 0)
            return;

        var intersection = network.Intersections[lastIntersection];
        intersection.Leave(this);
        lastIntersection = -1;
    }

    public override void Update()
    {
        if (Vehicle == null)
            return;
 
        // update road progress
        if (RoadInfo.RoadInstance  != null)
        {
            UpdateOnRoad();
        }

        // anything here only updates on room change
        if (RoomID == lastRoomId)
        {
            return;
        }
        lastRoomId = RoomID;

        // find what road we're on
        var vehPos = Vehicle.Model.transform.position;
        var aiNetwork = network;

        // null current road
        var currentInfo = this.RoadInfo;
        currentInfo.RoadInstance = null;
        this.RoadInfo = currentInfo;

        var components = aiNetwork.GetRoomComponents(RoomID);
        int intersectionComponentIndex = -1;
        for(int i=0; i < components.Count; i++)
        {
            if (components[i].Type == CompType.Intersection)
            {
                intersectionComponentIndex = i;
                break;
            }
        }

        if (intersectionComponentIndex >= 0)
        {
            // remove from previous things
            RemoveFromPreviousRoad();
            RemoveFromPreviousIntersection();

            // find and enter intersection
            int compId = components[intersectionComponentIndex].Id;
            aiNetwork.Intersections[compId].Enter(this);
            lastIntersection = compId;
        }
        else if (components.Count > 0)
        {
            // find road
            int foundRoad = -1;
            foreach (var component in components)
            {
                if (component.Type == CompType.Intersection) continue;

                var roadInstance = aiNetwork.GetRoadInstance(component.Id);
                var road = roadInstance.Road;
                if (road.IsPointOnRoad(vehPos))
                {
                    road.GetSidedInfoAtPoint(vehPos, out int railIndex, out int sectionIndex, out var side);
                    if (railIndex < 0 || side == RoadSide.Invalid)
                        continue;

                    var roadData = side == RoadSide.Right ? road.RightData : road.LeftData;
                    NormalizedPathProgress = ComputeNormalizedProgress(road, side, roadData, railIndex, sectionIndex, vehPos);

                    this.RoadInfo = new RoadPositioningInfo()
                    {
                        RoadInstance = roadInstance,
                        RailIndex = railIndex,
                        SideOfRoad = side
                    };

                  
                    foundRoad = road.Id;
                    break;
                }
            }

            RemoveFromPreviousIntersection();

            if (foundRoad >= 0 && foundRoad == lastRoadId)
            {
                // Same road, new room: already registered, just make sure we're in the right lane.
                var roadInstance = network.GetRoadInstance(foundRoad);
                var lane = roadInstance.GetLane(RoadInfo.SideOfRoad, RailType, RoadInfo.RailIndex);
                if (lane != null)
                    roadInstance.MoveToLane(this, lane);
            }
            else
            {
                RemoveFromPreviousRoad();
                if (foundRoad >= 0)
                {
                    network.GetRoadInstance(foundRoad).AddEntity(this);
                }
            }
            lastRoadId = foundRoad;
        }
        else
        {
            RemoveFromPreviousIntersection();
            RemoveFromPreviousRoad();
        }
    }
}