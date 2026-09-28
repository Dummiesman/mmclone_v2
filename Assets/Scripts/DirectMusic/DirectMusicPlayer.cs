using System;
using System.Collections;
using System.IO;
using UnityEngine;

namespace DirectMusicLite
{
    [RequireComponent(typeof(AudioSource))]
    [AddComponentMenu("Audio/DirectMusic Player")]
    public class DirectMusicPlayer : MonoBehaviour
    {
        [Header("Sound banks — stacked in order, later entries override earlier ones")]
        [Tooltip("Rename .dls files to .dls.bytes so Unity imports them as TextAsset. " +
                 "Put a General MIDI bank first and your custom banks after it.")]
        public TextAsset[] soundBankAssets;

        [Header("Song")]
        public TextAsset midiAsset;

        [Header("Or load from StreamingAssets at runtime")]
        public string[] soundBankStreamingPaths;
        public string midiStreamingPath = "";

        [Header("Playback")]
        public bool playOnStart = true;
        public bool loop = true;
        [Range(0f, 2f)] public float volume = 0.6f;
        [Range(0.25f, 4f)] public float playbackSpeed = 1f;
        [Range(8, 128)] public int maxVoices = 48;

        /// <summary>Raised on the main thread when a non-looping song reaches its end.</summary>
        public event Action Finished;

        readonly object _lock = new object();
        readonly DlsBankStack _banks = new DlsBankStack();
        DlsSynth _synth;
        MidiFile _midi;
        AudioSource _source;
        int _sampleRate = 44100;

        bool _playing;
        long _samplePos;
        int _eventIndex;
        double _tailSeconds = 2.0;
        volatile bool _finishedFlag;

        public bool IsPlaying { get { return _playing; } }
        public DlsSynth Synth { get { return _synth; } }
        /// <summary>The layered bank the synth reads from. Never null.</summary>
        public DlsBankStack SoundBanks { get { return _banks; } }
        public IDlsBank SoundBank { get { return _banks; } }
        public MidiFile Midi { get { return _midi; } }

        public double PositionSeconds
        {
            get { lock (_lock) { return _samplePos / (double)_sampleRate; } }
        }

        public double LengthSeconds
        {
            get { return _midi != null ? _midi.DurationSeconds : 0.0; }
        }

        // ------------------------------------------------------------ set-up

        void Awake()
        {
            _sampleRate = AudioSettings.outputSampleRate;
            if (_sampleRate <= 0) _sampleRate = 44100;

            _synth = new DlsSynth(_sampleRate, maxVoices);
            _synth.MasterGain = volume;
            _synth.SetBank(_banks);

            _source = GetComponent<AudioSource>();
            
            // OnAudioFilterRead only runs while the AudioSource is playing so we need a clip
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
            {
                for (int i = 0; i < soundBankAssets.Length; i++)
                {
                    if (soundBankAssets[i] != null) AddSoundBank(soundBankAssets[i].bytes);
                }
            }

            if (soundBankStreamingPaths != null)
            {
                for (int i = 0; i < soundBankStreamingPaths.Length; i++)
                {
                    if (!string.IsNullOrEmpty(soundBankStreamingPaths[i]))
                    {
                        yield return LoadStreamingAsset(soundBankStreamingPaths[i],
                            delegate (byte[] data) { AddSoundBank(data); });
                    }
                }
            }

            if (midiAsset != null)
            {
                LoadMidi(midiAsset.bytes);
            }
            else if (!string.IsNullOrEmpty(midiStreamingPath))
            {
                yield return LoadStreamingAsset(midiStreamingPath, LoadMidi);
            }

            if (playOnStart && _midi != null) Play();
        }

        void Update()
        {
            if (_synth != null) _synth.MasterGain = volume;

            if (_finishedFlag)
            {
                _finishedFlag = false;
                if (Finished != null) Finished();
            }
        }

        void OnDestroy()
        {
            lock (_lock)
            {
                _playing = false;
                if (_synth != null) _synth.AllSoundOff();
            }
        }

        // -------------------------------------------------------- loading

        /// <summary>Replaces the whole stack with a single collection.</summary>
        public DlsFile LoadSoundBank(byte[] bytes)
        {
            DlsFile bank = DlsFile.Load(bytes);          // parse outside the lock
            lock (_lock)
            {
                _banks.Clear();
                _banks.Add(bank);
                _synth.RefreshAllInstruments();
            }
            return bank;
        }

        public DlsFile AddSoundBank(byte[] bytes)
        {
            DlsFile bank = DlsFile.Load(bytes);
            lock (_lock)
            {
                _banks.Add(bank);
                _synth.RefreshAllInstruments();
            }
            return bank;
        }

        public DlsFile AddSoundBankBase(byte[] bytes)
        {
            DlsFile bank = DlsFile.Load(bytes);
            lock (_lock)
            {
                _banks.AddBase(bank);
                _synth.RefreshAllInstruments();
            }
            return bank;
        }

        public bool RemoveSoundBank(DlsFile bank)
        {
            lock (_lock)
            {
                bool removed = _banks.Remove(bank);
                if (removed) _synth.RefreshAllInstruments();
                return removed;
            }
        }

        public void ClearSoundBanks()
        {
            lock (_lock)
            {
                _synth.AllSoundOff();
                _banks.Clear();
                _synth.RefreshAllInstruments();
            }
        }

