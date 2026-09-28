using System;
using System.Runtime.InteropServices;

public sealed class Crc32
{
    public const uint Polynomial = 0xED7282A0;

    public const uint InitialValue = 0xFFFFFFFF;

    private static readonly uint[] Table = BuildTable();
    private uint crc = InitialValue;

    public static readonly Crc32 Shared = new Crc32();

    private static uint[] BuildTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint c = i;
            for (int bit = 0; bit < 8; bit++)
                c = (c & 1) != 0 ? (Polynomial ^ (c >> 1)) : (c >> 1);
            table[i] = c;
        }
        return table;
    }

    public uint Register => crc;
    public uint Value => ~crc;

    public uint Reset()
    {
        crc = InitialValue;
        return 0;
    }

    public uint Update(byte[] data)
    {
        if (data == null)
            return Reset();

        return Update(data, 0, data.Length);
    }

    public uint Update(byte[] data, int offset, int length)
    {
        if (data == null)
            return Reset();
        if (offset < 0 || length < 0 || offset + length > data.Length)
            throw new ArgumentOutOfRangeException(nameof(length));

        uint crc = this.crc;
        int end = offset + length;
        for (int i = offset; i < end; i++)
            crc = Table[(byte)(crc ^ data[i])] ^ (crc >> 8);

        this.crc = crc;
        return ~crc;
    }

    public uint Update(ReadOnlySpan<byte> data)
    {
        uint crc = this.crc;
        for (int i = 0; i < data.Length; i++)
            crc = Table[(byte)(crc ^ data[i])] ^ (crc >> 8);

        this.crc = crc;
        return ~crc;
    }

    /// <summary>Single byte, for streaming callers.</summary>
    public uint Update(byte value)
    {
        crc = Table[(byte)(crc ^ value)] ^ (crc >> 8);
        return ~crc;
    }
    public uint Update(sbyte value) => Update(unchecked((byte)value));
    public uint Update(short value) => Update(unchecked((ushort)value));
    public uint Update(ushort value)
    {
        uint crc = this.crc;
        crc = Table[(byte)(crc ^ (byte)value)] ^ (crc >> 8);
        crc = Table[(byte)(crc ^ (byte)(value >> 8))] ^ (crc >> 8);
        this.crc = crc;
        return ~crc;
    }

    public uint Update(int value) => Update(unchecked((uint)value));

    public uint Update(uint value)
    {
        uint crc = this.crc;
        crc = Table[(byte)(crc ^ (byte)value)] ^ (crc >> 8);
        crc = Table[(byte)(crc ^ (byte)(value >> 8))] ^ (crc >> 8);
        crc = Table[(byte)(crc ^ (byte)(value >> 16))] ^ (crc >> 8);
        crc = Table[(byte)(crc ^ (byte)(value >> 24))] ^ (crc >> 8);
        this.crc = crc;
        return ~crc;
    }

    public uint Update(long value) => Update(unchecked((ulong)value));

    public uint Update(ulong value)
    {
        uint crc = this.crc;
        for (int shift = 0; shift < 64; shift += 8)
            crc = Table[(byte)(crc ^ (byte)(value >> shift))] ^ (crc >> 8);
        this.crc = crc;
        return ~crc;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct FloatBits
    {
        [FieldOffset(0)] public float F;
        [FieldOffset(0)] public uint U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct DoubleBits
    {
        [FieldOffset(0)] public double D;
        [FieldOffset(0)] public ulong U;
    }

    public uint Update(float value)
    {
        var bits = new FloatBits { F = value };
        return Update(bits.U);
    }
    public uint Update(double value)
    {
        var bits = new DoubleBits { D = value };
        return Update(bits.U);
    }
    public uint Update(bool value) => Update((byte)(value ? 1 : 0));
    public uint UpdateBool32(bool value) => Update(value ? 1u : 0u);
    public uint UpdateFixedString(string value, int bufferSize, byte pad = 0)
    {
        if (bufferSize < 0)
            throw new ArgumentOutOfRangeException(nameof(bufferSize));

        var buffer = new byte[bufferSize];
        int count = value == null ? 0 : Math.Min(value.Length, bufferSize);
        for (int i = 0; i < count; i++)
            buffer[i] = unchecked((byte)value[i]);
        for (int i = count; i < bufferSize; i++)
            buffer[i] = pad;

        return Update(buffer);
    }

    public static uint Compute(byte[] data)
    {
        var crc = new Crc32();
        return crc.Update(data);
    }

    public static uint Compute(ReadOnlySpan<byte> data)
    {
        var crc = new Crc32();
        return crc.Update(data);
    }
}