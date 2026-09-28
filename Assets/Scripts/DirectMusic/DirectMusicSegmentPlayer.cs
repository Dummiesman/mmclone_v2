using System;
using System.Collections;
using System.IO;
using UnityEngine;

namespace DirectMusicLite
{
    [RequireComponent(typeof(AudioSource))]
    [AddComponentMenu("Audio/DirectMusic Segment Player")]
    public class DirectMusicSegmentPlayer : MonoBehaviour
    {
        [Header("Sound banks — stacked in order, later entries override earlier ones")]
        public TextAsset[] soundBankAssets;

        [Header("Segment and style (rename to .bytes to import as TextAsset)")]
        public TextAsset segmentAsset;
        public TextAsset styleAsset;

        [Header("Or load from StreamingAssets at runtime")]
        public string[] soundBankStreamingPaths;
        public string segmentStreamingPath = "";
        public string styleStreamingPath = "";

        [Header("Playback")]
        public bool playOnStart = true;
        [Tooltip("Where playOnStart and Play() begin from a stopped state. SegmentStart " +
                 "(mtPlayStart) is how DirectMusic plays a segment: any intro before the " +
                 "loop point is heard once. LoopStart jumps straight to the loop point and " +
                 "skips that intro.")]
        public DmStartPosition startPosition = DmStartPosition.SegmentStart;
        [Tooltip("Honour the segment's mtLoopStart/mtLoopEnd and repeat count. Turn off to " +
                 "let the style cycle forever regardless of the segment's length.")]
        public bool respectSegmentLoop = true;
        [Range(0f, 2f)] public float volume = 0.6f;
        [Tooltip("Stereo balance: -1 hard left, 0 centre, +1 hard right. Attenuates the " +
         "far side rather than boosting the near one, so centre stays unity. " +
         "This is a master control — per-channel placement comes from the " +
         "content's pan and SetChannelTrim.")]
        [Range(-1f, 1f)] public float balance;
        [Range(8, 128)] public int maxVoices = 48;
        [Tooltip("Synth channels. DirectMusic addresses PChannels in groups of 16 and content " +
                 "often uses several, so this is 64 rather than MIDI's 16.")]
        [Range(16, 128)] public int channelCount = 64;

        [Header("Automatic bank loading")]
        [Tooltip("Load the DLS files the segment's and style's bands refer to, the way " +
                 "DirectMusic's loader does. Runs after both files are loaded.")]
        public bool autoLoadReferencedBanks = true;
        [Tooltip("Extra folders to search for referenced .dls files. StreamingAssets and " +
                 "the folder holding the .sgt/.sty are searched automatically. Ignored " +
                 "once BankResolver is set to something other than a DmFileBankResolver.")]
        public string[] bankSearchPaths;
        [Tooltip("Fall back to the Windows GM set (System32/drivers/gm.dls) when a " +
                 "reference can't be resolved any other way.")]
        public bool allowSystemGeneralMidi = true;
        [Tooltip("Load the .sty a segment references, instead of requiring styleAsset or " +
                 "LoadStyle(). Only applies when no style is loaded already, and needs a " +
                 "resolver that implements ResolveStyle.")]
        public bool autoLoadReferencedStyle = true;
        [Tooltip("General MIDI collection used by band instruments that carry no DLS " +
                 "reference. Leave blank on Windows to use the system gm.dls; set it to a " +
                 "filename or path for platforms that have no system copy.")]
        public string defaultCollectionPath = "";

        [Header("Part to instrument mapping (change if instruments are wrong)")]
        [Tooltip("Which part-reference field identifies the band channel. If parts play " +
                 "through the wrong instruments, try LogicalPartId, then PartOrder.")]
        public DmPartChannelSource partChannelSource = DmPartChannelSource.PChannel;
        [Tooltip("Look everything up in bank 0, ignoring the band's bank select bits.")]
        public bool ignoreBankSelect;

        [Header("Groove")]
        [Tooltip("When on, the segment's command track drives the groove level. " +
                 "Turn off to drive it yourself with SetGrooveLevel.")]
        public bool followSegmentCommands = true;
        [Range(1, 100)] public int grooveLevel = 50;
        [Tooltip("How a music value's chord and scale fields combine. If notes are in the " +
                 "wrong key or voicing, compare the readings with DumpNoteResolution().")]
        public DmMusicValueMode musicValueMode = DmMusicValueMode.ChordThenScale;
        [Tooltip("Octaves added after resolving, for content that sits an octave out.")]
        public int octaveOffset;
        [Tooltip("Fold notes into the register a style part declares with its inversion " +
                 "range. Turn off if notes land in the wrong octave.")]
        public bool applyPartInversion = true;
        [Tooltip("Render DLS LFO modulation: vibrato and tremolo authored in articulation.")]
        public bool renderLfo = true;
        [Tooltip("Render controller curves authored in style parts - pan moves, volume " +
                 "swells, pitch bends. Turn off to hear the notes without them.")]
        public bool renderCurves = true;
        [Tooltip("Semitone offset applied after music values resolve. Use -12 or +12 if " +
                 "everything is an octave out.")]
        public int transpose;
        public int randomSeed = 12345;

