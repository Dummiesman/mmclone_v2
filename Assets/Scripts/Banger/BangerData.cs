using UnityEngine;
using System.Collections.Generic;

[System.Flags]
public enum BangerDataFlags
{
    Glass = 256,
    Unlit = 512
}

[System.Flags]
public enum BangerCollisionType
{
    NoCollision = 2,
    OnlyTerrain = 4,
    InstancesAndTerrain = 16,
    CollideWithWheels = 32,
    InstancesAndTerrainHighPrio = 64
}

public enum BangerCollisionPrimitive
{
    Mesh,
    Box,
    Capsule,
    Sphere
}

[System.Serializable]
public class BangerData
{
    public int ID = 0;
    public Mesh CollisionMesh = null;

    public string Name;
    public int AudioID;
    public Vector3 Size;
    public Vector3 CG;
    public List<Vector3> GlowOffsets = new List<Vector3>();
    public float Mass;
    public float Elasticity;
    public float Friction;
    public float ImpulseLimit;
    public int SpinAxis;
    public int Flash;
    public int NumParts;
    public int TexNumber;
    public BangerDataFlags BillFlags;
    public float YRadius;
    public int ColliderId;
    public BangerCollisionPrimitive CollisionPrim;
    public BangerCollisionType CollisionType;
    public ParticleBirthRule BirthRule;

    public void ReadSettings(TokenFileParser reader)
    {
        //init lists
        GlowOffsets.Clear();

        //read data
        reader.SkipTo("AudioId");
        AudioID = reader.Read("AudioID", 0);
        Size = reader.Read("Size", Vector3.one);
        CG = reader.Read("CG", Vector3.zero).ConvertCoordinateSpace();

        int numGlows = reader.Read("NumGlows", 0);
        for (int i = 0; i < numGlows; i++)
        {
            GlowOffsets.Add(reader.ReadVector3().ConvertCoordinateSpace());
            reader.Seek(1, System.IO.SeekOrigin.Current);
        }

        Mass = reader.Read("Mass", 1000f);
        Elasticity = reader.Read("Elasticity", 1f);
        Friction = reader.Read("Friction", 1f);
        ImpulseLimit = Mathf.Sqrt(reader.Read("ImpulseLimit2", 100f));
        SpinAxis = reader.Read("SpinAxis", 1);
        Flash = reader.Read("Flash", 0);
        NumParts = reader.Read("NumParts", 0);

        BirthRule = new ParticleBirthRule(reader);

        reader.SkipTo("TexNumber");
        TexNumber = reader.Read("TexNumber", 0);
        BillFlags = (BangerDataFlags)reader.Read("BillFlags", 0);
        YRadius = reader.Read("YRadius", 0f);
        ColliderId = reader.Read("ColliderId", 0);
        CollisionPrim = (BangerCollisionPrimitive)reader.Read("CollisionPrim", 0);
        CollisionType = (BangerCollisionType)reader.Read("CollisionType", 0);
    }

    public BangerData(string name)
    {
        this.Name = name;
    }

    public BangerData(TokenFileParser reader)
    {
        ReadSettings(reader);
    }

    public BangerData()
    {

    }
	
}
