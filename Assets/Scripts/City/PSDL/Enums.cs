using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PSDL
{
    public enum DividerType : byte
    {
        Invisible = 0,
        Flat = 1,
        Elevated = 2,
        Wedged = 3
    }

    [Flags]
    public enum DividerFlags : byte
    {
        ClosedEnd = 32,
        ClosedStart = 16,
    }

    public enum DividerTextureType
    {
        Top,
        Side,
        Cap,
        SideStrips
    }

    public enum RoadTextureType
    {
        Surface,
        Sidewalk,
        LOD
    }

    public enum CrossroadEnd
    {
        First,
        Last
    }

    public enum SidewalkRemovalMode
    {
        MoveSidewalkInwards,
        MoveRoadOutwards
    }
}