        readonly object _lock = new object();
        readonly DlsBankStack _banks = new DlsBankStack();
        readonly System.Collections.Generic.List<string> _extraSearchDirs = new System.Collections.Generic.List<string>();
        DmBankResolver.Result _lastResolution;
        DmBankResolver.StyleResult _lastStyleResolution;
        DmBankResolver _bankResolver;
        bool _paused;
        DlsSynth _synth;
        DmPerformance _performance;
        AudioSource _source;
        int _sampleRate = 44100;
        int _appliedGroove = -1;

        public DlsBankStack SoundBanks { get { return _banks; } }
        public DmSegment Segment { get { return _performance != null ? _performance.Segment : null; } }
        public DmStyle Style { get { return _performance != null ? _performance.Style : null; } }
        public DmPerformance Performance { get { return _performance; } }
        public bool IsPlaying { get { return _performance != null && _performance.IsPlaying; } }

        /// <summary>The pattern currently playing — useful for debugging groove mappings.</summary>
        public DmPattern CurrentPattern { get { return _performance != null ? _performance.CurrentPattern : null; } }

        /// <summary>Number of synth channels, for iterating meters.</summary>
        public int SynthChannelCount { get { return _synth != null ? _synth.ChannelCount : 0; } }
        public int ActiveVoiceCount { get { return _synth != null ? _synth.ActiveVoiceCount : 0; } }
        public int MaxVoices { get { return maxVoices; } }
        public int CurrentGrooveLevel { get { return _performance != null ? _performance.GrooveLevel : 0; } }
        public int CurrentMeasure { get { return _performance != null ? _performance.CurrentMeasure : 0; } }
        public double CurrentTempo { get { return _performance != null ? _performance.CurrentTempo : 0.0; } }

        /// <summary>Variation index (0-31) playing on a channel, or -1 if none.</summary>
        public int GetChannelVariation(int channel)
        {
            return _performance != null ? _performance.VariationForChannel(channel) : -1;
        }

        /// <summary>Live level, voice count and sounding notes for a channel.</summary>
        public DlsSynth.ChannelMeter GetChannelMeter(int channel)
        {
            return _synth != null ? _synth.GetChannelMeter(channel) : null;
        }

        /// <summary>What a channel is carrying: PChannel, band instrument, and style part.</summary>
        public bool TryDescribeChannel(int channel, out int pchannel, out string instrument, out string part)
        {
            pchannel = -1; instrument = null; part = null;
            if (_performance == null) return false;
            bool found = _performance.TryDescribeChannel(channel, out pchannel, out instrument, out part);
            PartDetail = _performance.PartDetail;
            return found;
        }

        /// <summary>Play mode and note count from the last TryDescribeChannel call.</summary>
        public string PartDetail { get; private set; }

        // ------------------------------------------------------ mute and solo

        /// <summary>
        /// Silences a channel while leaving it running, so meters still show what it's
        /// playing and unmuting is immediate.
        /// </summary>
        public void SetChannelMute(int channel, bool muted)
        {
            lock (_lock) { if (_synth != null) _synth.SetChannelMute(channel, muted); }
        }

        public bool IsChannelMuted(int channel)
        {
            return _synth != null && _synth.IsChannelMuted(channel);
        }

        /// <summary>While anything is soloed, everything not soloed is silenced.</summary>
        public void SetChannelSolo(int channel, bool solo)
        {
            lock (_lock) { if (_synth != null) _synth.SetChannelSolo(channel, solo); }
        }

        public bool IsChannelSolo(int channel)
        {
            return _synth != null && _synth.IsChannelSolo(channel);
        }

        /// <summary>True if the channel is actually heard right now.</summary>
        public bool IsChannelAudible(int channel)
        {
            return _synth != null && _synth.IsChannelAudible(channel);
        }

        public bool AnySolo { get { return _synth != null && _synth.AnySolo; } }

        /// <summary>Level trim for one channel in dB, on top of what the content authored.</summary>
        public void SetChannelTrim(int channel, float trimDb)
        {
            lock (_lock) { if (_synth != null) _synth.SetChannelTrim(channel, trimDb); }
        }

        public float GetChannelTrim(int channel)
        {
            return _synth != null ? _synth.GetChannelTrim(channel) : 0f;
        }

