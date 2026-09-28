// DmPerformance.cs — plays a DirectMusic style through DlsSynth.
//
// The loop, once per pattern boundary:
//   1. read the groove level (from the segment's command track, or set directly)
//   2. pick a pattern whose groove range covers it, filtered by pending embellishment
//   3. for each part reference in that pattern, pick a variation
//   4. expand the part's notes, resolving chord-relative music values into pitches
//   5. schedule note on/off against the sample clock
//
// Step 5 is sample-accurate. Step 4 is the approximate one — see MusicValueResolver.
//
// Part of DirectMusicLite.

using System;
using System.Collections.Generic;

namespace DirectMusicLite
{
    /// <summary>
    /// DMUS_COMPOSEF flags, as passed to AutoTransition. Verify the numeric values against
    /// your dmusici.h — these are reconstructed, though MEASURE = 0x20 is confirmed.
    /// </summary>
    [Flags]
    public enum DmComposeFlags
    {
        None = 0,
        Align = 0x1,
        Overlap = 0x2,
        Immediate = 0x4,
        Grid = 0x8,
        Beat = 0x10,
        Measure = 0x20,
        End = 0x40,
        Intro = 0x80,
        Fill = 0x100,
        Transition = 0x200,
        /// <summary>Chordmap-composed key modulation. Not implemented; ignored with a note.</summary>
        Modulate = 0x400,
        Long = 0x800,
        EntireTransition = 0x1000,
        OneBarTransition = 0x2000
    }

    /// <summary>Where playback begins, mirroring Producer's Play versus Play At Start.</summary>
    public enum DmStartPosition
    {
        /// <summary>Producer's Play: resume wherever the clock already is.</summary>
        Current,
        /// <summary>The segment's mtPlayStart.</summary>
        SegmentStart,
        /// <summary>Producer's Play At Start: the segment's mtLoopStart.</summary>
        LoopStart,
        /// <summary>An explicit MUSIC_TIME passed to StartAt.</summary>
        Custom
    }

    /// <summary>
    /// How a music value's chord and scale position fields combine. DirectMusic's exact
    /// rule isn't something this implementation can verify, so the plausible readings are
    /// selectable and can be compared by ear against Producer.
    /// </summary>
    public enum DmMusicValueMode
    {
        /// <summary>Chord tone from the chord field, then scale steps from the scale field.</summary>
        ChordThenScale,
        /// <summary>Always start at a chord tone, then move by scale steps, in every play mode.</summary>
        ChordToneThenScaleSteps,
        /// <summary>Use only the scale field; ignore the chord field entirely.</summary>
        ScalePositionOnly,
        /// <summary>Use only the chord field; ignore the scale field entirely.</summary>
        ChordPositionOnly
    }

    /// <summary>
    /// Which field of a pattern's part reference identifies the band channel.
    /// Producer versions disagree, and picking the wrong one plays every part through
    /// the wrong instrument while the timing stays correct.
    /// </summary>
    public enum DmPartChannelSource
    {
        /// <summary>Use dwPChannel, falling back to wLogicalPartID when absent.</summary>
        PChannel,
        /// <summary>Always use wLogicalPartID.</summary>
        LogicalPartId,
        /// <summary>Assign in the order parts appear, ignoring both fields.</summary>
        PartOrder
    }

    /// <summary>
    /// Converts a style note's chord-relative music value into a MIDI note number.
    /// Return -1 to suppress the note. Assign <see cref="DmPerformance.MusicValueResolver"/>
    /// to replace the built-in rule without touching the engine.
    /// </summary>
    public delegate int MusicValueResolver(int musicValue, int playModeFlags, DmSubChord chord);

    public sealed class DmPerformance
    {
        public DmStyle Style;
        public DmSegment Segment;

        /// <summary>1..100. Ignored while <see cref="FollowSegmentCommands"/> is on and a segment is loaded.</summary>
        public int GrooveLevel
        {
            get { return _grooveLevel; }
            set { _grooveLevel = value < 1 ? 1 : (value > 100 ? 100 : value); }
        }

        /// <summary>When true, the segment's command track drives the groove level and embellishments.</summary>
        public bool FollowSegmentCommands = true;

        /// <summary>Applied after the built-in or custom resolver, in semitones.</summary>
        public int Transpose;

        /// <summary>Swap in your own music-value rule if the built-in one mis-voices your content.</summary>
        public MusicValueResolver MusicValueResolver;

        /// <summary>How the chord and scale fields of a music value combine.</summary>
        public DmMusicValueMode MusicValueMode = DmMusicValueMode.ChordThenScale;

        /// <summary>Octaves added after resolving, for content that sits an octave out.</summary>
        public int OctaveOffset;

        /// <summary>
        /// Used when the segment has no chord track, or has one that didn't parse.
        /// Without this a chord-relative music value has nothing to resolve against and
        /// collapses to a meaningless pitch, which sounds like everything being wrong.
        /// Defaults to C major.
        /// </summary>
        public DmSubChord DefaultChord = MakeDefaultChord();

        public static DmSubChord MakeDefaultChord()
        {
            DmSubChord c = new DmSubChord();
            c.ChordRoot = 0;                 // C
            c.ScaleRoot = 0;
            // Four tones, not three. Chord position 3 is used heavily in real content,
            // and against a three-tone chord it wraps to the root — so a rising line like
            // (octave 2, tone 3) -> (octave 3, tone 0) resolves to the same pitch twice
            // and the climb stalls. Measured across two shipped styles, four tones cut
            // non-rising steps in rising lines from 3.8% to 3.1% and 1.6% to 1.2%.
            c.ChordPattern = 0x891;          // bits 0,4,7,11 - root, third, fifth, seventh
            c.ScalePattern = 0xAB5;          // bits 0,2,4,5,7,9,11 - major scale
            c.Levels = 0xFFFFFFFF;
            return c;
        }

        /// <summary>True when notes are resolving against DefaultChord rather than the segment.</summary>
        public bool UsingDefaultChord { get; private set; }

        /// <summary>How a part reference is matched to a band channel.</summary>
        public DmPartChannelSource PartChannelSource = DmPartChannelSource.PChannel;

        /// <summary>
        /// Ignore the bank select bits in band patches and look everything up in bank 0.
        /// Useful when a band's bank numbering doesn't match how the DLS was authored.
        /// </summary>
        public bool IgnoreBankSelect;

        public bool IsPlaying { get { return _playing; } }
        public DmPattern CurrentPattern { get { return _pattern; } }
        public int CurrentMeasure { get { return _measure; } }
        public double CurrentTempo { get { return _bpm; } }
        public DmEmbellishment PendingEmbellishment { get { return _pending; } }

        /// <summary>True if the content needs more than 16 PChannels, so some now share a channel.</summary>
        public bool ChannelsExhausted { get; private set; }

        readonly DlsSynth _synth;
        readonly int _sampleRate;
        readonly Random _rng;

        int _grooveLevel = 50;
        bool _playing;
        bool _hasStarted;
        bool _hasExpanded;
        double _musicTime;                 // MUSIC_TIME ticks, fractional
        double _bpm = 120.0;
        DmTimeSig _timeSig = DmTimeSig.Default;

        DmPattern _pattern;
        /// <summary>Tick the current pattern began at. Exposed for diagnostics.</summary>
        public int PatternStartTick { get { return _patternStartTick; } }
        int _patternStartTick;
        int _patternEndTick;
        int _measure;
        DmEmbellishment _pending = DmEmbellishment.Normal;
        bool _stopAfterPattern;

        readonly List<ScheduledNote> _scheduled = new List<ScheduledNote>();
        readonly List<ScheduledNote> _carryOver = new List<ScheduledNote>();
        int _eventIndex;

        readonly Dictionary<int, int> _pchannelToMidi = new Dictionary<int, int>();
        readonly Dictionary<int, DmBandInstrument> _channelInstrument = new Dictionary<int, DmBandInstrument>();
        readonly Dictionary<int, DmBandInstrument> _overrides = new Dictionary<int, DmBandInstrument>();
        readonly Dictionary<int, string> _pchannelParts = new Dictionary<int, string>();
        readonly Dictionary<int, string> _pchannelPartDetail = new Dictionary<int, string>();
        readonly Dictionary<int, int> _pchannelVariation = new Dictionary<int, int>();
        readonly List<int> _partOrder = new List<int>();

        DmSegment _pendingSegment;
        DmStyle _pendingStyle;
        DmComposeFlags _pendingFlags;
        DmEmbellishment _pendingTransitionEmbellishment = DmEmbellishment.Normal;
        int _switchTick = int.MaxValue;
        bool _transitionPlayed;
        bool _composeTransition;
        DmPattern _transitionPattern;

        int _loopsPlayed;
        int _nextMeasureTick;
        int _patternGroove = -1;      // groove level the current pattern was chosen for
        int _commandIndex;            // next unconsumed command-track event

        /// <summary>
        /// Where a segment arriving via a transition begins. DirectMusic starts a segment
        /// at mtPlayStart, but content that relies on its loop point wants LoopStart, so
        /// this follows whatever the player is configured to use for Play.
        /// </summary>
        public DmStartPosition SegmentEntryPosition = DmStartPosition.SegmentStart;

        /// <summary>
        /// Render controller curves authored in style parts (pan moves, volume swells,
        /// pitch bends). Turn off to hear the notes without them.
        /// </summary>
        public bool RenderCurves = true;

        /// <summary>
        /// Fold resolved notes back into the register a style part declares with
        /// bInvertUpper / bInvertLower. Without it a chord-relative note can resolve well
        /// outside the range the part was written for.
        /// </summary>
        public bool ApplyPartInversion = true;

        /// <summary>Honour the segment's loop points and repeat count.</summary>
        public bool RespectSegmentLoop = true;

