using System;
using System.IO;
using System.Text;
using UnityEngine;

public class PackageFile : IDisposable
{
    private const uint MagicPKG2 = 0x32474B50; // 'PKG2'
    private const uint MagicPKG3 = 0x33474B50; // 'PKG3'
    private const uint MagicFILE = 0x454C4946; // 'FILE'

    public string CurrentFileName => currentFileName;

    private string currentFileName = string.Empty;
    private int currentFileSize = 0;
    private uint magic;
    private BinaryReader reader;

    public BinaryReader OpenFile(string name)
    {
        if (reader == null) return null;
        return string.Equals(name, currentFileName, StringComparison.OrdinalIgnoreCase)
            ? reader
            : null;
    }

    public void CloseFile()
    {
        NextItem();
    }

    public void Skip()
    {
        if (reader == null) return;
        reader.BaseStream.Seek(currentFileSize, SeekOrigin.Current);
        NextItem();
    }

    public bool SkipTo(string name)
    {
        if (magic != MagicPKG3)
            throw new InvalidOperationException("PackageModel.SkipTo - old format, can't use SkipTo");

        while (reader != null && !string.Equals(currentFileName, name, StringComparison.OrdinalIgnoreCase))
        {
            bool atEnd = (currentFileName == "[[[ EOF ]]]");
            if (atEnd) return false;
            Skip();
        }
        return true;
    }

    public void NextItem()
    {
        if (reader == null) return;

        uint fileMagic;
        try
        {
            fileMagic = reader.ReadUInt32();
        }
        catch (EndOfStreamException)
        {
            currentFileName = "[[[ EOF ]]]";
            currentFileSize = 0;
            return;
        }

        if (fileMagic != MagicFILE)
        {
            throw new InvalidDataException("PackageModel: missing file magic, previous reader didn't consume all data.");
        }
            
        currentFileName = reader.ReadAGEString();
        currentFileSize = (magic == MagicPKG3) ? reader.ReadInt32() : 0;
    }

    public void Dispose()
    {
        if (reader == null) return;
        reader.Dispose();
        reader = null;
    }

    public PackageFile(string directory, string file) : this(AssetManager.Open(directory, $"{file}.pkg"))
    {

    }

    public PackageFile(Stream stream, bool leaveOpen = false)
    {
        if (stream == null)
        {
            Debug.LogError("PackageModel - null stream");
            return;
        }

        var r = new BinaryReader(stream, Encoding.ASCII, leaveOpen);

        try
        {
            magic = r.ReadUInt32();
        }
        catch (EndOfStreamException)
        {
            Debug.LogError("PackageModel - not enough data for magic");
            r.Dispose();
            return;
        }

        if (magic != MagicPKG2 && magic != MagicPKG3)
        {
            Debug.LogError($"PackageModel - bad magic 0x{magic:X8}");
            r.Dispose();
            return;
        }

        reader = r;
        NextItem();
    }
}
