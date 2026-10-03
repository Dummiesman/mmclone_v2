using PSDL;
using System.Collections.Generic;
using UnityEngine;

public class RoomInfo
{
    public int Id;
    public Room SDLRoom;
    public GameObject Object;
    public Bounds Bounds;
    public bool IsWaterRoom;
    public readonly List<LevelInstance> Instances = new List<LevelInstance>();
    public readonly List<Renderer> StaticRenderers = new List<Renderer>();
    public readonly List<RoomInfo> NeighboringRooms = new List<RoomInfo>();
    public readonly List<RoomInfo> WarpRooms = new List<RoomInfo>();
    public bool RenderingActive = true;

    public RoomFlags Flags => SDLRoom.Flags;
}