        /// <summary>Completed loops so far.</summary>
        public int LoopsPlayed { get { return _loopsPlayed; } }

        /// <summary>Current position on the segment's timeline, in MUSIC_TIME ticks.</summary>
        public int SegmentPositionTicks { get { return (int)_musicTime; } }

        /// <summary>Raised when a non-repeating segment reaches its end. Audio thread.</summary>
        public event Action SegmentFinished;

        /// <summary>Fires on the audio thread when a queued segment actually starts.</summary>
        public event Action SegmentSwitched;

        /// <summary>True while a queued segment is waiting for its boundary.</summary>
        public bool HasPendingSegment { get { return _pendingSegment != null || _pendingStyle != null; } }
        /// <summary>The transition pattern being played before the switch, if any.</summary>
        public DmPattern TransitionPattern { get { return _transitionPattern; } }
        sealed class VariationState
        {
            public int Cursor;
            public int Last = -1;
            public List<int> Row;
            public int RowIndex;
        }

        readonly Dictionary<int, VariationState> _variationState = new Dictionary<int, VariationState>();
        /// <summary>
        /// Variation picked for a (lock group, repetition start tick) inside the pattern
        /// being expanded.
        /// Parts sharing a variation lock id read the same entry rather than each advancing
        /// the group's cursor, which is what keeps them locked together.
        /// </summary>
        readonly Dictionary<long, int> _measureVariation = new Dictionary<long, int>();
        int _nextMidiChannel;

        enum ScheduledKind { NoteOn, NoteOff, Controller, PitchBend }

        struct ScheduledNote
        {
            public int Tick;
            public ScheduledKind Kind;
            public byte Channel, Note, Velocity;
            public int Value;            // controller value, or 14-bit pitch bend

            public bool On { get { return Kind == ScheduledKind.NoteOn; } }
        }

        public DmPerformance(DlsSynth synth, int sampleRate, int seed)
        {
            _synth = synth;
            _sampleRate = sampleRate > 0 ? sampleRate : 44100;
            _rng = new Random(seed);
        }

        // ------------------------------------------------------------ transport

        /// <summary>Starts at the segment's play start.</summary>
        public void Start()
        {
            StartAt(ResolvePosition(DmStartPosition.SegmentStart, 0));
        }

        /// <summary>Starts at the position implied by <paramref name="position"/>.</summary>
        public void StartFrom(DmStartPosition position)
        {
            StartAt(ResolvePosition(position, (int)_musicTime));
        }

        /// <summary>
        /// Resolves a start position into a MUSIC_TIME. Segment tracks are read at that
        /// tick, so starting at the loop point applies the groove, chord and band in
        /// effect there rather than whatever was authored at tick zero.
        /// </summary>
        public int ResolvePosition(DmStartPosition position, int customTick)
        {
            switch (position)
            {
                case DmStartPosition.Current: return (int)_musicTime;
                case DmStartPosition.LoopStart: return Segment != null ? Segment.ResolvedLoopStart : 0;
                case DmStartPosition.Custom: return customTick;
                default: return Segment != null ? Segment.ResolvedPlayStart : 0;
            }
        }

        public void StartAt(int startTick)
        {
            if (Style == null || Style.Patterns.Count == 0) return;
            if (startTick < 0) startTick = 0;

            _musicTime = startTick;
            _measure = 0;
            _eventIndex = 0;
            _scheduled.Clear();
            _pattern = null;
            _patternStartTick = startTick;
            _patternEndTick = startTick;
            _nextMeasureTick = startTick;
            _patternGroove = -1;
            _commandIndex = 0;
            _loopsPlayed = 0;
            _stopAfterPattern = false;
            _pending = DmEmbellishment.Normal;
            _pendingSegment = null;
            _pendingStyle = null;
            _switchTick = int.MaxValue;
            _transitionPlayed = false;
            _transitionPattern = null;
            _variationState.Clear();
            _measureVariation.Clear();
            _pchannelToMidi.Clear();
            _channelInstrument.Clear();
            _pchannelParts.Clear();
            _pchannelPartDetail.Clear();
            _pchannelVariation.Clear();
            _partOrder.Clear();
            _nextMidiChannel = 0;
            ChannelsExhausted = false;

            // Read the segment's tracks at the start tick, not at zero.
            _bpm = Segment != null && Segment.Tempos.Count > 0 ? Segment.TempoAt(startTick) : Style.Tempo;
            _timeSig = Segment != null && Segment.TimeSignatures.Count > 0
                ? Segment.TimeSigAt(startTick) : Style.TimeSig;
            if (!_timeSig.IsValid) _timeSig = DmTimeSig.Default;

            SeekCommands(startTick);
            ApplyBand(startTick);
            _hasStarted = true;
            _hasExpanded = false;
            _playing = true;
        }

        public void Stop()
        {
            _playing = false;
            _scheduled.Clear();
            _carryOver.Clear();
            _eventIndex = 0;
            _synth.AllNotesOff();
        }

        /// <summary>
        /// Equivalent of IDirectMusicComposer::AutoTransition. Composes a transition into
        /// the destination segment using the outgoing style's patterns, then switches.
        ///
        /// wCommand maps to <paramref name="command"/>; dwFlags to <paramref name="flags"/>.
        /// A call of AutoTransition(seg, 0, DMUS_COMPOSEF_MEASURE) is
        /// AutoTransition(seg, style, DmCommandType.Groove, DmComposeFlags.Measure).
        /// </summary>
        public void AutoTransition(DmSegment toSegment, DmStyle toStyle,
                                   DmCommandType command, DmComposeFlags flags)
        {
            DmEmbellishment embellishment = DmEmbellishment.Normal;
            if ((flags & DmComposeFlags.Fill) != 0) embellishment = DmEmbellishment.Fill;
            else if ((flags & DmComposeFlags.Intro) != 0) embellishment = DmEmbellishment.Intro;
            else if ((flags & DmComposeFlags.End) != 0) embellishment = DmEmbellishment.End;
            else if (command == DmCommandType.Fill) embellishment = DmEmbellishment.Fill;
            else if (command == DmCommandType.Intro) embellishment = DmEmbellishment.Intro;
            else if (command == DmCommandType.Break) embellishment = DmEmbellishment.Break;
            else if (command == DmCommandType.End) embellishment = DmEmbellishment.End;

            QueueSegment(toSegment, toStyle, flags, embellishment);
        }

        /// <summary>Queues a segment switch at the boundary implied by the flags.</summary>
        public void QueueSegment(DmSegment toSegment, DmStyle toStyle, DmComposeFlags flags)
        {
            QueueSegment(toSegment, toStyle, flags, DmEmbellishment.Normal);
        }

        void QueueSegment(DmSegment toSegment, DmStyle toStyle, DmComposeFlags flags,
                          DmEmbellishment embellishment)
        {
            if (toSegment == null && toStyle == null) return;

            _pendingSegment = toSegment;
            _pendingStyle = toStyle;
            _pendingFlags = flags;
            _pendingTransitionEmbellishment = embellishment;
            _transitionPlayed = false;
            _transitionPattern = null;
            _composeTransition = WantsComposedTransition(flags, embellishment);

            if (!_playing)
            {
                // Nothing running: adopt it immediately rather than waiting for a boundary
                // that will never arrive.
                ApplyPendingSegment((int)_musicTime);
                return;
            }

            _switchTick = NextBoundaryTick(flags);
        }

        /// <summary>
        /// Whether to compose a transition bar before switching. DMUS_COMPOSEF_MEASURE on
        /// its own only sets the boundary — it asks for an aligned switch, not for an
        /// extra bar of music. A transition is composed only when the flags or the command
        /// actually ask for one.
        /// </summary>
        static bool WantsComposedTransition(DmComposeFlags flags, DmEmbellishment embellishment)
        {
            const DmComposeFlags transitionFlags = DmComposeFlags.Transition
                                                 | DmComposeFlags.OneBarTransition
                                                 | DmComposeFlags.EntireTransition;
            if ((flags & transitionFlags) != 0) return true;
            return embellishment != DmEmbellishment.Normal;
        }

        int NextBoundaryTick(DmComposeFlags flags)
        {
            int now = (int)Math.Ceiling(_musicTime);

            if ((flags & DmComposeFlags.Immediate) != 0) return now;

            DmTimeSig ts = _timeSig.IsValid ? _timeSig : DmTimeSig.Default;
            int unit;
            if ((flags & DmComposeFlags.Grid) != 0) unit = Math.Max(1, ts.TicksPerGrid);
            else if ((flags & DmComposeFlags.Beat) != 0) unit = Math.Max(1, ts.TicksPerBeat);
            else if ((flags & DmComposeFlags.Measure) != 0) unit = Math.Max(1, ts.TicksPerMeasure);
            else return _patternEndTick;                 // default: the next pattern boundary

            // Boundaries sit on the segment's own grid, so a measure line is a measure of
            // the music rather than an offset from whenever the current pattern started.
            int steps = now / unit + 1;
            return steps * unit;
        }

        /// <summary>Queues an embellishment for the next pattern boundary.</summary>
        public void QueueEmbellishment(DmEmbellishment embellishment)
        {
            _pending = embellishment;
            if (embellishment == DmEmbellishment.End) _stopAfterPattern = true;
        }

        // -------------------------------------------------------------- render

