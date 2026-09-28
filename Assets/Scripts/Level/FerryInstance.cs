using UnityEngine;

namespace MM2.AI
{
    public class FerryInstance : UnhitBangerInstance
    {
        public static FerryInstance RequestFerry(SDLCity level, string propType, Vector3 propPos, Quaternion propRot)
        {
            var built = new GameObject(propType);
            var inst = built.AddComponent<FerryInstance>();
            inst.Init(level, propType, propPos, propRot, Vector3.one);
            return inst;
        }

        public override void Init(SDLCity level, string basename, Vector3 position, Quaternion rotation, Vector3 scale)
        {
            Unbreakable = true;
            base.Init(level, basename, position, rotation, scale);
            Flags &= ~(LevelInstanceFlags.Static | LevelInstanceFlags.DisableWhenRoomHidden);
        }

        protected override void Update()
        {
            base.Update();
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
}