using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;

public class WinRsrcLoader
{
    public List<string> StringResources = new List<string>();
    public int LanguageId;

    private class SectionHeader
    {
        public string Name;
        public uint VirtualAddress;
        public uint Offset;
        public uint Size;
        public uint RelocationsPointer;
        public uint LinenumberPointer;
        public ushort RelocationCount;
        public ushort LinenumberCount;

        private uint misc;
        public uint PhysicalAddress => misc;
        public uint VirtualSIze => misc;

        public uint Characteristics;

        public void Read(BinaryReader reader)
        {
            Name = new string(reader.ReadChars(8));
            Name = Name.Substring(0, Name.IndexOf('\x00'));

            misc = reader.ReadUInt32();
            VirtualAddress = reader.ReadUInt32();
            Size = reader.ReadUInt32();
            Offset = reader.ReadUInt32();

            RelocationsPointer = reader.ReadUInt32();
            LinenumberPointer = reader.ReadUInt32();
            RelocationCount = reader.ReadUInt16();
            LinenumberCount = reader.ReadUInt16();

            Characteristics = reader.ReadUInt32();
        }
    }

    //FUTURE: NamedResourceEntry and IdResourceEntry, not needed for this project
    /*private class NamedResourceDirectoryEntry : ResourceDirectoryEntry
    {
        public string Name;
    }

    private class IdResourceDirectoryEntry : ResourceDirectoryEntry
    {
        public uint Id;
    }*/

    private class ResourceDirectoryEntry
    {
        public uint Name;
        public uint DataOffset;

        public bool IsSubdirectory { get; private set; }

        public void Read(BinaryReader reader)
        {
            Name = reader.ReadUInt32();
            DataOffset = reader.ReadUInt32();

            //subdirectory check (highest bit set)
            if (DataOffset >= 2147483648)
            {
                IsSubdirectory = true;
                DataOffset -= 2147483648;
            }
        }

        public ResourceDirectoryEntry(BinaryReader reader)
        {
            Read(reader);
        }
    }

    private class ResourceDirectory
    {
        public uint Characteristics;
        public uint TimeDateStamp;
        public ushort MajorVersion;
        public ushort MinorVersion;
        
        public readonly List<ResourceDirectoryEntry> Entries = new List<ResourceDirectoryEntry>();

        public void Read(BinaryReader reader)
        {
            Characteristics = reader.ReadUInt32();
            TimeDateStamp = reader.ReadUInt32();
            MajorVersion = reader.ReadUInt16();
            MinorVersion = reader.ReadUInt16();

            var namedEntryCount = reader.ReadUInt16();
            var idEntryCount = reader.ReadUInt16();
            var entryCountSum = namedEntryCount + idEntryCount;

            for (int i = 0; i < entryCountSum; i++)
            {
                Entries.Add(new ResourceDirectoryEntry(reader));
            }
        }

        public ResourceDirectory(BinaryReader reader)
        {
            Read(reader);
        }
    }

    //helpers
    private ResourceDirectory EntryToDirectory(BinaryReader reader, SectionHeader resourceHeader, ResourceDirectoryEntry entry)
    {
        if(!entry.IsSubdirectory)
            throw new Exception("Tried to convert an invalid entry.");

        reader.BaseStream.Seek(resourceHeader.Offset + entry.DataOffset, SeekOrigin.Begin);
        return new ResourceDirectory(reader);
    }

    private IEnumerable<ResourceDirectoryEntry> EnumerateEntries(BinaryReader reader, SectionHeader resourceHeader, ResourceDirectory directory, bool includeSubdirectories = true)
    {
        foreach (var entry in directory.Entries)
        {
            if (entry.IsSubdirectory && includeSubdirectories)
            {
                foreach(var subEntry in EnumerateEntries(reader, resourceHeader, EntryToDirectory(reader, resourceHeader, entry)))
                {
                    yield return subEntry;
                }
            }
            else
            {
                yield return entry;
            }
        }
    }