        public void Render(float[] buffer, int offset, int frames, int channels)
        {
            if (!_playing || Style == null)
            {
                _synth.Render(buffer, offset, frames, channels);
                return;
            }

            int done = 0;
            int guard = 0;

            while (done < frames && guard++ < 4096)
            {
                double ticksPerFrame = TicksPerFrame();
                double now = _musicTime + done * ticksPerFrame;

                if (RespectSegmentLoop && Segment != null && Segment.HasLoopPoints &&
                    !HasPendingSegment && now >= Segment.ResolvedLoopEnd)
                {
                    if (!HandleSegmentEnd(done, TicksPerFrame())) break;
                    continue;
                }

                if (HasPendingSegment && now >= _switchTick)
                {
                    HandlePendingSwitch((int)_switchTick);
                    continue;
                }

                if (_pattern != null && now >= _nextMeasureTick && _nextMeasureTick < _patternEndTick)
                {
                    HandleMeasureBoundary(_nextMeasureTick);
                    continue;
                }

                if (_pattern == null || now >= _patternEndTick)
                {
                    if (!AdvancePattern(now)) { _synth.Render(buffer, offset + done * channels, frames - done, channels); done = frames; break; }
                    continue;
                }

                if (_eventIndex < _scheduled.Count && _scheduled[_eventIndex].Tick <= now)
                {
                    Fire(_scheduled[_eventIndex]);
                    _eventIndex++;
                    continue;
                }

                double nextTick = _patternEndTick;
                if (_nextMeasureTick > now && _nextMeasureTick < nextTick) nextTick = _nextMeasureTick;
                if (_eventIndex < _scheduled.Count && _scheduled[_eventIndex].Tick < nextTick)
                    nextTick = _scheduled[_eventIndex].Tick;
                if (HasPendingSegment && _switchTick < nextTick) nextTick = _switchTick;
                if (RespectSegmentLoop && Segment != null && Segment.HasLoopPoints &&
                    Segment.ResolvedLoopEnd < nextTick) nextTick = Segment.ResolvedLoopEnd;

                int framesUntil = (int)Math.Ceiling((nextTick - now) / ticksPerFrame);
                if (framesUntil < 1) framesUntil = 1;

                int chunk = frames - done;
                if (framesUntil < chunk) chunk = framesUntil;

                _synth.Render(buffer, offset + done * channels, chunk, channels);
                done += chunk;
            }

            if (done < frames)
                _synth.Render(buffer, offset + done * channels, frames - done, channels);

            _musicTime += frames * TicksPerFrame();
        }

        double TicksPerFrame()
        {
            double ticksPerSecond = _bpm / 60.0 * DmRiff.PPQ;
            return ticksPerSecond / _sampleRate;
        }

        void Fire(ScheduledNote e)
        {
            switch (e.Kind)
            {
                case ScheduledKind.NoteOn: _synth.NoteOn(e.Channel, e.Note, e.Velocity); break;
                case ScheduledKind.NoteOff: _synth.NoteOff(e.Channel, e.Note); break;
                case ScheduledKind.Controller: _synth.ControlChange(e.Channel, e.Note, e.Value); break;
                case ScheduledKind.PitchBend: _synth.PitchBend(e.Channel, e.Value); break;
            }
        }

        /// <summary>
        /// Reached the loop end. Returns false when the segment is finished and rendering
        /// should stop for this buffer.
        /// </summary>
        bool HandleSegmentEnd(int done, double ticksPerFrame)
        {
            bool more = Segment.RepeatsForever || _loopsPlayed < Segment.Repeats;
            if (!more)
            {
                Stop();
                if (SegmentFinished != null) SegmentFinished();
                return false;
            }

            _loopsPlayed++;

            FlushPendingState();
            _scheduled.Clear();
            _carryOver.Clear();
            _eventIndex = 0;
            _synth.AllNotesOff();

            // Rewind so that the position recomputed inside the loop lands exactly on the
            // loop start for the current sample offset.
            int loopStart = Segment.ResolvedLoopStart;
            _musicTime = loopStart - done * ticksPerFrame;

            _pattern = null;
            _patternStartTick = loopStart;
            _patternEndTick = loopStart;
            _nextMeasureTick = loopStart;
            _patternGroove = -1;
            _measure = 0;

            SeekCommands(loopStart);

            double bpm = Segment.TempoAt(loopStart);
            if (bpm > 1.0) _bpm = bpm;
            ApplyBand(loopStart);
            return true;
        }

        /// <summary>
        /// Moves everything the pattern scheduled but didn't reach into the next pattern,
        /// keeping absolute ticks so it fires when it was meant to.
        ///
        /// Notes are authored longer than the pattern that starts them — one shipped intro
        /// holds a note more than two bars past its own pattern — so firing those note-offs
        /// at the boundary chops the note. Carrying them keeps the note ringing and still
        /// releases it on time, and carrying pending controller events keeps a curve's
        /// reset or a pedal-up from being lost.
        /// </summary>
        void CarryPendingForward()
        {
            _carryOver.Clear();
            for (int i = _eventIndex; i < _scheduled.Count; i++)
                if (_scheduled[i].Kind != ScheduledKind.NoteOn) _carryOver.Add(_scheduled[i]);
            _eventIndex = _scheduled.Count;
        }

        /// <summary>
        /// Applies pending non-note-on events immediately. Used where playback is being
        /// cut anyway — a segment switch or the end of a loop — so nothing is stranded.
        /// </summary>
        void FlushPendingState()
        {
            while (_eventIndex < _scheduled.Count)
            {
                ScheduledNote e = _scheduled[_eventIndex];
                if (e.Kind != ScheduledKind.NoteOn) Fire(e);
                _eventIndex++;
            }
        }

        void HandlePendingSwitch(int tick)
        {
            bool overlap = (_pendingFlags & DmComposeFlags.Overlap) != 0;

            // Release what's sounding. With OVERLAP the tails ring into the new segment;
            // without it they're released now, which still decays rather than cutting.
            FlushPendingState();
            _scheduled.Clear();
            _carryOver.Clear();
            _eventIndex = 0;
            if (!overlap) _synth.AllNotesOff();

            // A transition measure from the outgoing style, chosen by destination groove.
            // Only when asked for: a bare boundary flag means switch at that boundary, and
            // inserting a bar of the outgoing style would sound like the current segment
            // restarting before the new one arrives.
            if (_composeTransition && !_transitionPlayed)
            {
                _transitionPlayed = true;
                DmPattern transition = ChooseTransitionPattern();
                if (transition != null)
                {
                    _transitionPattern = transition;
                    _pattern = transition;
                    _patternStartTick = tick;

                    DmTimeSig ts = transition.TimeSig.IsValid ? transition.TimeSig : _timeSig;
                    int measures = Math.Max(1, transition.Measures);
                    if ((_pendingFlags & DmComposeFlags.OneBarTransition) != 0) measures = 1;
                    _patternEndTick = tick + ts.TicksPerMeasure * measures;
                    ExpandPattern(transition, tick, ts);

                    _switchTick = _patternEndTick;      // switch once the transition finishes
                    return;
                }
            }

            ApplyPendingSegment(tick);
        }

        /// <summary>
        /// True when the flags ask for a composed transition bar. Boundary-only flags
        /// (Immediate/Grid/Beat/Measure) switch straight to the new segment.
        /// </summary>
        DmPattern ChooseTransitionPattern()
        {
            if (Style == null || _pendingSegment == null) return null;

            int toGroove = _pendingSegment.Commands.Count > 0
                ? _pendingSegment.GrooveLevelAt(0) : _grooveLevel;

            List<DmPattern> candidates =
                Style.PatternsForTransition(_grooveLevel, toGroove, _pendingTransitionEmbellishment);

            // An explicit embellishment that nothing matches falls back to an ordinary
            // destination-matched pattern rather than dropping the transition.
            if (candidates.Count == 0 && _pendingTransitionEmbellishment != DmEmbellishment.Normal)
                candidates = Style.PatternsForTransition(_grooveLevel, toGroove, DmEmbellishment.Normal);

            if (candidates.Count == 0) return null;
            return candidates[_rng.Next(candidates.Count)];
        }

        void ApplyPendingSegment(int tick)
        {
            if (_pendingSegment != null) Segment = _pendingSegment;
            if (_pendingStyle != null) Style = _pendingStyle;

            bool align = (_pendingFlags & DmComposeFlags.Align) != 0;

            _pendingSegment = null;
            _pendingStyle = null;
            _switchTick = int.MaxValue;
            _transitionPlayed = false;
            _composeTransition = false;
            _transitionPattern = null;

            // Variation state belongs to the old style; channel routing does not, so the
            // PChannel map is kept and the new band merges into it.
            _variationState.Clear();
            _measureVariation.Clear();
            _partOrder.Clear();
            _measure = 0;
            _pattern = null;
            _scheduled.Clear();
            _eventIndex = 0;

            if (Segment != null)
            {
                SeekCommands(Segment.ResolvedPlayStart);

                double bpm = Segment.TempoAt(0);
                if (bpm > 1.0) _bpm = bpm;
                DmTimeSig ts = Segment.TimeSigAt(0);
                if (ts.IsValid) _timeSig = ts;
                ApplyBand(0);
            }
            else if (Style != null && Style.Tempo > 1.0)
            {
                _bpm = Style.Tempo;
            }

            // Without ALIGN the new segment starts at the boundary; with it, it keeps the
            // outgoing segment's position within the measure.
            _loopsPlayed = 0;
            if (Segment != null && !align)
            {
                // The incoming segment's tracks are indexed from its own play start, so the
                // clock moves there; without it the new segment reads at the outgoing
                // segment's position and picks up the wrong groove and chord.
                int start = ResolvePosition(SegmentEntryPosition, Segment.ResolvedPlayStart);
                _musicTime = start;
                tick = start;
            }

            _patternStartTick = tick;
            _patternEndTick = tick;
            _nextMeasureTick = tick;
            _patternGroove = -1;
            _pending = DmEmbellishment.Normal;
            _stopAfterPattern = false;

            if (SegmentSwitched != null) SegmentSwitched();
        }

        // ---------------------------------------------------- pattern selection

