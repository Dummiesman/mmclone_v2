using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

public class CSVParser 
{
    private List<string> lines;
    private List<StringSlice> preparedLine = new List<StringSlice>();
    private int currentLine = 0;
    private Dictionary<string, int> currentHeader = new Dictionary<string, int>();

    public int TokenCount => (preparedLine != null && preparedLine.Count > 0) ? preparedLine.Count : -1;
    public int LineCount => lines.Count;

    public CSVParser(Stream stream)
    {
        string line;
        this.lines = new List<string>(256);

        using (var reader = new StreamReader(stream, Encoding.ASCII, detectEncodingFromByteOrderMarks: true, bufferSize: 1024, leaveOpen: true))
        {
            while ((line = reader.ReadLine()) != null)
            {
                string cleaned = line.Clean();
                if (!string.IsNullOrWhiteSpace(cleaned))
                {
                    lines.Add(cleaned);
                }
            }
        }
    }

    public string this[string key]
    {
        get 
        {
            if(currentHeader.TryGetValue(key, out int valueIndex))
            {
                return preparedLine[valueIndex].ToString();
            }
            return null;
        }
    }
    public string this[int key] => preparedLine[key].ToString();

    public void SetHeader(params string[] headerSplits)
    {
        currentHeader.Clear();
        for (int i = 0; i < headerSplits.Length; i++)
            currentHeader[headerSplits[i]] = i;
    }

    // seeking stuff
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

    public bool EOF()
    {
        return(currentLine >= lines.Count);
    }

    //reading
    public override string ToString()
    {
        string splits = "[";
        foreach(var splt in preparedLine)
        {
            splits += splt.ToString() + ",";
        }
        splits = splits.Substring(0, splits.Length - 1);
        return splits + "]";
    }

    public void PrepareHeader()
    {
        PrepareLine();

        var headerSplits = GetTokens();
        SetHeader(headerSplits);
    }

    public void PrepareLine()
    {
        preparedLine.Clear();
        preparedLine.AddRange(StringArrayParsingUtil.GetCommaSeparatedSlices(lines[currentLine]));
        currentLine++;
    }

    public string GetToken(int index)
    {
        return preparedLine[index].ToString();
    }

    public string[] GetTokens()
    {
        return preparedLine.Select(x => x.ToString()).ToArray();
    }

    public string GetColumnCaseInsensitive(string name)
    {
        foreach(var kvp in currentHeader)
        {
            if(kvp.Key.Equals(name, System.StringComparison.InvariantCultureIgnoreCase))
            {
                return preparedLine[kvp.Value].ToString();
            }
        }
        return null;
    }
}