        /// <summary>Everything affecting pitch per channel. Start here if notes play sharp or flat.</summary>
        public void DumpPitchChain()
        {
            if (_synth == null) { Debug.Log("DirectMusicLite: not initialised."); return; }
            Debug.Log(_synth.DescribePitchChain());
        }

        /// <summary>Shows where each channel's level comes from. Start here if one is too loud.</summary>
        public void DumpGainChain()
        {
            if (_synth == null) { Debug.Log("DirectMusicLite: not initialised."); return; }
            Debug.Log(_synth.DescribeGainChain());
        }

        public void ClearMutesAndSolos()
        {
            lock (_lock) { if (_synth != null) _synth.ClearMutesAndSolos(); }
        }

        /// <summary>Mutes every channel except the one given. Pass -1 to unmute everything.</summary>
        public void SoloChannel(int channel)
        {
            lock (_lock)
            {
                if (_synth == null) return;
                _synth.ClearMutesAndSolos();
                if (channel >= 0) _synth.SetChannelSolo(channel, true);
            }
        }

        /// <summary>Turn off if you're voice-count bound and don't need the meters.</summary>
        public bool MetersEnabled
        {
            get { return _synth != null && _synth.MetersEnabled; }
            set { if (_synth != null) _synth.MetersEnabled = value; }
        }

        // -------------------------------------------------------------- set-up

        void Awake()
        {
            _sampleRate = AudioSettings.outputSampleRate;
            if (_sampleRate <= 0) _sampleRate = 44100;

            _synth = new DlsSynth(_sampleRate, maxVoices, channelCount);
            _synth.MasterGain = volume;
            _synth.MasterBalance = balance;
            _synth.SetBank(_banks);

            _performance = new DmPerformance(_synth, _sampleRate, randomSeed);
            _performance.FollowSegmentCommands = followSegmentCommands;
            // Only seed groove from the inspector when nothing else will drive it. With
            // followSegmentCommands on, the segment's command track is the authority.
            if (!followSegmentCommands) _performance.GrooveLevel = grooveLevel;
            _appliedGroove = grooveLevel;
            _performance.Transpose = transpose;
            _performance.PartChannelSource = partChannelSource;
            _performance.IgnoreBankSelect = ignoreBankSelect;
            _performance.RespectSegmentLoop = respectSegmentLoop;
            _performance.RenderCurves = renderCurves;
            _performance.ApplyPartInversion = applyPartInversion;
            _performance.MusicValueMode = musicValueMode;
            _performance.OctaveOffset = octaveOffset;
            _synth.LfoEnabled = renderLfo;
            _performance.SegmentEntryPosition = startPosition == DmStartPosition.Current
                ? DmStartPosition.SegmentStart : startPosition;

            _source = GetComponent<AudioSource>();
            _source.clip = AudioClip.Create("DirectMusicLite_Silence", 4096, 1, _sampleRate, false);
            _source.loop = true;
            _source.playOnAwake = false;
            _source.spatialBlend = 0f;
            _source.volume = 1f;
            _source.Play();
        }

        IEnumerator Start()
        {
            if (soundBankAssets != null)
                for (int i = 0; i < soundBankAssets.Length; i++)
                    if (soundBankAssets[i] != null) AddSoundBank(soundBankAssets[i].bytes);

            if (soundBankStreamingPaths != null)
                for (int i = 0; i < soundBankStreamingPaths.Length; i++)
                    if (!string.IsNullOrEmpty(soundBankStreamingPaths[i]))
                        yield return LoadStreaming(soundBankStreamingPaths[i],
                            delegate (byte[] d) { AddSoundBank(d); });

            if (styleAsset != null) LoadStyle(styleAsset.bytes);
            else if (!string.IsNullOrEmpty(styleStreamingPath))
                yield return LoadStreaming(styleStreamingPath, delegate (byte[] d) { LoadStyle(d); });

            if (segmentAsset != null) LoadSegment(segmentAsset.bytes);
            else if (!string.IsNullOrEmpty(segmentStreamingPath))
                yield return LoadStreaming(segmentStreamingPath, delegate (byte[] d) { LoadSegment(d); });

            if (playOnStart) Play();
        }