        /// <summary>
        /// A measure line inside a pattern. Commands due here are applied, and if they
        /// change the groove level or queue an embellishment the pattern is re-chosen
        /// straight away. Waiting for the pattern to end instead makes a groove change
        /// take as long as the pattern — over a minute for a 96-measure one — which reads
        /// as the music refusing to react until every note of the old pattern has played.
        /// </summary>
        void HandleMeasureBoundary(int tick)
        {
            if (FollowSegmentCommands) ApplyDueCommands(tick);

            if (_grooveLevel != _patternGroove || _pending != DmEmbellishment.Normal)
            {
                AdvancePattern(tick);
                return;
            }

            int measureTicks = CurrentMeasureTicks();
            _nextMeasureTick = tick + measureTicks;
            if (_nextMeasureTick > _patternEndTick) _nextMeasureTick = _patternEndTick;
        }

        int CurrentMeasureTicks()
        {
            DmTimeSig ts = _pattern != null && _pattern.TimeSig.IsValid ? _pattern.TimeSig : _timeSig;
            if (!ts.IsValid) ts = DmTimeSig.Default;
            int ticks = ts.TicksPerMeasure;
            return ticks > 0 ? ticks : DmRiff.TicksPerMeasure(4, 4);
        }

        bool AdvancePattern(double now)
        {

            CarryPendingForward();

            if (_stopAfterPattern) { Stop(); return false; }

            // Starting point: the requested tick when re-choosing mid-pattern, otherwise
            // the end of the pattern that just finished.
            int startTick = _pattern == null ? (int)now
                          : (now < _patternEndTick ? (int)now : _patternEndTick);
            _measure = _pattern == null ? 0 : _measure + 1;

            if (Segment != null)
            {
                if (FollowSegmentCommands) ApplyDueCommands(startTick);
                double bpm = Segment.TempoAt(startTick);
                if (bpm > 1.0) _bpm = bpm;
                DmTimeSig ts = Segment.TimeSigAt(startTick);
                if (ts.IsValid) _timeSig = ts;
                ApplyBand(startTick);
            }

            DmPattern chosen = ChoosePattern();
            if (chosen == null) { Stop(); return false; }

            _pattern = chosen;
            _patternStartTick = startTick;
            _patternGroove = _grooveLevel;

            DmTimeSig patternTs = chosen.TimeSig.IsValid ? chosen.TimeSig : _timeSig;
            _patternEndTick = startTick + patternTs.TicksPerMeasure * Math.Max(1, chosen.Measures);
            _nextMeasureTick = startTick + patternTs.TicksPerMeasure;
            if (_nextMeasureTick > _patternEndTick) _nextMeasureTick = _patternEndTick;

            ExpandPattern(chosen, startTick, patternTs);
            _hasExpanded = true;
            _pending = DmEmbellishment.Normal;
            return true;
        }

        /// <summary>
        /// Applies every command-track event due at or before a tick, once each. Commands
        /// are authored at measure resolution, so they must be consumed as measures pass
        /// rather than only when a pattern ends — a segment with a groove change every
        /// four measures gets none of them applied inside a 96-measure pattern otherwise.
        /// </summary>
        void ApplyDueCommands(int tick)
        {
            if (Segment == null) return;

            while (_commandIndex < Segment.Commands.Count && Segment.Commands[_commandIndex].Time <= tick)
            {
                DmCommand c = Segment.Commands[_commandIndex];
                _commandIndex++;

                switch (c.Command)
                {
                    case DmCommandType.Groove: _grooveLevel = Clamp(c.GrooveLevel, 1, 100); break;
                    case DmCommandType.Fill: _pending = DmEmbellishment.Fill; break;
                    case DmCommandType.Intro: _pending = DmEmbellishment.Intro; break;
                    case DmCommandType.Break: _pending = DmEmbellishment.Break; break;
                    case DmCommandType.End: _pending = DmEmbellishment.End; _stopAfterPattern = true; break;
                    case DmCommandType.EndAndIntro: _pending = DmEmbellishment.End; break;
                }
            }
        }

        /// <summary>Rewinds the command pointer to a tick, applying everything up to it.</summary>
        void SeekCommands(int tick)
        {
            _commandIndex = 0;
            if (Segment == null) return;
            if (FollowSegmentCommands) ApplyDueCommands(tick);
            else
                while (_commandIndex < Segment.Commands.Count && Segment.Commands[_commandIndex].Time <= tick)
                    _commandIndex++;
        }

        DmPattern ChoosePattern()
        {
            List<DmPattern> candidates = Style.PatternsFor(_grooveLevel, _pending);

            // An embellishment with no matching pattern falls back to ordinary playback
            // rather than dropping a measure of music.
            if (candidates.Count == 0 && _pending != DmEmbellishment.Normal)
                candidates = Style.PatternsFor(_grooveLevel, DmEmbellishment.Normal);

            // Nothing covers this groove level: use the pattern whose range is nearest,
            // so an out-of-range level still plays instead of going silent.
            if (candidates.Count == 0 && Style.Patterns.Count > 0)
            {
                DmPattern best = null;
                int bestDistance = int.MaxValue;
                for (int i = 0; i < Style.Patterns.Count; i++)
                {
                    DmPattern p = Style.Patterns[i];
                    int d = _grooveLevel < p.GrooveBottom ? p.GrooveBottom - _grooveLevel
                          : (_grooveLevel > p.GrooveTop ? _grooveLevel - p.GrooveTop : 0);
                    if (d < bestDistance) { bestDistance = d; best = p; }
                }
                if (best != null) candidates.Add(best);
            }

            if (candidates.Count == 0) return null;
            if (candidates.Count == 1) return candidates[0];
            return candidates[_rng.Next(candidates.Count)];
        }

        // -------------------------------------------------------- note expansion

        void ExpandPattern(DmPattern pattern, int startTick, DmTimeSig patternTs)
        {
            _scheduled.Clear();
            _measureVariation.Clear();
            _eventIndex = 0;

            // Anything the previous pattern hadn't reached keeps its absolute tick and
            // fires during this one.
            if (_carryOver.Count > 0)
            {
                _scheduled.AddRange(_carryOver);
                _carryOver.Clear();
            }

            DmChordEvent chordEvent = Segment != null ? Segment.ChordAt(startTick) : null;
            UsingDefaultChord = chordEvent == null || chordEvent.SubChords.Count == 0;

            for (int i = 0; i < pattern.PartRefs.Count; i++)
            {
                DmPartRef pref = pattern.PartRefs[i];
                DmStylePart part;
                if (!Style.PartsById.TryGetValue(pref.PartId, out part)) continue;
                if (part.Notes.Count == 0) continue;

                int bandChannel = BandChannelFor(pref);
                int channel = AllocateChannel(bandChannel);
                string partLabel = part.Name;
                if (string.IsNullOrEmpty(partLabel))
                {
                    // Producer doesn't always name parts; fall back to its index in the
                    // style so it can be matched against DumpStyle() output.
                    int index = Style != null ? Style.Parts.IndexOf(part) : -1;
                    partLabel = index >= 0 ? "part " + index : "unnamed part";
                }
                _pchannelParts[bandChannel] = partLabel;
                _pchannelPartDetail[bandChannel] =
                    ((DmPlayMode)part.PlayModeFlags) + ", " + part.Notes.Count + " notes";
                DmSubChord chord = chordEvent != null ? chordEvent.ForLevel(pref.SubChordLevel) : null;
                if (chord == null) chord = DefaultChord;
                DmTimeSig partTs = part.TimeSig.IsValid ? part.TimeSig : patternTs;
                int ticksPerGrid = partTs.TicksPerGrid;

                // A part shorter than its pattern repeats to fill it: a four-measure drum
                // part in an eight-measure pattern plays twice. Scheduling it once leaves
                // the second half silent, which is heard as the drums stopping partway
                // through the pattern.
                int partTicks = partTs.TicksPerMeasure * Math.Max(1, part.Measures);
                int patternTicks = patternTs.TicksPerMeasure * Math.Max(1, pattern.Measures);
                int repeats = partTicks > 0 ? (patternTicks + partTicks - 1) / partTicks : 1;
                if (repeats < 1) repeats = 1;

                for (int rep = 0; rep < repeats; rep++)
                {
                int repeatOffset = rep * partTicks;
                if (repeatOffset >= patternTicks) break;

                // A variation is chosen once per repetition of the part, not once per
                // pattern and not once per measure. The part is the phrase: a one-measure
                // drum groove picks again every bar, while a two-measure bass line keeps
                // one variation across both of its measures. Choosing per measure would
                // splice the first half of one variation onto the second half of another.
                int variation = VariationForRepeat(part, pref, repeatOffset);
                uint variationBit = 1u << variation;
                if (rep == 0) _pchannelVariation[bandChannel] = variation;

                ExpandCurves(part, channel, startTick + repeatOffset, ticksPerGrid, variationBit);

                for (int n = 0; n < part.Notes.Count; n++)
                {
                    DmStyleNote note = part.Notes[n];
                    if ((note.VariationMask & variationBit) == 0) continue;

                    int playMode = EffectivePlayMode(note, part);

                    int midi = Resolve(note.MusicValue, playMode, chord);
                    midi = FoldIntoRange(midi, part);
                    if (midi < 0 || midi > 127) continue;

                    int onTick = startTick + repeatOffset + note.GridStart * ticksPerGrid + note.TimeOffset;
                    // A repeat only contributes notes that start inside the pattern.
                    if (onTick >= startTick + patternTicks) continue;
                    int offTick = onTick + Math.Max(1, note.Duration);

                    int velocity = note.Velocity;
                    if (note.VelocityRange > 0)
                        velocity += _rng.Next(-note.VelocityRange / 2, note.VelocityRange / 2 + 1);
                    velocity = Clamp(velocity, 1, 127);

                    ScheduledNote on = new ScheduledNote();
                    on.Tick = onTick; on.Kind = ScheduledKind.NoteOn;
                    on.Channel = (byte)channel; on.Note = (byte)midi; on.Velocity = (byte)velocity;
                    _scheduled.Add(on);

                    ScheduledNote off = new ScheduledNote();
                    off.Tick = offTick; off.Kind = ScheduledKind.NoteOff;
                    off.Channel = (byte)channel; off.Note = (byte)midi;
                    _scheduled.Add(off);
                }
                }
            }

            _scheduled.Sort(delegate (ScheduledNote a, ScheduledNote b)
            {
                if (a.Tick != b.Tick) return a.Tick < b.Tick ? -1 : 1;
                // At equal times: note-offs, then controllers, then note-ons. A pan or
                // volume move has to be in place before the note it applies to starts.
                int ra = Rank(a.Kind), rb = Rank(b.Kind);
                return ra == rb ? 0 : (ra < rb ? -1 : 1);
            });
        }

