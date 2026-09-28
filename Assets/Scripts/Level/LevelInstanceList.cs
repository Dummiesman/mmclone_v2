using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;


[Flags]
public enum InstanceFlags : byte
{
    Landmark = 1 << 0, // Landmark, no backfacing, has bound
    Banger = 1 << 1, // Instance is a banger instance
    SimpleCollision = 1 << 2, // No wheel collision, only in combination with Landmark flag
    Multiroom = 1 << 5 // Multiroom, has bound
}

public class LevelInstanceList
{
    //
    public readonly List<Instance> Instances = new List<Instance>();

    //
    private Dictionary<int, List<int>> instanceRoomMap;

    public class Instance
    {
        public string Name;
        public int RoomIndex;
        public ushort Modifiers;
        public Vector3 Origin;

        public static bool BakeLighting = false;

        public byte Variant
        {
            get
            {
                return (byte)(Modifiers & 0xff);
            }
            set
            {
                byte[] mbytes = BitConverter.GetBytes(Modifiers);
                mbytes[0] = value;
                Modifiers = BitConverter.ToUInt16(mbytes, 0);
            }
        }

        //1 - FixedY/Landmark (Has Bound) 
        //2 - Banger 
        //4 - No Wheel Collision (works with flag 1 only) 
        //32 - Load Bound (Has Bound)
        public InstanceFlags Flags
        {
            get
            {
                return (InstanceFlags)(Modifiers >> 8);
            }
            set
            {
                byte[] mbytes = BitConverter.GetBytes(Modifiers);
                mbytes[1] = (byte)value;
                Modifiers = BitConverter.ToUInt16(mbytes, 0);
            }
        }

        public virtual Quaternion GetRotation()
        {
            return Quaternion.identity;
        }

        public virtual Vector3 GetScale()
        {
            return Vector3.one;
        }

        public virtual void ReadBinary(BinaryReader reader)
        {

        }

        public virtual void WriteBinary(BinaryWriter writer)
        {

        }

        public virtual void FromGameObject(GameObject source)
        {

        }
    }

    public class CoordinateComponent : Instance
    {
        public Vector3 xAxis;
        public Vector3 yAxis;
        public Vector3 zAxis;

        public override Quaternion GetRotation()
        {
            return Quaternion.LookRotation(this.zAxis, this.yAxis);
        }

        public override Vector3 GetScale()
        {
            return new Vector3(this.xAxis.magnitude, this.yAxis.magnitude, this.zAxis.magnitude);
        }

        public override void ReadBinary(BinaryReader r)
        {
            xAxis = r.ReadVector3().ConvertCoordinateSpace();
            yAxis = r.ReadVector3().ConvertCoordinateSpace();
            zAxis = r.ReadVector3().ConvertCoordinateSpace();
            Origin = r.ReadVector3().ConvertCoordinateSpace();
        }

        public override void WriteBinary(BinaryWriter w)
        {
            w.Write(-xAxis.x);
            w.Write(xAxis.y);
            w.Write(xAxis.z);

            w.Write(-yAxis.x);
            w.Write(yAxis.y);
            w.Write(yAxis.z);

            w.Write(-zAxis.x);
            w.Write(zAxis.y);
            w.Write(zAxis.z);

            w.Write(-Origin.x);
            w.Write(Origin.y);
            w.Write(Origin.z);
        }

        public override void FromGameObject(GameObject source)
        {
            Name = source.name;
            Origin = source.transform.position;
            xAxis = source.transform.right * source.transform.localScale.x;
            yAxis = source.transform.up * source.transform.localScale.y;
            zAxis = source.transform.forward * source.transform.localScale.z;
        }

        public CoordinateComponent(GameObject source)
        {
            FromGameObject(source);
        }

        public CoordinateComponent() { }
    }

    public class SimpleComponent : Instance
    {
        public float xDelta;
        public float zDelta;

        public override Quaternion GetRotation()
        {
            Vector3 rotationAndScale = new Vector3(this.xDelta, 0, this.zDelta);
            return Quaternion.LookRotation(Vector3.Cross(Vector3.up, rotationAndScale.normalized));
        }

