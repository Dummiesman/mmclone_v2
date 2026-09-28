using System.IO;
using System;

public class PlayerRecord 
{
    public static int SizeOf = 96; // File size, used during parsing in PlayerCityRecord

    public float Time;
    public string VehicleName;
    public bool Passed;
    public int Score;

    private uint ComputeCRC()
    {
        var crc = new Crc32();
        crc.Reset();
        crc.Update(Time);
        crc.UpdateFixedString(VehicleName, 80);
        crc.UpdateBool32(Passed);
        return crc.Update(Score);
    }

    public bool Load(Stream stream)
    {
        using (var reader = new BinaryReader(stream, System.Text.Encoding.Default, true))
        {
            uint crc = reader.ReadUInt32();

            Time = reader.ReadSingle();
            VehicleName = reader.ReadFixedString(80);
            Passed = (reader.ReadInt32() != 0);
            Score = reader.ReadInt32();

            return crc == ComputeCRC();
        }
    }

    public void Save(Stream stream)
    {
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.Default, true))
        {
            writer.Write(ComputeCRC());
            writer.Write(Time);
            writer.WriteFixedString(VehicleName, 80);
            writer.Write(Passed ? 1 : 0);
            writer.Write(Score);
        }
    }

    public static bool operator ==(PlayerRecord a, PlayerRecord b)
    {
        if (ReferenceEquals(a, b))
        {
            return true;
        }
        if (a is null || b is null)
        {
            return false;
        }
        return a.Time == b.Time
            && a.VehicleName == b.VehicleName
            && a.Passed == b.Passed
            && a.Score == b.Score;
    }

    public static bool operator !=(PlayerRecord a, PlayerRecord b)
    {
        return !(a == b);
    }

    public override bool Equals(object obj)
    {
        return this == obj as PlayerRecord;
    }

    public override int GetHashCode()
    {
        unchecked
        {
            int hash = 17;
            hash = hash * 31 + Time.GetHashCode();
            hash = hash * 31 + (VehicleName?.GetHashCode() ?? 0);
            hash = hash * 31 + Passed.GetHashCode();
            hash = hash * 31 + Score;
            return hash;
        }
    }

    public PlayerRecord()
    {

    }

    public PlayerRecord(PlayerRecord other)
    {
        Time = other.Time;
        VehicleName = other.VehicleName;
        Passed = other.Passed;
        Score = other.Score;
    }
}
