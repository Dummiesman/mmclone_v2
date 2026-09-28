using System;
using System.Collections.Generic;
using System.IO;

namespace DirectMusicLite
{
    public struct MidiEvent
    {
        public double Time;    // seconds from the start of the song
        public byte Status;    // full status byte, e.g. 0x90 | channel
        public byte Data1;
        public byte Data2;

        public int Channel { get { return Status & 0x0F; } }
        public int Command { get { return Status & 0xF0; } }
    }

    public sealed class MidiFile
    {
        public readonly List<MidiEvent> Events = new List<MidiEvent>();
        public double DurationSeconds;
        public int Format;
        public int TrackCount;

        struct RawEvent
        {
            public long Tick;
            public long Seq;
            public byte Status, Data1, Data2;
            public int Tempo;     // > 0 marks a tempo change (microseconds per quarter note)
        }

        public static MidiFile Load(string path)
        {
            return Load(File.ReadAllBytes(path));
        }

        public static MidiFile Load(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 14)
                throw new InvalidDataException("MIDI: file is empty or truncated.");

            MidiFile midi = new MidiFile();
            MemoryStream s = new MemoryStream(bytes, false);

            if (ReadFourCC(s) != "MThd")
                throw new InvalidDataException("MIDI: missing MThd header.");
            uint headerSize = ReadUInt32(s);
            long headerEnd = s.Position + headerSize;

            midi.Format = ReadUInt16(s);
            midi.TrackCount = ReadUInt16(s);
            short division = (short)ReadUInt16(s);
            s.Position = headerEnd;

            List<RawEvent> raw = new List<RawEvent>();
            long seq = 0;

            while (s.Position + 8 <= s.Length)
            {
                string id = ReadFourCC(s);
                if (id == null) break;
                uint size = ReadUInt32(s);
                long trackEnd = Math.Min(s.Length, s.Position + size);

                if (id != "MTrk") { s.Position = trackEnd; continue; }

                ReadTrack(s, trackEnd, raw, ref seq);
                s.Position = trackEnd;
            }

            raw.Sort(delegate (RawEvent a, RawEvent b)
            {
                if (a.Tick != b.Tick) return a.Tick < b.Tick ? -1 : 1;
                return a.Seq < b.Seq ? -1 : (a.Seq > b.Seq ? 1 : 0);
            });

            midi.BuildTimeline(raw, division);
            return midi;
        }

        static void ReadTrack(Stream s, long end, List<RawEvent> raw, ref long seq)
        {
            long tick = 0;
            byte runningStatus = 0;

            while (s.Position < end)
            {
                tick += ReadVarLen(s, end);
                int b = s.ReadByte();
                if (b < 0) break;
                byte status;

                if (b >= 0x80)
                {
                    status = (byte)b;
                    if (status < 0xF0) runningStatus = status;
                }
                else
                {
                    if (runningStatus == 0) break;         // corrupt stream
                    status = runningStatus;
                    s.Position--;                           // the byte we peeked is data
                }

                if (status == 0xFF)                          // meta event
                {
                    int type = s.ReadByte();
                    long len = ReadVarLen(s, end);
                    long dataStart = s.Position;

                    if (type == 0x51 && len >= 3)            // set tempo
                    {
                        int t = (s.ReadByte() << 16) | (s.ReadByte() << 8) | s.ReadByte();
                        RawEvent ev = new RawEvent();
                        ev.Tick = tick; ev.Seq = seq++; ev.Tempo = t > 0 ? t : 500000;
                        raw.Add(ev);
                    }

                    s.Position = Math.Min(end, dataStart + len);
                    if (type == 0x2F) break;                 // end of track
                    continue;
                }

                if (status == 0xF0 || status == 0xF7)        // sysex — skipped
                {
                    long len = ReadVarLen(s, end);
                    s.Position = Math.Min(end, s.Position + len);
                    continue;
                }

                int command = status & 0xF0;
                int d1 = s.ReadByte();
                int d2 = 0;
                bool twoData = command != 0xC0 && command != 0xD0;
                if (twoData) d2 = s.ReadByte();
                if (d1 < 0 || d2 < 0) break;

                RawEvent me = new RawEvent();
                me.Tick = tick;
                me.Seq = seq++;
                me.Status = status;
                me.Data1 = (byte)(d1 & 0x7F);
                me.Data2 = (byte)(d2 & 0x7F);
                raw.Add(me);
            }
        }

        void BuildTimeline(List<RawEvent> raw, short division)
        {
            double secondsPerTick;
            bool smpte = division < 0;
            double smpteSecondsPerTick = 0.0;

            if (smpte)
            {
                int fps = -(division >> 8);
                int ticksPerFrame = division & 0xFF;
                if (fps == 29) smpteSecondsPerTick = 1.0 / (29.97 * Math.Max(1, ticksPerFrame));
                else smpteSecondsPerTick = 1.0 / (Math.Max(1, fps) * Math.Max(1, ticksPerFrame));
                secondsPerTick = smpteSecondsPerTick;
            }
            else
            {
                int tpqn = division > 0 ? division : 96;
                secondsPerTick = 500000.0 / 1000000.0 / tpqn;   // default 120 BPM
            }

            long lastTick = 0;
            double time = 0.0;

            for (int i = 0; i < raw.Count; i++)
            {
                RawEvent ev = raw[i];
                time += (ev.Tick - lastTick) * secondsPerTick;
                lastTick = ev.Tick;

                if (ev.Tempo > 0)
                {
                    if (!smpte)
                    {
                        int tpqn = division > 0 ? division : 96;
                        secondsPerTick = ev.Tempo / 1000000.0 / tpqn;
                    }
                    continue;                                   // tempo events are not dispatched
                }

                MidiEvent m = new MidiEvent();
                m.Time = time;
                m.Status = ev.Status;
                m.Data1 = ev.Data1;
                m.Data2 = ev.Data2;
                Events.Add(m);
            }

            DurationSeconds = Events.Count > 0 ? Events[Events.Count - 1].Time : 0.0;
        }

        // ---------------------------------------------------------- helpers

        static long ReadVarLen(Stream s, long end)
        {
            long value = 0;
            for (int i = 0; i < 4; i++)
            {
                if (s.Position >= end) break;
                int b = s.ReadByte();
                if (b < 0) break;
                value = (value << 7) | (uint)(b & 0x7F);
                if ((b & 0x80) == 0) break;
            }
            return value;
        }

        static string ReadFourCC(Stream s)
        {
            byte[] b = new byte[4];
            if (s.Read(b, 0, 4) < 4) return null;
            return System.Text.Encoding.ASCII.GetString(b);
        }

        static uint ReadUInt32(Stream s)   // MIDI is big-endian
        {
            uint b0 = (uint)(s.ReadByte() & 0xFF);
            uint b1 = (uint)(s.ReadByte() & 0xFF);
            uint b2 = (uint)(s.ReadByte() & 0xFF);
            uint b3 = (uint)(s.ReadByte() & 0xFF);
            return (b0 << 24) | (b1 << 16) | (b2 << 8) | b3;
        }

        static int ReadUInt16(Stream s)
        {
            return (s.ReadByte() << 8) | s.ReadByte();
        }
    }
}
