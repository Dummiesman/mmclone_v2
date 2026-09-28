using UnityEngine;
using System.IO;
using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Linq;
using System.Text;

public class TokenFileParser 
{
    private List<string> lines;
    private int currentLine = 0;

    public TokenFileParser(Stream stream)
    {
        if (stream == null) return;

        string line;
        this.lines = new List<string>(256);

        using (var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 1024, leaveOpen: true))
        {
            while ((line = reader.ReadLine()) != null)
            {
                lines.Add(line.Clean());
            }
        }
    }

    public void Seek(int position, SeekOrigin origin = SeekOrigin.Begin)
    {
        switch (origin)
        {
            case SeekOrigin.Begin:
                currentLine = position;
                break;
            case SeekOrigin.Current:
                currentLine += position;
                break;
            case SeekOrigin.End:
                currentLine = lines.Count + position;
                break;
        }
    }

    public void SeekToData()
    {
        bool atData = false;
        while (!atData)
        {
            var tokens = ReadTokensArray();
            atData = (tokens[0] != "type:" && tokens[1] != "{" && tokens[0] != "}");
        }
        Seek(-1, SeekOrigin.Current);
    }

    public bool SkipTo(int maxSkip = -1, params string[] tokens)
    {
        int lastFilePosition = currentLine;
        string lastToken = string.Empty;
        bool foundToken = false;

        while (!foundToken)
        {
            // see if we reached EOF
            if (EOF() || (maxSkip >= 0 && (currentLine - lastFilePosition) >= maxSkip))
            {
                currentLine = lastFilePosition;
                return false;
            }

            // try and get a token
            lastToken = ReadToken(true);
            foreach (var token in tokens)
            {
                if (token == lastToken)
                {
                    foundToken = true;
                }
            }
        }

        // found token
        Seek(-1, SeekOrigin.Current);
        return true;
    }

    public bool SkipTo(string token, int maxSkip = -1)
    {
        int lastFilePosition = currentLine;
        string lastToken = string.Empty;
        while(lastToken != token)
        {
            // see if we reached EOF
            if (EOF() || (maxSkip >= 0 && (currentLine - lastFilePosition) >= maxSkip)) {
                currentLine = lastFilePosition;
                return false;
            }

            // try and get a token
            lastToken = ReadToken(true);
        }
        // found token
        Seek(-1, SeekOrigin.Current);
        return true;
    }

    public bool SkipToSection(string token, int maxSkip = -1)
    {
        int lastFilePosition = currentLine;
        string lastToken = string.Empty;
        bool lastTokenWasSection = false;
        while (lastToken != token || !lastTokenWasSection)
        {
            // see if we reached EOF
            if (EOF() || (maxSkip >= 0 && (currentLine - lastFilePosition) >= maxSkip))
            {
                currentLine = lastFilePosition;
                return false;
            }

            // try and get a token
            lastToken = ReadToken(false);

            string sectionToken = ReadToken(true, 1);
            lastTokenWasSection = (sectionToken != null && sectionToken == "{");
        }
        // found token
        Seek(-1, SeekOrigin.Current);
        return true;
    }

    public bool EOF()
    {
        return (currentLine >= lines.Count);
    }

    // internal functions
    private string ReadLine(bool increment = true)
    {
        string line = lines[currentLine];
        if(increment)
            currentLine++;
        return line;
    }

    // reading functions
    public int GetTreeDepth()
    {
        int level = 0;
        for(int i=currentLine; i > 0; i--)
        {
            string ln = lines[i];
            if (ln.Contains('{'))
            {
                level++;
            }else if (ln.Contains('}'))
            {
                level--;
            }
        }
        return level;
    }

    public string GetTypeName()
    {
        int oldLine = currentLine;
        currentLine = 0;
        string returnString = ReadLine(false).Split(' ')[1];
        currentLine = oldLine;
        return returnString;
    }

    public string ReadToken(bool advancePosition = false, int index = 0)
    {
        string line = ReadLine(advancePosition);
        if(index == 0)
        {
            int spaceIndex = line.IndexOf(' ');
            if (spaceIndex < 0)
                return line;
            return line.Substring(0, spaceIndex);
        }
        else
        {
            index--;

            int charIndex = line.IndexOfNth(' ', index);

            if (charIndex < 0)
                return null;

            int nextCharIndex = line.IndexOf(' ', charIndex + 1);
            if (nextCharIndex < 0)
                nextCharIndex = line.Length;

            return line.Substring(charIndex + 1, nextCharIndex - charIndex - 1);
        }
    }

    private string[] GetLineData()
    {
        return ReadLine(false).Split(' ');
    }

    public string[] ReadTokensArray(bool advancePosition = true)
    {
        var ret = GetLineData();
        if(advancePosition)
            Seek(1, SeekOrigin.Current);
        return ret;
    }

    public IEnumerable<StringSlice> ReadTokensNonAlloc(bool advancePosition = true)
    {
        string line = ReadLine(advancePosition);
        return StringArrayParsingUtil.GetSpaceSeparatedSlices(line);
    }

    public List<StringSlice> ReadTokens(bool advancePosition = true)
    {
        return ReadTokensNonAlloc(advancePosition).ToList();
    }
    

    public string ReadString()
    {
        return ReadImmediate<string>();
    }

    public float ReadSingle()
    {
        return ReadImmediate<float>();
    }

    public int ReadInt()
    {
        return ReadImmediate<int>();
    }

    public Vector3 ReadVector3()
    {
        return ReadImmediate<Vector3>();
    }

    public Vector2 ReadVector2()
    {
        return ReadImmediate<Vector2>();
    }

    public Vector2 ReadVector2FX()
    {
        return ReadImmediate<Vector2>().ConvertCoordinateSpace();
    }

    public Vector3 ReadVector3FX()
    {
        return ReadImmediate<Vector3>().ConvertCoordinateSpace();
    }

    public Color ReadColor()
    {
        return ReadImmediate<Color>();
    }


    public T ReadImmediate<T>()
    {
        string line = ReadLine(false);

        int firstSpaceIndex = line.IndexOf(' ');
        int nextSpaceIndex = line.IndexOf(' ', firstSpaceIndex + 1);

        if (firstSpaceIndex >= 0 && nextSpaceIndex < 0)
            nextSpaceIndex = line.Length;

        Type tType = typeof(T);
        switch (Type.GetTypeCode(tType))
        {
            case TypeCode.Boolean:
                {
                    string parse = line.Substring(firstSpaceIndex + 1, nextSpaceIndex - firstSpaceIndex - 1);
                    if (parse == "1") return (T)(object)true;
                    if (parse == "0") return (T)(object)false;
                    return (T)(object)bool.Parse(parse);
                }
            case TypeCode.Int32:
                {
                    var slice = new StringSlice(line, firstSpaceIndex + 1, nextSpaceIndex - firstSpaceIndex - 1);
                    int result = slice.ToInt();
                    return (T)(object)result;
                }
            case TypeCode.Int16:
                {
                    var slice = new StringSlice(line, firstSpaceIndex + 1, nextSpaceIndex - firstSpaceIndex - 1);
                    short result = (short)slice.ToInt();
                    return (T)(object)result;
                }
            case TypeCode.UInt32:
                {
                    var slice = new StringSlice(line, firstSpaceIndex + 1, nextSpaceIndex - firstSpaceIndex - 1);
                    uint result = (uint)slice.ToInt();
                    return (T)(object)result;
                }
            case TypeCode.UInt16:
                {
                    var slice = new StringSlice(line, firstSpaceIndex + 1, nextSpaceIndex - firstSpaceIndex - 1);
                    ushort result = (ushort)slice.ToInt();
                    return (T)(object)result;
                }
            case TypeCode.Single:
                {
                    var slice = new StringSlice(line, firstSpaceIndex + 1, nextSpaceIndex - firstSpaceIndex - 1);
                    float result = slice.ToFloat();
                    return (T)(object)result;
                }
            case TypeCode.Double:
                {
                    var slice = new StringSlice(line, firstSpaceIndex + 1, nextSpaceIndex - firstSpaceIndex - 1);
                    double result = slice.ToFloat();
                    return (T)(object)result;
                }
            case TypeCode.String:
                {
                    return (T)(object)line.Substring(firstSpaceIndex + 1, nextSpaceIndex - firstSpaceIndex - 1);
                }
        }

        // non-primitives
        if (tType == typeof(Color))
        {
            string[] splits = ReadTokensArray(false);
            return (T)(object)new Color(FastFloatParser.Parse(splits[1]), FastFloatParser.Parse(splits[2]), FastFloatParser.Parse(splits[3]));
        }
        else if(tType == typeof(Vector2))
        {
            string[] splits = ReadTokensArray(false);

            float x = 0f;
            float y = 0f;

            FastFloatParser.TryParse(splits[1], out x);
            FastFloatParser.TryParse(splits[2], out y);

            return (T)(object)new Vector2(x, y);
        }
        else if (tType == typeof(Vector3))
        {
            string[] splits = ReadTokensArray(false);

            float x = 0f;
            float y = 0f;
            float z = 0f;
            FastFloatParser.TryParse(splits[1], out x);
            FastFloatParser.TryParse(splits[2], out y);
            FastFloatParser.TryParse(splits[3], out z);

            return (T)(object)new Vector3(x, y, z);
        }
        else if(tType == typeof(Vector2Int))
        {
            var slices = ReadTokens(false);
            return (T)(object)new Vector2Int(slices[1].ToInt(), slices[2].ToInt());
        }
        else if (tType == typeof(Vector3Int))
        {
            var slices = ReadTokens(false);
            return (T)(object)new Vector3Int(slices[1].ToInt(), slices[2].ToInt(), slices[3].ToInt());
        }
        else
        {
            string[] splits = ReadTokensArray(false);
            var typeConverter = TypeDescriptor.GetConverter(tType);
            return (T)typeConverter.ConvertFrom(splits[1]);
        }
    }

    public T Read<T>(string token, T defaultValue = default)
    {
        int readLines = 0;
        while(readLines < lines.Count)
        {
            string tok = ReadToken();
            if(string.Equals(token, tok, StringComparison.OrdinalIgnoreCase))
            {
                var result = ReadImmediate<T>();
                Seek(1, SeekOrigin.Current);
                return result;
            }
            else
            {
                Seek(1, SeekOrigin.Current);
                if (EOF())
                    Seek(0, SeekOrigin.Begin);
            }
            readLines++;
        }

        Debug.LogWarning("asNodeReader::Read(\"" + token + "\") returning default value.");
        return defaultValue;
    }
}
