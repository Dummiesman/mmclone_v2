using JetBrains.Annotations;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public struct StringSlice
{
    private string source;

    public int StartIndex;
    public int Length;

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

    public static StringSlice FromStartEndIndex(string source, int startIndex, int endIndex)
    {
        int length = endIndex - startIndex;
        return new StringSlice() { source = source, StartIndex = startIndex, Length = length };
    }
    
    public StringSlice(string source, int startIndex, int length)
    {
        this.source = source;
        this.StartIndex = startIndex;
        this.Length = length;
    }
}
