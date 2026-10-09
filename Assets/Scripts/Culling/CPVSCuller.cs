using UnityEngine;

public class CPVSCuller : MonoBehaviour
{
    private CPVS cpvs;
    private int lastRoom = -1;
    private SDLCity city;

#if UNITY_EDITOR
    [Header("Debug")]
    public bool drawRoomGizmo = true;
    public bool drawWarpRooms = true;

    private void DrawRoomPerimeter(RoomInfo room, Color color)
    {
        var sdlRoom = room.SDLRoom;
        if (sdlRoom?.Perimeter == null || sdlRoom.Perimeter.Count < 2)
            return;

        float floor = room.Bounds.min.y;
        float ceiling = room.Bounds.max.y;

        Gizmos.color = color;

        int count = sdlRoom.Perimeter.Count;
        for (int i = 0; i < count; i++)
        {
            var a = sdlRoom.Perimeter[i].Vertex;
            var b = sdlRoom.Perimeter[(i + 1) % count].Vertex;

            // x negated to match TryComputeRoomBounds / PointInRoom world space
            var a2 = new Vector2(-a.x, a.z);
            var b2 = new Vector2(-b.x, b.z);

            Gizmos.DrawLine(new Vector3(a2.x, floor, a2.y), new Vector3(b2.x, floor, b2.y));
            Gizmos.DrawLine(new Vector3(a2.x, ceiling, a2.y), new Vector3(b2.x, ceiling, b2.y));
            Gizmos.DrawLine(new Vector3(a2.x, floor, a2.y), new Vector3(a2.x, ceiling, a2.y));
        }
    }

    private void OnDrawGizmos()
    {
        if (!drawRoomGizmo || city == null || lastRoom <= 0)
            return;

        var room = city.GetRoom(lastRoom);
        if (room == null)
            return;

        if (drawWarpRooms)
        {
            foreach (var warp in room.WarpRooms)
                DrawRoomPerimeter(warp, new Color(1f, 0.6f, 0f, 0.5f));
        }

        DrawRoomPerimeter(room, Color.green);

        UnityEditor.Handles.color = Color.white;
        UnityEditor.Handles.Label(
            room.Bounds.center,
            $"Room {lastRoom}\nfloor {room.Bounds.min.y:F2}  ceil {room.Bounds.max.y:F2}\nwarps {room.WarpRooms.Count}");
    }
#endif

    private void UpdateRoomVisibility(int sourceRoom)
    {
        // funstuff is about to happen
        // sourceRoom comes in as +1 from lvlLevel convention
        bool[] visList = cpvs.Decompress(sourceRoom-1);

        for (int i = 0; i < city.RoomCount; i++)
        {
            bool enable = false;
            if(i < visList.Length - 1)
            {
                enable = visList[i+1];
            }
            city.SetRoomRenderingEnabled(i + 1, enable); // cpvs includes room 0, need to offset by 1
        }
    }

    public void UpdateCameraPosition(Vector3 position)
    {
        if (cpvs == null) return;

        int newRoom = city.FindRoomIdWithWarpsCheckMiss(position, lastRoom);
        if(newRoom != lastRoom)
        {
            UpdateRoomVisibility(newRoom);
            lastRoom = newRoom;
        }
    }

    public void Init(SDLCity city, CPVS cpvs)
    {
        this.cpvs = cpvs;
        this.city = city;
    }
}
