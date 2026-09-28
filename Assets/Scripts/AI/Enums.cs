using System;

namespace MM2.AI
{
    public enum RoadPosition
    {
        OnRoad = 1, // |lateral| < curb edge - margin
        OnSidewalk = 2, // past the curb, but |lateral| < sidewalk outer edge - margin
        OffRoad = 3, // beyond the sidewalk (or no sections)
    }

    public enum PoliceState 
    {
        Idle = 0x0,
        Apprehend = 0x1,
        FollowPerp = 0x2,
        Invalid = 0x5,
        Incapacitated = 0xC,
    }

    public enum PoliceApprehendState
    {
        Ram = 3, // flag 8 in police data
        PushLeft = 4, // flag 4 in police data; aims 3m off the perp's left
        PushRight = 5, // Push flips between Left and Right on arrival
        Block = 6, // flag 1 in police data
        BlockWait = 7, // reached block point; Update mirrors the perp
        Barricade = 8, // flag 2 in police data; cut, Barricade() is empty
    }

    public enum VehiclePhysicsState : short
    {
        Forward = 0,
        Backup = 1,
        Shortcut = 2,
        Stop = 3,
    }

    public enum CompType 
    { 
        None = 0, 
        Road = 1, 
        Shortcut = 2,
        Intersection = 3 
    }

    public enum TrafficLightState
    {
       Green,
       Yellow,
       Red
    }

    public enum PedLightState
    {
        NoWalk,
        Walk
    }

    public enum RoadSide
    {
        Invalid = -1,
        Left = 0,
        Right = 1
    }

    [Flags]
    public enum PathFlags
    {
        Divided = 1,
        Alleyway = 2,
        Freeway = 4,
        Flat = 8
    }

    [Flags]
    public enum AmbientTypeFlags
    {
        None = 0,
        Vehicles = 1,
        Pedestrians = 2
    }

    public enum VehicleRule
    {
        StopSign,
        TrafficLight,
        AlwaysStop,
        NeverStop
    }

    public enum RailType
    {
        Vehicle,
        Pedestrian,
        Tram,
        Subway
    }
}