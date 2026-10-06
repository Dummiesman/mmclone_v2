using System;

public struct StringSlice : IEquatable<StringSlice>, IEquatable<string>
{
    private string source;

    public int StartIndex;
    public int Length;

    public StringSlice(string source, int startIndex, int length)
    {
        this.source = source;
        this.StartIndex = startIndex;
        this.Length = length;
    }

    public static StringSlice FromStartEndIndex(string source, int startIndex, int endIndex)
    {
        int length = endIndex - startIndex;
        return new StringSlice() { source = source, StartIndex = startIndex, Length = length };
    }

    public ReadOnlySpan<char> AsSpan()
    {
        return source.AsSpan(StartIndex, Length);
    }

    public float ToFloat()
    {
        return FastFloatParser.Parse(source, StartIndex, StartIndex + Length);
    }

    public int ToInt()
    {
        return FastIntParser.Parse(source, StartIndex, StartIndex + Length);
    }

    public override string ToString()
    {
        return source.Substring(StartIndex, Length);
    }

    // ---------------------------------------------------------------- equality

    public bool Equals(StringSlice other)
    {
        if (Length != other.Length)
            return false;

        if (source == null || other.source == null)
            return source == other.source;

        return AsSpan().SequenceEqual(other.AsSpan());
    }

    public bool Equals(string other)
    {
        if (source == null || other == null)
            return source == null && other == null;

        if (other.Length != Length)
            return false;

        return AsSpan().SequenceEqual(other.AsSpan());
    }

    public override bool Equals(object obj)
    {
        if (obj is StringSlice slice)
            return Equals(slice);

        if (obj is string str)
            return Equals(str);

        return false;
    }

    public override int GetHashCode()
    {
        if (source == null)
            return 0;

        unchecked
        {
            const uint offset = 2166136261;
            const uint prime = 16777619;

            uint hash = offset;
            int end = StartIndex + Length;

            for (int i = StartIndex; i < end; i++)
                hash = (hash ^ source[i]) * prime;

            return (int)hash;
        }
    }

    // --------------------------------------------------------------- operators

    public static bool operator ==(StringSlice a, StringSlice b) => a.Equals(b);
    public static bool operator !=(StringSlice a, StringSlice b) => !a.Equals(b);

    public static bool operator ==(StringSlice a, string b) => a.Equals(b);
    public static bool operator !=(StringSlice a, string b) => !a.Equals(b);

    public static bool operator ==(string a, StringSlice b) => b.Equals(a);
    public static bool operator !=(string a, StringSlice b) => !b.Equals(a);
}