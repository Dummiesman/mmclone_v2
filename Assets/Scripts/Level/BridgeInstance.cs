using System;
using UnityEngine;

namespace MM2.AI
{
    public class BridgeInstance : UnhitBangerInstance
    {
        public static BridgeInstance RequestBridge(SDLCity level, string propType, Vector3 propPos, Quaternion propRot)
        {
            var built = new GameObject(propType);
            var inst = built.AddComponent<BridgeInstance>();
            inst.Init(level, propType, propPos, propRot, Vector3.one);
            return inst;
        }

        public override void Init(SDLCity level, string basename, Vector3 position, Quaternion rotation, Vector3 scale)
        {
            Unbreakable = true;
            base.Init(level, basename, position, rotation, scale);
            Flags &= ~LevelInstanceFlags.Static; // prevent reparenting from level system
        }
    }
}