    public void Read(BinaryReader reader)
    {
        ushort dosHeader = reader.ReadUInt16();
        if (dosHeader != 23117) //MZ
        {
            throw new Exception("DOS header incorrect.");
        }

        //seek to pe offset
        reader.BaseStream.Seek(60, SeekOrigin.Begin);
        uint peOffset = reader.ReadUInt32();

        //seek to pe
        reader.BaseStream.Seek(peOffset, SeekOrigin.Begin);

        uint peHeader = reader.ReadUInt32();
        if (peHeader != 17744) //PE\x00\x00
        {
            throw new Exception("PE header incorrect.");
        }

        //get section count
        reader.BaseStream.Seek(2, SeekOrigin.Current);
        int numberOfSections = reader.ReadUInt16();

        //read stuff until we get to section headers
        reader.BaseStream.Seek(12, SeekOrigin.Current);
        int optionalHeaderSize = reader.ReadUInt16();
        reader.BaseStream.Seek(2 + optionalHeaderSize, SeekOrigin.Current);

        //read in section headers
        var headers = new List<SectionHeader>();
        for (int i = 0; i < numberOfSections; i++)
        {
            var header = new SectionHeader();
            header.Read(reader);
            headers.Add(header);
        }

        //look for RSRC header
        int rsrcHeaderIndex = headers.FindIndex(x => x.Name.ToLowerInvariant() == ".rsrc");
        if (rsrcHeaderIndex < 0)
        {
            throw new Exception("File has no RSRC section.");
        }

        var rsrcHeader = headers[rsrcHeaderIndex];

        //read RSRC string tables
        ReadStringTables(reader, rsrcHeader);
    }

    private void ReadStringTables(BinaryReader reader, SectionHeader rsrcHeader)
    {
        reader.BaseStream.Seek(rsrcHeader.Offset, SeekOrigin.Begin);

        var resourceRootDirectory = new ResourceDirectory(reader);
        var rootEntries = EnumerateEntries(reader, rsrcHeader, resourceRootDirectory, false).ToArray();

        //get string table entries.
        //these are tables of language id directories
        //0  < we're storing this level
        //--1033  < so the next loop can grab this
        //----<string table> < and this
        var stringTableRoots = rootEntries.Where(x => x.Name == 6 && x.IsSubdirectory).ToArray();
        List<ResourceDirectoryEntry> stringTableEntries = new List<ResourceDirectoryEntry>();

        foreach (var stRootEntry in stringTableRoots)
        {
            var directory = EntryToDirectory(reader, rsrcHeader, stRootEntry);
            foreach (var stEntry in EnumerateEntries(reader, rsrcHeader, directory, false))
            {
                stringTableEntries.Add(stEntry);
            }
        }

        //read strings!
        foreach (var stEntry in stringTableEntries)
        {
            var directory = EntryToDirectory(reader, rsrcHeader, stEntry);
            foreach (var stLangEntry in EnumerateEntries(reader, rsrcHeader, directory, false))
            {
                //store langid
                var langId = stLangEntry.Name;
                LanguageId = (int) langId;


                //go to the place that stores the offset to this table
                var stOffsetOffset = stLangEntry.DataOffset;
                reader.BaseStream.Seek(stOffsetOffset + rsrcHeader.Offset, SeekOrigin.Begin);

                //get strings
                var firstStringOffset = reader.ReadUInt32();

                reader.BaseStream.Seek(firstStringOffset, SeekOrigin.Begin);
                for (int i = 0; i < 16; i++)
                {
                    ushort strLen = reader.ReadUInt16();

                    byte[] utf16Array = reader.ReadBytes(strLen * 2);
                    StringResources.Add(System.Text.Encoding.Unicode.GetString(utf16Array)); //TODO : make this a dict and save the actual ID
                }
            }
        }
    }
}