        /// <summary>
        /// Turns a controller curve into a series of discrete controller events. The synth
        /// smooths gain changes over about 11ms, so a sweep sampled every 1/32 note comes
        /// out continuous rather than stepped.
        /// </summary>
        void ExpandCurves(DmStylePart part, int channel, int startTick, int ticksPerGrid, uint variationBit)
        {
            if (!RenderCurves) return;

            for (int i = 0; i < part.Curves.Count; i++)
            {
                DmStyleCurve curve = part.Curves[i];
                if ((curve.VariationMask & variationBit) == 0) continue;

                bool isPitchBend = curve.Type == DmCurveType.PitchBend;
                if (!isPitchBend && curve.Type != DmCurveType.ControlChange) continue;

                int begin = startTick + curve.GridStart * ticksPerGrid + curve.TimeOffset;
                int duration = curve.Duration;

                // An instant curve, or one with no duration, is a single event.
                int steps = 1;
                if (duration > 0 && curve.Shape != DmCurveShape.Instant)
                {
                    steps = duration / CurveStepTicks;
                    if (steps < 1) steps = 1;
                    if (steps > MaxCurveSteps) steps = MaxCurveSteps;
                }

                for (int step = 0; step <= steps; step++)
                {
                    float t = steps > 0 ? step / (float)steps : 1f;
                    int tick = begin + (int)(duration * t);
                    float value = curve.ValueAt(t);

                    ScheduledNote e = new ScheduledNote();
                    e.Tick = tick;
                    e.Channel = (byte)channel;

                    if (isPitchBend)
                    {
                        // Producer stores these either as a raw 14-bit value centred on
                        // 8192, or as a signed offset centred on 0. Adding 8192 to a raw
                        // value pins the channel to maximum bend, so the convention is
                        // decided from the curve's own endpoints.
                        e.Kind = ScheduledKind.PitchBend;
                        bool signedOffset = curve.StartValue < 0 || curve.EndValue < 0;
                        e.Value = Clamp((int)value + (signedOffset ? 8192 : 0), 0, 16383);
                    }
                    else
                    {
                        e.Kind = ScheduledKind.Controller;
                        e.Note = (byte)(curve.ControllerNumber & 0x7F);
                        e.Value = Clamp((int)value, 0, 127);
                    }
                    _scheduled.Add(e);

                    if (steps == 1 && curve.Shape == DmCurveShape.Instant) break;
                }

                // Return the controller to its reset value, so a volume swell or pan move
                // doesn't leave the channel parked where the curve ended.
                if (curve.ResetsAfterwards)
                    ScheduleReset(curve, channel, begin + duration, isPitchBend);
            }
        }

        /// <summary>
        /// Restores a controller to its reset value once, at mtResetDuration after the
        /// curve ends. This is a single event, not a ramp: content uses a reset duration
        /// as long as the whole pattern, and sweeping the controller across that span
        /// overrides every later curve on the same controller. A sustain pedal authored
        /// as instant down/up pairs is the clearest case — a ramp from the pedal-down
        /// curve keeps re-pressing the pedal over every pedal-up that follows, and the
        /// notes never release.
        /// </summary>
        void ScheduleReset(DmStyleCurve curve, int channel, int endTick, bool isPitchBend)
        {
            ScheduledNote e = new ScheduledNote();
            e.Tick = endTick + Math.Max(0, curve.ResetDuration);
            e.Channel = (byte)channel;

            if (isPitchBend)
            {
                e.Kind = ScheduledKind.PitchBend;
                bool signedOffset = curve.ResetValue < 0 || curve.EndValue < 0;
                e.Value = Clamp(curve.ResetValue + (signedOffset ? 8192 : 0), 0, 16383);
            }
            else
            {
                e.Kind = ScheduledKind.Controller;
                e.Note = (byte)(curve.ControllerNumber & 0x7F);
                e.Value = Clamp(curve.ResetValue, 0, 127);
            }
            _scheduled.Add(e);
        }

        const int CurveStepTicks = DmRiff.PPQ / 8;   // 1/32 note
        const int MaxCurveSteps = 64;

        static int Rank(ScheduledKind kind)
        {
            switch (kind)
            {
                case ScheduledKind.NoteOff: return 0;
                case ScheduledKind.Controller: return 1;
                case ScheduledKind.PitchBend: return 1;
                default: return 2;
            }
        }

        /// <summary>
        /// Moves a note by octaves until it sits inside the part's inversion range.
        /// This is the dominant audible part of DirectMusic's inversion; the full system
        /// also re-voices chord tones using the subchord's inversion points, which isn't
        /// implemented. Ranges narrower than an octave, or outside 0-127, are treated as
        /// unauthored rather than trusted, so a misparsed header can't wreck the pitch.
        /// </summary>
        int FoldIntoRange(int midi, DmStylePart part)
        {
            if (!ApplyPartInversion || midi < 0) return midi;

            int lo = part.InvertLower, hi = part.InvertUpper;
            if (lo < 0 || hi > 127 || hi - lo < 12) return midi;

            while (midi > hi) midi -= 12;
            while (midi < lo) midi += 12;
            return midi;
        }

        /// <summary>
        /// The variation for one repetition of a part reference, starting at the given
        /// tick within the pattern.
        ///
        /// Parts sharing a non-zero variation lock id resolve to the same entry when their
        /// repetitions line up, so the first of them to ask advances the group's ordering
        /// and the rest follow it. Advancing once per asking part instead would leave a
        /// locked bass and drum pair playing different variations, which is the thing the
        /// lock exists to prevent. Locked parts of unequal length can't line up on every
        /// repetition and will only agree where their boundaries coincide.
        /// </summary>
        int VariationForRepeat(DmStylePart part, DmPartRef pref, int tickInPattern)
        {
            int key = pref.VariationLockId != 0 ? 0x10000 + pref.VariationLockId : pref.LogicalPartId;
            long cacheKey = ((long)key << 32) ^ (uint)tickInPattern;

            int variation;
            if (_measureVariation.TryGetValue(cacheKey, out variation)) return variation;

            variation = ChooseVariation(part, pref);
            _measureVariation[cacheKey] = variation;
            return variation;
        }

        /// <summary>
        /// Picks the next variation for a part reference, following the ordering it asks
        /// for. Call it through VariationForMeasure rather than directly: this advances
        /// the ordering every time, which is only correct once per measure per lock group.
        /// </summary>
        int ChooseVariation(DmStylePart part, DmPartRef pref)
        {
            List<int> usable = part.UsableVariations();
            if (usable.Count == 0) return 0;
            if (usable.Count == 1) return usable[0];

            int key = pref.VariationLockId != 0 ? 0x10000 + pref.VariationLockId : pref.LogicalPartId;

            VariationState state;
            if (!_variationState.TryGetValue(key, out state))
            {
                state = new VariationState();
                state.Cursor = pref.VariationOrder == DmVariationOrder.RandomStart
                    ? _rng.Next(usable.Count) : 0;
                state.Last = -1;
                _variationState[key] = state;
            }

            int chosen;
            switch (pref.VariationOrder)
            {
                case DmVariationOrder.Random:
                    chosen = usable[_rng.Next(usable.Count)];
                    break;

                case DmVariationOrder.NoRepeat:
                {
                    // Random, but never the same one twice running.
                    int pick = usable[_rng.Next(usable.Count)];
                    if (pick == state.Last && usable.Count > 1)
                    {
                        int index = usable.IndexOf(pick);
                        pick = usable[(index + 1 + _rng.Next(usable.Count - 1)) % usable.Count];
                    }
                    chosen = pick;
                    break;
                }

                case DmVariationOrder.RandomRow:
                {
                    // Shuffle every variation, play the whole row, then reshuffle.
                    if (state.Row == null || state.RowIndex >= state.Row.Count)
                    {
                        state.Row = new List<int>(usable);
                        for (int i = state.Row.Count - 1; i > 0; i--)
                        {
                            int j = _rng.Next(i + 1);
                            int t = state.Row[i]; state.Row[i] = state.Row[j]; state.Row[j] = t;
                        }
                        state.RowIndex = 0;
                    }
                    chosen = state.Row[state.RowIndex++];
                    break;
                }

                case DmVariationOrder.RandomStart:
                case DmVariationOrder.Sequential:
                default:
                    chosen = usable[state.Cursor % usable.Count];
                    state.Cursor++;
                    break;
            }

            state.Last = chosen;
            return chosen;
        }

        int MidiChannelFor(DmPartRef pref)
        {
            return AllocateChannel(BandChannelFor(pref));
        }

