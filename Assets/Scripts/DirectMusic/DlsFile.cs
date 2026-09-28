using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace DirectMusicLite
{
    /// <summary>
    /// A source of instruments. Implemented by both a single DLS collection
    /// (<see cref="DlsFile"/>) and a priority stack of them (<see cref="DlsBankStack"/>),
    /// so the synth doesn't care which it was given.
    /// </summary>
    public interface IDlsBank
    {
        DlsInstrument FindInstrument(int bank, int program, bool drum);
        string Dump();
    }

    /// <summary>A single sample loop taken from a 'wsmp' chunk.</summary>
    public sealed class DlsLoop
    {
        public uint Type;    // 0 = forward, 1 = release (DLS2)
        public uint Start;   // in frames
        public uint Length;  // in frames
    }

    /// <summary>Contents of a 'wsmp' chunk (per-wave or per-region override).</summary>
    public sealed class DlsSampleInfo
    {
        public int UnityNote = 60;
        public float FineTuneCents;
        public float GainDb;
        public DlsLoop Loop;   // first loop only; DLS1 allows at most one

        public DlsSampleInfo Clone()
        {
            return new DlsSampleInfo
            {
                UnityNote = UnityNote,
                FineTuneCents = FineTuneCents,
                GainDb = GainDb,
                Loop = Loop
            };
        }
    }

    /// <summary>Which fields a DLS articulation block actually specified.</summary>
    [Flags]
    public enum DlsArtField
    {
        None = 0,
        Attack = 1,
        Decay = 2,
        Sustain = 4,
        Release = 8,
        Pan = 16,
        Attenuation = 32,
        LfoFrequency = 64,
        LfoDelay = 128,
        LfoToPitch = 256,
        LfoToGain = 512,
        Eg2 = 1024,
        Eg2ToPitch = 2048,
        TimeScaling = 4096,
        VelocityToAttenuation = 8192
    }

    /// <summary>Simplified articulation: the DLS connection blocks we actually use.</summary>
    public sealed class DlsArticulation
    {
        /// <summary>
        /// Fields this block set. A region articulation that specifies only pan must not
        /// wipe out the instrument's envelope, so unspecified fields fall through.
        /// </summary>
        public DlsArtField Specified;

        public float AttackSeconds;
        public float DecaySeconds = 30f;    // effectively "no decay"
        public float ReleaseSeconds = 0.06f;
        public float SustainLevel = 1f;     // 0..1
        public float Pan;                   // -1..1

        /// <summary>
        /// Static level trim from a CONN_DST_ATTENUATION / CONN_DST_GAIN connection,
        /// in dB. Clamped to zero or below: DLS1 and DLS2 disagree on the sign of this
        /// destination, and treating it as attenuation-only means a sign mismatch can
        /// never make an instrument louder than authored.
        /// </summary>
        public float AttenuationDb;

        // ---- LFO (vibrato and tremolo) ----

        /// <summary>LFO rate in Hz. The DLS default is 5 Hz when nothing is authored.</summary>
        public float LfoFrequencyHz = 5f;
        /// <summary>Delay before the LFO starts, in seconds.</summary>
        public float LfoStartDelaySeconds;
        /// <summary>Vibrato depth in cents, always applied.</summary>
        public float LfoToPitchCents;
        /// <summary>Extra vibrato depth in cents, scaled by the mod wheel (CC1).</summary>
        public float LfoToPitchCentsModWheel;
        /// <summary>Tremolo depth in dB, always applied.</summary>
        public float LfoToGainDb;
        /// <summary>Extra tremolo depth in dB, scaled by the mod wheel (CC1).</summary>
        public float LfoToGainDbModWheel;

        // ---- EG2: the pitch envelope ----

        public float Eg2AttackSeconds;
        public float Eg2DecaySeconds = 0.5f;
        /// <summary>
        /// Default 0, not 1. A pitch envelope authored with a decay time and no sustain
        /// is meant to sweep back to pitch; defaulting to full sustain would hold the
        /// offset forever and make the authored decay meaningless.
        /// </summary>
        public float Eg2SustainLevel;
        public float Eg2ReleaseSeconds = 0.05f;
        /// <summary>Depth in cents that EG2 applies to pitch.</summary>
        public float Eg2ToPitchCents;

        public bool HasEg2 { get { return Eg2ToPitchCents != 0f; } }

        /// <summary>
        /// Attenuation in dB at zero velocity, easing to 0 dB at full velocity. Zero means
        /// the bank didn't author one and the synth's default curve applies.
        /// </summary>
        public float VelocityToAttenuationDb;
        public bool HasVelocityToAttenuation { get { return VelocityToAttenuationDb < 0f; } }

        // ---- Envelope time scaling ----
        // Raw 16.16 timecent scales applied against a normalised source. Higher notes
        // decaying faster, harder hits attacking faster: the source is normalised to
        // 0..1 and the scale is added to the base time in timecents.

        public int Eg1DecayKeyScale;
        public int Eg1AttackVelocityScale;
        public int Eg2DecayKeyScale;
        public int Eg2AttackVelocityScale;

        public bool HasTimeScaling
        {
            get
            {
                return Eg1DecayKeyScale != 0 || Eg1AttackVelocityScale != 0
                    || Eg2DecayKeyScale != 0 || Eg2AttackVelocityScale != 0;
            }
        }

        /// <summary>
        /// Multiplier for an envelope time from a scaling connection.
        /// <paramref name="normalised"/> is the source value over its full range.
        /// </summary>
        public static float TimeScaleFactor(int scale, float normalised)
        {
            if (scale == 0) return 1f;
            double timecents = (scale / 65536.0) * normalised;
            double factor = Math.Pow(2.0, timecents / 1200.0);
            if (factor < 0.02) factor = 0.02;
            if (factor > 50.0) factor = 50.0;
            return (float)factor;
        }

        public bool HasLfo
        {
            get
            {
                return LfoToPitchCents != 0f || LfoToPitchCentsModWheel != 0f
                    || LfoToGainDb != 0f || LfoToGainDbModWheel != 0f;
            }
        }

        /// <summary>Linear multiplier for <see cref="AttenuationDb"/>.</summary>
        public float AttenuationLinear
        {
            get { return AttenuationDb >= 0f ? 1f : (float)Math.Pow(10.0, AttenuationDb / 20.0); }
        }

        public static DlsArticulation Default { get { return new DlsArticulation(); } }

        /// <summary>
        /// Overlays this block on top of another, taking each field from whichever block
        /// specified it and preferring this one where both did.
        /// </summary>
        public DlsArticulation MergeOver(DlsArticulation baseArt)
        {
            if (baseArt == null) return this;

            DlsArticulation merged = new DlsArticulation();
            merged.AttackSeconds = Take(DlsArtField.Attack, AttackSeconds, baseArt, baseArt.AttackSeconds);
            merged.DecaySeconds = Take(DlsArtField.Decay, DecaySeconds, baseArt, baseArt.DecaySeconds);
            merged.SustainLevel = Take(DlsArtField.Sustain, SustainLevel, baseArt, baseArt.SustainLevel);
            merged.ReleaseSeconds = Take(DlsArtField.Release, ReleaseSeconds, baseArt, baseArt.ReleaseSeconds);
            merged.Pan = Take(DlsArtField.Pan, Pan, baseArt, baseArt.Pan);
            merged.AttenuationDb = Take(DlsArtField.Attenuation, AttenuationDb, baseArt, baseArt.AttenuationDb);
            merged.LfoFrequencyHz = Take(DlsArtField.LfoFrequency, LfoFrequencyHz, baseArt, baseArt.LfoFrequencyHz);
            merged.LfoStartDelaySeconds = Take(DlsArtField.LfoDelay, LfoStartDelaySeconds, baseArt, baseArt.LfoStartDelaySeconds);
            merged.LfoToPitchCents = Take(DlsArtField.LfoToPitch, LfoToPitchCents, baseArt, baseArt.LfoToPitchCents);
            merged.LfoToPitchCentsModWheel = Take(DlsArtField.LfoToPitch, LfoToPitchCentsModWheel, baseArt, baseArt.LfoToPitchCentsModWheel);
            merged.LfoToGainDb = Take(DlsArtField.LfoToGain, LfoToGainDb, baseArt, baseArt.LfoToGainDb);
            merged.LfoToGainDbModWheel = Take(DlsArtField.LfoToGain, LfoToGainDbModWheel, baseArt, baseArt.LfoToGainDbModWheel);
            merged.Eg2AttackSeconds = Take(DlsArtField.Eg2, Eg2AttackSeconds, baseArt, baseArt.Eg2AttackSeconds);
            merged.Eg2DecaySeconds = Take(DlsArtField.Eg2, Eg2DecaySeconds, baseArt, baseArt.Eg2DecaySeconds);
            merged.Eg2SustainLevel = Take(DlsArtField.Eg2, Eg2SustainLevel, baseArt, baseArt.Eg2SustainLevel);
            merged.Eg2ReleaseSeconds = Take(DlsArtField.Eg2, Eg2ReleaseSeconds, baseArt, baseArt.Eg2ReleaseSeconds);
            merged.Eg2ToPitchCents = Take(DlsArtField.Eg2ToPitch, Eg2ToPitchCents, baseArt, baseArt.Eg2ToPitchCents);
            merged.VelocityToAttenuationDb = Take(DlsArtField.VelocityToAttenuation,
                VelocityToAttenuationDb, baseArt, baseArt.VelocityToAttenuationDb);
            merged.Eg1DecayKeyScale = TakeInt(DlsArtField.TimeScaling, Eg1DecayKeyScale, baseArt, baseArt.Eg1DecayKeyScale);
            merged.Eg1AttackVelocityScale = TakeInt(DlsArtField.TimeScaling, Eg1AttackVelocityScale, baseArt, baseArt.Eg1AttackVelocityScale);
            merged.Eg2DecayKeyScale = TakeInt(DlsArtField.TimeScaling, Eg2DecayKeyScale, baseArt, baseArt.Eg2DecayKeyScale);
            merged.Eg2AttackVelocityScale = TakeInt(DlsArtField.TimeScaling, Eg2AttackVelocityScale, baseArt, baseArt.Eg2AttackVelocityScale);
            merged.Specified = Specified | baseArt.Specified;
            return merged;
        }

        int TakeInt(DlsArtField field, int mine, DlsArticulation baseArt, int theirs)
        {
            if ((Specified & field) != 0) return mine;
            if ((baseArt.Specified & field) != 0) return theirs;
            return mine;
        }

        float Take(DlsArtField field, float mine, DlsArticulation baseArt, float theirs)
        {
            if ((Specified & field) != 0) return mine;
            if ((baseArt.Specified & field) != 0) return theirs;
            return mine;                      // neither specified it: the shared default
        }

        public string Dump()
        {
            string eg2 = HasEg2
                ? string.Format("  EG2 {0:F0}c A {1:F3}s D {2:F3}s S {3:P0}",
                    Eg2ToPitchCents, Eg2AttackSeconds, Eg2DecaySeconds, Eg2SustainLevel)
                : "";
            string scaling = HasTimeScaling
                ? string.Format("  scale[decay/key {0} attack/vel {1}]", Eg1DecayKeyScale, Eg1AttackVelocityScale)
                : "";
            string lfo = HasLfo
                ? string.Format("  LFO {0:F2}Hz delay {1:F2}s pitch {2:F0}c(+{3:F0}c mod) gain {4:F1}dB(+{5:F1} mod)",
                    LfoFrequencyHz, LfoStartDelaySeconds, LfoToPitchCents, LfoToPitchCentsModWheel,
                    LfoToGainDb, LfoToGainDbModWheel)
                : "";
            return string.Format("A {0:F3}s  D {1:F3}s  S {2:P0}  R {3:F3}s  pan {4:F2}  atten {5:F1}dB{6}{7}{9}  [{8}]",
                AttackSeconds, DecaySeconds, SustainLevel, ReleaseSeconds, Pan, AttenuationDb, lfo, eg2,
                Specified == DlsArtField.None ? "all defaulted" : Specified.ToString(), scaling);
        }
    }

    /// <summary>A decoded wave from the wave pool ('wvpl').</summary>
    public sealed class DlsWave
    {
        public float[] Data = new float[0];   // interleaved, -1..1
        public int Channels = 1;
        public int SampleRate = 44100;
        public int FrameCount;
        public DlsSampleInfo Info;            // wave-level 'wsmp', may be null
        public string Name;
    }

    /// <summary>A key/velocity zone inside an instrument.</summary>
    public sealed class DlsRegion
    {
        public int KeyLow, KeyHigh = 127;
        public int VelLow, VelHigh = 127;
        public int KeyGroup;              // exclusive group (hi-hats etc.)
        public bool SelfNonExclusive;
        public int WaveTableIndex = -1;   // index into the 'ptbl' cue table
        public int WaveIndex = -1;        // resolved index into DlsFile.Waves
        public DlsSampleInfo Info;        // region-level 'wsmp' override, may be null
        public DlsArticulation Articulation;

        // Resolved once at load time. Regions stay valid when their DLS is stacked
        // with others, because they no longer index back into a specific file.
        public DlsWave Wave;
        public DlsSampleInfo ResolvedInfo = new DlsSampleInfo();
        public DlsArticulation ResolvedArticulation = DlsArticulation.Default;
    }

    public sealed class DlsInstrument
    {
        public int Bank;          // (bankMSB << 7) | bankLSB
        public int Program;       // 0..127
        public bool IsDrum;
        public string Name;
        public readonly List<DlsRegion> Regions = new List<DlsRegion>();
        public DlsArticulation Articulation;
        /// <summary>The collection this instrument came from — useful when banks are stacked.</summary>
        public DlsFile Owner;

        /// <summary>Finds the region covering a key/velocity pair, with graceful fallbacks.</summary>
        public DlsRegion FindRegion(int key, int velocity)
        {
            for (int i = 0; i < Regions.Count; i++)
            {
                DlsRegion r = Regions[i];
                if (key >= r.KeyLow && key <= r.KeyHigh &&
                    velocity >= r.VelLow && velocity <= r.VelHigh &&
                    r.Wave != null)
                    return r;
            }
            // Ignore velocity split if nothing matched.
            for (int i = 0; i < Regions.Count; i++)
            {
                DlsRegion r = Regions[i];
                if (key >= r.KeyLow && key <= r.KeyHigh && r.Wave != null)
                    return r;
            }
            // Last resort: nearest region by key, so a sparse custom DLS still sounds.
            DlsRegion best = null;
            int bestDist = int.MaxValue;
            for (int i = 0; i < Regions.Count; i++)
            {
                DlsRegion r = Regions[i];
                if (r.Wave == null) continue;
                int d = key < r.KeyLow ? r.KeyLow - key : (key > r.KeyHigh ? key - r.KeyHigh : 0);
                if (d < bestDist) { bestDist = d; best = r; }
            }
            return best;
        }

        public override string ToString()
        {
            return string.Format("{0} (bank {1}{2}, program {3}, {4} regions)",
                Name ?? "<unnamed>", Bank, IsDrum ? " drum" : "", Program, Regions.Count);
        }
    }

    /// <summary>One kind of DLS connection block seen while parsing, and whether it was used.</summary>
    public sealed class DlsConnectionStat
    {
        public int Source, Control, Destination;
        public int Count;
        public bool Applied;

        public override string ToString()
        {
            return string.Format("{0,-28} {1,-22} x{2,-5} {3}",
                DlsFile.DestinationName(Destination),
                Source == 0 && Control == 0 ? "static" : DlsFile.SourceName(Source) +
                    (Control != 0 ? " ctrl 0x" + Control.ToString("X") : ""),
                Count,
                Applied ? "applied" : "IGNORED");
        }
    }

    public sealed class DlsFile : IDlsBank
    {
        public readonly List<DlsWave> Waves = new List<DlsWave>();
        public readonly List<DlsInstrument> Instruments = new List<DlsInstrument>();
        public string Name;
        /// <summary>The collection's DLSID, used to match band references by identity.</summary>
        public Guid Id;
        /// <summary>Where this collection was loaded from, when known.</summary>
        public string SourcePath;

        /// <summary>Every connection block encountered, with whether this synth applies it.</summary>
        public readonly List<DlsConnectionStat> ArticulationConnections = new List<DlsConnectionStat>();
        /// <summary>Counts of DLS1-style ('art1') and DLS2-style ('art2') articulation chunks.</summary>
        public int Art1Count, Art2Count;

        readonly Dictionary<long, int> _waveOffsetToIndex = new Dictionary<long, int>();
        readonly List<uint> _cueOffsets = new List<uint>();
        int[] _cueToWave = new int[0];
        Dictionary<int, DlsInstrument> _lookup;

        // ---------------------------------------------------------------- load

        public static DlsFile Load(string path)
        {
            return Load(File.ReadAllBytes(path));
        }

        public static DlsFile Load(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 12)
                throw new InvalidDataException("DLS: file is empty or truncated.");

            DlsFile dls = new DlsFile();
            MemoryStream ms = new MemoryStream(bytes, false);
            BinaryReader r = new BinaryReader(ms);

            if (ReadFourCC(r) != "RIFF")
                throw new InvalidDataException("DLS: not a RIFF file.");
            uint riffSize = r.ReadUInt32();
            string form = ReadFourCC(r);
            if (form != "DLS ")
                throw new InvalidDataException("DLS: RIFF form is '" + form + "', expected 'DLS '.");

            long end = Math.Min(ms.Length, 8L + riffSize);
            dls.ParseTopLevel(r, ms, end);
            dls.ResolveCues();
            dls.ResolveReferences();
            dls.BuildLookup();
            return dls;
        }

        void ParseTopLevel(BinaryReader r, Stream s, long end)
        {
            while (s.Position + 8 <= end)
            {
                string id = ReadFourCC(r);
                if (id == null) break;
                uint size = r.ReadUInt32();
                long next = s.Position + size + (size & 1);
                if (next > end) next = end;

                if (id == "LIST")
                {
                    string type = ReadFourCC(r);
                    if (type == "lins") ParseInstrumentList(r, s, next);
                    else if (type == "wvpl") ParseWavePool(r, s, next, s.Position);
                    else if (type == "INFO") Name = ParseInfoName(r, s, next);
                }
                else if (id == "ptbl")
                {
                    ParseCueTable(r, s, next);
                }
                else if (id == "dlid" && size >= 16)
                {
                    Id = new Guid(r.ReadBytes(16));
                }

                s.Position = next;
            }
        }

        // ------------------------------------------------------- instruments

        void ParseInstrumentList(BinaryReader r, Stream s, long end)
        {
            while (s.Position + 8 <= end)
            {
                string id = ReadFourCC(r);
                if (id == null) break;
                uint size = r.ReadUInt32();
                long next = s.Position + size + (size & 1);
                if (next > end) next = end;

                if (id == "LIST" && ReadFourCC(r) == "ins ")
                    Instruments.Add(ParseInstrument(r, s, next));

                s.Position = next;
            }
        }

        DlsInstrument ParseInstrument(BinaryReader r, Stream s, long end)
        {
            DlsInstrument ins = new DlsInstrument();
            while (s.Position + 8 <= end)
            {
                string id = ReadFourCC(r);
                if (id == null) break;
                uint size = r.ReadUInt32();
                long next = s.Position + size + (size & 1);
                if (next > end) next = end;

                if (id == "insh")
                {
                    r.ReadUInt32();                       // cRegions (we count them ourselves)
                    uint bank = r.ReadUInt32();
                    uint program = r.ReadUInt32();
                    ins.IsDrum = (bank & 0x80000000u) != 0;
                    int msb = (int)((bank >> 8) & 0x7F);
                    int lsb = (int)(bank & 0x7F);
                    ins.Bank = (msb << 7) | lsb;
                    ins.Program = (int)(program & 0x7F);
                }
                else if (id == "LIST")
                {
                    string type = ReadFourCC(r);
                    if (type == "lrgn") ParseRegionList(r, s, next, ins);
                    else if (type == "lart" || type == "lar2")
                    {
                        DlsArticulation parsed = ParseArticulationList(r, s, next, this);
                        // Prefer DLS2 when a file carries both for compatibility.
                        if (parsed != null && (ins.Articulation == null || type == "lar2"))
                            ins.Articulation = parsed;
                    }
                    else if (type == "INFO") ins.Name = ParseInfoName(r, s, next);
                }

                s.Position = next;
            }
            return ins;
        }

        void ParseRegionList(BinaryReader r, Stream s, long end, DlsInstrument ins)
        {
            while (s.Position + 8 <= end)
            {
                string id = ReadFourCC(r);
                if (id == null) break;
                uint size = r.ReadUInt32();
                long next = s.Position + size + (size & 1);
                if (next > end) next = end;

                if (id == "LIST")
                {
                    string type = ReadFourCC(r);
                    if (type == "rgn " || type == "rgn2")
                        ins.Regions.Add(ParseRegion(r, s, next));
                }

                s.Position = next;
            }
        }

        DlsRegion ParseRegion(BinaryReader r, Stream s, long end)
        {
            DlsRegion rgn = new DlsRegion();
            while (s.Position + 8 <= end)
            {
                string id = ReadFourCC(r);
                if (id == null) break;
                uint size = r.ReadUInt32();
                long next = s.Position + size + (size & 1);
                if (next > end) next = end;

                if (id == "rgnh")
                {
                    rgn.KeyLow = r.ReadUInt16();
                    rgn.KeyHigh = r.ReadUInt16();
                    rgn.VelLow = r.ReadUInt16();
                    rgn.VelHigh = r.ReadUInt16();
                    ushort options = r.ReadUInt16();
                    rgn.SelfNonExclusive = (options & 0x0001) != 0;
                    rgn.KeyGroup = r.ReadUInt16();
                }
                else if (id == "wsmp")
                {
                    rgn.Info = ParseWaveSample(r, s, next);
                }
                else if (id == "wlnk")
                {
                    r.ReadUInt16();                    // fusOptions
                    r.ReadUInt16();                    // usPhaseGroup
                    r.ReadUInt32();                    // ulChannel
                    rgn.WaveTableIndex = (int)r.ReadUInt32();
                }
                else if (id == "LIST")
                {
                    string type = ReadFourCC(r);
                    if (type == "lart" || type == "lar2")
                    {
                        DlsArticulation parsed = ParseArticulationList(r, s, next, this);
                        if (parsed != null && (rgn.Articulation == null || type == "lar2"))
                            rgn.Articulation = parsed;
                    }
                }

                s.Position = next;
            }

            if (rgn.VelHigh == 0 && rgn.VelLow == 0) rgn.VelHigh = 127;   // some authoring tools leave this blank
            if (rgn.KeyHigh == 0 && rgn.KeyLow == 0) rgn.KeyHigh = 127;
            return rgn;
        }

        // ------------------------------------------------------ articulation

        static DlsArticulation ParseArticulationList(BinaryReader r, Stream s, long end, DlsFile owner)
        {
            DlsArticulation art = null;
            while (s.Position + 8 <= end)
            {
                string id = ReadFourCC(r);
                if (id == null) break;
                uint size = r.ReadUInt32();
                long next = s.Position + size + (size & 1);
                if (next > end) next = end;

                if (id == "art1" || id == "art2")
                {
                    if (owner != null)
                    {
                        if (id == "art1") owner.Art1Count++;
                        else owner.Art2Count++;
                    }
                    art = ParseArticulation(r, s, next, owner);
                }

                s.Position = next;
            }
            return art;
        }

        // Connection block destinations we understand.
        const ushort DST_GAIN = 0x0001;
        const ushort DST_PAN = 0x0004;
        const ushort DST_EG1_ATTACK = 0x0206;
        const ushort DST_EG1_DECAY = 0x0207;
        // 0x0208 is CONN_DST_EG1_RESERVED in the DLS connection list; sustain level is
        // 0x020A. Both are accepted so a file using either convention still works, but
        // real content authors sustain at 0x020A, paired one-for-one with decay.
        const ushort DST_EG1_RESERVED = 0x0208;
        const ushort DST_EG1_RELEASE = 0x0209;
        const ushort DST_EG1_SUSTAIN = 0x020A;

        // EG2 destinations. The 0x030A base is inferred from real content: a file with a
        // pitch-from-EG2 connection also carries a static 0x030B, which fits decay time
        // under this mapping. The 0x0306-based mirror of the EG1 offsets is accepted too,
        // since the two don't collide.
        const ushort DST_EG2_ATTACK = 0x030A;
        const ushort DST_EG2_DECAY = 0x030B;
        const ushort DST_EG2_RELEASE = 0x030D;
        const ushort DST_EG2_SUSTAIN = 0x030E;
        // Alternate ids for destinations whose number differs between the two DLS
        // numbering conventions. Content written for broad compatibility (the GS set
        // among it) emits both, always the same value, so mapping both is idempotent.
        const ushort DST_PAN_ALT = 0x0002;
        const ushort DST_EG2_SUSTAIN_ALT = 0x030C;
        const ushort DST_EG2_ATTACK_ALT = 0x0306;
        const ushort DST_EG2_DECAY_ALT = 0x0307;
        const ushort DST_EG2_RELEASE_ALT = 0x0309;

        const ushort DST_LFO_FREQUENCY = 0x0104;
        const ushort DST_LFO_STARTDELAY = 0x0105;
        const ushort DST_PITCH = 0x0003;

        static DlsArticulation ParseArticulation(BinaryReader r, Stream s, long end, DlsFile owner)
        {
            long start = s.Position;
            uint cbSize = r.ReadUInt32();
            uint count = r.ReadUInt32();
            if (cbSize >= 8 && start + cbSize <= end) s.Position = start + cbSize;

            DlsArticulation art = new DlsArticulation();
            for (uint i = 0; i < count && s.Position + 12 <= end; i++)
            {
                ushort source = r.ReadUInt16();
                ushort control = r.ReadUInt16();
                ushort dest = r.ReadUInt16();
                r.ReadUInt16();                    // transform
                int scale = r.ReadInt32();

                bool isStatic = source == 0 && control == 0;
                bool applied = false;

                const ushort SRC_LFO = 0x0001;
                const ushort SRC_EG2 = 0x0005;
                const ushort CTRL_CC1 = 0x0081;      // mod wheel

                const ushort SRC_KEYONVELOCITY = 0x0002;
                const ushort SRC_KEYNUMBER = 0x0003;

                if (control == 0 && source == SRC_KEYONVELOCITY && dest == DST_GAIN)
                {
                    // Depth in dB at zero velocity, reaching 0 dB at full velocity. When a
                    // bank specifies this it replaces the synth's built-in velocity curve
                    // rather than stacking with it.
                    art.VelocityToAttenuationDb = Clamp(scale / 655360f, -96f, 0f);
                    art.Specified |= DlsArtField.VelocityToAttenuation;
                    applied = true;
                }

                if (control == 0 && (source == SRC_KEYNUMBER || source == SRC_KEYONVELOCITY))
                {
                    bool byKey = source == SRC_KEYNUMBER;
                    switch (dest)
                    {
                        case DST_EG1_DECAY:
                            if (byKey) { art.Eg1DecayKeyScale = scale; applied = true; }
                            break;
                        case DST_EG1_ATTACK:
                            if (!byKey) { art.Eg1AttackVelocityScale = scale; applied = true; }
                            break;
                        case DST_EG2_DECAY:
                        case DST_EG2_DECAY_ALT:
                            if (byKey) { art.Eg2DecayKeyScale = scale; applied = true; }
                            break;
                        case DST_EG2_ATTACK:
                        case DST_EG2_ATTACK_ALT:
                            if (!byKey) { art.Eg2AttackVelocityScale = scale; applied = true; }
                            break;
                    }
                    // Parsed and carried, but the synth does not scale envelope times yet,
                    // so these must not be reported as applied.
                    if (applied) { art.Specified |= DlsArtField.TimeScaling; applied = false; }
                }

                if (source == SRC_EG2 && control == 0 && dest == DST_PITCH)
                {
                    art.Eg2ToPitchCents = Clamp(scale / 65536f, -9600f, 9600f);
                    art.Specified |= DlsArtField.Eg2ToPitch;
                    applied = true;
                }

                if (source == SRC_LFO && (control == 0 || control == CTRL_CC1))
                {
                    bool viaModWheel = control == CTRL_CC1;
                    if (dest == DST_PITCH)
                    {
                        float cents = Clamp(scale / 65536f, -2400f, 2400f);
                        if (viaModWheel) art.LfoToPitchCentsModWheel = cents;
                        else art.LfoToPitchCents = cents;
                        art.Specified |= DlsArtField.LfoToPitch;
                        applied = true;
                    }
                    else if (dest == DST_GAIN)
                    {
                        float db = Clamp(scale / 655360f, -48f, 48f);
                        if (viaModWheel) art.LfoToGainDbModWheel = db;
                        else art.LfoToGainDb = db;
                        art.Specified |= DlsArtField.LfoToGain;
                        applied = true;
                    }
                }

                // Only static (unmodulated) connections; runtime modulators are out of scope.
                if (isStatic)
                {
                    switch (dest)
                    {
                        case DST_EG1_ATTACK:
                            art.AttackSeconds = TimeCentsToSeconds(scale);
                            art.Specified |= DlsArtField.Attack; applied = true; break;
                        case DST_EG1_DECAY:
                            art.DecaySeconds = TimeCentsToSeconds(scale);
                            art.Specified |= DlsArtField.Decay; applied = true; break;
                        case DST_EG1_RELEASE:
                            art.ReleaseSeconds = TimeCentsToSeconds(scale);
                            art.Specified |= DlsArtField.Release; applied = true; break;
                        case DST_EG1_SUSTAIN:
                        case DST_EG1_RESERVED:
                            art.SustainLevel = PercentToUnit(scale);
                            art.Specified |= DlsArtField.Sustain; applied = true; break;
                        case DST_PAN:
                        case DST_PAN_ALT:
                            art.Pan = Clamp(scale / 65536f / 50f, -1f, 1f);
                            art.Specified |= DlsArtField.Pan; applied = true; break;
                        case DST_LFO_FREQUENCY:
                            // Absolute pitch cents, where 0 is 8.176 Hz.
                            art.LfoFrequencyHz = Clamp(
                                (float)(8.176 * Math.Pow(2.0, scale / (1200.0 * 65536.0))), 0.05f, 50f);
                            art.Specified |= DlsArtField.LfoFrequency; applied = true; break;
                        case DST_EG2_ATTACK:
                        case DST_EG2_ATTACK_ALT:
                            art.Eg2AttackSeconds = TimeCentsToSeconds(scale);
                            art.Specified |= DlsArtField.Eg2; applied = true; break;
                        case DST_EG2_DECAY:
                        case DST_EG2_DECAY_ALT:
                            art.Eg2DecaySeconds = TimeCentsToSeconds(scale);
                            art.Specified |= DlsArtField.Eg2; applied = true; break;
                        case DST_EG2_RELEASE:
                        case DST_EG2_RELEASE_ALT:
                            art.Eg2ReleaseSeconds = TimeCentsToSeconds(scale);
                            art.Specified |= DlsArtField.Eg2; applied = true; break;
                        case DST_EG2_SUSTAIN:
                        case DST_EG2_SUSTAIN_ALT:
                            art.Eg2SustainLevel = PercentToUnit(scale);
                            art.Specified |= DlsArtField.Eg2; applied = true; break;
                        case DST_LFO_STARTDELAY:
                            art.LfoStartDelaySeconds = TimeCentsToSeconds(scale);
                            art.Specified |= DlsArtField.LfoDelay; applied = true; break;
                        case DST_GAIN:
                            // Same 16.16 relative-gain units as the wsmp lGain field.
                            art.AttenuationDb = Clamp(scale / 655360f, -96f, 0f);
                            art.Specified |= DlsArtField.Attenuation;
                            applied = true;
                            break;
                    }
                }

                if (owner != null) owner.RecordConnection(source, control, dest, applied);
            }

            if (art.ReleaseSeconds < 0.005f) art.ReleaseSeconds = 0.005f;
            return art;
        }

        void RecordConnection(int source, int control, int destination, bool applied)
        {
            for (int i = 0; i < ArticulationConnections.Count; i++)
            {
                DlsConnectionStat stat = ArticulationConnections[i];
                if (stat.Source == source && stat.Control == control && stat.Destination == destination)
                {
                    stat.Count++;
                    return;
                }
            }
            DlsConnectionStat added = new DlsConnectionStat();
            added.Source = source; added.Control = control;
            added.Destination = destination; added.Applied = applied; added.Count = 1;
            ArticulationConnections.Add(added);
        }

        // Names for the constants this synth acts on. Anything else prints as hex rather
        // than guessing, since the DLS2 destination list is long and version-dependent.
        internal static string DestinationName(int destination)
        {
            switch (destination)
            {
                case DST_GAIN: return "attenuation/gain";
                case DST_PITCH: return "pitch";
                case DST_PAN: return "pan";
                case DST_EG1_ATTACK: return "EG1 attack";
                case DST_EG1_DECAY: return "EG1 decay";
                case DST_EG1_SUSTAIN: return "EG1 sustain";
                case DST_EG1_RESERVED: return "EG1 sustain (0x0208)";
                case DST_EG1_RELEASE: return "EG1 release";
                case DST_LFO_FREQUENCY: return "LFO frequency";
                case DST_LFO_STARTDELAY: return "LFO start delay";
                case DST_EG2_ATTACK: return "EG2 attack";
                case DST_EG2_DECAY: return "EG2 decay";
                case DST_EG2_RELEASE: return "EG2 release";
                case DST_EG2_SUSTAIN: return "EG2 sustain";
                case DST_EG2_SUSTAIN_ALT: return "EG2 sustain (alt id)";
                case DST_PAN_ALT: return "pan (alt id)";
                default: return "destination 0x" + destination.ToString("X4");
            }
        }

        internal static string SourceName(int source)
        {
            switch (source)
            {
                case 0x0000: return "none";
                case 0x0001: return "LFO";
                case 0x0002: return "key-on velocity";
                case 0x0003: return "key number";
                case 0x0004: return "EG1";
                case 0x0005: return "EG2";
                case 0x0006: return "pitch wheel";
                default: return "source 0x" + source.ToString("X4");
            }
        }

        /// <summary>
        /// Per-instrument envelope report: what each one resolved to and which fields were
        /// actually authored versus defaulted. A note that never decays usually shows
        /// sustain at 100% with no Decay or Sustain in its specified list.
        /// </summary>
        public string DescribeEnvelopes(int maxInstruments)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Envelopes in " + (Name ?? SourcePath ?? "<unnamed>") + ":");

            for (int i = 0; i < Instruments.Count && i < maxInstruments; i++)
            {
                DlsInstrument ins = Instruments[i];
                sb.AppendLine("  " + (ins.Name ?? "<unnamed>") +
                              "  (bank " + ins.Bank + " program " + ins.Program + (ins.IsDrum ? " drum" : "") + ")");
                sb.AppendLine("    instrument art: " +
                    (ins.Articulation != null ? ins.Articulation.Dump() : "none"));

                for (int j = 0; j < ins.Regions.Count && j < 3; j++)
                {
                    DlsRegion rgn = ins.Regions[j];
                    bool looped = rgn.ResolvedInfo != null && rgn.ResolvedInfo.Loop != null;
                    sb.AppendLine(string.Format("    region {0} keys {1}-{2} {3}: {4}",
                        j, rgn.KeyLow, rgn.KeyHigh, looped ? "looped" : "one-shot",
                        rgn.ResolvedArticulation.Dump()));

                    if (looped && rgn.ResolvedArticulation.SustainLevel > 0.99f &&
                        (rgn.ResolvedArticulation.Specified & DlsArtField.Decay) == 0)
                        sb.AppendLine("      note: looped sample, full sustain, no authored decay - " +
                                      "this holds at a constant level until note off.");
                }
                if (ins.Regions.Count > 3) sb.AppendLine("    ...");
            }
            if (Instruments.Count > maxInstruments) sb.AppendLine("  ...");
            return sb.ToString();
        }

        /// <summary>
        /// Reports the articulation this collection uses and how much of it this synth
        /// acts on. Anything marked IGNORED is authored expression that won't be heard.
        /// </summary>
        public string DescribeArticulation()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Articulation in " + (Name ?? SourcePath ?? "<unnamed>") + ":");
            sb.AppendLine("  art1 (DLS1) chunks: " + Art1Count + "    art2 (DLS2) chunks: " + Art2Count);

            if (Art1Count > 0 && Art2Count == 0)
                sb.AppendLine("  DLS1 articulation throughout - this is the model the synth implements.");
            else if (Art2Count > 0 && Art1Count == 0)
                sb.AppendLine("  DLS2 articulation throughout - anything beyond the DLS1 envelope is ignored below.");
            else if (Art1Count > 0 && Art2Count > 0)
                sb.AppendLine("  Mixed; where both are present on one instrument, the DLS2 block wins.");
            else
                sb.AppendLine("  No articulation authored - default envelope applies (instant attack, " +
                              "full sustain, short release).");

            if (ArticulationConnections.Count == 0) return sb.ToString();

            sb.AppendLine("  connection blocks:");
            int ignored = 0;
            for (int i = 0; i < ArticulationConnections.Count; i++)
            {
                sb.AppendLine("    " + ArticulationConnections[i]);
                if (!ArticulationConnections[i].Applied) ignored += ArticulationConnections[i].Count;
            }
            sb.AppendLine(ignored == 0
                ? "  Everything authored here is applied."
                : "  " + ignored + " block(s) ignored. If the sound differs from Producer, that's where.");
            return sb.ToString();
        }

        // DLS time values are 16.16 fixed-point timecents: seconds = 2^(value / (1200 * 65536)).
        static float TimeCentsToSeconds(int value)
        {
            if (value <= -0x3FFFFFFF) return 0f;
            double sec = Math.Pow(2.0, value / (1200.0 * 65536.0));
            if (sec < 0.0 || double.IsNaN(sec)) sec = 0.0;
            if (sec > 40.0) sec = 40.0;
            return (float)sec;
        }

        // Percent units vary between authoring tools (percent vs. 0.1%); normalise both.
        static float PercentToUnit(int value)
        {
            float pct = value / 65536f;
            if (pct > 100f) pct /= 10f;
            return Clamp(pct / 100f, 0f, 1f);
        }

        // --------------------------------------------------------- wave pool

        void ParseCueTable(BinaryReader r, Stream s, long end)
        {
            long start = s.Position;
            uint cbSize = r.ReadUInt32();
            uint cues = r.ReadUInt32();
            if (cbSize >= 8 && start + cbSize <= end) s.Position = start + cbSize;

            _cueOffsets.Clear();
            for (uint i = 0; i < cues && s.Position + 4 <= end; i++)
                _cueOffsets.Add(r.ReadUInt32());
        }

        void ParseWavePool(BinaryReader r, Stream s, long end, long poolDataStart)
        {
            while (s.Position + 8 <= end)
            {
                long chunkStart = s.Position;
                string id = ReadFourCC(r);
                if (id == null) break;
                uint size = r.ReadUInt32();
                long next = s.Position + size + (size & 1);
                if (next > end) next = end;

                if (id == "LIST" && ReadFourCC(r) == "wave")
                {
                    long offset = chunkStart - poolDataStart;
                    if (!_waveOffsetToIndex.ContainsKey(offset))
                        _waveOffsetToIndex[offset] = Waves.Count;
                    Waves.Add(ParseWave(r, s, next));
                }

                s.Position = next;
            }
        }

        DlsWave ParseWave(BinaryReader r, Stream s, long end)
        {
            DlsWave wave = new DlsWave();
            int formatTag = 1, bits = 16, channels = 1;
            byte[] raw = null;

            while (s.Position + 8 <= end)
            {
                string id = ReadFourCC(r);
                if (id == null) break;
                uint size = r.ReadUInt32();
                long next = s.Position + size + (size & 1);
                if (next > end) next = end;

                if (id == "fmt ")
                {
                    formatTag = r.ReadUInt16();
                    channels = r.ReadUInt16();
                    wave.SampleRate = (int)r.ReadUInt32();
                    r.ReadUInt32();                 // avg bytes/sec
                    r.ReadUInt16();                 // block align
                    bits = r.ReadUInt16();
                }
                else if (id == "data")
                {
                    raw = r.ReadBytes((int)size);
                }
                else if (id == "wsmp")
                {
                    wave.Info = ParseWaveSample(r, s, next);
                }
                else if (id == "LIST" && ReadFourCC(r) == "INFO")
                {
                    wave.Name = ParseInfoName(r, s, next);
                }

                s.Position = next;
            }

            if (channels < 1) channels = 1;
            if (wave.SampleRate <= 0) wave.SampleRate = 44100;
            wave.Channels = channels;
            wave.Data = DecodePcm(raw, formatTag, bits, channels);
            wave.FrameCount = wave.Data.Length / channels;
            return wave;
        }

        static float[] DecodePcm(byte[] raw, int formatTag, int bits, int channels)
        {
            if (raw == null || raw.Length == 0) return new float[0];

            // 0xFFFE (WAVE_FORMAT_EXTENSIBLE) is PCM with an extended header.
            bool isFloat = formatTag == 3;
            if (formatTag != 1 && formatTag != 3 && formatTag != 0xFFFE)
                return new float[0];               // ADPCM/MP3 payloads are not supported

            int bytesPerSample = Math.Max(1, bits / 8);
            int total = raw.Length / bytesPerSample;
            total -= total % channels;
            float[] outp = new float[total];

            if (isFloat && bits == 32)
            {
                for (int i = 0; i < total; i++) outp[i] = BitConverter.ToSingle(raw, i * 4);
            }
            else if (bits == 8)
            {
                for (int i = 0; i < total; i++) outp[i] = (raw[i] - 128) / 128f;
            }
            else if (bits == 16)
            {
                for (int i = 0; i < total; i++)
                {
                    short v = (short)(raw[i * 2] | (raw[i * 2 + 1] << 8));
                    outp[i] = v / 32768f;
                }
            }
            else if (bits == 24)
            {
                for (int i = 0; i < total; i++)
                {
                    int b = i * 3;
                    int v = raw[b] | (raw[b + 1] << 8) | (raw[b + 2] << 16);
                    if ((v & 0x800000) != 0) v |= unchecked((int)0xFF000000);
                    outp[i] = v / 8388608f;
                }
            }
            else if (bits == 32)
            {
                for (int i = 0; i < total; i++)
                    outp[i] = BitConverter.ToInt32(raw, i * 4) / 2147483648f;
            }
            else
            {
                return new float[0];
            }

            return outp;
        }

        static DlsSampleInfo ParseWaveSample(BinaryReader r, Stream s, long end)
        {
            long start = s.Position;
            DlsSampleInfo info = new DlsSampleInfo();

            uint cbSize = r.ReadUInt32();
            info.UnityNote = r.ReadUInt16();
            info.FineTuneCents = r.ReadInt16();          // already in cents
            int gain = r.ReadInt32();                     // 16.16 fixed-point centibels
            r.ReadUInt32();                               // fulOptions
            uint loopCount = r.ReadUInt32();

            info.GainDb = Clamp(gain / 655360f, -96f, 24f);
            if (info.UnityNote > 127) info.UnityNote = 60;

            if (cbSize >= 20 && start + cbSize <= end) s.Position = start + cbSize;

            if (loopCount > 0 && s.Position + 16 <= end)
            {
                uint loopStructSize = r.ReadUInt32();
                DlsLoop loop = new DlsLoop();
                loop.Type = r.ReadUInt32();
                loop.Start = r.ReadUInt32();
                loop.Length = r.ReadUInt32();
                if (loop.Length > 0) info.Loop = loop;
                if (loopStructSize == 0) { /* malformed; ignore */ }
            }

            return info;
        }

        // ---------------------------------------------------------- resolve

        void ResolveCues()
        {
            _cueToWave = new int[_cueOffsets.Count];
            for (int i = 0; i < _cueOffsets.Count; i++)
            {
                int index;
                if (_waveOffsetToIndex.TryGetValue(_cueOffsets[i], out index)) _cueToWave[i] = index;
                else _cueToWave[i] = i < Waves.Count ? i : -1;   // fall back to positional mapping
            }

            for (int i = 0; i < Instruments.Count; i++)
            {
                List<DlsRegion> regions = Instruments[i].Regions;
                for (int j = 0; j < regions.Count; j++)
                {
                    DlsRegion rgn = regions[j];
                    int t = rgn.WaveTableIndex;
                    if (t < 0) continue;
                    if (_cueToWave.Length > 0)
                        rgn.WaveIndex = t < _cueToWave.Length ? _cueToWave[t] : -1;
                    else
                        rgn.WaveIndex = t < Waves.Count ? t : -1;

                    if (rgn.WaveIndex >= Waves.Count) rgn.WaveIndex = -1;
                }
            }
        }

        /// <summary>
        /// Binds each region to its wave and precomputes the sample info and articulation
        /// it will actually use, applying region-over-instrument-over-default precedence.
        /// </summary>
        void ResolveReferences()
        {
            for (int i = 0; i < Instruments.Count; i++)
            {
                DlsInstrument ins = Instruments[i];
                ins.Owner = this;

                for (int j = 0; j < ins.Regions.Count; j++)
                {
                    DlsRegion rgn = ins.Regions[j];
                    rgn.Wave = (rgn.WaveIndex >= 0 && rgn.WaveIndex < Waves.Count) ? Waves[rgn.WaveIndex] : null;

                    DlsSampleInfo info = rgn.Info;
                    if (info == null && rgn.Wave != null) info = rgn.Wave.Info;
                    rgn.ResolvedInfo = info != null ? info : new DlsSampleInfo();

                    // Region articulation layers over the instrument's rather than
                    // replacing it: a region block that sets only pan would otherwise
                    // discard the instrument's envelope and leave notes holding flat.
                    DlsArticulation art;
                    if (rgn.Articulation != null && ins.Articulation != null)
                        art = rgn.Articulation.MergeOver(ins.Articulation);
                    else if (rgn.Articulation != null) art = rgn.Articulation;
                    else art = ins.Articulation;

                    rgn.ResolvedArticulation = art != null ? art : DlsArticulation.Default;
                }
            }
        }

        void BuildLookup()
        {
            _lookup = new Dictionary<int, DlsInstrument>();
            for (int i = 0; i < Instruments.Count; i++)
            {
                DlsInstrument ins = Instruments[i];
                int key = Key(ins.Bank, ins.Program, ins.IsDrum);
                if (!_lookup.ContainsKey(key)) _lookup[key] = ins;
            }
        }

        static int Key(int bank, int program, bool drum)
        {
            return (drum ? 1 << 24 : 0) | (bank << 8) | (program & 0x7F);
        }

        /// <summary>
        /// Exact bank/program/drum match only, with no fallbacks. Used by
        /// <see cref="DlsBankStack"/> so a lower layer isn't beaten by a higher
        /// layer's approximate match.
        /// </summary>
        public DlsInstrument FindExact(int bank, int program, bool drum)
        {
            if (_lookup == null) BuildLookup();
            DlsInstrument ins;
            return _lookup.TryGetValue(Key(bank, program, drum), out ins) ? ins : null;
        }

        /// <summary>The first instrument in the collection, or null if it has none.</summary>
        public DlsInstrument FirstInstrument
        {
            get { return Instruments.Count > 0 ? Instruments[0] : null; }
        }

        /// <summary>Any drum kit in the collection, so a percussion request never silently
        /// resolves to a melodic instrument while a kit is available.</summary>
        public DlsInstrument FirstDrumInstrument
        {
            get
            {
                for (int i = 0; i < Instruments.Count; i++)
                    if (Instruments[i].IsDrum) return Instruments[i];
                return null;
            }
        }

        /// <summary>
        /// Finds the best instrument for a bank/program pair, falling back to bank 0
        /// and then to any instrument so a sparse custom DLS still makes sound.
        /// </summary>
        public DlsInstrument FindInstrument(int bank, int program, bool drum)
        {
            if (_lookup == null) BuildLookup();
            DlsInstrument ins;
            if (_lookup.TryGetValue(Key(bank, program, drum), out ins)) return ins;
            if (_lookup.TryGetValue(Key(0, program, drum), out ins)) return ins;
            if (drum)
            {
                // A custom DLS may have put its drum kit in a melodic slot.
                if (_lookup.TryGetValue(Key(bank, program, false), out ins)) return ins;
                if (_lookup.TryGetValue(Key(0, 0, true), out ins)) return ins;
            }
            if (_lookup.TryGetValue(Key(0, 0, false), out ins)) return ins;
            return Instruments.Count > 0 ? Instruments[0] : null;
        }

        /// <summary>Region wsmp overrides wave wsmp; otherwise sensible defaults.</summary>
        public DlsSampleInfo GetSampleInfo(DlsRegion region)
        {
            return region.ResolvedInfo;
        }

        public DlsArticulation GetArticulation(DlsInstrument instrument, DlsRegion region)
        {
            if (region != null && region.Articulation != null) return region.Articulation;
            if (instrument != null && instrument.Articulation != null) return instrument.Articulation;
            return DlsArticulation.Default;
        }

        // ---------------------------------------------------------- helpers

        static string ReadFourCC(BinaryReader r)
        {
            byte[] b = r.ReadBytes(4);
            if (b.Length < 4) return null;
            return Encoding.ASCII.GetString(b);
        }

        static string ParseInfoName(BinaryReader r, Stream s, long end)
        {
            string name = null;
            while (s.Position + 8 <= end)
            {
                string id = ReadFourCC(r);
                if (id == null) break;
                uint size = r.ReadUInt32();
                long next = s.Position + size + (size & 1);
                if (next > end) next = end;

                if (id == "INAM")
                {
                    byte[] b = r.ReadBytes((int)size);
                    int len = Array.IndexOf(b, (byte)0);
                    if (len < 0) len = b.Length;
                    name = Encoding.ASCII.GetString(b, 0, len);
                }

                s.Position = next;
            }
            return name;
        }

        static float Clamp(float v, float lo, float hi)
        {
            return v < lo ? lo : (v > hi ? hi : v);
        }

        public string Dump()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("DLS: " + (Name ?? "<unnamed>"));
            sb.AppendLine("Waves: " + Waves.Count + "   Instruments: " + Instruments.Count);
            sb.AppendLine("Articulation: art1=" + Art1Count + " art2=" + Art2Count +
                          ", " + ArticulationConnections.Count + " distinct connection block(s)" +
                          " - call DescribeArticulation() for detail");
            for (int i = 0; i < Instruments.Count && i < 64; i++)
                sb.AppendLine("  " + Instruments[i]);
            if (Instruments.Count > 64) sb.AppendLine("  ...");
            return sb.ToString();
        }
    }
}
