// DmRiff.cs — tolerant RIFF walker shared by the segment and style readers.
//
// Every chunk a parser doesn't claim is recorded rather than skipped silently.
// That log is the point: DirectMusic Producer wrote these files across several
// versions, and the unclaimed list is how you find out what your content actually
// contains versus what this code expects.
//
// Part of DirectMusicLite.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace DirectMusicLite
{
    /// <summary>
    /// Handles one chunk. Return true if the chunk was recognised and consumed;
    /// false to have it recorded as unclaimed.
    /// </summary>
    public delegate bool ChunkHandler(string id, string listType, long dataStart, long dataEnd);

    public static class DmRiff
    {
        public const int PPQ = 768;              // DMUS_PPQ: MUSIC_TIME ticks per quarter note

        public static string FourCC(BinaryReader r)
        {
            byte[] b = r.ReadBytes(4);
            if (b.Length < 4) return null;
            return Encoding.ASCII.GetString(b);
        }

        /// <summary>Iterates the chunks between the current position and <paramref name="end"/>.</summary>
        public static void Walk(BinaryReader r, Stream s, long end, string path,
                                List<string> unclaimed, ChunkHandler handler)
        {
            while (s.Position + 8 <= end)
            {
                string id = FourCC(r);
                if (id == null) break;
                uint size = r.ReadUInt32();

                long dataStart = s.Position;
                long dataEnd = dataStart + size;
                if (dataEnd > end) dataEnd = end;
                long next = dataStart + size + (size & 1);
                if (next > end || next < dataStart) next = end;

                string listType = null;
                if ((id == "LIST" || id == "RIFF") && dataStart + 4 <= dataEnd)
                {
                    listType = FourCC(r);
                    dataStart = s.Position;
                }

                s.Position = dataStart;
                bool handled = false;
                try
                {
                    handled = handler(id, listType, dataStart, dataEnd);
                }
                catch (Exception e)
                {
                    if (unclaimed != null)
                        unclaimed.Add(path + "/" + (listType ?? id) + " — parse error: " + e.Message);
                    handled = true;
                }

                if (!handled && unclaimed != null)
                {
                    unclaimed.Add(string.Format("{0}/{1}{2} — {3} bytes, not interpreted",
                        path, listType ?? id, listType != null ? " (LIST)" : "", size));
                }

                s.Position = next;
            }
        }

        /// <summary>Reads a null-terminated UTF-16 string, as used by UNAM/name/file chunks.</summary>
        public static string ReadWideString(BinaryReader r, long dataEnd)
        {
            StringBuilder sb = new StringBuilder();
            while (r.BaseStream.Position + 2 <= dataEnd)
            {
                ushort c = r.ReadUInt16();
                if (c == 0) break;
                sb.Append((char)c);
            }
            return sb.ToString();
        }

        public static Guid ReadGuid(BinaryReader r)
        {
            return new Guid(r.ReadBytes(16));
        }

        /// <summary>
        /// Reads one of DirectMusic's "sized array" chunks: a DWORD giving the size of
        /// each record, followed by the records. Striding by the stored size is what
        /// keeps this working across format versions that appended fields.
        /// </summary>
        public static int ReadArrayHeader(BinaryReader r, long dataEnd, int minSize, int maxSize)
        {
            if (r.BaseStream.Position + 4 > dataEnd) return 0;
            uint size = r.ReadUInt32();
            if (size < minSize || size > maxSize) return -(int)size;   // negative signals "implausible"
            return (int)size;
        }

        /// <summary>MUSIC_TIME ticks per measure for a time signature.</summary>
        public static int TicksPerMeasure(int beatsPerMeasure, int beatValue)
        {
            if (beatsPerMeasure < 1) beatsPerMeasure = 4;
            if (beatValue < 1) beatValue = 4;
            return TicksPerBeat(beatValue) * beatsPerMeasure;
        }

        public static int TicksPerBeat(int beatValue)
        {
            if (beatValue < 1) beatValue = 4;
            return PPQ * 4 / beatValue;
        }
    }

    /// <summary>DMUS_IO_TIMESIG.</summary>
    public struct DmTimeSig
    {
        public int BeatsPerMeasure;
        public int Beat;              // note value of one beat: 4 = quarter, 8 = eighth
        public int GridsPerBeat;

        public static DmTimeSig Default
        {
            get
            {
                DmTimeSig t = new DmTimeSig();
                t.BeatsPerMeasure = 4; t.Beat = 4; t.GridsPerBeat = 4;
                return t;
            }
        }

        public bool IsValid
        {
            get { return BeatsPerMeasure > 0 && BeatsPerMeasure < 64 && Beat > 0 && GridsPerBeat > 0; }
        }

        public static DmTimeSig Read(System.IO.BinaryReader r)
        {
            DmTimeSig t = new DmTimeSig();
            t.BeatsPerMeasure = r.ReadByte();
            t.Beat = r.ReadByte();
            t.GridsPerBeat = r.ReadUInt16();
            return t;
        }

        public int TicksPerMeasure { get { return DmRiff.TicksPerMeasure(BeatsPerMeasure, Beat); } }
        public int TicksPerBeat { get { return DmRiff.TicksPerBeat(Beat); } }
        public int TicksPerGrid
        {
            get { return GridsPerBeat > 0 ? TicksPerBeat / GridsPerBeat : TicksPerBeat; }
        }

        public override string ToString()
        {
            return BeatsPerMeasure + "/" + Beat + " (" + GridsPerBeat + " grids/beat)";
        }
    }
}