        /// <summary>
        /// Looks up what a synth channel is currently carrying: the PChannel routed to it,
        /// the band instrument name, and the style part playing through it. For meters.
        /// </summary>
        /// <summary>Play mode and note count for the part returned by the last TryDescribeChannel call.</summary>
        public string PartDetail { get; private set; }

        public bool TryDescribeChannel(int midiChannel, out int pchannel, out string instrument, out string part)
        {
            pchannel = -1; instrument = null; part = null; PartDetail = null;

            foreach (KeyValuePair<int, int> kv in _pchannelToMidi)
            {
                if (kv.Value != midiChannel) continue;
                // Later PChannels overwrite earlier ones on a shared channel; report the
                // one whose instrument is actually loaded.
                if (pchannel < 0) pchannel = kv.Key;
                string name;
                if (_pchannelParts.TryGetValue(kv.Key, out name))
                {
                    pchannel = kv.Key;
                    part = name;
                    string detail;
                    if (_pchannelPartDetail.TryGetValue(kv.Key, out detail)) PartDetail = detail;
                }
            }

            DmBandInstrument ins;
            _channelInstrument.TryGetValue(midiChannel, out ins);

            // Prefer a name a human authored, then the name of the DLS instrument the
            // lookup actually landed on, and only fall back to numbers if neither exists.
            if (ins != null && !string.IsNullOrEmpty(ins.Name))
            {
                instrument = ins.Name;
            }
            else
            {
                DlsInstrument resolved = _synth.GetChannelInstrument(midiChannel);
                if (resolved != null && !string.IsNullOrEmpty(resolved.Name))
                    instrument = resolved.Name;
                else if (ins != null)
                    instrument = string.Format("bank {0}/{1} prog {2}{3}", ins.BankMsb, ins.BankLsb,
                                               ins.Program, ins.IsDrum ? " drum" : "");
            }

            return pchannel >= 0 || instrument != null;
        }

        /// <summary>
        /// Variation index (0-31) currently playing on a synth channel, or -1. Updates at
        /// pattern boundaries, so it holds steady through a pattern and changes on the bar.
        /// </summary>
        public int VariationForChannel(int midiChannel)
        {
            foreach (KeyValuePair<int, int> kv in _pchannelToMidi)
            {
                int variation;
                if (kv.Value == midiChannel && _pchannelVariation.TryGetValue(kv.Key, out variation))
                    return variation;
            }
            return -1;
        }

        /// <summary>
        /// The play mode a note actually uses.
        ///
        /// A note carries an override or a sentinel meaning "no override, use the part's".
        /// Authoring tools write that sentinel two ways: 0, and DMUS_PLAYMODE_NONE (16).
        /// Both mean inherit. Reading 0 as Fixed makes the music value a raw MIDI note
        /// number; reading 16 as "do not play" silences the part outright — one shipped
        /// ambience style writes 16 on every note while its parts are AlwaysPlay, and
        /// treating that literally produced no sound at all.
        ///
        /// A part that is itself NONE genuinely doesn't play, which is checked after
        /// inheritance rather than before it.
        /// </summary>
        public static int EffectivePlayMode(DmStyleNote note, DmStylePart part)
        {
            int noteMode = note.PlayModeFlags;
            if (noteMode <= 0 || noteMode == 0xFF) return part.PlayModeFlags;
            if (noteMode == (int)DmPlayMode.None) return part.PlayModeFlags;
            return noteMode;
        }

        /// <summary>The band channel a part reference addresses, under the current interpretation.</summary>
        public int BandChannelFor(DmPartRef pref)
        {
            switch (PartChannelSource)
            {
                case DmPartChannelSource.LogicalPartId:
                    return pref.LogicalPartId;

                case DmPartChannelSource.PartOrder:
                {
                    int key = pref.LogicalPartId | (pref.PChannel << 16);
                    int index = _partOrder.IndexOf(key);
                    if (index < 0) { _partOrder.Add(key); index = _partOrder.Count - 1; }
                    return index;
                }

                default:
                    return pref.PChannel >= 0 ? pref.PChannel : pref.LogicalPartId;
            }
        }

        int AllocateChannel(int pchannel)
        {
            int channel;
            if (_pchannelToMidi.TryGetValue(pchannel, out channel)) return channel;

            channel = _nextMidiChannel % _synth.ChannelCount;
            _nextMidiChannel++;
            if (_nextMidiChannel > _synth.ChannelCount)
                ChannelsExhausted = true;

            _pchannelToMidi[pchannel] = channel;

            // MIDI channel 9 is a drum channel by default. A DirectMusic PChannel is not,
            // so anything the band didn't explicitly claim is reset to melodic here —
            // otherwise a part allocated onto channel 9 plays percussion.
            if (!_channelInstrument.ContainsKey(channel))
                _synth.SetProgram(channel, 0, 0, 0, false);

            return channel;
        }

        /// <summary>
        /// Instrument assignments come from the segment's band track when it has one,
        /// and otherwise from bands embedded in the style. Styles authored for reuse
        /// across segments usually carry their own band.
        /// </summary>
        public List<DmBandEvent> ActiveBands
        {
            get
            {
                if (Segment != null && Segment.Bands.Count > 0) return Segment.Bands;
                if (Style != null && Style.Bands.Count > 0) return Style.Bands;
                return null;
            }
        }

        /// <summary>
        /// Forces a band channel onto a specific patch, overriding whatever the band says.
        /// Set drum true for percussion. Call before Start, or it applies at the next
        /// pattern boundary.
        /// </summary>
        public void SetInstrumentOverride(int bandChannel, int bankMsb, int bankLsb, int program, bool drum)
        {
            DmBandInstrument ins = new DmBandInstrument();
            ins.PChannel = bandChannel;
            ins.Patch = (uint)(((bankMsb & 0x7F) << 16) | ((bankLsb & 0x7F) << 8) | (program & 0x7F))
                      | (drum ? 0x80000000u : 0u);
            ins.DlsReference = "(override)";
            _overrides[bandChannel] = ins;
        }

        public void ClearInstrumentOverrides() { _overrides.Clear(); }

        void ApplyBand(int tick)
        {
            List<DmBandEvent> bands = ActiveBands;
            if (bands == null || bands.Count == 0) return;

            DmBandEvent band = null;
            for (int i = 0; i < bands.Count; i++)
            {
                if (bands[i].Time > tick) break;
                band = bands[i];
            }
            if (band == null) band = bands[0];

            for (int i = 0; i < band.Instruments.Count; i++)
            {
                DmBandInstrument ins = band.Instruments[i];
                DmBandInstrument replacement;
                if (_overrides.TryGetValue(ins.PChannel, out replacement)) ins = replacement;
                int channel = AllocateChannel(ins.PChannel);

                // Every field below is only meaningful when its DMUS_IO_INST_ flag is set.
                // Applying an unflagged one means acting on whatever bytes happened to be
                // there — an unflagged nTranspose in particular detunes the whole channel.
                int msb = IgnoreBankSelect ? 0 : ins.BankMsb;
                int lsb = IgnoreBankSelect ? 0 : ins.BankLsb;
                _synth.SetProgram(channel, msb, lsb, ins.Program, ins.IsDrum);

                if (ins.HasVolume) _synth.ControlChange(channel, 7, Clamp(ins.Volume, 0, 127));
                if (ins.HasPan) _synth.ControlChange(channel, 10, Clamp(ins.Pan, 0, 127));
                _synth.SetChannelTranspose(channel, ins.HasTranspose ? ins.Transpose : 0);
                _synth.SetChannelPitchBendRange(channel,
                    ins.HasPitchBendRange ? NormalisePitchBendRange(ins.PitchBendRange) : 2f);

                _channelInstrument[channel] = ins;
            }

            foreach (KeyValuePair<int, DmBandInstrument> kv in _overrides)
            {
                if (_channelInstrument.ContainsKey(AllocateChannel(kv.Key))) continue;
                int channel = AllocateChannel(kv.Key);
                DmBandInstrument ins = kv.Value;
                _synth.SetProgram(channel, IgnoreBankSelect ? 0 : ins.BankMsb,
                                  IgnoreBankSelect ? 0 : ins.BankLsb, ins.Program, ins.IsDrum);
                _channelInstrument[channel] = ins;
            }
        }

        // ------------------------------------------------- music value resolution

        int Resolve(int musicValue, int playMode, DmSubChord chord)
        {
            int midi;
            if (MusicValueResolver != null) midi = MusicValueResolver(musicValue, playMode, chord);
            else midi = Resolve(musicValue, playMode, chord, MusicValueMode);

            if (midi < 0) return -1;
            midi += Transpose + OctaveOffset * 12;
            return midi < 0 || midi > 127 ? -1 : midi;
        }

        /// <summary>
        /// Built-in music value rule.
        ///
        /// A music value packs octave (bits 12-15), chord position (8-11), scale position
        /// (4-7) and a signed accidental (0-3). Fixed play modes use the value as a MIDI
        /// note directly. Otherwise the position fields index into the subchord's chord
        /// and scale bit patterns, relative to the chord or key root.
        ///
        /// The interaction between the chord and scale positions is the part most likely
        /// to differ from DirectMusic's own output on real content — replace this via
        /// MusicValueResolver if your style voices incorrectly.
        /// </summary>
        public static int DefaultResolve(int musicValue, int playMode, DmSubChord chord)
        {
            return Resolve(musicValue, playMode, chord, DmMusicValueMode.ChordThenScale);
        }