        void Update()
        {
            if (_synth != null)
            {
                _synth.MasterGain = volume;
                _synth.MasterBalance = balance;
            }
            if (_performance == null) return;

            // Let the inspector slider drive groove while tuning, without stomping
            // changes made from script every frame.
            if (followSegmentCommands)
            {
                // The segment owns the groove level; mirror it into the inspector so the
                // field shows what's playing instead of overwriting it every frame.
                int current = _performance.GrooveLevel;
                grooveLevel = current;
                _appliedGroove = current;
            }
            else if (grooveLevel != _appliedGroove)
            {
                _appliedGroove = grooveLevel;
                lock (_lock) { _performance.GrooveLevel = grooveLevel; }
            }
            _performance.FollowSegmentCommands = followSegmentCommands;
            _performance.Transpose = transpose;
            _performance.RespectSegmentLoop = respectSegmentLoop;
            _performance.RenderCurves = renderCurves;
            _performance.ApplyPartInversion = applyPartInversion;
            _performance.MusicValueMode = musicValueMode;
            _performance.OctaveOffset = octaveOffset;
            _synth.LfoEnabled = renderLfo;
            _performance.SegmentEntryPosition = startPosition == DmStartPosition.Current
                ? DmStartPosition.SegmentStart : startPosition;

            if (_performance.PartChannelSource != partChannelSource ||
                _performance.IgnoreBankSelect != ignoreBankSelect)
            {
                lock (_lock)
                {
                    _performance.PartChannelSource = partChannelSource;
                    _performance.IgnoreBankSelect = ignoreBankSelect;
                    if (_performance.IsPlaying) _performance.Start();   // remap takes effect immediately
                }
            }
        }

        /// <summary>Forces a band channel onto a specific patch, overriding the band.</summary>
        public void SetInstrumentOverride(int bandChannel, int bankMsb, int bankLsb, int program, bool drum)
        {
            lock (_lock) { _performance.SetInstrumentOverride(bandChannel, bankMsb, bankLsb, program, drum); }
        }

        public void ClearInstrumentOverrides()
        {
            lock (_lock) { _performance.ClearInstrumentOverrides(); }
        }

        void OnDestroy()
        {
            lock (_lock)
            {
                if (_performance != null) _performance.Stop();
                if (_synth != null) _synth.AllSoundOff();
            }
        }

        // ------------------------------------------------------------- loading

        public DlsFile AddSoundBank(byte[] bytes)
        {
            DlsFile bank = DlsFile.Load(bytes);
            lock (_lock) { _banks.Add(bank); _synth.RefreshAllInstruments(); }
            return bank;
        }

        public DlsFile AddSoundBankFromFile(string path)
        {
            NoteSearchDirectory(path);
            DlsFile bank = AddSoundBank(File.ReadAllBytes(path));
            bank.SourcePath = path;
            return bank;
        }

        public DmStyle LoadStyle(byte[] bytes)
        {
            DmStyle style = DmStyle.Load(bytes);
            lock (_lock) { _performance.Style = style; }
            if (style.UnclaimedChunks.Count > 0)
                Debug.Log("DirectMusicLite: style parsed with " + style.UnclaimedChunks.Count +
                          " unclaimed chunk(s). Call DumpStyle() to inspect.");
            return style;
        }

        public DmSegment LoadSegment(byte[] bytes)
        {
            DmSegment segment = DmSegment.Load(bytes);
            lock (_lock) { _performance.Segment = segment; }

            // The style has to be in place before banks resolve: its bands carry DLS
            // references of their own.
            if (segment.StyleReferences.Count > 0 && Style == null && autoLoadReferencedStyle)
                LoadReferencedStyle(segment);

            if (segment.StyleReferences.Count > 0 && Style == null)
                Debug.LogWarning("DirectMusicLite: segment references style '" +
                                 segment.StyleReferences[0] + "' but no .sty is loaded. " +
                                 "Load it with LoadStyle(), or give the resolver a " +
                                 "ResolveStyle. Call DumpStyleResolution() for detail.");

            if (segment.Chords.Count == 0)
                Debug.LogWarning("DirectMusicLite: segment has no chord track. Chord-relative style notes " +
                                 "will resolve against DefaultChord (C major), so anything the content " +
                                 "reharmonised will play in the wrong key. Call DumpNoteResolution().");

            if (autoLoadReferencedBanks) LoadReferencedBanks();
            return segment;
        }

        public DmStyle LoadStyleFromFile(string path)
        {
            NoteSearchDirectory(path);
            return LoadStyle(File.ReadAllBytes(path));
        }

        public DmSegment LoadSegmentFromFile(string path)
        {
            NoteSearchDirectory(path);
            return LoadSegment(File.ReadAllBytes(path));
        }

        void NoteSearchDirectory(string filePath)
        {
            try
            {
                string dir = Path.GetDirectoryName(Path.GetFullPath(filePath));
                if (!string.IsNullOrEmpty(dir) && !_extraSearchDirs.Contains(dir))
                    _extraSearchDirs.Add(dir);
            }
            catch (System.Exception) { }
        }

        System.Collections.Generic.List<string> SearchDirectories()
        {
            System.Collections.Generic.List<string> dirs = new System.Collections.Generic.List<string>();
            if (bankSearchPaths != null)
                for (int i = 0; i < bankSearchPaths.Length; i++)
                    if (!string.IsNullOrEmpty(bankSearchPaths[i])) dirs.Add(bankSearchPaths[i]);

            dirs.AddRange(_extraSearchDirs);
            dirs.Add(Application.streamingAssetsPath);
            dirs.Add(Application.persistentDataPath);
            return dirs;
        }

