// DmStyle.cs — DirectMusic style (.sty) reader, RIFF form 'DMST'.
//
// A style holds parts (lists of chord-relative notes) and patterns. Each pattern
// carries the groove range that selects it: bGrooveBottom..bGrooveTop, plus an
// embellishment type (fill, intro, break, end). Setting a groove level of 60 means
// "choose among patterns whose range covers 60".
//
// Part of DirectMusicLite.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace DirectMusicLite
{
    /// <summary>DMUS_PLAYMODE flags — how a note's music value is resolved to a pitch.</summary>
    [Flags]
    public enum DmPlayMode
    {
        Fixed = 0,
        KeyRoot = 1,
        ChordRoot = 2,
        ScaleIntervals = 4,
        ChordIntervals = 8,
        None = 16,

        PedalPoint = KeyRoot | ScaleIntervals,        // 5
        Melodic = ChordRoot | ScaleIntervals,         // 6
        NormalChord = ChordRoot | ChordIntervals,     // 10
        AlwaysPlay = Melodic | NormalChord            // 14
    }

    /// <summary>DMUS_NOTEF_* — the note's own flags, distinct from its play mode.</summary>
    [Flags]
    public enum DmNoteFlags
    {
        None = 0,
        NoteOn = 0x01,
        NoInvalidate = 0x02,
        NoInvalidateInScale = 0x04,
        NoInvalidateInChord = 0x08,
        Regenerate = 0x10
    }

    public sealed class DmStyleNote
    {
        public int GridStart;          // grid index within the part
        public uint VariationMask;     // which of the 32 variations this note belongs to
        public int Duration;           // MUSIC_TIME
        public int TimeOffset;         // MUSIC_TIME offset from the grid
        public int MusicValue;         // chord-relative, unless the play mode is Fixed
        public int Velocity = 100;
        public int TimeRange, DurationRange, VelocityRange;
        public int InversionId;
        public int PlayModeFlags = -1; // -1 or 0 means inherit from the part
        /// <summary>DMUS_NOTEF_* from bNoteFlags. Separate from bPlayModeFlags.</summary>
        public DmNoteFlags NoteFlags = DmNoteFlags.NoteOn;
    }

    /// <summary>DMUS_CURVET_* — what a style curve controls.</summary>
    public enum DmCurveType
    {
        PitchBend = 0x03,
        ControlChange = 0x04,
        MonoAftertouch = 0x05,
        PolyAftertouch = 0x06,
        Rpn = 0x07,
        Nrpn = 0x08
    }

    /// <summary>DMUS_CURVES_* — the shape a curve sweeps through.</summary>
    public enum DmCurveShape
    {
        Linear = 0, Instant = 1, Exponential = 2, Logarithmic = 3, Sine = 4
    }

    /// <summary>
    /// A controller sweep authored in a style part: volume swells, pan moves, pitch
    /// bends. Panning that alternates between speakers is usually one of these rather
    /// than anything in the band.
    /// </summary>
    public sealed class DmStyleCurve
    {
        public int GridStart;
        public uint VariationMask;
        public int Duration;         // MUSIC_TIME
        public int ResetDuration;
        public int TimeOffset;
        public int StartValue;
        public int EndValue;
        public int ResetValue;
        public DmCurveType Type = DmCurveType.ControlChange;
        public DmCurveShape Shape = DmCurveShape.Linear;
        public int ControllerNumber = 10;   // meaningful when Type is ControlChange
        public int Flags;

        /// <summary>
        /// DMUS_CURVE_RESET. When set, the controller returns to ResetValue after the
        /// curve, so a swell doesn't leave the channel parked at its end value. Gated on
        /// the flag: applying a reset that wasn't asked for would slam controllers to 0.
        /// </summary>
        public bool ResetsAfterwards { get { return (Flags & 0x1) != 0; } }

        /// <summary>Value at a fraction 0..1 through the curve.</summary>
        public float ValueAt(float t)
        {
            if (t <= 0f) return StartValue;
            if (t >= 1f) return EndValue;

            float shaped;
            switch (Shape)
            {
                case DmCurveShape.Instant: shaped = 1f; break;
                case DmCurveShape.Exponential: shaped = t * t; break;
                case DmCurveShape.Logarithmic: shaped = (float)Math.Sqrt(t); break;
                case DmCurveShape.Sine: shaped = (float)(0.5 - 0.5 * Math.Cos(Math.PI * t)); break;
                default: shaped = t; break;
            }
            return StartValue + (EndValue - StartValue) * shaped;
        }

        public override string ToString()
        {
            string what = Type == DmCurveType.ControlChange ? "CC" + ControllerNumber : Type.ToString();
            return string.Format("{0} {1} {2}->{3} over {4} ticks at grid {5}{6}",
                what, Shape, StartValue, EndValue, Duration, GridStart,
                ResetsAfterwards ? " then reset to " + ResetValue : "");
        }
    }

    public sealed class DmStylePart
    {
        public Guid PartId;
        public DmTimeSig TimeSig = DmTimeSig.Default;
        public int Measures = 1;
        public int PlayModeFlags = (int)DmPlayMode.Melodic;
        public int InvertUpper = 127, InvertLower;
        public readonly uint[] VariationChoices = new uint[32];
        public readonly List<DmStyleNote> Notes = new List<DmStyleNote>();
        public readonly List<DmStyleCurve> Curves = new List<DmStyleCurve>();
        public string Name;

        /// <summary>
        /// Variation slots that are actually used
        /// Ignores DMUS_VARIATIONF as it's set on all variations
        /// </summary>
        public List<int> UsableVariations()
        {
            List<int> list = new List<int>();
            const uint DMUS_VARIATIONF_MODES = 0xE0000000; // separates modes (which are always set) from
                                                           // legitimate variant flags
            for (int v = 0; v < 32; v++)
            {
                if ((VariationChoices[v] & ~DMUS_VARIATIONF_MODES) != 0) list.Add(v);
            }
                
            return list;
        }
    }

    /// <summary>
    /// DMUS_VARIATIONT_* — how a part reference steps through its enabled variations.
    /// Producer exposes this per part reference.
    /// </summary>
    public enum DmVariationOrder
    {
        /// <summary>In order, cycling through the enabled variations.</summary>
        Sequential = 0,
        /// <summary>A fresh random pick each time, repeats allowed.</summary>
        Random = 1,
        /// <summary>Start somewhere at random, then continue in order.</summary>
        RandomStart = 2,
        /// <summary>Random, but never the same variation twice running.</summary>
        NoRepeat = 3,
        /// <summary>A shuffled run through every variation before any repeats.</summary>
        RandomRow = 4
    }

    public sealed class DmPartRef
    {
        public Guid PartId;
        public int LogicalPartId;      // maps to a PChannel
        public int VariationLockId;    // parts sharing a non-zero id pick the same variation
        public int SubChordLevel;
        public int Priority;
        public int RandomVariation;

        /// <summary>The variation ordering this part reference asks for.</summary>
        public DmVariationOrder VariationOrder
        {
            get
            {
                return RandomVariation >= 0 && RandomVariation <= 4
                    ? (DmVariationOrder)RandomVariation : DmVariationOrder.Sequential;
            }
        }
        public int PChannel = -1;
    }

    public sealed class DmPattern
    {
        public string Name;
        public DmTimeSig TimeSig = DmTimeSig.Default;
        public int GrooveBottom = 1;
        public int GrooveTop = 100;
        public DmEmbellishment Embellishment = DmEmbellishment.Normal;
        public int Measures = 1;
        public int DestGrooveBottom = 1, DestGrooveTop = 100;
        public readonly List<DmPartRef> PartRefs = new List<DmPartRef>();
        /// <summary>True if the pattern carries motif settings, which aren't rendered.</summary>
        public bool HasMotifSettings;

        public bool MatchesGroove(int level)
        {
            return level >= GrooveBottom && level <= GrooveTop;
        }

        public override string ToString()
        {
            return string.Format("{0}: groove {1}-{2}, {3}, {4} measure(s), {5} part(s)",
                Name ?? "<unnamed>", GrooveBottom, GrooveTop, Embellishment, Measures, PartRefs.Count);
        }
    }

    public sealed class DmStyle
    {
        public string Name;
        public double Tempo = 120.0;
        public DmTimeSig TimeSig = DmTimeSig.Default;

        public readonly List<DmStylePart> Parts = new List<DmStylePart>();
        public readonly List<DmPattern> Patterns = new List<DmPattern>();
        public readonly Dictionary<Guid, DmStylePart> PartsById = new Dictionary<Guid, DmStylePart>();
        public readonly List<string> UnclaimedChunks = new List<string>();

        /// <summary>
        /// Bands embedded in the style. These carry the PChannel -> instrument
        /// assignments, including the DLS collection each instrument comes from.
        /// </summary>
        public readonly List<DmBandEvent> Bands = new List<DmBandEvent>();

        public static DmStyle Load(string path) { return Load(File.ReadAllBytes(path)); }

        public static DmStyle Load(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 12)
                throw new InvalidDataException("Style: file is empty or truncated.");

            MemoryStream ms = new MemoryStream(bytes, false);
            BinaryReader r = new BinaryReader(ms);

            if (DmRiff.FourCC(r) != "RIFF")
                throw new InvalidDataException("Style: not a RIFF file.");
            uint size = r.ReadUInt32();
            string form = DmRiff.FourCC(r);
            if (form != "DMST")
                throw new InvalidDataException(
                    "Style: RIFF form is '" + form + "', expected 'DMST'. " +
                    (form == "DMSG" ? "This looks like a segment (.sgt), not a style (.sty)." : ""));

            DmStyle style = new DmStyle();
            long end = Math.Min(ms.Length, 8L + size);
            DmRiff.Walk(r, ms, end, "DMST", style.UnclaimedChunks,
                delegate (string id, string listType, long ds, long de) { return style.OnTop(r, ms, id, listType, ds, de); });

            for (int i = 0; i < style.Parts.Count; i++)
                style.PartsById[style.Parts[i].PartId] = style.Parts[i];

            return style;
        }

        bool OnTop(BinaryReader r, Stream s, string id, string listType, long ds, long de)
        {
            if (id == "styh")
            {
                TimeSig = DmTimeSig.Read(r);
                // The double is 8-byte aligned, so there are 4 pad bytes after the time signature.
                long afterTs = r.BaseStream.Position;
                if (afterTs + 12 <= de) { r.ReadUInt32(); Tempo = r.ReadDouble(); }
                else if (afterTs + 8 <= de) { Tempo = r.ReadDouble(); }
                if (Tempo < 1.0 || Tempo > 1000.0) Tempo = 120.0;
                return true;
            }
            if (id == "guid" || id == "vers" || id == "date") return true;
            if (listType == "UNFO") { Name = ReadUnfoName(r, s, de); return true; }
            if (listType == "part") { ReadPart(r, s, de); return true; }
            if (listType == "pttn") { ReadPattern(r, s, de); return true; }
            if (listType == "lbdl")
            {
                DmRiff.Walk(r, s, de, "DMST/lbdl", UnclaimedChunks,
                    delegate (string i2, string l2, long d2, long e2)
                    {
                        if (l2 != "lbnd") return false;
                        DmBandReader.ReadBandItem(r, s, e2, Bands, UnclaimedChunks);
                        return true;
                    });
                return true;
            }
            if (listType == "DMBD")
            {
                DmBandEvent band = new DmBandEvent();
                DmBandReader.ReadBand(r, s, de, band, UnclaimedChunks);
                Bands.Add(band);
                return true;
            }
            if (listType == "DMBT")
            {
                DmBandReader.ReadBandTrack(r, s, de, Bands, UnclaimedChunks);
                return true;
            }
            return false;
        }

        // ---------------------------------------------------------------- parts

        void ReadPart(BinaryReader r, Stream s, long end)
        {
            DmStylePart part = new DmStylePart();
            DmRiff.Walk(r, s, end, "DMST/part", UnclaimedChunks,
                delegate (string id, string listType, long ds, long de)
                {
                    switch (id)
                    {
                        case "prth":
                            part.TimeSig = DmTimeSig.Read(r);
                            for (int i = 0; i < 32 && r.BaseStream.Position + 4 <= de; i++)
                                part.VariationChoices[i] = r.ReadUInt32();
                            if (r.BaseStream.Position + 16 <= de) part.PartId = DmRiff.ReadGuid(r);
                            if (r.BaseStream.Position + 2 <= de) part.Measures = r.ReadUInt16();
                            if (r.BaseStream.Position + 1 <= de) part.PlayModeFlags = r.ReadByte();
                            if (r.BaseStream.Position + 1 <= de) part.InvertUpper = r.ReadByte();
                            if (r.BaseStream.Position + 1 <= de) part.InvertLower = r.ReadByte();
                            if (part.Measures < 1) part.Measures = 1;
                            return true;

                        case "note": ReadNotes(r, de, part); return true;
                        case "crve": ReadCurves(r, de, part); return true;
                        case "mrkr": case "rsln": case "anpn": return true;
                    }
                    if (listType == "UNFO") { part.Name = ReadUnfoName(r, s, de); return true; }
                    return false;
                });

            Parts.Add(part);
        }

        void ReadNotes(BinaryReader r, long end, DmStylePart part)
        {
            int stride = DmRiff.ReadArrayHeader(r, end, 16, 512);
            if (stride <= 0)
            {
                UnclaimedChunks.Add("DMST/part/note — implausible record size " + (-stride) + ", skipped");
                return;
            }

            while (r.BaseStream.Position + stride <= end)
            {
                long recStart = r.BaseStream.Position;
                DmStyleNote n = new DmStyleNote();
                n.GridStart = r.ReadInt32();
                n.VariationMask = r.ReadUInt32();
                n.Duration = r.ReadInt32();
                n.TimeOffset = r.ReadInt16();
                n.MusicValue = r.ReadUInt16();
                n.Velocity = r.ReadByte();
                n.TimeRange = r.ReadByte();
                n.DurationRange = r.ReadByte();
                n.VelocityRange = r.ReadByte();
                n.InversionId = r.ReadByte();
                if (recStart + stride > r.BaseStream.Position) n.PlayModeFlags = r.ReadByte();
                if (recStart + stride > r.BaseStream.Position) n.NoteFlags = (DmNoteFlags)r.ReadByte();
                part.Notes.Add(n);
                r.BaseStream.Position = recStart + stride;
            }
        }

        void ReadCurves(BinaryReader r, long end, DmStylePart part)
        {
            int stride = DmRiff.ReadArrayHeader(r, end, 20, 512);
            if (stride <= 0)
            {
                UnclaimedChunks.Add("DMST/part/crve — implausible record size " + (-stride) + ", skipped");
                return;
            }

            while (r.BaseStream.Position + stride <= end)
            {
                long recStart = r.BaseStream.Position;
                DmStyleCurve c = new DmStyleCurve();
                c.GridStart = r.ReadInt32();
                c.VariationMask = r.ReadUInt32();
                c.Duration = r.ReadInt32();
                c.ResetDuration = r.ReadInt32();
                c.TimeOffset = r.ReadInt16();
                c.StartValue = r.ReadInt16();
                c.EndValue = r.ReadInt16();
                c.ResetValue = r.ReadInt16();
                if (recStart + stride > r.BaseStream.Position) c.Type = (DmCurveType)r.ReadByte();
                if (recStart + stride > r.BaseStream.Position) c.Shape = (DmCurveShape)r.ReadByte();
                if (recStart + stride > r.BaseStream.Position) c.ControllerNumber = r.ReadByte();
                if (recStart + stride > r.BaseStream.Position) c.Flags = r.ReadByte();

                part.Curves.Add(c);
                r.BaseStream.Position = recStart + stride;
            }
        }

        // ------------------------------------------------------------- patterns

        void ReadPattern(BinaryReader r, Stream s, long end)
        {
            DmPattern pattern = new DmPattern();
            DmRiff.Walk(r, s, end, "DMST/pttn", UnclaimedChunks,
                delegate (string id, string listType, long ds, long de)
                {
                    if (id == "ptnh")
                    {
                        pattern.TimeSig = DmTimeSig.Read(r);
                        pattern.GrooveBottom = r.ReadByte();
                        pattern.GrooveTop = r.ReadByte();
                        pattern.Embellishment = (DmEmbellishment)r.ReadUInt16();
                        pattern.Measures = r.ReadUInt16();
                        if (r.BaseStream.Position + 2 <= de)
                        {
                            pattern.DestGrooveBottom = r.ReadByte();
                            pattern.DestGrooveTop = r.ReadByte();
                        }
                        if (pattern.Measures < 1) pattern.Measures = 1;
                        if (pattern.GrooveBottom < 1) pattern.GrooveBottom = 1;
                        if (pattern.GrooveTop < pattern.GrooveBottom) pattern.GrooveTop = 100;
                        return true;
                    }
                    if (id == "rhtm") return true;      // rhythm signature for pattern matching
                    if (id == "mtfs")
                    {
                        // Motif settings. Motifs are secondary segments triggered by name
                        // rather than part of groove playback, so the pattern still plays
                        // correctly without them; noted rather than reported as unknown.
                        pattern.HasMotifSettings = true;
                        return true;
                    }
                    if (listType == "UNFO") { pattern.Name = ReadUnfoName(r, s, de); return true; }
                    if (listType == "pref") { ReadPartRef(r, s, de, pattern); return true; }
                    return false;
                });

            Patterns.Add(pattern);
        }

        void ReadPartRef(BinaryReader r, Stream s, long end, DmPattern pattern)
        {
            DmPartRef pref = new DmPartRef();
            bool got = false;

            DmRiff.Walk(r, s, end, "DMST/pttn/pref", UnclaimedChunks,
                delegate (string id, string listType, long ds, long de)
                {
                    // Producer versions differ on this chunk id, so accept the known spellings.
                    if (id == "prfc" || id == "prfh" || id == "prf ")
                    {
                        pref.PartId = DmRiff.ReadGuid(r);
                        pref.LogicalPartId = r.ReadUInt16();
                        pref.VariationLockId = r.ReadByte();
                        pref.SubChordLevel = r.ReadByte();
                        pref.Priority = r.ReadByte();
                        pref.RandomVariation = r.ReadByte();
                        if (r.BaseStream.Position + 6 <= de)
                        {
                            r.ReadUInt16();                       // padding
                            pref.PChannel = (int)r.ReadUInt32();
                        }
                        got = true;
                        return true;
                    }
                    if (listType == "UNFO") return true;
                    return false;
                });

            if (got) pattern.PartRefs.Add(pref);
        }

        // --------------------------------------------------------------- query

        /// <summary>
        /// Patterns eligible at a groove level, filtered by embellishment.
        /// This is the selection rule behind SetGrooveLevel.
        /// </summary>
        public List<DmPattern> PatternsFor(int grooveLevel, DmEmbellishment embellishment)
        {
            List<DmPattern> hits = new List<DmPattern>();
            for (int i = 0; i < Patterns.Count; i++)
            {
                DmPattern p = Patterns[i];
                if (!p.MatchesGroove(grooveLevel)) continue;

                if (embellishment == DmEmbellishment.Normal)
                {
                    // Plain groove playback: ordinary patterns only, never a fill or an ending.
                    if (p.Embellishment == DmEmbellishment.Normal || p.Embellishment == DmEmbellishment.Groove)
                        hits.Add(p);
                }
                else if ((p.Embellishment & embellishment) != 0)
                {
                    hits.Add(p);
                }
            }
            return hits;
        }

        /// <summary>
        /// Patterns suitable as a transition: playable at the current groove level, and
        /// whose destination groove range covers where we're heading. That destination
        /// range (bDestGrooveBottom/Top) exists in the format for exactly this purpose.
        /// </summary>
        public List<DmPattern> PatternsForTransition(int fromGroove, int toGroove, DmEmbellishment embellishment)
        {
            List<DmPattern> hits = new List<DmPattern>();
            for (int i = 0; i < Patterns.Count; i++)
            {
                DmPattern p = Patterns[i];
                if (!p.MatchesGroove(fromGroove)) continue;
                if (toGroove < p.DestGrooveBottom || toGroove > p.DestGrooveTop) continue;

                if (embellishment == DmEmbellishment.Normal)
                {
                    if (p.Embellishment == DmEmbellishment.Normal || p.Embellishment == DmEmbellishment.Groove)
                        hits.Add(p);
                }
                else if ((p.Embellishment & embellishment) != 0)
                {
                    hits.Add(p);
                }
            }
            return hits;
        }

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

        /// <summary>Distinct DLS collections this style's bands refer to.</summary>
        public List<string> ReferencedDlsFiles()
        {
            return DmBandReader.ReferencedDlsFiles(Bands);
        }

        public string Describe()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Style: " + (Name ?? "<unnamed>"));
            sb.AppendLine("  tempo=" + Tempo.ToString("F1") + " bpm, " + TimeSig);
            sb.AppendLine("  parts=" + Parts.Count + "  patterns=" + Patterns.Count);
            sb.AppendLine("  embedded bands=" + Bands.Count);
            for (int i = 0; i < Bands.Count; i++)
                for (int j = 0; j < Bands[i].Instruments.Count && j < 16; j++)
                    sb.AppendLine("    " + Bands[i].Instruments[j]);

            for (int i = 0; i < Patterns.Count && i < 32; i++)
            {
                DmPattern p = Patterns[i];
                sb.AppendLine("    " + p);
                for (int j = 0; j < p.PartRefs.Count && j < 12; j++)
                {
                    DmPartRef pr = p.PartRefs[j];
                    DmStylePart part;
                    PartsById.TryGetValue(pr.PartId, out part);
                    sb.AppendLine(string.Format("      logical={5} pchannel={6} -> {1} ({2} notes, {7} curves, {3} variations {10}, playmode {4}, invert {8}-{9})",
                        pr.LogicalPartId,
                        part != null ? (part.Name ?? "<unnamed>") : "MISSING",
                        part != null ? part.Notes.Count : 0,
                        part != null ? part.UsableVariations().Count : 0,
                        part != null ? ((DmPlayMode)part.PlayModeFlags).ToString() : "?",
                        pr.LogicalPartId,
                        pr.PChannel >= 0 ? pr.PChannel.ToString() : "ABSENT",
                        part != null ? part.Curves.Count : 0,
                        part != null ? part.InvertLower : 0,
                        part != null ? part.InvertUpper : 127,
                        pr.VariationOrder));
                }
            }
            if (Patterns.Count > 32) sb.AppendLine("    ...");

            Dictionary<int, int> playModeCounts = new Dictionary<int, int>();
            for (int i = 0; i < Parts.Count; i++)
                for (int j = 0; j < Parts[i].Notes.Count; j++)
                {
                    int pm = Parts[i].Notes[j].PlayModeFlags;
                    int count;
                    playModeCounts.TryGetValue(pm, out count);
                    playModeCounts[pm] = count + 1;
                }
            if (playModeCounts.Count > 0)
            {
                sb.AppendLine("  note play mode values (0 means inherit the part's):");
                foreach (KeyValuePair<int, int> kv in playModeCounts)
                    sb.AppendLine(string.Format("    {0,3} ({1,-12}) x{2}",
                        kv.Key, ((DmPlayMode)kv.Key).ToString(), kv.Value));
            }

            int curveTotal = 0;
            for (int i = 0; i < Parts.Count; i++) curveTotal += Parts[i].Curves.Count;
            sb.AppendLine("  controller curves: " + curveTotal);
            for (int i = 0; i < Parts.Count; i++)
                for (int j = 0; j < Parts[i].Curves.Count && j < 6; j++)
                    sb.AppendLine("    " + (Parts[i].Name ?? "part " + i) + ": " + Parts[i].Curves[j]);

            int lo = 101, hi = 0;
            for (int i = 0; i < Patterns.Count; i++)
            {
                if (Patterns[i].GrooveBottom < lo) lo = Patterns[i].GrooveBottom;
                if (Patterns[i].GrooveTop > hi) hi = Patterns[i].GrooveTop;
            }
            if (Patterns.Count > 0)
                sb.AppendLine("  groove levels covered: " + lo + " to " + hi);

            sb.AppendLine("  unclaimed chunks=" + UnclaimedChunks.Count);
            for (int i = 0; i < UnclaimedChunks.Count && i < 40; i++)
                sb.AppendLine("    " + UnclaimedChunks[i]);
            if (UnclaimedChunks.Count > 40) sb.AppendLine("    ...");
            return sb.ToString();
        }
    }
}