        public static int Resolve(int musicValue, int playMode, DmSubChord chord, DmMusicValueMode mode)
        {
            DmPlayMode flags = (DmPlayMode)playMode;
            if ((flags & DmPlayMode.None) != 0) return -1;

            bool chordRooted = (flags & DmPlayMode.ChordRoot) != 0;
            bool keyRooted = (flags & DmPlayMode.KeyRoot) != 0;
            bool chordIntervals = (flags & DmPlayMode.ChordIntervals) != 0;
            bool scaleIntervals = (flags & DmPlayMode.ScaleIntervals) != 0;

            // Fixed: the music value is already a MIDI note number.
            if (!chordRooted && !keyRooted && !chordIntervals && !scaleIntervals)
                return musicValue & 0x7F;

            // Never reached from the engine, which substitutes DefaultChord, but a caller
            // resolving values directly might pass null.
            if (chord == null) chord = MakeDefaultChord();

            int octave = (musicValue >> 12) & 0xF;
            int chordPos = (musicValue >> 8) & 0xF;
            int scalePos = (musicValue >> 4) & 0xF;
            int accidental = musicValue & 0xF;
            if (accidental > 7) accidental -= 16;

            int root = keyRooted ? chord.ScaleRoot : chord.ChordRoot;
            uint chordPattern = chord.ChordPattern != 0 ? chord.ChordPattern : 0x91u;    // root, third, fifth
            uint scalePattern = chord.ScalePattern != 0 ? chord.ScalePattern : 0xAB5u;   // major scale

            int semitones;
            switch (mode)
            {
                case DmMusicValueMode.ChordToneThenScaleSteps:
                    semitones = NthInterval(chordPattern, chordPos);
                    if (scalePos != 0) semitones = StepUpScale(scalePattern, semitones, scalePos);
                    break;

                case DmMusicValueMode.ScalePositionOnly:
                    semitones = NthInterval(scalePattern, scalePos);
                    break;

                case DmMusicValueMode.ChordPositionOnly:
                    semitones = NthInterval(chordPattern, chordPos);
                    break;

                default:
                    if (chordIntervals)
                    {
                        semitones = NthInterval(chordPattern, chordPos);
                        if (scalePos != 0) semitones = StepUpScale(scalePattern, semitones, scalePos);
                    }
                    else if (scaleIntervals)
                    {
                        semitones = NthInterval(scalePattern, scalePos);
                        if (chordPos != 0) semitones = StepUpScale(scalePattern, semitones, chordPos);
                    }
                    else semitones = 0;
                    break;
            }

            return octave * 12 + (root % 12) + semitones + accidental;
        }

        /// <summary>The nth set bit of an interval bitfield, wrapping into higher octaves.</summary>
        static int NthInterval(uint pattern, int index)
        {
            int count = 0;
            for (int i = 0; i < 24; i++) if ((pattern & (1u << i)) != 0) count++;
            if (count == 0) return index;

            int octaveShift = 0;
            while (index >= count) { index -= count; octaveShift++; }
            while (index < 0) { index += count; octaveShift--; }

            int seen = 0;
            for (int i = 0; i < 24; i++)
            {
                if ((pattern & (1u << i)) == 0) continue;
                if (seen == index) return i + 12 * octaveShift;
                seen++;
            }
            return 12 * octaveShift;
        }

        /// <summary>Moves up a number of scale steps from a starting semitone offset.</summary>
        static int StepUpScale(uint scalePattern, int startSemitones, int steps)
        {
            int current = startSemitones;
            for (int s = 0; s < steps; s++)
            {
                int next = int.MaxValue;
                for (int i = 0; i < 24; i++)
                {
                    if ((scalePattern & (1u << i)) == 0) continue;
                    if (i > current && i < next) next = i;
                }
                if (next == int.MaxValue) { current += 12; continue; }
                current = next;
            }
            return current;
        }

        /// <summary>
        /// Reports PChannel -> MIDI channel -> requested patch -> the DLS instrument that
        /// was actually selected. This is the fastest way to find out why something is
        /// playing the wrong sound: a name that doesn't match the patch means the bank
        /// lacked that program and a fallback was substituted.
        /// </summary>
        public string DescribeChannelMap()
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.AppendLine("Channel map  (part interpretation: " + PartChannelSource +
                          (IgnoreBankSelect ? ", bank select ignored" : "") + ")");
            sb.AppendLine(ActiveBands == null
                ? "  NO BAND: neither the segment nor the style supplied one, so every channel is program 0."
                : "  band applied from " + (Segment != null && Segment.Bands.Count > 0 ? "segment" : "style"));
            if (ChannelsExhausted)
                sb.AppendLine("  WARNING: more PChannels than synth channels (" + _synth.ChannelCount +
                              "); some now share one and will overwrite each other's instrument.");

            int bandInstruments = 0;
            List<DmBandEvent> active = ActiveBands;
            if (active != null)
                for (int i = 0; i < active.Count; i++) bandInstruments += active[i].Instruments.Count;
            sb.AppendLine("  bands=" + (active == null ? 0 : active.Count) +
                          "  instruments in band=" + bandInstruments);

            List<int> channels = new List<int>(_pchannelToMidi.Keys);
            channels.Sort();

            if (channels.Count == 0)
            {
                sb.AppendLine("  NO CHANNELS MAPPED. In order of likelihood:");
                if (!_hasStarted)
                    sb.AppendLine("    1. The performance hasn't started. This map is built by Play(), so call " +
                                  "this after Play() - and after a frame or two for the part rows.");
                else if (bandInstruments == 0)
                    sb.AppendLine("    1. The band parsed with zero instruments. The band list was found but its " +
                                  "contents weren't - check the unclaimed chunks in DumpSegment()/DumpStyle().");
                else
                    sb.AppendLine("    1. The band has instruments but none were applied - unexpected; " +
                                  "please report the unclaimed chunk list.");

                if (_hasStarted && !_hasExpanded)
                    sb.AppendLine("    2. No pattern has been expanded yet. Give it one audio buffer, or check " +
                                  "that a pattern covers groove level " + _grooveLevel + " via DumpGrooveMap().");
                else if (_hasStarted)
                    sb.AppendLine("    2. Patterns expanded but no part reference resolved to a style part. " +
                                  "If DumpStyle() shows patterns with 0 parts, the part-reference chunk id " +
                                  "wasn't recognised.");
                return sb.ToString();
            }

            for (int i = 0; i < channels.Count; i++)
            {
                int bandChannel = channels[i];
                int midi = _pchannelToMidi[bandChannel];

                string playing;
                if (_pchannelParts.TryGetValue(bandChannel, out playing))
                {
                    string detail;
                    if (_pchannelPartDetail.TryGetValue(bandChannel, out detail))
                        playing += " [" + detail + "]";
                }
                else playing = "(no style part plays here - band entry only)";

                DmBandInstrument ins;
                bool haveBand = _channelInstrument.TryGetValue(midi, out ins);
                string wanted = haveBand
                    ? string.Format("bank {0}/{1} program {2}{3}", ins.BankMsb, ins.BankLsb, ins.Program,
                                    ins.IsDrum ? " DRUM" : "")
                    : "nothing assigned -> bank 0/0 program 0";

                DlsInstrument got = _synth.GetChannelInstrument(midi);
                string resolved = got != null
                    ? string.Format("{0} (bank {1} program {2}{3})", got.Name ?? "<unnamed>",
                                    got.Bank, got.Program, got.IsDrum ? " DRUM" : "")
                    : "NOTHING - no bank loaded, or it has no instruments";

                string label = "";
                if (ins != null)
                {
                    if (!string.IsNullOrEmpty(ins.Name)) label = "  \"" + ins.Name + "\"";
                    else if (got != null && !string.IsNullOrEmpty(got.Name)) label = "  \"" + got.Name + "\"";
                }
                sb.AppendLine(string.Format("  PChannel {0} (group {1} ch {2}) -> MIDI {3}{4}",
                    bandChannel, bandChannel / 16 + 1, bandChannel % 16, midi, label));
                sb.AppendLine("      part:   " + playing);
                sb.AppendLine("      wanted: " + wanted +
                    (haveBand && !string.IsNullOrEmpty(ins.DlsReference)
                        ? "   from " + ins.DlsReference +
                          (string.IsNullOrEmpty(ins.DlsReferenceName) ? "" : " (\"" + ins.DlsReferenceName + "\")")
                        : ""));
                sb.AppendLine("      got:    " + resolved);

                if (haveBand && got != null)
                {
                    bool programMismatch = got.Program != ins.Program;
                    bool drumMismatch = got.IsDrum != ins.IsDrum;
                    if (drumMismatch)
                        sb.AppendLine("      *** MISMATCH: asked for " + (ins.IsDrum ? "a drum kit" : "a melodic instrument") +
                                      " and got " + (got.IsDrum ? "a drum kit" : "a melodic instrument") + ".");
                    else if (programMismatch)
                        sb.AppendLine("      *** MISMATCH: program " + ins.Program + " requested, " + got.Program +
                                      " substituted - no loaded bank defines it.");
                }
            }

            if (_pchannelParts.Count > 0)
            {
                sb.AppendLine("  style parts seen: " + _pchannelParts.Count +
                    ".  If a part name sits next to an instrument it shouldn't, the part-to-channel");
                sb.AppendLine("  mapping is wrong - try PartChannelSource = LogicalPartId or PartOrder.");
            }
            return sb.ToString();
        }

        /// <summary>
        /// Counts notes that resolve onto a pitch another note is already sounding at the
        /// same moment. Those unisons are inaudible as separate notes, so a high count is
        /// what "missing notes" actually sounds like. The usual cause is resolving against
        /// a chord with fewer tones than the content assumes: chord position 3 against a
        /// three-note triad wraps to the root, colliding with position 0 an octave up.
        /// </summary>
        public string DescribeUnisonCollapse()
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            if (Style == null) return "No style loaded.\n";

            DmChordEvent chordEvent = Segment != null ? Segment.ChordAt(0) : null;
            bool haveChords = chordEvent != null && chordEvent.SubChords.Count > 0;
            DmSubChord chord = haveChords ? chordEvent.ForLevel(0) : DefaultChord;