        /// <summary>
        /// Where referenced DLS collections are fetched from. Defaults to a
        /// DmFileBankResolver driven by bankSearchPaths and the folders content was loaded
        /// from. Assign your own — a DmDelegateBankResolver wrapping a virtual filesystem,
        /// say — to take those files from somewhere other than disk. Set it before loading
        /// a segment, or call LoadReferencedBanks afterwards.
        /// </summary>
        public DmBankResolver BankResolver
        {
            get
            {
                if (_bankResolver == null) _bankResolver = new DmFileBankResolver();
                return _bankResolver;
            }
            set { lock (_lock) { _bankResolver = value; } }
        }

        /// <summary>Same, for a type that implements IDmBankResolver without inheriting.</summary>
        public void SetBankResolver(IDmBankResolver resolver)
        {
            BankResolver = DmBankResolver.Wrap(resolver);
        }

        /// <summary>
        /// Hands the resolver whatever the inspector knows before it runs. A file resolver
        /// gets the current search directories; anything else is left alone apart from a
        /// blank default collection, which defaultCollectionPath fills in.
        /// </summary>
        DmBankResolver ActiveResolver()
        {
            DmBankResolver resolver = BankResolver;

            DmFileBankResolver files = resolver as DmFileBankResolver;
            if (files != null)
            {
                files.SearchDirectories.Clear();
                files.AddSearchDirectories(SearchDirectories());
                files.AllowSystemGeneralMidi = allowSystemGeneralMidi;
                files.DefaultCollectionReference = defaultCollectionPath;
            }
            else if (string.IsNullOrEmpty(resolver.DefaultCollectionReference) &&
                     !string.IsNullOrEmpty(defaultCollectionPath))
            {
                resolver.DefaultCollectionReference = defaultCollectionPath;
            }

            return resolver;
        }

        /// <summary>
        /// Loads every DLS the loaded segment and style refer to. Called automatically
        /// after LoadSegment when autoLoadReferencedBanks is on; safe to call again.
        /// </summary>
        public DmBankResolver.Result LoadReferencedBanks()
        {
            DmBankResolver.Result result;
            lock (_lock)
            {
                result = ActiveResolver().LoadReferenced(Segment, Style, _banks);
                _synth.RefreshAllInstruments();
            }

            _lastResolution = result;
            if (result.Missing.Count > 0)
                Debug.LogWarning("DirectMusicLite: " + result.Missing.Count +
                                 " referenced DLS file(s) not found. Call DumpBankResolution() for detail.");

            if (result.InstrumentsNeedingDefault > 0 && result.DefaultCollection == null)
                Debug.LogWarning("DirectMusicLite: " + result.InstrumentsNeedingDefault +
                                 " band instrument(s) need the default General MIDI collection and none was " +
                                 "found. Those channels will play the wrong instrument. Set " +
                                 "defaultCollectionPath (or the resolver's " +
                                 "DefaultCollectionReference) to a General MIDI .dls.");
            return result;
        }

        /// <summary>
        /// Loads the .sty the current segment references, using the same resolver the
        /// banks come from. Called automatically by LoadSegment when
        /// autoLoadReferencedStyle is on and no style is loaded yet.
        /// </summary>
        public DmBankResolver.StyleResult LoadReferencedStyle()
        {
            return LoadReferencedStyle(Segment);
        }

        DmBankResolver.StyleResult LoadReferencedStyle(DmSegment segment)
        {
            DmBankResolver.StyleResult result = ActiveResolver().LoadReferencedStyle(segment);
            _lastStyleResolution = result;

            if (result.Style != null)
            {
                lock (_lock) { _performance.Style = result.Style; }
                if (result.Style.UnclaimedChunks.Count > 0)
                    Debug.Log("DirectMusicLite: style parsed with " + result.Style.UnclaimedChunks.Count +
                              " unclaimed chunk(s). Call DumpStyle() to inspect.");
            }
            else if (!result.NoneReferenced && result.Supported)
            {
                Debug.LogWarning("DirectMusicLite: couldn't resolve the referenced style. " +
                                 "Call DumpStyleResolution() for what was tried.");
            }
            return result;
        }

        public void DumpStyleResolution()
        {
            if (_lastStyleResolution == null) { Debug.Log("DirectMusicLite: no style resolution has run yet."); return; }
            Debug.Log(_lastStyleResolution.Describe());
        }

        public void DumpBankResolution()
        {
            if (_lastResolution == null) { Debug.Log("DirectMusicLite: no resolution has run yet."); return; }
            Debug.Log(_lastResolution.Describe());
        }