        public DlsFile LoadSoundBankFromFile(string absolutePath)
        {
            return LoadSoundBank(File.ReadAllBytes(absolutePath));
        }

        public DlsFile AddSoundBankFromFile(string absolutePath)
        {
            return AddSoundBank(File.ReadAllBytes(absolutePath));
        }

        public void LoadMidi(byte[] bytes)
        {
            MidiFile midi = MidiFile.Load(bytes);
            lock (_lock)
            {
                _midi = midi;
                _eventIndex = 0;
                _samplePos = 0;
                _synth.AllSoundOff();
                _synth.Reset();
            }
        }

        public void LoadMidiFromFile(string absolutePath)
        {
            LoadMidi(File.ReadAllBytes(absolutePath));
        }

        IEnumerator LoadStreamingAsset(string relativePath, Action<byte[]> onLoaded)
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

        // ------------------------------------------------------- transport

        public void Play()
        {
            lock (_lock)
            {
                if (_midi == null) { Debug.LogWarning("DirectMusicLite: no MIDI loaded."); return; }
                if (_samplePos == 0) _synth.Reset();
                _playing = true;
            }
        }

        /// <summary>Stops and rewinds to the start.</summary>
        public void Stop()
        {
            lock (_lock)
            {
                _playing = false;
                _samplePos = 0;
                _eventIndex = 0;
                _synth.AllSoundOff();
                _synth.Reset();
            }
        }

        /// <summary>Stops without rewinding; notes are released rather than cut.</summary>
        public void Pause()
        {
            lock (_lock)
            {
                _playing = false;
                _synth.AllNotesOff();
            }
        }

        public void Resume()
        {
            lock (_lock) { if (_midi != null) _playing = true; }
        }

        /// <summary>Jumps to a position, replaying controller/program changes so the mix is correct.</summary>
        public void Seek(double seconds)
        {
            lock (_lock)
            {
                if (_midi == null) return;
                if (seconds < 0) seconds = 0;

                _synth.AllSoundOff();
                _synth.Reset();
                _samplePos = (long)(seconds * _sampleRate);
                _eventIndex = 0;

                double target = seconds * playbackSpeed;
                while (_eventIndex < _midi.Events.Count && _midi.Events[_eventIndex].Time < target)
                {
                    MidiEvent ev = _midi.Events[_eventIndex];
                    int cmd = ev.Command;
                    if (cmd == 0xB0 || cmd == 0xC0 || cmd == 0xE0) _synth.Dispatch(ev);
                    _eventIndex++;
                }
            }
        }

        // ------------------------------------------- live (non-sequenced) use

        public void NoteOn(int channel, int note, int velocity)
        {
            lock (_lock) { _synth.NoteOn(channel, note, velocity); }
        }

        public void NoteOff(int channel, int note)
        {
            lock (_lock) { _synth.NoteOff(channel, note); }
        }

        /// <summary>Selects an instrument from the DLS by bank/program.</summary>
        public void SetProgram(int channel, int bankMsb, int bankLsb, int program, bool drumKit)
        {
            lock (_lock) { _synth.SetProgram(channel, bankMsb, bankLsb, program, drumKit); }
        }

        /// <summary>Fire-and-forget one shot, handy for stingers and UI sounds.</summary>
        public void PlayOneShot(int channel, int note, int velocity, float seconds)
        {
            NoteOn(channel, note, velocity);
            StartCoroutine(NoteOffAfter(channel, note, seconds));
        }

        IEnumerator NoteOffAfter(int channel, int note, float seconds)
        {
            yield return new WaitForSeconds(seconds);
            NoteOff(channel, note);
        }

        // ------------------------------------------------------ audio thread

        void OnAudioFilterRead(float[] data, int channels)
        {
            if (_synth == null) { Array.Clear(data, 0, data.Length); return; }

            int frames = data.Length / channels;

            lock (_lock)
            {
                if (_midi == null || !_playing)
                {
                    _synth.Render(data, 0, frames, channels);   // still lets live notes sound
                    return;
                }

                double speed = playbackSpeed <= 0f ? 1f : playbackSpeed;
                int done = 0;

                while (done < frames)
                {
                    int chunk = frames - done;
                    double now = (_samplePos + done) / (double)_sampleRate * speed;

                    if (_eventIndex < _midi.Events.Count)
                    {
                        double eventTime = _midi.Events[_eventIndex].Time;
                        if (eventTime <= now)
                        {
                            _synth.Dispatch(_midi.Events[_eventIndex]);
                            _eventIndex++;
                            continue;
                        }

                        int framesUntil = (int)Math.Ceiling((eventTime - now) / speed * _sampleRate);
                        if (framesUntil < 1) framesUntil = 1;
                        if (framesUntil < chunk) chunk = framesUntil;
                    }
                    else if (now >= _midi.DurationSeconds + _tailSeconds)
                    {
                        if (loop)
                        {
                            _samplePos = -done;              // restart cleanly at this sample
                            _eventIndex = 0;
                            _synth.AllNotesOff();
                            continue;
                        }

                        _playing = false;
                        _finishedFlag = true;
                        _synth.Render(data, done * channels, chunk, channels);
                        return;
                    }

                    _synth.Render(data, done * channels, chunk, channels);
                    done += chunk;
                }

                _samplePos += frames;
            }
        }
    }
}