        public override Vector3 GetScale()
        {
            Vector3 rotationAndScale = new Vector3(this.xDelta, 0, this.zDelta);
            float xzScale = rotationAndScale.magnitude;
            return new Vector3(xzScale, 1.0f, xzScale);
        }

        public override void ReadBinary(BinaryReader r)
        {
            xDelta = -r.ReadSingle();
            zDelta = r.ReadSingle();
            Origin = r.ReadVector3().ConvertCoordinateSpace();
        }

        public override void WriteBinary(BinaryWriter w)
        {
            w.Write(-xDelta);
            w.Write(zDelta);

            w.Write(-Origin.x);
            w.Write(Origin.y);
            w.Write(Origin.z);
        }

        public override void FromGameObject(GameObject source)
        {
            Name = source.name;
            Origin = source.transform.position;

            Vector3 xzDelta = -source.transform.right.normalized;
            float xzScale = Mathf.Max(source.transform.localScale.x, source.transform.localScale.z);

            xDelta = xzDelta.x * xzScale;
            zDelta = xzDelta.z * xzScale;
        }

        public SimpleComponent(GameObject source)
        {
            FromGameObject(source);
        }

        public SimpleComponent() { }
    }

    public void BuildInstanceRoomMap()
    {
        instanceRoomMap = new Dictionary<int, List<int>>();
        for (int i = 0; i < Instances.Count; i++)
        {
            var instance = Instances[i];
            if (instance.RoomIndex < 0)
                continue;

            List<int> roomInstanceIndices;
            if (!instanceRoomMap.TryGetValue(instance.RoomIndex, out roomInstanceIndices))
            {
                roomInstanceIndices = new List<int>();
                instanceRoomMap[instance.RoomIndex] = roomInstanceIndices;
            }
            roomInstanceIndices.Add(i);
        }
    }

    public IEnumerable<Instance> GetInstancesInRoom(int room)
    {
        if (instanceRoomMap == null)
        {
            Debug.LogError($"GetInstancesInRoom cannot be called before room map is built!");
            yield break;
        }

        if (instanceRoomMap.TryGetValue(room, out var mapIndices))
        {
            foreach (var mapIndex in mapIndices)
                yield return Instances[mapIndex];
        }
    }

    public void WriteBinary(BinaryWriter w)
    {
        foreach (var instance in Instances)
        {
            //write header
            w.Write((short)(instance.RoomIndex + 1));
            w.Write(instance.Modifiers);

            //write name / type field
            byte nameLength = (byte)(instance.Name.Length + 1);
            if (instance is SimpleComponent)
                nameLength += 128;
            w.Write(nameLength);

            for (int i = 0; i < instance.Name.Length; i++)
                w.Write(instance.Name[i]);
            w.Write((byte)0x00);

            //write component
            instance.WriteBinary(w);
        }
    }

    public void ReadBinary(BinaryReader r)
    {
        while (r.BaseStream.Position < r.BaseStream.Length)
        {
            int roomIndex = r.ReadUInt16();
            ushort modifiers = r.ReadUInt16();

            //read name and type
            byte nameAndType = r.ReadByte();
            int nameLength = nameAndType & 0x7f;
            int type = System.Math.Max(0, (nameAndType & 0x80) - 127);
            string name = new string(r.ReadChars(nameLength)).Replace("\x00", "");

#if UNITY_EDITOR
            if (roomIndex == 0)
                Debug.Log("Room index 0!! Object: " + name);
#endif

            //read instance
            Instance instance = null;
            switch (type)
            {
                case 1:
                    {
                        instance = new SimpleComponent();
                        break;
                    }
                case 0:
                    {
                        instance = new CoordinateComponent();
                        break;
                    }
                default:
                    {
                        Debug.LogError($"Failed to read instance {name} because it has an invalid type. ({type})");
                        return;
                    }
            }
            instance.ReadBinary(r);

            //set data and add to list
            instance.Modifiers = modifiers;
            instance.RoomIndex = roomIndex;
            instance.Name = name;

            Instances.Add(instance);
        }
    }

}