        /// <summary>
        /// Shows what each channel was asked to play and what the bank actually gave it.
        /// Start here when the wrong instruments are sounding.
        /// </summary>
        /// <summary>
        /// Shows how music values convert to pitches. Run this when the rhythm is right
        /// but the notes are wrong.
        /// </summary>
        public void DumpNoteResolution(int notesPerPart)
        {
            if (_performance == null) { Debug.Log("DirectMusicLite: not initialised."); return; }
            Debug.Log(_performance.DescribeNoteResolution(notesPerPart));
        }

        public void DumpNoteResolution() { DumpNoteResolution(8); }

        /// <summary>
        /// Reports what articulation each loaded DLS uses and how much of it is applied.
        /// Blocks marked IGNORED are authored expression this synth doesn't render.
        /// </summary>
        public void DumpArticulation()
        {
            if (_banks.LayerCount == 0) { Debug.Log("DirectMusicLite: no sound banks loaded."); return; }
            for (int i = 0; i < _banks.Layers.Count; i++)
                Debug.Log(_banks.Layers[i].DescribeArticulation());
        }

        /// <summary>
        /// Per-instrument envelope report. Use when notes hold at a constant level instead
        /// of decaying: it shows what each envelope resolved to and which fields the
        /// content actually authored.
        /// </summary>
        public void DumpEnvelopes(int maxInstrumentsPerBank)
        {
            if (_banks.LayerCount == 0) { Debug.Log("DirectMusicLite: no sound banks loaded."); return; }
            for (int i = 0; i < _banks.Layers.Count; i++)
                Debug.Log(_banks.Layers[i].DescribeEnvelopes(maxInstrumentsPerBank));
        }

        public void DumpEnvelopes() { DumpEnvelopes(24); }

        /// <summary>
        /// Counts notes resolving onto a pitch already sounding. High counts are what
        /// "missing notes" sounds like; the usual cause is the wrong chord.
        /// </summary>
        public void DumpUnisonCollapse()
        {
            if (_performance == null) { Debug.Log("DirectMusicLite: not initialised."); return; }
            Debug.Log(_performance.DescribeUnisonCollapse());
        }

        public void DumpChannelMap()
        {
            if (_performance == null) { Debug.Log("DirectMusicLite: not initialised."); return; }
            Debug.Log(_performance.DescribeChannelMap());
        }

        /// <summary>
        /// Waits for playback to populate the map, then dumps it. The channel rows only
        /// exist once Play() has run and at least one audio buffer has been rendered, so
        /// dumping immediately after Play() shows an empty map.
        /// </summary>
        public Coroutine DumpChannelMapWhenReady() { return StartCoroutine(DumpWhenReady()); }

        IEnumerator DumpWhenReady()
        {
            for (int i = 0; i < 120; i++)
            {
                if (_performance != null && _performance.IsPlaying && _performance.CurrentPattern != null)
                    break;
                yield return null;
            }
            yield return null;                    // one more frame for the audio thread
            DumpChannelMap();
        }

        IEnumerator LoadStreaming(string relativePath, Action<byte[]> onLoaded)
        {
            string path = Path.Combine(Application.streamingAssetsPath, relativePath);
            if (path.Contains("://"))
            {
                UnityEngine.Networking.UnityWebRequest req = UnityEngine.Networking.UnityWebRequest.Get(path);
                yield return req.SendWebRequest();
#if UNITY_2020_1_OR_NEWER
                bool failed = req.result != UnityEngine.Networking.UnityWebRequest.Result.Success;
#else
                bool failed = req.isNetworkError || req.isHttpError;
#endif
                if (failed) { Debug.LogError("DirectMusicLite: cannot read " + path + " — " + req.error); yield break; }
                onLoaded(req.downloadHandler.data);
            }
            else
            {
                if (!File.Exists(path)) { Debug.LogError("DirectMusicLite: missing file " + path); yield break; }
                onLoaded(File.ReadAllBytes(path));
            }
        }

        // ----------------------------------------------------------- transport

        /// <summary>
        /// Producer's Play: resumes from the current position, or starts from
        /// <see cref="startPosition"/> when stopped.
        /// </summary>
        public void Play()
        {
            PlayFrom(_paused ? DmStartPosition.Current : startPosition);
        }

