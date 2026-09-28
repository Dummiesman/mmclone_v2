using System.IO;
using System.Text;
using UnityEngine;

public static class BinaryExtensions 
{
    public static string ReadNullTerminatedString(this BinaryReader r)
    {
        StringBuilder builder = new StringBuilder();
        while (r.BaseStream.Position < r.BaseStream.Length)
        {
            byte b = r.ReadByte();
            if (b == 0)
                break;
            builder.Append((char)b);
        }
        return builder.ToString();
    }

    public static string ReadAGEString(this BinaryReader r)
    {
        int len = r.ReadByte();
        if (len == 0)
        {
            return string.Empty;
        }
        else
        {
            string str = new string(r.ReadChars(len - 1));
            r.BaseStream.Seek(1, SeekOrigin.Current);
            return str;
        }
    }

    public static string ReadFixedString(this BinaryReader reader, int length)
    {
        if (length <= 0)
        {
            return string.Empty;
        }

        byte[] bytes = reader.ReadBytes(length);

        int end = System.Array.IndexOf<byte>(bytes, 0);
        if (end < 0)
        {
            end = bytes.Length;
        }

        return Encoding.ASCII.GetString(bytes, 0, end);
    }

    public static Color32 ReadColor4d(this BinaryReader r)
    {
        int value = r.ReadInt32();
        return new Color32(
            (byte)value,
            (byte)(value >> 8),
            (byte)(value >> 16),
            (byte)(value >> 24));
    }

    public static Color ReadColor4f(this BinaryReader r)
    {
        return new Color(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
    }

    public static Vector3 ReadVector3(this BinaryReader r)
    {
        return new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
    }

    public static Vector3 ReadVector3Flipped(this BinaryReader r)
    {
        return new Vector3(r.ReadSingle() * -1, r.ReadSingle(), r.ReadSingle());
    }

    public static void WriteFixedString(this BinaryWriter writer, string value, int length)
    {
        if (length <= 0)
        {
            return;
        }

        byte[] bytes = new byte[length];
        int count = Encoding.ASCII.GetBytes(value ?? string.Empty, 0,
            System.Math.Min(value?.Length ?? 0, length - 1), bytes, 0);

        writer.Write(bytes, 0, length); // remaining bytes are already 0
    }

    public static void WriteNullTerminatedString(this BinaryWriter writer, string value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            writer.Write(value.ToCharArray());
        }

        writer.Write('\0');
    }

    public static void WriteAGEString(this BinaryWriter w, string str)
    {
        if (str.Length >= 255)
        {
            Debug.LogError($"WriteAGEString: str.Length >= 255");
            return;
        }

        //write empty string
        if (string.IsNullOrEmpty(str))
        {
            w.Write((byte)0);
            return;
        }

        //write AGE string
        w.Write((byte)(str.Length + 1));
        for (int i = 0; i < str.Length; i++)
        {
            w.Write(str[i]);
        }
        w.Write((byte)0);
    }

    public static void WriteColor4d(this BinaryWriter w, Color32 color)
    {
        w.Write(color.r);
        w.Write(color.g);
        w.Write(color.b);
        w.Write(color.a);
    }

    public static void WriteColor4f(this BinaryWriter w, Color color)
    {
        w.Write(color.r);
        w.Write(color.g);
        w.Write(color.b);
        w.Write(color.a);
    }

    public static void WriteVector3(this BinaryWriter w, Vector3 vec)
    {
        w.Write(vec.x);
        w.Write(vec.y);
        w.Write(vec.z);
    }

    public static void WriteVector3Flipped(this BinaryWriter w, Vector3 vec)
    {
        w.Write(-vec.x);
        w.Write(vec.y);
        w.Write(vec.z);
    }

    public static void WriteVector2(this BinaryWriter w, Vector2 vec)
    {
        w.Write(vec.x);
        w.Write(vec.y);
    }

    // Texture loading
    public static Color32 ReadColor32RGBA(this BinaryReader r)
    {
        return ReadColor4d(r);
    }

    public static Color32 ReadColor32RGB(this BinaryReader r)
    {
        var bytes = r.ReadBytes(3);
        return new Color32(bytes[0], bytes[1], bytes[2], 255);
    }
}
