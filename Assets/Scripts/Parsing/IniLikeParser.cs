using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public class IniLikeParser
{
    private string[] lines;
    private int currentLine = 0;

    //ctor
    public IniLikeParser(Stream stream)
    {
        var reader = new StreamReader(stream);

        string line;

        var lines = new List<string>();

        while ((line = reader.ReadLine()) != null)
        {
            lines.Add(line.Clean());
        }

        // I hate this, but well...
        this.lines = lines.ToArray();
    }

    public IniLikeParser(string[] lines)
    {
        for (int i = 0; i < lines.Length; i++)
            lines[i] = lines[i].Clean();
        this.lines = lines;
    }

    //seeking stuff
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
                currentLine = lines.Length + position;
                break;
        }
    }

    //
    public bool EOF()
    {
        return (currentLine >= lines.Length);
    }

    //reading stuff
    public string[] GetTokens()
    {
        return lines[currentLine].Clean().Split(' ');
    }

    public string GetLine()
    {
        return lines[currentLine];
    }

    public bool SkipToSection(string token, int maxSkip = -1)
    {
        int lastFilePosition = currentLine;
        bool lastLineWasSection = false;

        while (!lastLineWasSection)
        {
            //see if we reached EOF
            if (EOF() || (maxSkip >= 0 && (currentLine - lastFilePosition) >= maxSkip))
            {
                currentLine = lastFilePosition;
                return false;
            }

            //see if we're at the section
            lastLineWasSection = lines[currentLine].IndexOf("[") == 0 &&
                                 lines[currentLine].IndexOf(token, StringComparison.InvariantCultureIgnoreCase) >= 0;
            currentLine++;
        }
        
        //found token
        Seek(-1, SeekOrigin.Current);
        return true;
    }

}