        /// <summary>
        /// Begins at the segment's mtLoopStart — the point the loop returns to, not the
        /// start of the segment. When a segment has an intro before its loop point this
        /// skips it; <see cref="PlayFromSegmentStart"/> plays from the beginning.
        /// </summary>
        public void PlayAtStart()
        {
            DmSegment segment = Segment;
            if (segment != null && segment.ResolvedLoopStart > segment.ResolvedPlayStart)
            {
                int skipped = segment.ResolvedLoopStart - segment.ResolvedPlayStart;
                DmTimeSig ts = segment.TimeSigAt(segment.ResolvedPlayStart);
                int measures = ts.TicksPerMeasure > 0 ? skipped / ts.TicksPerMeasure : 0;
                Debug.LogWarning(string.Format(
                    "DirectMusicLite: PlayAtStart() begins at mtLoopStart, skipping the first " +
                    "{0} measure(s) of '{1}' and the groove commands in them (groove {2} at the " +
                    "loop point, {3} at the segment start). Use PlayFromSegmentStart() to play " +
                    "the intro.",
                    measures, segment.Name ?? "<unnamed>",
                    segment.GrooveLevelAt(segment.ResolvedLoopStart),
                    segment.GrooveLevelAt(segment.ResolvedPlayStart)));
            }
            PlayFrom(DmStartPosition.LoopStart);
        }

        /// <summary>Begins at the segment's mtPlayStart.</summary>
        public void PlayFromSegmentStart()
        {
            PlayFrom(DmStartPosition.SegmentStart);
        }

        public void PlayFrom(DmStartPosition position)
        {
            lock (_lock)
            {
                if (!CanPlay()) return;
                _paused = false;
                if (position == DmStartPosition.Current && _performance.IsPlaying) return;
                _performance.StartFrom(position);
            }
        }

        /// <summary>Begins at an explicit MUSIC_TIME (768 ticks per quarter note).</summary>
        public void PlayFromTick(int tick)
        {
            lock (_lock)
            {
                if (!CanPlay()) return;
                _paused = false;
                _performance.StartAt(tick);
            }
        }

        bool CanPlay()
        {
            if (_performance.Style != null) return true;
            Debug.LogWarning("DirectMusicLite: no style loaded — nothing to play. " +
                             "A segment alone only supplies groove commands, chords and the band.");
            return false;
        }

        /// <summary>Stops but keeps the position, so Play() resumes where it left off.</summary>
        public void Pause()
        {
            lock (_lock)
            {
                _paused = _performance.IsPlaying;
                _performance.Stop();
            }
        }

        /// <summary>Position on the segment's timeline, in MUSIC_TIME ticks.</summary>
        public int SegmentPositionTicks { get { return _performance != null ? _performance.SegmentPositionTicks : 0; } }
        public int LoopsPlayed { get { return _performance != null ? _performance.LoopsPlayed : 0; } }

        /// <summary>Raised when a non-repeating segment ends. Fires on the audio thread.</summary>
        public event Action SegmentFinished
        {
            add { if (_performance != null) _performance.SegmentFinished += value; }
            remove { if (_performance != null) _performance.SegmentFinished -= value; }
        }

        public void Stop()
        {
            lock (_lock) { _paused = false; _performance.Stop(); }
        }

        // -------------------------------------------------------------- groove

        /// <summary>
        /// Sets the groove level (1-100). Takes effect at the next pattern boundary,
        /// which is how DirectMusic behaves — the change lands musically rather than
        /// cutting the current bar.
        /// </summary>
        public void SetGrooveLevel(int level)
        {
            if (level < 1) level = 1;
            if (level > 100) level = 100;
            grooveLevel = level;
            _appliedGroove = level;
            lock (_lock)
            {
                _performance.FollowSegmentCommands = false;   // explicit control wins
                _performance.GrooveLevel = level;
            }
            followSegmentCommands = false;
        }

        // ---------------------------------------------------------- transitions

        /// <summary>
        /// Equivalent of IDirectMusicComposer::AutoTransition. Plays a transition drawn
        /// from the current style, then switches to the given segment.
        ///
        /// A game calling AutoTransition(seg, 0, DMUS_COMPOSEF_MEASURE) maps to
        /// TransitionTo(seg, style, DmCommandType.Groove, DmComposeFlags.Measure).
        /// </summary>
        public void TransitionTo(DmSegment segment, DmStyle style,
                                 DmCommandType command, DmComposeFlags flags)
        {
            if ((flags & DmComposeFlags.Modulate) != 0)
                Debug.LogWarning("DirectMusicLite: DMUS_COMPOSEF_MODULATE requires chordmap " +
                                 "composition, which isn't implemented. Transitioning without it.");

            lock (_lock)
            {
                _performance.AutoTransition(segment, style, command, flags);
                if (!_performance.IsPlaying && (segment != null || style != null)) _performance.Start();
            }
        }

        /// <summary>Measure-aligned groove transition — the common case.</summary>
        public void TransitionTo(DmSegment segment, DmStyle style)
        {
            TransitionTo(segment, style, DmCommandType.Groove, DmComposeFlags.Measure);
        }

