using System;
using System.Collections.Generic;

//TODO : Figure out WTF all this shit is
namespace PSDL
{
    public class AIRoad
    {
        [Flags]
        public enum PropulationFlags : uint
        {
            Propulate = 0x0000FFFF,
            InvertPropulation = 0xFFFF0000
        }

        public PropulationFlags Flags;
        public List<float> Unknown3;
        public List<float> Unknown4;
        public ushort Unknown5;
        public List<Room> Rooms;

        public AIRoad StripInvalidRooms()
        {
            var road = new AIRoad(Flags, Unknown3, Unknown4, Unknown5);
            foreach (var room in Rooms)
            {
                if (room.VerifyForPropulation())
                    road.Rooms.Add(room);
            }
            return road;
        }

        public AIRoad(PropulationFlags Flags, IEnumerable<float> u3, IEnumerable<float> u4, ushort u5, IEnumerable<Room> rooms = null)
        {
            this.Flags = Flags;
            Unknown5 = u5;

            Unknown3 = new List<float>();
            Unknown3.AddRange(u3);

            Unknown4 = new List<float>();
            Unknown4.AddRange(u4);

            Rooms = new List<Room>();

            //
            if (rooms != null)
                Rooms.AddRange(rooms);
        }
    }
}