            int tones = 0;
            for (int i = 0; i < 24; i++) if ((chord.ChordPattern & (1u << i)) != 0) tones++;

            sb.AppendLine("Unison collapse check:");
            sb.AppendLine("  chord source: " + (haveChords ? "segment chord track" : "DefaultChord fallback"));
            sb.AppendLine(string.Format("  chord pattern 0x{0:X} has {1} tone(s)", chord.ChordPattern, tones));

            for (int i = 0; i < Style.Patterns.Count; i++)
            {
                DmPattern pattern = Style.Patterns[i];
                DmTimeSig ts = pattern.TimeSig.IsValid ? pattern.TimeSig : Style.TimeSig;

                List<int[]> notes = new List<int[]>();
                for (int j = 0; j < pattern.PartRefs.Count; j++)
                {
                    DmStylePart part;
                    if (!Style.PartsById.TryGetValue(pattern.PartRefs[j].PartId, out part)) continue;
                    DmTimeSig pts = part.TimeSig.IsValid ? part.TimeSig : ts;
                    int tpg = pts.TicksPerGrid;

                    for (int n = 0; n < part.Notes.Count; n++)
                    {
                        DmStyleNote note = part.Notes[n];
                        int pm = EffectivePlayMode(note, part);
                        int midi = Resolve(note.MusicValue, pm, chord, MusicValueMode);
                        midi = FoldIntoRange(midi, part);
                        if (midi < 0 || midi > 127) continue;
                        int on = note.GridStart * tpg + note.TimeOffset;
                        notes.Add(new int[] { on, on + Math.Max(1, note.Duration), midi,
                                              pattern.PartRefs[j].LogicalPartId });
                    }
                }

                // Count each note at most once, as masked if any earlier note of the same
                // pitch is still sounding when it starts.
                int collisions = 0;
                notes.Sort(delegate (int[] a, int[] b) { return a[0].CompareTo(b[0]); });
                for (int b = 0; b < notes.Count; b++)
                {
                    for (int a = b - 1; a >= 0; a--)
                    {
                        if (notes[a][0] == notes[b][0] && a != b && notes[a][2] == notes[b][2])
                        {
                            collisions++;
                            break;
                        }
                        if (notes[a][1] <= notes[b][0]) continue;      // already finished
                        if (notes[a][2] == notes[b][2]) { collisions++; break; }
                    }
                }

                if (notes.Count == 0) continue;
                sb.AppendLine(string.Format("  {0,-16} {1,5} notes, {2,4} overlapping unisons ({3:P1})",
                    pattern.Name ?? "<unnamed>", notes.Count, collisions, (double)collisions / notes.Count));
            }

            if (!haveChords)
                sb.AppendLine("  No chord track in play, so every note resolves against the fallback " +
                              "chord. If that has fewer tones than the content expects, distinct notes " +
                              "land on the same pitch and go missing.");
            return sb.ToString();
        }

        static readonly string[] NoteNames = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };

        public static string NoteName(int midi)
        {
            if (midi < 0 || midi > 127) return "out of range";
            return NoteNames[midi % 12] + (midi / 12 - 1) + " (" + midi + ")";
        }

        /// <summary>
        /// Shows how each part's music values convert to pitches: the raw value, the fields
        /// it decomposes into, the chord it resolved against, and the resulting note.
        /// Run this when the timing is right but the notes are wrong.
        /// </summary>
        public string DescribeNoteResolution(int notesPerPart)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            if (Style == null) return "No style loaded.\n";

            DmChordEvent chordEvent = Segment != null ? Segment.ChordAt(0) : null;
            bool haveChords = chordEvent != null && chordEvent.SubChords.Count > 0;

            sb.AppendLine("Note resolution:");
            if (Segment == null)
                sb.AppendLine("  NO SEGMENT: resolving against DefaultChord (C major).");
            else if (Segment.Chords.Count == 0)
                sb.AppendLine("  NO CHORD TRACK PARSED: the segment yielded 0 chords, so every chord-relative");
            else if (!haveChords)
                sb.AppendLine("  CHORD FOUND BUT NO SUBCHORDS: the crdb chunk parsed only partly.");
            else
                sb.AppendLine("  chords in segment: " + Segment.Chords.Count +
                              ", first has " + chordEvent.SubChords.Count + " subchord(s)");

            if (Segment != null && Segment.Chords.Count == 0)
                sb.AppendLine("  value falls back to C major. Check the unclaimed chunks in DumpSegment().");

            DmSubChord chord = haveChords ? chordEvent.ForLevel(0) : DefaultChord;
            sb.AppendLine(string.Format("  chord used: root {0}, chordPattern 0x{1:X}, scalePattern 0x{2:X}",
                NoteName(chord.ChordRoot % 12 + 60), chord.ChordPattern, chord.ScalePattern));
            sb.AppendLine("  transpose: " + Transpose + " semitone(s)");

            for (int i = 0; i < Style.Patterns.Count && i < 4; i++)
            {
                DmPattern pattern = Style.Patterns[i];
                sb.AppendLine("  pattern " + (pattern.Name ?? "<unnamed>"));

                for (int j = 0; j < pattern.PartRefs.Count; j++)
                {
                    DmStylePart part;
                    if (!Style.PartsById.TryGetValue(pattern.PartRefs[j].PartId, out part)) continue;

                    DmPlayMode partMode = (DmPlayMode)part.PlayModeFlags;
                    sb.AppendLine("    part " + (part.Name ?? "<unnamed>") + "  playmode " +
                                  partMode + " (0x" + part.PlayModeFlags.ToString("X") + ")");

                    for (int n = 0; n < part.Notes.Count && n < notesPerPart; n++)
                    {
                        DmStyleNote note = part.Notes[n];
                        int playMode = EffectivePlayMode(note, part);

                        int midi = Resolve(note.MusicValue, playMode, chord);
                        DmPlayMode mode = (DmPlayMode)playMode;
                        bool fixedMode = (mode & (DmPlayMode.KeyRoot | DmPlayMode.ChordRoot |
                                                  DmPlayMode.ScaleIntervals | DmPlayMode.ChordIntervals)) == 0;

                        if (fixedMode)
                        {
                            sb.AppendLine(string.Format("      value 0x{0:X4} fixed          -> {1}",
                                note.MusicValue, NoteName(midi)));
                        }
                        else
                        {
                            int octave = (note.MusicValue >> 12) & 0xF;
                            int chordPos = (note.MusicValue >> 8) & 0xF;
                            int scalePos = (note.MusicValue >> 4) & 0xF;
                            int accidental = note.MusicValue & 0xF;
                            if (accidental > 7) accidental -= 16;
                            sb.AppendLine(string.Format(
                                "      value 0x{0:X4} oct {1} chord {2} scale {3} acc {4} -> {5}",
                                note.MusicValue, octave, chordPos, scalePos, accidental, NoteName(midi)));
                        }
                    }
                }
            }

            sb.AppendLine();
            sb.AppendLine("  Same notes under each interpretation (current mode: " + MusicValueMode + "):");
            sb.AppendLine(string.Format("    {0,-8} {1,-14} {2,-14} {3,-14} {4}",
                "value", "ChordThenScale", "ChordToneScale", "ScaleOnly", "ChordOnly"));

            int shown = 0;
            for (int i = 0; i < Style.Patterns.Count && shown < 12; i++)
            {
                DmPattern pattern = Style.Patterns[i];
                for (int j = 0; j < pattern.PartRefs.Count && shown < 12; j++)
                {
                    DmStylePart part;
                    if (!Style.PartsById.TryGetValue(pattern.PartRefs[j].PartId, out part)) continue;

                    for (int n = 0; n < part.Notes.Count && shown < 12; n++)
                    {
                        DmStyleNote note = part.Notes[n];
                        int pm = EffectivePlayMode(note, part);

                        DmPlayMode playModeFlags = (DmPlayMode)pm;
                        bool fixedMode = (playModeFlags & (DmPlayMode.KeyRoot | DmPlayMode.ChordRoot |
                                          DmPlayMode.ScaleIntervals | DmPlayMode.ChordIntervals)) == 0;
                        if (fixedMode) continue;

                        sb.AppendLine(string.Format("    0x{0:X4}   {1,-14} {2,-14} {3,-14} {4}",
                            note.MusicValue,
                            NoteName(Resolve(note.MusicValue, pm, chord, DmMusicValueMode.ChordThenScale)),
                            NoteName(Resolve(note.MusicValue, pm, chord, DmMusicValueMode.ChordToneThenScaleSteps)),
                            NoteName(Resolve(note.MusicValue, pm, chord, DmMusicValueMode.ScalePositionOnly)),
                            NoteName(Resolve(note.MusicValue, pm, chord, DmMusicValueMode.ChordPositionOnly))));
                        shown++;
                    }
                }
            }
            if (shown == 0) sb.AppendLine("    (every note is fixed play mode; the chord rule doesn't apply)");

            sb.AppendLine();
            sb.AppendLine("  If these notes are uniformly high or low, adjust Transpose or OctaveOffset.");
            sb.AppendLine("  If they are scattered or nonsensical, the chord track or the music value rule");
            sb.AppendLine("  is wrong - replace it with MusicValueResolver.");
            return sb.ToString();
        }

        /// <summary>
        /// Band pitch bend range, normalised to semitones. Values large enough to be cents
        /// are converted rather than clamped to an absurd semitone count.
        /// </summary>
        static float NormalisePitchBendRange(int raw)
        {
            if (raw <= 0) return 2f;
            if (raw > 48) return Clamp(raw / 100, 1, 48);      // stored in cents
            return raw;
        }

        static int Clamp(int v, int lo, int hi) { return v < lo ? lo : (v > hi ? hi : v); }
    }
}
