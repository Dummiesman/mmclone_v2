using UnityEngine;

public class AIVehicleData
{
    public int ID;
    public string Name;
    public Vector3[] WheelPositions = new Vector3[6];

    public float Mass = 1000f;
    public Vector3 CG = Vector3.zero;
    public Vector3 Size = Vector3.one;
    public Vector3 MaxAng = Vector3.zero;
    public float Elasticity = 0.5f;
    public float Friction = 0.5f;
    public float MaxDamage = 50000f;
    public float PtxThresh = 50000f;
    public float Spring = 13000f;
    public float Damping = 1000f;
    public float Limit = 0.1f;
    public float RubberSpring = 10000f;
    public float RubberDamp = 500f;

    public float Length => Mathf.Abs(Size.z);

    public void ReadSettings(TokenFileParser parser)
    {
        Mass = parser.Read("Mass", Mass);
        Size = parser.Read("Size", Size);
        MaxAng = parser.Read("MaxAng", MaxAng);
        Elasticity = parser.Read("Elasticity", Elasticity);
        Friction = parser.Read("Friction", Friction);

        MaxDamage = parser.Read("MaxDamage", MaxDamage);
        PtxThresh = parser.Read("PtxThresh", PtxThresh);

        Spring = parser.Read("Spring", Spring);
        Damping = parser.Read("Damping", Damping);
        Limit = parser.Read("Limit", Limit);
        RubberSpring = parser.Read("RubberSpring", Limit);
        RubberDamp = parser.Read("RubberDamp", Limit);
        
        CG = parser.Read("CG", CG);
        CG.z = -CG.z;
    }

    public AIVehicleData(TokenFileParser parser)
    {
        ReadSettings(parser);
    }

    public AIVehicleData()
    {

    }
}