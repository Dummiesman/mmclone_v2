using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class WaypointInstance : PowerupInstance
{
    protected override void Update()
    {
        if (Level != null)
        {
            int curRoom = Level.FindRoomIdWithWarpsCheckMiss(this.transform.position, RoomID);
            if (curRoom != RoomID)
            {
                Level.MoveToRoom(this, curRoom);
            }
        }
    }
}
