using UnityEngine;

public class CPVSCuller 
{
    private CPVS cpvs;
    private int lastRoom = -1;
    private SDLCity city;

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

    public CPVSCuller(SDLCity city, CPVS cpvs)
    {
        this.cpvs = cpvs;
        this.city = city;
    }
}