        /// <summary>Loads a segment (and optionally a style) from disk and transitions to it.</summary>
        public void TransitionToFile(string segmentPath, string stylePath, DmComposeFlags flags)
        {
            DmStyle style = null;
            if (!string.IsNullOrEmpty(stylePath))
            {
                NoteSearchDirectory(stylePath);
                style = DmStyle.Load(File.ReadAllBytes(stylePath));
            }

            DmSegment segment = null;
            if (!string.IsNullOrEmpty(segmentPath))
            {
                NoteSearchDirectory(segmentPath);
                segment = DmSegment.Load(File.ReadAllBytes(segmentPath));
            }

            // The incoming content may reference banks the outgoing one didn't.
            if (autoLoadReferencedBanks && (segment != null || style != null))
            {
                lock (_lock)
                {
                    ActiveResolver().LoadReferenced(segment, style, _banks);
                    _synth.RefreshAllInstruments();
                }
            }

            TransitionTo(segment, style, DmCommandType.Groove, flags);
        }

        /// <summary>
        /// Parses a segment and loads any DLS it references, without switching to it.
        /// Do this during loading so a later TransitionTo touches no disk.
        /// </summary>
        public DmSegment PreloadSegmentFile(string path)
        {
            NoteSearchDirectory(path);
            DmSegment segment = DmSegment.Load(File.ReadAllBytes(path));
            ResolveBanksFor(segment, null);
            return segment;
        }

        /// <summary>
        /// Parses a segment and loads any DLS it references, without switching to it.
        /// Do this during loading so a later TransitionTo touches no disk.
        /// </summary>
        public DmSegment PreloadSegmentFile(byte[] data)
        {
            DmSegment segment = DmSegment.Load(data);
            ResolveBanksFor(segment, null);
            return segment;
        }

        /// <summary>Parses a style and loads any DLS it references, without switching to it.</summary>
        public DmStyle PreloadStyleFile(string path)
        {
            NoteSearchDirectory(path);
            DmStyle style = DmStyle.Load(File.ReadAllBytes(path));
            ResolveBanksFor(null, style);
            return style;
        }

        void ResolveBanksFor(DmSegment segment, DmStyle style)
        {
            if (!autoLoadReferencedBanks) return;
            lock (_lock)
            {
                _lastResolution = ActiveResolver().LoadReferenced(segment, style, _banks);
                _synth.RefreshAllInstruments();
            }
        }

        /// <summary>True while a queued segment is waiting for its boundary.</summary>
        public bool HasPendingSegment { get { return _performance != null && _performance.HasPendingSegment; } }

        /// <summary>Raised when a queued segment actually starts. Fires on the audio thread.</summary>
        public event Action SegmentSwitched
        {
            add { if (_performance != null) _performance.SegmentSwitched += value; }
            remove { if (_performance != null) _performance.SegmentSwitched -= value; }
        }

        public void Fill() { Queue(DmEmbellishment.Fill); }
        public void Intro() { Queue(DmEmbellishment.Intro); }
        public void Break() { Queue(DmEmbellishment.Break); }
        public void End() { Queue(DmEmbellishment.End); }

        void Queue(DmEmbellishment embellishment)
        {
            lock (_lock) { _performance.QueueEmbellishment(embellishment); }
        }

        // --------------------------------------------------------- diagnostics

        /// <summary>Dumps the parsed style, including chunks this reader didn't interpret.</summary>
        public void DumpStyle()
        {
            if (Style == null) { Debug.Log("DirectMusicLite: no style loaded."); return; }
            Debug.Log(Style.Describe());
        }

        public void DumpSegment()
        {
            if (Segment == null) { Debug.Log("DirectMusicLite: no segment loaded."); return; }
            Debug.Log(Segment.Describe());
        }

        /// <summary>Lists which patterns each groove level would select. Run this first.</summary>
        public void DumpGrooveMap()
        {
            if (Style == null) { Debug.Log("DirectMusicLite: no style loaded."); return; }

            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.AppendLine("Groove map for style " + (Style.Name ?? "<unnamed>") + ":");
            for (int level = 1; level <= 100; level++)
            {
                System.Collections.Generic.List<DmPattern> hits =
                    Style.PatternsFor(level, DmEmbellishment.Normal);
                if (hits.Count == 0) continue;

                bool boundary = level == 1 ||
                    Style.PatternsFor(level - 1, DmEmbellishment.Normal).Count != hits.Count;
                if (!boundary) continue;

                sb.Append("  level " + level + ": ");
                for (int i = 0; i < hits.Count; i++)
                    sb.Append((i > 0 ? ", " : "") + (hits[i].Name ?? "<unnamed>"));
                sb.AppendLine();
            }
            Debug.Log(sb.ToString());
        }

        // ---------------------------------------------------------- audio thread

        void OnAudioFilterRead(float[] data, int channels)
        {
            if (_performance == null) { Array.Clear(data, 0, data.Length); return; }
            lock (_lock)
            {
                _performance.Render(data, 0, data.Length / channels, channels);
            }
        }
    }
}
