// DmSegment.cs — DirectMusic segment (.sgt) reader, RIFF form 'DMSG'.
//
// A segment is a container of tracks. The ones that matter for style playback:
//   command track  — groove levels and embellishments (this is what you asked about)
//   style track    — a reference, usually by filename, to an external .sty
//   chord track    — the harmony that style notes are resolved against
//   band track     — PChannel -> DLS instrument assignments
//   tempo/timesig  — the clock
//
// And the one that doesn't go through a style at all:
//   sequence track — literal MIDI events on the segment's own timeline
//
// A sequence track is what a segment imported from a MIDI file is made of, and what
// Producer writes for anything hand-placed on the timeline rather than composed from
// a pattern. Its events carry absolute MUSIC_TIME and a PChannel, so they need no
// chord, groove or variation — only the band, to say which instrument a PChannel is.
//
// Tracks are dispatched on their data chunk id rather than the track class GUID,
// because the GUIDs vary across DirectMusic versions and the chunk ids don't.
//
// Part of DirectMusicLite.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace DirectMusicLite
{
    public enum DmCommandType
    {
        Groove = 0, Fill = 1, Intro = 2, Break = 3, End = 4, EndAndIntro = 5
    }

    [Flags]
    public enum DmEmbellishment
    {
        Normal = 0, Groove = 1, Fill = 2, Intro = 4, Break = 8, End = 16, Motif = 32
    }

    public sealed class DmCommand
    {
        public int Time;                  // MUSIC_TIME
        public int Measure;
        public int Beat;
        public DmCommandType Command = DmCommandType.Groove;
        public int GrooveLevel = 50;      // 1..100
        public int GrooveRange;
        public int RepeatMode;

        public override string ToString()
        {
            return string.Format("m{0} b{1}: {2} groove {3} (+/-{4})",
                Measure, Beat, Command, GrooveLevel, GrooveRange);
        }
    }

    public sealed class DmTempoEvent { public int Time; public double Bpm = 120.0; }

    public sealed class DmTimeSigEvent { public int Time; public DmTimeSig TimeSig = DmTimeSig.Default; }

    public sealed class DmSubChord
    {
        public uint ChordPattern;     // bitfield of semitone intervals present in the chord
        public uint ScalePattern;     // bitfield of semitone intervals in the scale
        public uint InversionPoints;
        public uint Levels;
        public int ChordRoot;         // 0 = C
        public int ScaleRoot;
    }

    public sealed class DmChordEvent
    {
        public int Time;
        public int Measure;
        public int Beat;
        public string Name;
        public readonly List<DmSubChord> SubChords = new List<DmSubChord>();

        /// <summary>Picks the subchord for a part's subchord level, or the first available.</summary>
        public DmSubChord ForLevel(int level)
        {
            for (int i = 0; i < SubChords.Count; i++)
                if ((SubChords[i].Levels & (1u << level)) != 0) return SubChords[i];
            return SubChords.Count > 0 ? SubChords[0] : null;
        }
    }

    public sealed class DmStyleRef
    {
        public int Time;
        public Guid Id;
        public string FileName;
        public string Name;

        public override string ToString()
        {
            return (Name ?? "<unnamed>") + (string.IsNullOrEmpty(FileName) ? "" : " -> " + FileName);
        }
    }

    /// <summary>
    /// DMUS_IO_INST_* validity flags. Each field of a band instrument is only meaningful
    /// when its bit is set; reading one without checking gives whatever happened to be in
    /// those bytes.
    /// </summary>
    [Flags]
    public enum DmInstrumentFlags
    {
        None = 0,
        Patch = 1 << 0,
        BankSelect = 1 << 1,
        AssignPatch = 1 << 3,
        NoteRanges = 1 << 4,
        Pan = 1 << 5,
        Volume = 1 << 6,
        Transpose = 1 << 7,
        GM = 1 << 8,
        GS = 1 << 9,
        XG = 1 << 10,
        ChannelPriority = 1 << 11,
        UseDefaultGmSet = 1 << 12,
        PitchBendRange = 1 << 13
    }

    public sealed class DmBandInstrument
    {
        public uint Patch;
        public DmInstrumentFlags Flags;

        public bool HasPatch { get { return (Flags & DmInstrumentFlags.Patch) != 0; } }
        public bool HasBankSelect { get { return (Flags & DmInstrumentFlags.BankSelect) != 0; } }
        public bool HasPan { get { return (Flags & DmInstrumentFlags.Pan) != 0; } }
        public bool HasVolume { get { return (Flags & DmInstrumentFlags.Volume) != 0; } }
        public bool HasTranspose { get { return (Flags & DmInstrumentFlags.Transpose) != 0; } }
        public bool HasPitchBendRange { get { return (Flags & DmInstrumentFlags.PitchBendRange) != 0; } }
        /// <summary>Use the default General MIDI set regardless of what else is loaded.</summary>
        public bool UsesDefaultGmSet
        {
            get
            {
                return (Flags & (DmInstrumentFlags.UseDefaultGmSet | DmInstrumentFlags.GM)) != 0;
            }
        }
        public int PChannel;
        public int Pan = 64;
        public int Volume = 100;
        public int Transpose;
        public int PitchBendRange = 2;
        public string DlsReference;
        /// <summary>Name authored on the band instrument itself, when present.</summary>
        public string Name;
        /// <summary>Friendly name of the referenced DLS collection, when present.</summary>
        public string DlsReferenceName;

        /// <summary>DirectMusic addresses channels in groups of 16: PChannel = group * 16 + channel.</summary>
        public int ChannelGroup { get { return PChannel / 16; } }
        public int ChannelInGroup { get { return PChannel % 16; } }

        public int Program { get { return (int)(Patch & 0x7F); } }
        // Bank bits are only meaningful when BANKSELECT is set; otherwise the patch is a
        // bare program number and the bank part is undefined.
        public int BankLsb { get { return HasBankSelect ? (int)((Patch >> 8) & 0x7F) : 0; } }
        public int BankMsb { get { return HasBankSelect ? (int)((Patch >> 16) & 0x7F) : 0; } }
        public bool IsDrum { get { return (Patch & 0x80000000u) != 0; } }

        public override string ToString()
        {
            return string.Format("PChannel {0} (group {1} ch {2}){3}: bank {4}/{5} program {6}{7} " +
                                 "vol {8}{9} pan {10}{11}{12}{13}  [{14}]",
                PChannel, ChannelGroup + 1, ChannelInGroup,
                string.IsNullOrEmpty(Name) ? "" : " \"" + Name + "\"",
                BankMsb, BankLsb, Program, IsDrum ? " drum" : "",
                Volume, HasVolume ? "" : "(invalid)",
                Pan, HasPan ? "" : "(invalid)",
                HasTranspose ? "  transpose " + Transpose : "",
                HasPitchBendRange ? "  bend +/-" + PitchBendRange : "",
                Flags == DmInstrumentFlags.None ? "no flags" : Flags.ToString());
        }
    }

    public sealed class DmBandEvent
    {
        public int Time;
        public readonly List<DmBandInstrument> Instruments = new List<DmBandInstrument>();
    }

    /// <summary>
    /// DMUS_IO_SEQ_ITEM — one event of a sequence track: a plain MIDI message placed at
    /// an absolute point on the segment's timeline. No chord resolution, no variations,
    /// no groove; what is authored is what plays.
    ///
    /// The channel nibble of <see cref="Status"/> is not the channel. DirectMusic routes
    /// the event by <see cref="PChannel"/>, which the band maps to an instrument, exactly
    /// as it does for style parts.
    /// </summary>
    public sealed class DmSequenceEvent
    {
        public int Time;              // MUSIC_TIME, the authored position
        public int Duration;          // note length; 0 on everything that isn't a note
        public int PChannel;

        /// <summary>nOffset: how far from Time the event actually sounds, for feel.</summary>
        public int Offset;

        public byte Status;           // MIDI status byte, channel nibble unused
        public byte Byte1, Byte2;

        /// <summary>When the event sounds: its authored time plus its offset.</summary>
        public int PlayTime { get { return Time + Offset; } }

        /// <summary>Status with the channel nibble masked off: 0x90, 0xB0, 0xE0 and so on.</summary>
        public int Command { get { return Status & 0xF0; } }

        /// <summary>A note-on with velocity 0 is a note-off, as in a MIDI file.</summary>
        public bool IsNoteOn { get { return Command == 0x90 && Byte2 > 0; } }
        public bool IsNoteOff { get { return Command == 0x80 || (Command == 0x90 && Byte2 == 0); } }

        /// <summary>14-bit pitch bend value, valid when Command is 0xE0.</summary>
        public int BendValue { get { return Byte1 | (Byte2 << 7); } }

        /// <summary>Last tick this event occupies — the note-off time, for a note.</summary>
        public int EndTime { get { return PlayTime + (IsNoteOn ? Math.Max(1, Duration) : 0); } }

        public override string ToString()
        {
            string what;
            switch (Command)
            {
                case 0x80: what = "note off " + Byte1; break;
                case 0x90: what = Byte2 == 0 ? "note off " + Byte1
                                : "note " + Byte1 + " vel " + Byte2 + " len " + Duration; break;
                case 0xA0: what = "poly aftertouch " + Byte1 + " = " + Byte2; break;
                case 0xB0: what = "CC" + Byte1 + " = " + Byte2; break;
                case 0xC0: what = "program " + Byte1; break;
                case 0xD0: what = "channel aftertouch " + Byte1; break;
                case 0xE0: what = "pitch bend " + BendValue; break;
                default: what = "status 0x" + Status.ToString("X2"); break;
            }
            return "t" + PlayTime + " pch" + PChannel + ": " + what;
        }
    }

    /// <summary>
    /// DMUS_IO_CURVE_ITEM — a controller sweep on a sequence track. Same idea as the
    /// curves authored inside style parts, but positioned in absolute MUSIC_TIME against
    /// a PChannel rather than on a part's grid against a variation.
    /// </summary>
    public sealed class DmSequenceCurve
    {
        public int Time;              // MUSIC_TIME
        public int Duration;
        public int ResetDuration;
        public int PChannel;
        public int Offset;
        public int StartValue;
        public int EndValue;
        public int ResetValue;
        public DmCurveType Type = DmCurveType.ControlChange;
        public DmCurveShape Shape = DmCurveShape.Linear;
        public int ControllerNumber = 10;   // meaningful when Type is ControlChange
        public int Flags;
        /// <summary>DX8 additions; carried for completeness, not acted on.</summary>
        public int ParamType, MergeIndex;

        /// <summary>DMUS_CURVE_RESET: return the controller to ResetValue afterwards.</summary>
        public bool ResetsAfterwards { get { return (Flags & 0x1) != 0; } }

        public int PlayTime { get { return Time + Offset; } }
        public int EndTime { get { return PlayTime + Math.Max(0, Duration); } }

        /// <summary>Value at a fraction 0..1 through the curve.</summary>
        public float ValueAt(float t)
        {
            return DmCurveMath.Evaluate(Shape, StartValue, EndValue, t);
        }

        public override string ToString()
        {
            string what = Type == DmCurveType.ControlChange ? "CC" + ControllerNumber : Type.ToString();
            return string.Format("t{0} pch{1}: {2} {3} {4}->{5} over {6} ticks{7}",
                PlayTime, PChannel, what, Shape, StartValue, EndValue, Duration,
                ResetsAfterwards ? " then reset to " + ResetValue : "");
        }
    }

    public sealed class DmSegment
    {
        public string Name;
        public int Repeats;
        public int Length;
        public int PlayStart, LoopStart, LoopEnd;
        public int Resolution;

        /// <summary>DMUS_SEG_REPEAT_INFINITE (0xFFFFFFFF) arrives as -1.</summary>
        public bool RepeatsForever { get { return Repeats < 0; } }

        /// <summary>Where playback begins: mtPlayStart.</summary>
        public int ResolvedPlayStart { get { return PlayStart > 0 ? PlayStart : 0; } }

        /// <summary>Where a loop returns to: mtLoopStart.</summary>
        public int ResolvedLoopStart { get { return LoopStart > 0 ? LoopStart : 0; } }

        /// <summary>
        /// Where a loop turns around. mtLoopEnd of 0 means "the whole segment", so it
        /// falls back to mtLength; 0 here means there is no end at all.
        /// </summary>
        public int ResolvedLoopEnd
        {
            get
            {
                if (LoopEnd > ResolvedLoopStart) return LoopEnd;
                if (Length > ResolvedLoopStart) return Length;
                return 0;
            }
        }

        public bool HasLoopPoints { get { return ResolvedLoopEnd > ResolvedLoopStart; } }

        public readonly List<DmCommand> Commands = new List<DmCommand>();
        public readonly List<DmTempoEvent> Tempos = new List<DmTempoEvent>();
        public readonly List<DmTimeSigEvent> TimeSignatures = new List<DmTimeSigEvent>();
        public readonly List<DmChordEvent> Chords = new List<DmChordEvent>();
        public readonly List<DmStyleRef> StyleReferences = new List<DmStyleRef>();
        public readonly List<DmBandEvent> Bands = new List<DmBandEvent>();

        /// <summary>
        /// Sequence track events, merged across every sequence track in the segment and
        /// ordered by play time. They address instruments by PChannel, the same way the
        /// band and the style's parts do, so several tracks sharing a PChannel are just
        /// one stream of events to that instrument.
        /// </summary>
        public readonly List<DmSequenceEvent> SequenceEvents = new List<DmSequenceEvent>();

        /// <summary>Controller sweeps from the sequence tracks' 'curl' chunks.</summary>
        public readonly List<DmSequenceCurve> SequenceCurves = new List<DmSequenceCurve>();

        /// <summary>How many sequence tracks contributed to the lists above.</summary>
        public int SequenceTrackCount;

        public bool HasSequence { get { return SequenceEvents.Count > 0 || SequenceCurves.Count > 0; } }

        /// <summary>
        /// Last tick the sequence occupies, note tails included. A segment imported from
        /// a MIDI file often leaves mtLength at 0, and then this is the only thing that
        /// says when the music is over.
        /// </summary>
        public int SequenceEndTick
        {
            get
            {
                int last = 0;
                for (int i = 0; i < SequenceEvents.Count; i++)
                {
                    int t = SequenceEvents[i].EndTime;
                    if (t > last) last = t;
                }
                for (int i = 0; i < SequenceCurves.Count; i++)
                {
                    int t = SequenceCurves[i].EndTime + Math.Max(0, SequenceCurves[i].ResetDuration);
                    if (t > last) last = t;
                }
                return last;
            }
        }

        /// <summary>Distinct PChannels the sequence addresses, in ascending order.</summary>
        public List<int> SequencePChannels()
        {
            List<int> channels = new List<int>();
            for (int i = 0; i < SequenceEvents.Count; i++)
                if (!channels.Contains(SequenceEvents[i].PChannel)) channels.Add(SequenceEvents[i].PChannel);
            for (int i = 0; i < SequenceCurves.Count; i++)
                if (!channels.Contains(SequenceCurves[i].PChannel)) channels.Add(SequenceCurves[i].PChannel);
            channels.Sort();
            return channels;
        }

        /// <summary>Chunks this reader did not interpret. Check this against real content.</summary>
        public readonly List<string> UnclaimedChunks = new List<string>();

        public int TrackCount;

        public static DmSegment Load(string path) { return Load(File.ReadAllBytes(path)); }

        public static DmSegment Load(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 12)
                throw new InvalidDataException("Segment: file is empty or truncated.");

            MemoryStream ms = new MemoryStream(bytes, false);
            BinaryReader r = new BinaryReader(ms);

            if (DmRiff.FourCC(r) != "RIFF")
                throw new InvalidDataException("Segment: not a RIFF file.");
            uint size = r.ReadUInt32();
            string form = DmRiff.FourCC(r);
            if (form != "DMSG")
                throw new InvalidDataException(
                    "Segment: RIFF form is '" + form + "', expected 'DMSG'. " +
                    (form == "DMST" ? "This looks like a style (.sty), not a segment (.sgt)." : ""));

            DmSegment seg = new DmSegment();
            long end = Math.Min(ms.Length, 8L + size);
            DmRiff.Walk(r, ms, end, "DMSG", seg.UnclaimedChunks,
                delegate (string id, string listType, long ds, long de) { return seg.OnTop(r, ms, id, listType, ds, de); });
            seg.SortSequence();
            return seg;
        }

        bool OnTop(BinaryReader r, Stream s, string id, string listType, long ds, long de)
        {
            if (id == "segh") { ReadHeader(r, de); return true; }
            if (id == "guid" || id == "vers" || id == "date") return true;
            if (listType == "UNFO") { Name = ReadUnfoName(r, s, de); return true; }
            if (listType == "trkl")
            {
                DmRiff.Walk(r, s, de, "DMSG/trkl", UnclaimedChunks,
                    delegate (string i2, string l2, long d2, long e2)
                    {
                        if (l2 != "DMTK") return false;
                        TrackCount++;
                        ReadTrack(r, s, d2, e2);
                        return true;
                    });
                return true;
            }
            return false;
        }

        void ReadHeader(BinaryReader r, long end)
        {
            // Later versions append fields; read only what's present.
            long start = r.BaseStream.Position;
            Repeats = (int)r.ReadUInt32();
            Length = r.ReadInt32();
            PlayStart = r.ReadInt32();
            LoopStart = r.ReadInt32();
            LoopEnd = r.ReadInt32();
            if (r.BaseStream.Position + 4 <= end) Resolution = (int)r.ReadUInt32();
            r.BaseStream.Position = start;
        }

        void ReadTrack(BinaryReader r, Stream s, long start, long end)
        {
            // One track counts once however its sequence chunks are wrapped: a 'seqt'
            // list, or a bare 'evtl' and 'curl' pair.
            bool[] countedSequence = new bool[1];

            DmRiff.Walk(r, s, end, "DMSG/trkl/DMTK", UnclaimedChunks,
                delegate (string id, string listType, long ds, long de)
                {
                    switch (id)
                    {
                        case "trkh": return true;      // class GUID; we dispatch on data chunks instead
                        case "trkx": return true;
                        case "cmnd": ReadCommands(r, de); return true;
                        case "tetr": ReadTempos(r, de); return true;
                        case "tims": ReadTimeSignatures(r, de); return true;

                        // 'seqt' holds the sequence track's chunks. The format documents
                        // it as a LIST, and that case is handled below, but what Producer
                        // actually writes — in both .sgt and .sgp — is a plain chunk whose
                        // payload is the same 'evtl' and 'curl' pair. Treating only the
                        // documented spelling as a sequence track means every real file
                        // lands in the unclaimed list instead of playing.
                        case "seqt": CountSequenceTrack(countedSequence); ReadSequenceTrack(r, s, de); return true;

                        // And the ids inside it are unambiguous on their own, so an
                        // unwrapped pair is read rather than reported.
                        case "evtl": CountSequenceTrack(countedSequence); ReadSequenceEvents(r, de); return true;
                        case "curl": CountSequenceTrack(countedSequence); ReadSequenceCurves(r, de); return true;
                    }

                    switch (listType)
                    {
                        case "cord": ReadChordTrack(r, s, de); return true;
                        case "sttr": ReadStyleTrack(r, s, de); return true;
                        case "DMBT": ReadBandTrack(r, s, de); return true;
                        case "seqt": CountSequenceTrack(countedSequence); ReadSequenceTrack(r, s, de); return true;

                        // Producer wraps the time signature array in a LIST of its own.
                        // Missed, the segment silently falls back to the style's time
                        // signature, which puts every measure line in the wrong place.
                        case "TIMS":
                            DmRiff.Walk(r, s, de, "DMSG/TIMS", UnclaimedChunks,
                                delegate (string i2, string l2, long d2, long e2)
                                {
                                    if (i2 != "tims") return false;
                                    ReadTimeSignatures(r, e2);
                                    return true;
                                });
                            return true;

                        case "UNFO": return true;
                    }
                    return false;
                });
        }

        // ------------------------------------------------------- command track

        void ReadCommands(BinaryReader r, long end)
        {
            int stride = DmRiff.ReadArrayHeader(r, end, 8, 256);
            if (stride <= 0)
            {
                UnclaimedChunks.Add("DMSG/cmnd — implausible record size " + (-stride) + ", skipped");
                return;
            }

            while (r.BaseStream.Position + stride <= end)
            {
                long recStart = r.BaseStream.Position;
                DmCommand c = new DmCommand();
                c.Time = r.ReadInt32();
                c.Measure = r.ReadUInt16();
                c.Beat = r.ReadByte();
                c.Command = (DmCommandType)r.ReadByte();
                c.GrooveLevel = r.ReadByte();
                c.GrooveRange = r.ReadByte();
                if (recStart + stride > r.BaseStream.Position) c.RepeatMode = r.ReadByte();
                Commands.Add(c);
                r.BaseStream.Position = recStart + stride;
            }
        }

        void ReadTempos(BinaryReader r, long end)
        {
            int stride = DmRiff.ReadArrayHeader(r, end, 12, 256);
            if (stride <= 0) { UnclaimedChunks.Add("DMSG/tetr — implausible record size"); return; }

            while (r.BaseStream.Position + stride <= end)
            {
                long recStart = r.BaseStream.Position;
                DmTempoEvent t = new DmTempoEvent();
                t.Time = r.ReadInt32();
                r.ReadUInt32();                          // alignment padding before the double
                t.Bpm = r.ReadDouble();
                if (t.Bpm > 1.0 && t.Bpm < 1000.0) Tempos.Add(t);
                r.BaseStream.Position = recStart + stride;
            }
        }

        void ReadTimeSignatures(BinaryReader r, long end)
        {
            int stride = DmRiff.ReadArrayHeader(r, end, 8, 256);
            if (stride <= 0) { UnclaimedChunks.Add("DMSG/tims — implausible record size"); return; }

            while (r.BaseStream.Position + stride <= end)
            {
                long recStart = r.BaseStream.Position;
                DmTimeSigEvent t = new DmTimeSigEvent();
                t.Time = r.ReadInt32();
                t.TimeSig = DmTimeSig.Read(r);
                if (t.TimeSig.IsValid) TimeSignatures.Add(t);
                r.BaseStream.Position = recStart + stride;
            }
        }

        // ------------------------------------------------------ sequence track

        /// <summary>
        /// The sequence track form: LIST 'seqt' holding an 'evtl' array of events and
        /// optionally a 'curl' array of controller curves.
        /// </summary>
        void CountSequenceTrack(bool[] counted)
        {
            if (counted[0]) return;
            counted[0] = true;
            SequenceTrackCount++;
        }

        void ReadSequenceTrack(BinaryReader r, Stream s, long end)
        {
            DmRiff.Walk(r, s, end, "DMSG/seqt", UnclaimedChunks,
                delegate (string id, string listType, long ds, long de)
                {
                    if (id == "evtl") { ReadSequenceEvents(r, de); return true; }
                    if (id == "curl") { ReadSequenceCurves(r, de); return true; }
                    return listType == "UNFO";
                });
        }

        /// <summary>DMUS_IO_SEQ_ITEM records: 17 bytes of fields, normally padded to 20.</summary>
        void ReadSequenceEvents(BinaryReader r, long end)
        {
            int stride = DmRiff.ReadArrayHeader(r, end, 17, 256);
            if (stride <= 0)
            {
                UnclaimedChunks.Add("DMSG/seqt/evtl — implausible record size " + (-stride) + ", skipped");
                return;
            }

            while (r.BaseStream.Position + stride <= end)
            {
                long recStart = r.BaseStream.Position;
                DmSequenceEvent e = new DmSequenceEvent();
                e.Time = r.ReadInt32();
                e.Duration = r.ReadInt32();
                e.PChannel = (int)r.ReadUInt32();
                e.Offset = r.ReadInt16();
                e.Status = r.ReadByte();
                e.Byte1 = r.ReadByte();
                e.Byte2 = r.ReadByte();

                // A status byte below 0x80 isn't a MIDI message at all; it means this
                // record didn't land where the stride said it would. Keeping it would
                // fire nonsense at the synth, so it's reported instead.
                if (e.Status < 0x80)
                    UnclaimedChunks.Add("DMSG/seqt/evtl — event at t" + e.Time +
                                        " has status 0x" + e.Status.ToString("X2") + ", skipped");
                else
                    SequenceEvents.Add(e);

                r.BaseStream.Position = recStart + stride;
            }
        }

        /// <summary>DMUS_IO_CURVE_ITEM records: 28 bytes, 32 with the DX8 additions.</summary>
        void ReadSequenceCurves(BinaryReader r, long end)
        {
            int stride = DmRiff.ReadArrayHeader(r, end, 28, 512);
            if (stride <= 0)
            {
                UnclaimedChunks.Add("DMSG/seqt/curl — implausible record size " + (-stride) + ", skipped");
                return;
            }

            while (r.BaseStream.Position + stride <= end)
            {
                long recStart = r.BaseStream.Position;
                DmSequenceCurve c = new DmSequenceCurve();
                c.Time = r.ReadInt32();
                c.Duration = r.ReadInt32();
                c.ResetDuration = r.ReadInt32();
                c.PChannel = (int)r.ReadUInt32();
                c.Offset = r.ReadInt16();
                c.StartValue = r.ReadInt16();
                c.EndValue = r.ReadInt16();
                c.ResetValue = r.ReadInt16();
                c.Type = (DmCurveType)r.ReadByte();
                c.Shape = (DmCurveShape)r.ReadByte();
                c.ControllerNumber = r.ReadByte();
                c.Flags = r.ReadByte();
                // Later fields, only on files written by DX8 and after.
                if (r.BaseStream.Position + 2 <= recStart + stride) c.ParamType = r.ReadUInt16();
                if (r.BaseStream.Position + 2 <= recStart + stride) c.MergeIndex = r.ReadUInt16();

                SequenceCurves.Add(c);
                r.BaseStream.Position = recStart + stride;
            }
        }

        /// <summary>
        /// Orders the merged sequence lists by play time. Each track is authored in
        /// order, so this only has work to do where tracks were merged — an insertion
        /// sort costs nothing on the common case and keeps events authored at the same
        /// tick in the order the file listed them, which decides whether a program
        /// change lands before or after the note it was written for.
        /// </summary>
        void SortSequence()
        {
            for (int i = 1; i < SequenceEvents.Count; i++)
            {
                DmSequenceEvent v = SequenceEvents[i];
                int j = i - 1;
                while (j >= 0 && SequenceEvents[j].PlayTime > v.PlayTime) { SequenceEvents[j + 1] = SequenceEvents[j]; j--; }
                SequenceEvents[j + 1] = v;
            }
            for (int i = 1; i < SequenceCurves.Count; i++)
            {
                DmSequenceCurve v = SequenceCurves[i];
                int j = i - 1;
                while (j >= 0 && SequenceCurves[j].PlayTime > v.PlayTime) { SequenceCurves[j + 1] = SequenceCurves[j]; j--; }
                SequenceCurves[j + 1] = v;
            }
        }

        // --------------------------------------------------------- chord track

        void ReadChordTrack(BinaryReader r, Stream s, long end)
        {
            DmRiff.Walk(r, s, end, "DMSG/cord", UnclaimedChunks,
                delegate (string id, string listType, long ds, long de)
                {
                    if (id == "crdh") { r.ReadUInt32(); return true; }   // root/scale of the track
                    if (id == "crdb") { ReadChordBody(r, de); return true; }
                    return false;
                });
        }

        void ReadChordBody(BinaryReader r, long end)
        {
            int chordSize = DmRiff.ReadArrayHeader(r, end, 20, 512);
            if (chordSize <= 0) { UnclaimedChunks.Add("DMSG/cord/crdb — implausible chord size"); return; }

            long chordStart = r.BaseStream.Position;
            DmChordEvent c = new DmChordEvent();
            c.Name = ReadFixedWide(r, 16);
            c.Time = r.ReadInt32();
            c.Measure = r.ReadUInt16();
            c.Beat = r.ReadByte();
            r.ReadByte();                                   // bFlags
            r.BaseStream.Position = chordStart + chordSize;

            if (r.BaseStream.Position + 8 > end) { Chords.Add(c); return; }
            uint count = r.ReadUInt32();
            int subSize = DmRiff.ReadArrayHeader(r, end, 16, 512);
            if (subSize <= 0) { Chords.Add(c); return; }

            for (uint i = 0; i < count && r.BaseStream.Position + subSize <= end; i++)
            {
                long subStart = r.BaseStream.Position;
                DmSubChord sc = new DmSubChord();
                sc.ChordPattern = r.ReadUInt32();
                sc.ScalePattern = r.ReadUInt32();
                sc.InversionPoints = r.ReadUInt32();
                sc.Levels = r.ReadUInt32();
                sc.ChordRoot = r.ReadByte();
                sc.ScaleRoot = r.ReadByte();
                c.SubChords.Add(sc);
                r.BaseStream.Position = subStart + subSize;
            }
            Chords.Add(c);
        }

        // --------------------------------------------------------- style track

        void ReadStyleTrack(BinaryReader r, Stream s, long end)
        {
            int pendingTime = 0;
            List<DmStyleRef> into = StyleReferences;
            List<string> unclaimedForStyle = UnclaimedChunks;
            DmRiff.Walk(r, s, end, "DMSG/sttr", UnclaimedChunks,
                delegate (string id, string listType, long ds, long de)
                {
                    if (id == "stmp") { pendingTime = r.ReadInt32(); return true; }
                    if (id == "strf" || listType == "strf")
                    {
                        // Style reference chunk: a timestamp followed by an embedded
                        // reference list, rather than the bare DMRF this reader expected.
                        long strfEnd = de;
                        if (r.BaseStream.Position + 4 <= strfEnd) pendingTime = r.ReadInt32();
                        // The strf payload begins with a fixed header this reader doesn't
                        // model, so scanning it turns those bytes into bogus chunk records.
                        // Only the embedded reference is wanted; residue isn't reported.
                        DmRiff.Walk(r, s, strfEnd, "DMSG/sttr/strf", null,
                            delegate (string i3, string l3, long d3, long e3)
                            {
                                if (l3 != "DMRF") return false;
                                DmStyleRef inner = DmBandReader.ReadReference(r, s, e3, unclaimedForStyle);
                                inner.Time = pendingTime;
                                into.Add(inner);
                                return true;
                            });
                        return true;
                    }
                    if (listType == "DMRF")
                    {
                        DmStyleRef sr = ReadReference(r, s, de);
                        sr.Time = pendingTime;
                        StyleReferences.Add(sr);
                        return true;
                    }
                    return false;
                });
        }

        DmStyleRef ReadReference(BinaryReader r, Stream s, long end)
        {
            return DmBandReader.ReadReference(r, s, end, UnclaimedChunks);
        }

        void ReadBandTrack(BinaryReader r, Stream s, long end)
        {
            DmBandReader.ReadBandTrack(r, s, end, Bands, UnclaimedChunks);
        }

        // --------------------------------------------------------- helpers

        string ReadUnfoName(BinaryReader r, Stream s, long end)
        {
            string name = null;
            DmRiff.Walk(r, s, end, "UNFO", null,
                delegate (string id, string listType, long ds, long de)
                {
                    if (id == "UNAM") { name = DmRiff.ReadWideString(r, de); return true; }
                    return true;
                });
            return name;
        }

        static string ReadFixedWide(BinaryReader r, int chars)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < chars; i++)
            {
                ushort c = r.ReadUInt16();
                if (c != 0) sb.Append((char)c);
            }
            return sb.ToString();
        }

        /// <summary>Groove level in effect at a given music time.</summary>
        public int GrooveLevelAt(int musicTime)
        {
            int level = 50;
            for (int i = 0; i < Commands.Count; i++)
            {
                if (Commands[i].Time > musicTime) break;
                if (Commands[i].Command == DmCommandType.Groove) level = Commands[i].GrooveLevel;
            }
            return level;
        }

        public DmChordEvent ChordAt(int musicTime)
        {
            DmChordEvent current = null;
            for (int i = 0; i < Chords.Count; i++)
            {
                if (Chords[i].Time > musicTime) break;
                current = Chords[i];
            }
            return current != null ? current : (Chords.Count > 0 ? Chords[0] : null);
        }

        public double TempoAt(int musicTime)
        {
            double bpm = 120.0;
            for (int i = 0; i < Tempos.Count; i++)
            {
                if (Tempos[i].Time > musicTime) break;
                bpm = Tempos[i].Bpm;
            }
            return bpm;
        }

        public DmTimeSig TimeSigAt(int musicTime)
        {
            DmTimeSig ts = DmTimeSig.Default;
            for (int i = 0; i < TimeSignatures.Count; i++)
            {
                if (TimeSignatures[i].Time > musicTime) break;
                ts = TimeSignatures[i].TimeSig;
            }
            return ts;
        }

        /// <summary>Distinct DLS collections this segment's bands refer to.</summary>
        public List<string> ReferencedDlsFiles()
        {
            return DmBandReader.ReferencedDlsFiles(Bands);
        }

        public string Describe()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Segment: " + (Name ?? "<unnamed>"));
            sb.AppendLine("  tracks=" + TrackCount + " length=" + Length +
                          " repeats=" + (RepeatsForever ? "infinite" : Repeats.ToString()));
            sb.AppendLine("  playStart=" + PlayStart + " loopStart=" + LoopStart +
                          " loopEnd=" + LoopEnd + "  (resolved loop " + ResolvedLoopStart +
                          " to " + ResolvedLoopEnd + ")");
            sb.AppendLine("  tempo events=" + Tempos.Count + (Tempos.Count > 0 ? " (first " + Tempos[0].Bpm.ToString("F1") + " bpm)" : ""));
            sb.AppendLine("  time signatures=" + TimeSignatures.Count + (TimeSignatures.Count > 0 ? " (first " + TimeSignatures[0].TimeSig + ")" : ""));
            sb.AppendLine("  chords=" + Chords.Count);

            sb.AppendLine("  style references=" + StyleReferences.Count);
            for (int i = 0; i < StyleReferences.Count; i++)
                sb.AppendLine("    " + StyleReferences[i]);

            if (SequenceTrackCount > 0 || HasSequence)
            {
                sb.AppendLine("  sequence tracks=" + SequenceTrackCount + ": " +
                              SequenceEvents.Count + " events, " + SequenceCurves.Count +
                              " curves, ending at tick " + SequenceEndTick);
                List<int> channels = SequencePChannels();
                sb.Append("    pchannels:");
                for (int i = 0; i < channels.Count; i++) sb.Append(" " + channels[i]);
                sb.AppendLine();
                for (int i = 0; i < SequenceEvents.Count && i < 24; i++)
                    sb.AppendLine("    " + SequenceEvents[i]);
                if (SequenceEvents.Count > 24) sb.AppendLine("    ...");
                for (int i = 0; i < SequenceCurves.Count && i < 8; i++)
                    sb.AppendLine("    " + SequenceCurves[i]);
                if (SequenceCurves.Count > 8) sb.AppendLine("    ...");
            }

            sb.AppendLine("  commands=" + Commands.Count);
            for (int i = 0; i < Commands.Count && i < 24; i++)
                sb.AppendLine("    " + Commands[i]);
            if (Commands.Count > 24) sb.AppendLine("    ...");

            sb.AppendLine("  bands=" + Bands.Count);
            for (int i = 0; i < Bands.Count; i++)
            {
                sb.AppendLine("    band at " + Bands[i].Time + " — " + Bands[i].Instruments.Count + " instruments");
                for (int j = 0; j < Bands[i].Instruments.Count && j < 16; j++)
                    sb.AppendLine("      " + Bands[i].Instruments[j]);
            }

            sb.AppendLine("  unclaimed chunks=" + UnclaimedChunks.Count);
            for (int i = 0; i < UnclaimedChunks.Count && i < 40; i++)
                sb.AppendLine("    " + UnclaimedChunks[i]);
            if (UnclaimedChunks.Count > 40) sb.AppendLine("    ...");
            return sb.ToString();
        }
    }

    /// <summary>
    /// Band and reference readers, shared because bands appear in both segments
    /// (as a band track) and styles (embedded). The DLS a band refers to is carried
    /// by a DMRF reference on each instrument — that is where instrument assignment
    /// actually comes from.
    /// </summary>
    public static class DmBandReader
    {
        /// <summary>Distinct, non-empty DLS references across a set of bands, in order.</summary>
        public static List<string> ReferencedDlsFiles(List<DmBandEvent> bands)
        {
            List<string> names = new List<string>();
            for (int i = 0; i < bands.Count; i++)
            {
                for (int j = 0; j < bands[i].Instruments.Count; j++)
                {
                    string reference = bands[i].Instruments[j].DlsReference;
                    if (string.IsNullOrEmpty(reference)) continue;
                    if (!names.Contains(reference)) names.Add(reference);
                }
            }
            return names;
        }

        public static DmStyleRef ReadReference(BinaryReader r, Stream s, long end, List<string> unclaimed)
        {
            DmStyleRef sr = new DmStyleRef();
            DmRiff.Walk(r, s, end, "DMRF", unclaimed,
                delegate (string id, string listType, long ds, long de)
                {
                    switch (id)
                    {
                        case "refh": return true;
                        case "guid": sr.Id = DmRiff.ReadGuid(r); return true;
                        case "name": sr.Name = DmRiff.ReadWideString(r, de); return true;
                        case "file": sr.FileName = DmRiff.ReadWideString(r, de); return true;
                        case "catg": case "vers": case "date": return true;
                    }
                    return listType == "UNFO";
                });
            return sr;
        }

        // ---------------------------------------------------------- band track

        public static void ReadBandTrack(BinaryReader r, Stream s, long end, List<DmBandEvent> into, List<string> unclaimed)
        {
            DmRiff.Walk(r, s, end, "DMSG/DMBT", unclaimed,
                delegate (string id, string listType, long ds, long de)
                {
                    if (id == "bdth") return true;
                    if (listType == "lbdl")
                    {
                        DmRiff.Walk(r, s, de, "DMSG/DMBT/lbdl", unclaimed,
                            delegate (string i2, string l2, long d2, long e2)
                            {
                                if (l2 != "lbnd") return false;
                                ReadBandItem(r, s, e2, into, unclaimed);
                                return true;
                            });
                        return true;
                    }
                    return listType == "UNFO";
                });
        }

        public static void ReadBandItem(BinaryReader r, Stream s, long end, List<DmBandEvent> into, List<string> unclaimed)
        {
            DmBandEvent band = new DmBandEvent();
            DmRiff.Walk(r, s, end, "DMSG/DMBT/lbnd", unclaimed,
                delegate (string id, string listType, long ds, long de)
                {
                    if (id == "bdih") { band.Time = r.ReadInt32(); return true; }
                    if (id == "bd2h")
                    {
                        // DMUS_IO_BAND_ITEM_HEADER2: a logical time and a physical one.
                        // The physical time is when the band actually takes effect.
                        int logical = r.ReadInt32();
                        band.Time = r.BaseStream.Position + 4 <= de ? r.ReadInt32() : logical;
                        return true;
                    }
                    if (listType == "DMBD") { ReadBand(r, s, de, band, unclaimed); return true; }
                    return false;
                });
            into.Add(band);
        }

        public static void ReadBand(BinaryReader r, Stream s, long end, DmBandEvent band, List<string> unclaimed)
        {
            long bandStart = s.Position;

            DmRiff.Walk(r, s, end, "DMBD", unclaimed,
                delegate (string id, string listType, long ds, long de)
                {
                    if (id == "guid" || id == "vers") return true;
                    if (listType == "UNFO") return true;
                    if (listType == "lbil")
                    {
                        DmRiff.Walk(r, s, de, "DMBD/lbil", unclaimed,
                            delegate (string i2, string l2, long d2, long e2)
                            {
                                if (l2 != "lbin") return false;
                                ReadInstrument(r, s, e2, band, unclaimed);
                                return true;
                            });
                        return true;
                    }
                    return false;
                });

            // If the expected nesting didn't yield anything, sweep the band for instrument
            // chunks at any depth. Producer versions vary in how they wrap these, and an
            // empty band is the difference between correct instruments and none at all.
            if (band.Instruments.Count == 0)
            {
                s.Position = bandStart;
                int found = FindInstrumentsDeep(r, s, bandStart, end, band, 0);
                if (found > 0)
                    unclaimed.Add("DMBD - instrument list nesting was unexpected; recovered " +
                                  found + " instrument(s) by deep scan");
            }
        }

        /// <summary>Recursively hunts for 'bins' chunks regardless of intermediate list nesting.</summary>
        static int FindInstrumentsDeep(BinaryReader r, Stream s, long start, long end,
                                       DmBandEvent band, int depth)
        {
            if (depth > 6) return 0;
            int found = 0;
            s.Position = start;

            DmRiff.Walk(r, s, end, "deep", null,
                delegate (string id, string listType, long ds, long de)
                {
                    if (id == "bins")
                    {
                        DmBandInstrument ins = ReadInstrumentHeader(r, de);
                        if (ins != null) { band.Instruments.Add(ins); found++; }
                        return true;
                    }
                    if (listType != null) found += FindInstrumentsDeep(r, s, ds, de, band, depth + 1);
                    return true;
                });
            return found;
        }

        /// <summary>Reads DMUS_IO_INSTRUMENT, tolerating the shorter early-version layout.</summary>
        public static DmBandInstrument ReadInstrumentHeader(BinaryReader r, long end)
        {
            if (r.BaseStream.Position + 12 > end) return null;

            DmBandInstrument ins = new DmBandInstrument();
            ins.Patch = r.ReadUInt32();
            r.ReadUInt32();                                   // dwAssignPatch
            for (int i = 0; i < 4 && r.BaseStream.Position + 4 <= end; i++) r.ReadUInt32();
            if (r.BaseStream.Position + 4 <= end) ins.PChannel = (int)r.ReadUInt32();
            if (r.BaseStream.Position + 4 <= end) ins.Flags = (DmInstrumentFlags)r.ReadUInt32();
            if (r.BaseStream.Position + 1 <= end) ins.Pan = r.ReadByte();
            if (r.BaseStream.Position + 1 <= end) ins.Volume = r.ReadByte();
            if (r.BaseStream.Position + 2 <= end) ins.Transpose = r.ReadInt16();
            if (r.BaseStream.Position + 6 <= end)
            {
                r.ReadUInt32();                               // dwChannelPriority
                ins.PitchBendRange = r.ReadInt16();
            }
            return ins;
        }

        public static void ReadInstrument(BinaryReader r, Stream s, long end, DmBandEvent band, List<string> unclaimed)
        {
            DmBandInstrument ins = new DmBandInstrument();
            DmRiff.Walk(r, s, end, "DMBD/lbil/lbin", unclaimed,
                delegate (string id, string listType, long ds, long de)
                {
                    if (id == "bins")
                    {
                        DmBandInstrument parsed = ReadInstrumentHeader(r, de);
                        if (parsed != null)
                        {
                            ins.Patch = parsed.Patch;
                            ins.Flags = parsed.Flags;
                            ins.PChannel = parsed.PChannel;
                            ins.Pan = parsed.Pan;
                            ins.Volume = parsed.Volume;
                            ins.Transpose = parsed.Transpose;
                            ins.PitchBendRange = parsed.PitchBendRange;
                        }
                        return true;
                    }
                    if (listType == "DMRF")
                    {
                        DmStyleRef reference = ReadReference(r, s, de, unclaimed);
                        ins.DlsReference = reference.FileName ?? reference.Name;
                        ins.DlsReferenceName = reference.Name;
                        return true;
                    }
                    if (listType == "UNFO")
                    {
                        DmRiff.Walk(r, s, de, "lbin/UNFO", null,
                            delegate (string i3, string l3, long d3, long e3)
                            {
                                if (i3 == "UNAM") ins.Name = DmRiff.ReadWideString(r, e3);
                                return true;
                            });
                        return true;
                    }
                    return false;
                });
            band.Instruments.Add(ins);
        }

    }
}
