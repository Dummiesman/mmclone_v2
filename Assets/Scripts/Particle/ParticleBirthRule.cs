
using UnityEngine;

public class ParticleBirthRule
{
    public class VariableProperty<T>
    {
        public T Variation;
        public T Value;

        public static VariableProperty<T> ReadFromNode(TokenFileParser reader, string name)
        {
            return new VariableProperty<T>()
            {
                Value = reader.Read<T>(name),
                Variation = reader.Read<T>(name + "Var")
            };
        }
    }

    public VariableProperty<Vector3> Position;
    public VariableProperty<Vector3> Velocity;
    public VariableProperty<float> Life;
    public VariableProperty<float> Mass;
    public VariableProperty<float> Radius;
    public VariableProperty<float> Drag;
    public VariableProperty<float> Damp;
    public VariableProperty<float> DRadius;
    public VariableProperty<float> DAlpha;
    public VariableProperty<float> DRotation;
    public int InitialBlast;
    public float SpewRate;
    public float SpewTimeLimit;
    public float Gravity;
    public int TexFrameStart;
    public int TexFrameEnd;
    public ParticleBirthFlags BirthFlags;
    public float Height;
    public float Intensity;
    public Color Color;

    public void Dump()
    {
        void dumpVarPropFloat(string name, VariableProperty<float> input)
        {
            Debug.Log($"{name}: {input.Value}");
            Debug.Log($"{name}Var: {input.Variation}");
        }
        void dumpVarPropFloat3(string name, VariableProperty<Vector3> input)
        {
            Debug.Log($"{name}: {input.Value}");
            Debug.Log($"{name}Var: {input.Variation}");
        }

        dumpVarPropFloat3("Position", Position);
        dumpVarPropFloat3("Velocity", Velocity);
        dumpVarPropFloat("Life", Life);
        dumpVarPropFloat("Mass", Mass);
        dumpVarPropFloat("Radius", Radius);
        dumpVarPropFloat("Drag", Drag);
        dumpVarPropFloat("Damp", Damp);
        dumpVarPropFloat("DRadius", DRadius);
        dumpVarPropFloat("DAlpha", DAlpha);
        dumpVarPropFloat("DRotation", DRotation);
        Debug.Log($"InitialBlast: {InitialBlast}");
        Debug.Log($"SpewRate: {SpewRate}");
        Debug.Log($"SpewTimeLimit: {SpewTimeLimit}");
        Debug.Log($"Gravity: {Gravity}");
        Debug.Log($"TexFrameStart: {TexFrameStart}");
        Debug.Log($"TexFrameEnd: {TexFrameEnd}");
        Debug.Log($"BirthFlags: {BirthFlags}");
        Debug.Log($"Height: {Height}");
        Debug.Log($"Intensity: {Intensity}");
        Debug.Log($"Color: {Color}");
    }

    public void ReadSettings(TokenFileParser reader)
    {
        Position = VariableProperty<Vector3>.ReadFromNode(reader, "Position");
        Velocity = VariableProperty<Vector3>.ReadFromNode(reader, "Velocity");
        Life = VariableProperty<float>.ReadFromNode(reader, "Life");
        Mass = VariableProperty<float>.ReadFromNode(reader, "Mass");
        Radius = VariableProperty<float>.ReadFromNode(reader, "Radius");
        Drag = VariableProperty<float>.ReadFromNode(reader, "Drag");
        Damp = VariableProperty<float>.ReadFromNode(reader, "Damp");
        DRadius = VariableProperty<float>.ReadFromNode(reader, "DRadius");
        DAlpha = VariableProperty<float>.ReadFromNode(reader, "DAlpha");
        DRotation = VariableProperty<float>.ReadFromNode(reader, "DRotation");
        InitialBlast = reader.Read("InitialBlast", 0);
        SpewRate = reader.Read("SpewRate", 0f);
        SpewTimeLimit = reader.Read("SpewTimeLimit", 0f);
        Gravity = reader.Read("Gravity", -9.8f);
        TexFrameStart = reader.Read("TexFrameStart", 0);
        TexFrameEnd = reader.Read("TexFrameEnd", 0);
        BirthFlags = (ParticleBirthFlags)reader.Read("BirthFlags", (uint)0);
        Height = reader.Read("Height", 1f);
        Intensity = reader.Read("Intensity", 1f);

        int packed = reader.Read("Color", int.MaxValue);
        Color = new Color32((byte)packed, (byte)(packed >> 8), (byte)(packed >> 16), (byte)(packed >> 24));
    }

    public ParticleBirthRule(TokenFileParser reader)
    {
        ReadSettings(reader);
    }

    public ParticleBirthRule()
    {

    }
}