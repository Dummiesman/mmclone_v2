// DlsSynth.cs — polyphonic sample-playback synthesizer that renders a DLS
// collection in response to MIDI messages. Pure C#, no Unity dependency, so it
// can also be unit-tested or dropped into any host that gives you a float buffer.
//
// Part of DirectMusicLite.

using System;

namespace DirectMusicLite
{
    public sealed class DlsSynth
    {
        /// <summary>Channel count when none is specified — matches MIDI's 16.</summary>
        public const int DefaultChannelCount = 16;
        public const int DrumChannel = 9;

        /// <summary>
        /// Number of independent channels. MIDI files need 16, but DirectMusic addresses
        /// PChannels in groups of 16 and content routinely uses several groups, so the
        /// segment player asks for more.
        /// </summary>
        public int ChannelCount { get { return _channels.Length; } }

        /// <summary>Applied after mixing. Lower this if loud banks clip.</summary>
        public float MasterGain = 0.6f;

        /// <summary>
        /// Master stereo balance: -1 hard left, 0 centre, +1 hard right. Attenuates the
        /// far side rather than boosting the near one, so centre stays unity and moving
        /// the control can never push the mix further into the limiter. Ignored when
        /// rendering to a mono buffer.
        /// </summary>
        public float MasterBalance
        {
            get { return _masterBalance; }
            set
            {
                float b = value < -1f ? -1f : (value > 1f ? 1f : value);
                _masterBalance = b;
                _balanceL = b > 0f ? 1f - b : 1f;
                _balanceR = b < 0f ? 1f + b : 1f;
            }
        }

        float _masterBalance;
        float _balanceL = 1f, _balanceR = 1f;

        /// <summary>
        /// Per-channel activity for meters. Costs one pass over the voice array per block,
        /// so it can be switched off if you're voice-count bound.
        /// </summary>
        public bool MetersEnabled = true;

        /// <summary>Render DLS LFO modulation: vibrato and tremolo. Turn off to A/B it.</summary>
        public bool LfoEnabled = true;

        /// <summary>Live level, voice count and sounding notes for one channel.</summary>
        public sealed class ChannelMeter
        {
            /// <summary>Smoothed 0..1 level with a falloff, for a VU-style bar.</summary>
            public float Level;
            /// <summary>Slower-falling peak hold.</summary>
            public float Peak;
            public int Voices;
            public int LowestNote = -1, HighestNote = -1;

            internal float Instant;
            ulong _notesLow, _notesHigh;

            public bool IsNoteOn(int note)
            {
                if (note < 0 || note > 127) return false;
                return note < 64 ? (_notesLow & (1UL << note)) != 0
                                 : (_notesHigh & (1UL << (note - 64))) != 0;
            }

            internal void SetNote(int note)
            {
                if (note < 0 || note > 127) return;
                if (note < 64) _notesLow |= 1UL << note;
                else _notesHigh |= 1UL << (note - 64);
                if (LowestNote < 0 || note < LowestNote) LowestNote = note;
                if (note > HighestNote) HighestNote = note;
            }

            internal void Begin()
            {
                Voices = 0;
                Instant = 0f;
                _notesLow = 0;
                _notesHigh = 0;
                LowestNote = -1;
                HighestNote = -1;
            }

            public bool IsActive { get { return Voices > 0 || Level > 0.001f; } }
        }

        ChannelMeter[] _meters;
        bool[] _muted;
        bool[] _solo;
        bool _anySolo;

        // ------------------------------------------------------- mute and solo

        /// <summary>
        /// Silences a channel. Notes still trigger and meter, so you can see what a muted
        /// part is doing and unmuting is immediate — muted voices are just the first
        /// candidates for stealing when polyphony runs short.
        /// </summary>
        public void SetChannelMute(int channel, bool muted)
        {
            if (_muted == null || channel < 0 || channel >= _muted.Length) return;
            if (_muted[channel] == muted) return;
            _muted[channel] = muted;
            RefreshAudibility();
        }

        public bool IsChannelMuted(int channel)
        {
            return _muted != null && channel >= 0 && channel < _muted.Length && _muted[channel];
        }

        /// <summary>While any channel is soloed, every channel not soloed is silenced.</summary>
        public void SetChannelSolo(int channel, bool solo)
        {
            if (_solo == null || channel < 0 || channel >= _solo.Length) return;
            if (_solo[channel] == solo) return;
            _solo[channel] = solo;
            RefreshAudibility();
        }

        public bool IsChannelSolo(int channel)
        {
            return _solo != null && channel >= 0 && channel < _solo.Length && _solo[channel];
        }

        /// <summary>True if the channel is currently heard, accounting for mute and solo.</summary>
        public bool IsChannelAudible(int channel)
        {
            if (_channels == null || channel < 0 || channel >= _channels.Length) return false;
            return _channels[channel].Audible;
        }

        public bool AnySolo { get { return _anySolo; } }

        /// <summary>True while the sustain pedal is held on a channel. Diagnostic.</summary>
        public bool IsChannelSustained(int channel)
        {
            return _channels != null && channel >= 0 && channel < _channels.Length && _channels[channel].Sustain;
        }

        /// <summary>
        /// Level trim for one channel, in dB, on top of whatever the content authored.
        /// Use this to balance a mix rather than to correct a parsing problem — check
        /// DescribeGainChain() first to see where a channel's level actually comes from.
        /// </summary>
        public void SetChannelTrim(int channel, float trimDb)
        {
            if (_channels == null || channel < 0 || channel >= _channels.Length) return;
            if (trimDb > 24f) trimDb = 24f;
            if (trimDb < -96f) trimDb = -96f;

            MidiChannel c = _channels[channel];
            c.TrimDb = trimDb;
            c.TrimLinear = trimDb == 0f ? 1f : (float)Math.Pow(10.0, trimDb / 20.0);
            UpdateChannelGains(channel);
        }

        public float GetChannelTrim(int channel)
        {
            if (_channels == null || channel < 0 || channel >= _channels.Length) return 0f;
            return _channels[channel].TrimDb;
        }

        /// <summary>
        /// Breaks down where each channel's level comes from: the band's CC7 and
        /// expression, the instrument's articulation attenuation, the sample's own wsmp
        /// gain, and any trim. A channel that stands out usually has one term other
        /// channels don't.
        /// </summary>
        public string DescribeGainChain()
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.AppendLine("Gain chain (velocity 127 reference, master " +
                          (20.0 * Math.Log10(Math.Max(0.0001, MasterGain))).ToString("F1") + " dB):");

            for (int i = 0; i < _channels.Length; i++)
            {
                MidiChannel c = _channels[i];
                ChannelMeter m = _meters[i];
                // Only channels doing something: sounding, trimmed, or explicitly programmed.
                bool interesting = m.Voices > 0 || m.Level > 0.001f || c.TrimDb != 0f ||
                                   c.Program != 0 || c.BankMsb != 0 || c.BankLsb != 0;
                if (!interesting) continue;

                float articulation = 0f, sample = 0f;
                bool haveVoice = false;
                for (int v = 0; v < _voices.Length; v++)
                {
                    if (!_voices[v].Active || _voices[v].Channel != i) continue;
                    articulation = _voices[v].ArticulationDb;
                    sample = _voices[v].SampleDb;
                    haveVoice = true;
                    break;
                }

                float volumeDb = (float)(20.0 * Math.Log10(Math.Max(0.0001f, c.Volume)));
                float expressionDb = (float)(20.0 * Math.Log10(Math.Max(0.0001f, c.Expression)));
                float total = volumeDb + expressionDb + articulation + sample + c.TrimDb;

                sb.AppendLine(string.Format(
                    "  ch {0,2} {1,-20} volume {2,6:F1}  expression {3,5:F1}  articulation {4,6:F1}{5}  " +
                    "sample {6,6:F1}  trim {7,5:F1}  =  {8,6:F1} dB",
                    i,
                    c.Instrument != null ? (c.Instrument.Name ?? "<unnamed>") : "(none)",
                    volumeDb, expressionDb, articulation, haveVoice ? " " : "?",
                    sample, c.TrimDb, total));
            }
            sb.AppendLine("  articulation and sample columns read from a sounding voice; ? means none was" +
                          " playing, so those terms are unknown for that channel right now.");
            return sb.ToString();
        }

        /// <summary>
        /// Everything affecting a channel's pitch: band transpose, bend range, the current
        /// bend, and the modulation depths of any sounding voice. Start here when notes
        /// play at the wrong pitch.
        /// </summary>
        public string DescribePitchChain()
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.AppendLine("Pitch chain:");

            for (int i = 0; i < _channels.Length; i++)
            {
                MidiChannel c = _channels[i];
                ChannelMeter m = _meters[i];
                bool interesting = m.Voices > 0 || c.TransposeSemitones != 0f ||
                                   c.PitchBendSemitones != 0f || c.PitchBendRange != 2f ||
                                   c.Program != 0 || c.BankMsb != 0 || c.BankLsb != 0;
                if (!interesting) continue;

                string voiceInfo = "";
                for (int v = 0; v < _voices.Length; v++)
                {
                    if (!_voices[v].Active || _voices[v].Channel != i) continue;
                    voiceInfo = string.Format("  note {0}  ratio {1:F4}",
                        _voices[v].Note, _voices[v].PitchRatio);
                    break;
                }

                sb.AppendLine(string.Format(
                    "  ch {0,2} {1,-18} transpose {2,5:F1}  bend {3,5:F2} of +/-{4,4:F1}  modwheel {5:F2}{6}",
                    i, c.Instrument != null ? (c.Instrument.Name ?? "<unnamed>") : "(none)",
                    c.TransposeSemitones, c.PitchBendSemitones, c.PitchBendRange, c.ModWheel, voiceInfo));
            }
            sb.AppendLine("  ratio is the playback rate against the sample's own rate, " +
                          "after unity note, tune, transpose, bend, LFO and EG2.");
            return sb.ToString();
        }

        public void ClearMutesAndSolos()
        {
            if (_muted == null) return;
            for (int i = 0; i < _muted.Length; i++) { _muted[i] = false; _solo[i] = false; }
            RefreshAudibility();
        }

        void RefreshAudibility()
        {
            _anySolo = false;
            for (int i = 0; i < _solo.Length; i++) if (_solo[i]) { _anySolo = true; break; }

            for (int i = 0; i < _channels.Length; i++)
            {
                bool audible = !_muted[i] && (!_anySolo || _solo[i]);
                if (_channels[i].Audible == audible) continue;
                _channels[i].Audible = audible;
                UpdateChannelGains(i);
            }
        }

        /// <summary>Meter for a channel, or null if the index is out of range.</summary>
        public ChannelMeter GetChannelMeter(int channel)
        {
            if (_meters == null || channel < 0 || channel >= _meters.Length) return null;
            return _meters[channel];
        }

        readonly int _sampleRate;
        readonly Voice[] _voices;
        readonly MidiChannel[] _channels;
        IDlsBank _bank;
        long _voiceCounter;

        public DlsSynth(int sampleRate, int maxVoices)
            : this(sampleRate, maxVoices, DefaultChannelCount) { }

        public DlsSynth(int sampleRate, int maxVoices, int channelCount)
        {
            _sampleRate = sampleRate > 0 ? sampleRate : 44100;
            if (maxVoices < 4) maxVoices = 4;
            if (channelCount < 1) channelCount = DefaultChannelCount;

            _voices = new Voice[maxVoices];
            for (int i = 0; i < _voices.Length; i++) _voices[i] = new Voice();

            _channels = new MidiChannel[channelCount];
            _meters = new ChannelMeter[channelCount];
            _muted = new bool[channelCount];
            _solo = new bool[channelCount];
            for (int i = 0; i < _channels.Length; i++)
            {
                _channels[i] = new MidiChannel();
                _meters[i] = new ChannelMeter();
            }
            Reset();
        }

        public IDlsBank Bank { get { return _bank; } }
        public int SampleRate { get { return _sampleRate; } }

        public int ActiveVoiceCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _voices.Length; i++) if (_voices[i].Active) n++;
                return n;
            }
        }

        public void SetBank(IDlsBank bank)
        {
            AllSoundOff();
            _bank = bank;
            RefreshAllInstruments();
        }

        /// <summary>
        /// Re-resolves every channel's instrument. Call after adding or removing a layer
        /// from a <see cref="DlsBankStack"/>, since the bank reference itself doesn't change.
        /// </summary>
        public void RefreshAllInstruments()
        {
            for (int i = 0; i < _channels.Length; i++) RefreshInstrument(i);
        }

        public void Reset()
        {
            AllSoundOff();
            for (int i = 0; i < _channels.Length; i++)
            {
                MidiChannel c = _channels[i];
                c.BankMsb = 0;
                c.BankLsb = 0;
                c.Program = 0;
                c.Volume = 100f / 127f;
                c.Expression = 1f;
                c.Pan = 0f;
                c.Sustain = false;
                c.PitchBendSemitones = 0f;
                c.PitchBendRange = 2f;
                c.TransposeSemitones = 0f;
                c.ModWheel = 0f;
                c.RpnMsb = 127;
                c.RpnLsb = 127;
                c.IsDrum = i == DrumChannel;
                c.Audible = !_muted[i] && (!_anySolo || _solo[i]);
                RefreshInstrument(i);
            }
        }

        // ------------------------------------------------------------ events

        public void Dispatch(MidiEvent ev)
        {
            int ch = ev.Channel;
            switch (ev.Command)
            {
                case 0x80: NoteOff(ch, ev.Data1); break;
                case 0x90:
                    if (ev.Data2 == 0) NoteOff(ch, ev.Data1);
                    else NoteOn(ch, ev.Data1, ev.Data2);
                    break;
                case 0xB0: ControlChange(ch, ev.Data1, ev.Data2); break;
                case 0xC0: ProgramChange(ch, ev.Data1); break;
                case 0xE0: PitchBend(ch, ev.Data1 | (ev.Data2 << 7)); break;
                // 0xA0 poly aftertouch and 0xD0 channel pressure are ignored.
            }
        }

        public void NoteOn(int channel, int note, int velocity)
        {
            if (_bank == null || channel < 0 || channel >= _channels.Length) return;
            if (velocity <= 0) { NoteOff(channel, note); return; }
            note &= 0x7F;

            MidiChannel c = _channels[channel];
            DlsInstrument ins = c.Instrument;
            if (ins == null) return;

            DlsRegion region = ins.FindRegion(note, velocity);
            if (region == null || region.Wave == null || region.Wave.FrameCount < 2) return;

            if (region.KeyGroup != 0) KillKeyGroup(channel, region.KeyGroup, note);

            Voice v = AllocateVoice();
            v.Start(region.Wave, region.ResolvedInfo, region.ResolvedArticulation,
                    c, channel, note, velocity, region.KeyGroup, _sampleRate, ++_voiceCounter);
        }

        public void NoteOff(int channel, int note)
        {
            if (channel < 0 || channel >= _channels.Length) return;
            note &= 0x7F;
            MidiChannel c = _channels[channel];

            // Release only the oldest sounding voice at this pitch, pairing each note-off
            // with one note-on. Releasing every match means a short note ending cuts off a
            // longer one that is still meant to be sounding — overlapping unisons are
            // common in style content, so this drops audible notes.
            // A voice already held by the pedal has had its note-off; skipping those
            // pairs each note-off with a distinct voice. Without that, repeated notes at
            // one pitch under a held pedal all re-mark the same oldest voice, and every
            // other voice is left with no note-off at all — it is never released, not
            // even when the pedal lifts, and voices pile up until they run out.
            Voice oldest = null;
            for (int i = 0; i < _voices.Length; i++)
            {
                Voice v = _voices[i];
                if (!v.Active || v.Channel != channel || v.Note != note || v.Releasing) continue;
                if (v.HeldBySustain) continue;
                if (oldest == null || v.Serial < oldest.Serial) oldest = v;
            }
            if (oldest == null) return;

            if (c.Sustain) oldest.HeldBySustain = true;
            else oldest.Release();
        }

        public void ControlChange(int channel, int cc, int value)
        {
            if (channel < 0 || channel >= _channels.Length) return;
            MidiChannel c = _channels[channel];

            switch (cc)
            {
                case 0: c.BankMsb = value; RefreshInstrument(channel); break;
                case 32: c.BankLsb = value; RefreshInstrument(channel); break;
                case 1: c.ModWheel = value / 127f; break;      // mod wheel
                case 6:                                       // data entry MSB
                    if (c.RpnMsb == 0 && c.RpnLsb == 0) c.PitchBendRange = Math.Max(1, value);
                    break;
                case 7: c.Volume = value / 127f; UpdateChannelGains(channel); break;
                case 10: c.Pan = (value - 64) / 63f; UpdateChannelGains(channel); break;
                case 11: c.Expression = value / 127f; UpdateChannelGains(channel); break;
                case 64:
                    c.Sustain = value >= 64;
                    if (!c.Sustain) ReleaseSustained(channel);
                    break;
                case 100: c.RpnLsb = value; break;
                case 101: c.RpnMsb = value; break;
                case 120: AllSoundOff(channel); break;
                case 121: ResetControllers(channel); break;
                case 123: AllNotesOff(channel); break;
            }
        }

        public void ProgramChange(int channel, int program)
        {
            if (channel < 0 || channel >= _channels.Length) return;
            _channels[channel].Program = program & 0x7F;
            RefreshInstrument(channel);
        }

        public void PitchBend(int channel, int value14)
        {
            if (channel < 0 || channel >= _channels.Length) return;
            MidiChannel c = _channels[channel];
            c.PitchBendSemitones = (value14 - 8192) / 8192f * c.PitchBendRange;

            for (int i = 0; i < _voices.Length; i++)
                if (_voices[i].Active && _voices[i].Channel == channel)
                    _voices[i].UpdatePitch(_sampleRate);
        }

        /// <summary>Forces a channel onto a specific bank/program, bypassing MIDI messages.</summary>
        /// <summary>
        /// Transposes a channel in semitones. Applied to pitch only, so a transposed drum
        /// channel still plays the sample the note selected.
        /// </summary>
        public void SetChannelTranspose(int channel, float semitones)
        {
            if (channel < 0 || channel >= _channels.Length) return;
            if (semitones < -48f) semitones = -48f;
            if (semitones > 48f) semitones = 48f;
            _channels[channel].TransposeSemitones = semitones;
            RefreshChannelPitch(channel);
        }

        public float GetChannelTranspose(int channel)
        {
            return channel >= 0 && channel < _channels.Length ? _channels[channel].TransposeSemitones : 0f;
        }

        /// <summary>Pitch bend range in semitones, as the band or an RPN 0 message sets it.</summary>
        public void SetChannelPitchBendRange(int channel, float semitones)
        {
            if (channel < 0 || channel >= _channels.Length) return;
            if (semitones < 0.1f) semitones = 0.1f;
            if (semitones > 48f) semitones = 48f;

            MidiChannel c = _channels[channel];
            float fraction = c.PitchBendRange > 0f ? c.PitchBendSemitones / c.PitchBendRange : 0f;
            c.PitchBendRange = semitones;
            c.PitchBendSemitones = fraction * semitones;
            RefreshChannelPitch(channel);
        }

        public float GetChannelPitchBendRange(int channel)
        {
            return channel >= 0 && channel < _channels.Length ? _channels[channel].PitchBendRange : 2f;
        }

        void RefreshChannelPitch(int channel)
        {
            for (int i = 0; i < _voices.Length; i++)
                if (_voices[i].Active && _voices[i].Channel == channel)
                    _voices[i].UpdatePitch(_sampleRate);
        }

        public void SetProgram(int channel, int bankMsb, int bankLsb, int program, bool drum)
        {
            if (channel < 0 || channel >= _channels.Length) return;
            MidiChannel c = _channels[channel];
            c.BankMsb = bankMsb;
            c.BankLsb = bankLsb;
            c.Program = program & 0x7F;
            c.IsDrum = drum;
            RefreshInstrument(channel);
        }

        public DlsInstrument GetChannelInstrument(int channel)
        {
            if (channel < 0 || channel >= _channels.Length) return null;
            return _channels[channel].Instrument;
        }

        public void AllNotesOff()
        {
            for (int i = 0; i < _channels.Length; i++) AllNotesOff(i);
        }

        public void AllNotesOff(int channel)
        {
            for (int i = 0; i < _voices.Length; i++)
            {
                Voice v = _voices[i];
                if (v.Active && v.Channel == channel) v.Release();
            }
        }

        public void AllSoundOff()
        {
            for (int i = 0; i < _voices.Length; i++) _voices[i].Kill();
        }

        public void AllSoundOff(int channel)
        {
            for (int i = 0; i < _voices.Length; i++)
                if (_voices[i].Channel == channel) _voices[i].Kill();
        }

        void ResetControllers(int channel)
        {
            MidiChannel c = _channels[channel];
            c.Expression = 1f;
            c.Sustain = false;
            c.PitchBendSemitones = 0f;
            ReleaseSustained(channel);
            UpdateChannelGains(channel);
        }

        void ReleaseSustained(int channel)
        {
            for (int i = 0; i < _voices.Length; i++)
            {
                Voice v = _voices[i];
                if (v.Active && v.Channel == channel && v.HeldBySustain)
                {
                    v.HeldBySustain = false;
                    v.Release();
                }
            }
        }

        void KillKeyGroup(int channel, int keyGroup, int exceptNote)
        {
            for (int i = 0; i < _voices.Length; i++)
            {
                Voice v = _voices[i];
                if (v.Active && v.Channel == channel && v.KeyGroup == keyGroup && v.Note != exceptNote)
                    v.Release(0.02f);
            }
        }

        void RefreshInstrument(int channel)
        {
            MidiChannel c = _channels[channel];
            if (_bank == null) { c.Instrument = null; return; }
            int bank = ((c.BankMsb & 0x7F) << 7) | (c.BankLsb & 0x7F);
            c.Instrument = _bank.FindInstrument(bank, c.Program, c.IsDrum);
        }

        void UpdateChannelGains(int channel)
        {
            for (int i = 0; i < _voices.Length; i++)
                if (_voices[i].Active && _voices[i].Channel == channel)
                    _voices[i].UpdateGains();
        }

        Voice AllocateVoice()
        {
            // Free voice first.
            for (int i = 0; i < _voices.Length; i++)
                if (!_voices[i].Active) return _voices[i];

            // A voice on a muted or un-soloed channel is inaudible, so it is the cheapest
            // thing to take before stealing from something the listener can hear.
            for (int i = 0; i < _voices.Length; i++)
            {
                Voice v = _voices[i];
                int channel = v.Channel;
                if (channel >= 0 && channel < _channels.Length && !_channels[channel].Audible) return v;
            }

            // Otherwise steal the quietest releasing voice, else the oldest.
            Voice best = null;
            float bestLevel = float.MaxValue;
            for (int i = 0; i < _voices.Length; i++)
            {
                Voice v = _voices[i];
                if (!v.Releasing) continue;
                if (v.EnvelopeLevel < bestLevel) { bestLevel = v.EnvelopeLevel; best = v; }
            }
            if (best != null) return best;

            best = _voices[0];
            for (int i = 1; i < _voices.Length; i++)
                if (_voices[i].Serial < best.Serial) best = _voices[i];
            return best;
        }

        // ------------------------------------------------------------ render

        /// <summary>
        /// Mixes <paramref name="frames"/> frames into an interleaved buffer.
        /// The region written is fully overwritten, not added to.
        /// </summary>
        public void Render(float[] buffer, int offset, int frames, int outChannels)
        {
            if (buffer == null || frames <= 0 || outChannels <= 0) return;
            int count = frames * outChannels;
            if (offset < 0 || offset + count > buffer.Length) return;

            Array.Clear(buffer, offset, count);

            for (int i = 0; i < _voices.Length; i++)
            {
                Voice v = _voices[i];
                if (v.Active) v.Render(buffer, offset, frames, outChannels, LfoEnabled);
            }

            if (MetersEnabled) UpdateMeters();

            bool stereo = outChannels > 1;
            float gainL = MasterGain * (stereo ? _balanceL : 1f);
            float gainR = MasterGain * (stereo ? _balanceR : 1f);

            int lane = 0;
            for (int i = 0; i < count; i++)
            {
                float s = buffer[offset + i] * (lane == 0 ? gainL : gainR);
                // Soft knee limiter, keeps dense chords from hard-clipping.
                if (s > 0.95f) s = 0.95f + (s - 0.95f) / (1f + (s - 0.95f) * 4f);
                else if (s < -0.95f) s = -0.95f + (s + 0.95f) / (1f - (s + 0.95f) * 4f);
                if (s > 1f) s = 1f; else if (s < -1f) s = -1f;
                buffer[offset + i] = s;
                if (++lane >= outChannels) lane = 0;
            }
        }

        void UpdateMeters()
        {
            for (int i = 0; i < _meters.Length; i++) _meters[i].Begin();

            for (int i = 0; i < _voices.Length; i++)
            {
                Voice v = _voices[i];
                if (!v.Active) continue;
                int channel = v.Channel;
                if (channel < 0 || channel >= _meters.Length) continue;

                ChannelMeter m = _meters[channel];
                m.Voices++;
                m.SetNote(v.Note);

                float level = v.MeterLevel;
                if (level > m.Instant) m.Instant = level;
            }

            for (int i = 0; i < _meters.Length; i++)
            {
                ChannelMeter m = _meters[i];
                float instant = m.Instant * MasterGain;
                if (instant > 1f) instant = 1f;

                // Fast attack, slow release, so short percussive hits stay readable.
                m.Level = instant > m.Level ? instant : m.Level * 0.80f;
                if (m.Level < 0.0005f) m.Level = 0f;

                if (m.Level > m.Peak) m.Peak = m.Level;
                else m.Peak *= 0.97f;
            }
        }

        // ----------------------------------------------------- inner classes

        internal sealed class MidiChannel
        {
            public int BankMsb, BankLsb, Program;
            public float Volume = 100f / 127f;
            public float Expression = 1f;
            public float Pan;
            public bool Sustain;
            public float PitchBendSemitones;
            public float PitchBendRange = 2f;
            /// <summary>
            /// Band transpose, applied as a pitch offset rather than by shifting the note
            /// number, so it never changes which region or drum sample is selected.
            /// </summary>
            public float TransposeSemitones;
            public int RpnMsb = 127, RpnLsb = 127;
            /// <summary>CC1, 0..1. Scales the mod-wheel portion of the LFO depths.</summary>
            public float ModWheel;
            public bool IsDrum;
            public DlsInstrument Instrument;
            /// <summary>False when muted, or when another channel is soloed.</summary>
            public bool Audible = true;
            /// <summary>User trim in dB, applied on top of the content's own levels.</summary>
            public float TrimDb;
            public float TrimLinear = 1f;
        }

        internal sealed class Voice
        {
            public bool Active;
            public int Channel = -1;
            public int Note;
            public int KeyGroup;
            public bool HeldBySustain;
            public bool Releasing { get { return _stage == Stage.Release; } }
            public float EnvelopeLevel { get { return _env; } }

            /// <summary>Approximate output level: envelope times the louder pan side.</summary>
            /// <summary>Current playback rate multiplier, after every pitch influence.</summary>
            public double PitchRatio { get { return _increment; } }

            public float MeterLevel
            {
                get { return _env * _lfoGain * (_gainL > _gainR ? _gainL : _gainR); }
            }
            public long Serial;

            enum Stage { Attack, Decay, Sustain, Release }

            MidiChannel _chan;
            float[] _data;
            int _srcChannels;
            int _frameCount;

            double _pos;
            double _increment;
            double _baseRatio;          // pitch ratio ignoring bend
            bool _looping;
            double _loopStart, _loopEnd, _loopLength;
            bool _oneShot;              // drum-style: ignore note off

            Stage _stage;
            float _env;
            float _attackInc, _decayCoef, _releaseCoef, _sustain;
            float _velocityGain, _sampleGain;
            float _articulationDb, _sampleDb;

            // LFO. Updated every LfoUpdateInterval samples rather than per sample: at
            // musical rates a 0.7ms grid is far finer than the ear resolves, and it keeps
            // a trig call out of the inner loop.
            const int LfoUpdateInterval = 32;
            bool _lfoActive;
            double _lfoPhase, _lfoPhaseIncrement;
            int _lfoDelaySamples, _lfoElapsed, _lfoCountdown;
            float _lfoPitchCents, _lfoPitchCentsMod;
            float _lfoGainDb, _lfoGainDbMod;
            float _lfoGain = 1f;
            double _pitchRatioBase;         // increment before modulation

            // EG2: a second envelope whose output modulates pitch.
            bool _eg2Active;
            Stage _eg2Stage;
            float _eg2Level, _eg2AttackInc, _eg2DecayCoef, _eg2Sustain, _eg2ReleaseCoef;
            float _eg2ToPitchCents;
            public float ArticulationDb { get { return _articulationDb; } }
            public float SampleDb { get { return _sampleDb; } }
            int _outRate = 44100;
            float _pan;
            float _gainL, _gainR, _targetL, _targetR;
            const float GainSmoothing = 0.002f;

            public void Start(DlsWave wave, DlsSampleInfo info, DlsArticulation art,
                              MidiChannel chan, int channel, int note, int velocity,
                              int keyGroup, int outRate, long serial)
            {
                _chan = chan;
                _data = wave.Data;
                _srcChannels = wave.Channels;
                _frameCount = wave.FrameCount;

                Channel = channel;
                Note = note;
                KeyGroup = keyGroup;
                Serial = serial;
                _outRate = outRate;
                HeldBySustain = false;

                double semitones = (note - info.UnityNote) + info.FineTuneCents / 100.0;
                _baseRatio = (double)wave.SampleRate / outRate * Math.Pow(2.0, semitones / 12.0);

                _pos = 0.0;
                UpdatePitch(outRate);

                _lfoActive = art.HasLfo;
                _lfoPhase = 0.0;
                _lfoElapsed = 0;
                _lfoCountdown = 0;
                _lfoGain = 1f;
                _lfoPhaseIncrement = art.LfoFrequencyHz / outRate;
                _lfoDelaySamples = (int)(art.LfoStartDelaySeconds * outRate);
                _lfoPitchCents = art.LfoToPitchCents;
                _lfoPitchCentsMod = art.LfoToPitchCentsModWheel;
                _lfoGainDb = art.LfoToGainDb;
                _lfoGainDbMod = art.LfoToGainDbModWheel;

                _eg2Active = art.HasEg2;
                _eg2ToPitchCents = art.Eg2ToPitchCents;
                _eg2Sustain = Clamp01(art.Eg2SustainLevel);
                _eg2Level = art.Eg2AttackSeconds <= 0.0005f ? 1f : 0f;
                _eg2Stage = art.Eg2AttackSeconds <= 0.0005f ? Stage.Decay : Stage.Attack;
                _eg2AttackInc = art.Eg2AttackSeconds <= 0.0005f
                    ? 1f : LfoUpdateInterval / (art.Eg2AttackSeconds * outRate);
                _eg2DecayCoef = art.Eg2DecaySeconds <= 0.0005f
                    ? 0f : (float)Math.Exp(-6.9078 * LfoUpdateInterval / (art.Eg2DecaySeconds * outRate));
                _eg2ReleaseCoef = (float)Math.Exp(
                    -6.9078 * LfoUpdateInterval / (Math.Max(0.005f, art.Eg2ReleaseSeconds) * outRate));

                _looping = false;
                if (info.Loop != null && info.Loop.Length > 1)
                {
                    double start = info.Loop.Start;
                    double len = info.Loop.Length;
                    if (start >= 0 && start + len <= _frameCount)
                    {
                        _loopStart = start;
                        _loopLength = len;
                        _loopEnd = start + len;
                        _looping = true;
                    }
                }
                _oneShot = !_looping && chan.IsDrum;

                _sustain = Clamp01(art.SustainLevel);
                float attack = art.AttackSeconds;
                float decay = art.DecaySeconds;
                float release = Math.Max(0.005f, art.ReleaseSeconds);

                _attackInc = attack <= 0.0005f ? 1f : 1f / (attack * outRate);
                _decayCoef = decay <= 0.0005f ? 0f : (float)Math.Exp(-6.9078 / (decay * outRate));
                _releaseCoef = (float)Math.Exp(-6.9078 / (release * outRate));

                float v = velocity / 127f;
                // A bank that authors velocity-to-attenuation replaces the default curve;
                // applying both would double-count the dynamics.
                _velocityGain = art.HasVelocityToAttenuation
                    ? (float)Math.Pow(10.0, art.VelocityToAttenuationDb * (1f - v) / 20.0)
                    : v * v;
                _sampleGain = (float)Math.Pow(10.0, info.GainDb / 20.0) * art.AttenuationLinear;
                _articulationDb = art.AttenuationDb;
                _sampleDb = info.GainDb;
                _pan = Clamp(art.Pan, -1f, 1f);

                _env = _attackInc >= 1f ? 1f : 0f;
                _stage = _attackInc >= 1f ? Stage.Decay : Stage.Attack;

                UpdateGains();
                _gainL = _targetL;
                _gainR = _targetR;
                Active = true;
            }

            public void UpdatePitch(int outRate)
            {
                double bend = _chan != null
                    ? _chan.PitchBendSemitones + _chan.TransposeSemitones : 0.0;
                _pitchRatioBase = _baseRatio * (bend == 0.0 ? 1.0 : Math.Pow(2.0, bend / 12.0));
                _increment = _pitchRatioBase;
            }

            /// <summary>
            /// Advances the LFO and EG2 and applies them to pitch and gain. Both are
            /// stepped on the same coarse grid, so pitch is written once per update.
            /// </summary>
            void UpdateModulation(bool enabled)
            {
                _lfoCountdown = LfoUpdateInterval;

                float eg2Cents = 0f;
                if (enabled && _eg2Active)
                {
                    AdvanceEg2();
                    eg2Cents = _eg2ToPitchCents * _eg2Level;
                }

                if (!enabled || !_lfoActive)
                {
                    _lfoGain = 1f;
                    _increment = eg2Cents == 0f
                        ? _pitchRatioBase
                        : _pitchRatioBase * Math.Pow(2.0, eg2Cents / 1200.0);
                    return;
                }

                _lfoElapsed += LfoUpdateInterval;
                if (_lfoElapsed < _lfoDelaySamples)
                {
                    _lfoGain = 1f;
                    _increment = eg2Cents == 0f
                        ? _pitchRatioBase
                        : _pitchRatioBase * Math.Pow(2.0, eg2Cents / 1200.0);
                    return;
                }

                _lfoPhase += _lfoPhaseIncrement * LfoUpdateInterval;
                if (_lfoPhase >= 1.0) _lfoPhase -= Math.Floor(_lfoPhase);
                float wave = (float)Math.Sin(_lfoPhase * 2.0 * Math.PI);

                float mod = _chan != null ? _chan.ModWheel : 0f;

                float cents = wave * (_lfoPitchCents + _lfoPitchCentsMod * mod) + eg2Cents;
                _increment = cents == 0f
                    ? _pitchRatioBase
                    : _pitchRatioBase * Math.Pow(2.0, cents / 1200.0);

                float db = _lfoGainDb + _lfoGainDbMod * mod;
                _lfoGain = db == 0f ? 1f : (float)Math.Pow(10.0, wave * db / 20.0);
            }

            public void UpdateGains()
            {
                if (_chan == null) return;
                float amp = _chan.Audible
                    ? _velocityGain * _sampleGain * _chan.Volume * _chan.Expression * _chan.TrimLinear
                    : 0f;
                float pan = Clamp(_pan + _chan.Pan, -1f, 1f);
                // Equal-power pan.
                double angle = (pan + 1.0) * 0.25 * Math.PI;
                _targetL = amp * (float)Math.Cos(angle);
                _targetR = amp * (float)Math.Sin(angle);
            }

            public void Release()
            {
                Release(-1f);
            }

            public void Release(float overrideSeconds)
            {
                if (!Active) return;
                if (_oneShot) return;                 // let unlooped percussion ring out
                if (overrideSeconds > 0f)
                    _releaseCoef = (float)Math.Exp(-6.9078 / (overrideSeconds * Math.Max(1, _outRate)));
                _stage = Stage.Release;
                _eg2Stage = Stage.Release;
            }

            public void Kill()
            {
                Active = false;
                Channel = -1;
                _env = 0f;
                _data = null;
            }

            public void Render(float[] buf, int offset, int frames, int outChannels, bool lfoEnabled)
            {
                float[] src = _data;
                if (src == null || src.Length == 0) { Kill(); return; }

                int srcCh = _srcChannels;
                int last = _frameCount - 1;
                bool stereoOut = outChannels > 1;

                for (int i = 0; i < frames; i++)
                {
                    if (--_lfoCountdown <= 0) UpdateModulation(lfoEnabled);

                    // --- envelope ---
                    switch (_stage)
                    {
                        case Stage.Attack:
                            _env += _attackInc;
                            if (_env >= 1f) { _env = 1f; _stage = Stage.Decay; }
                            break;
                        case Stage.Decay:
                            if (_decayCoef <= 0f) { _env = _sustain; _stage = Stage.Sustain; }
                            else
                            {
                                _env *= _decayCoef;
                                if (_env <= _sustain) { _env = _sustain; _stage = Stage.Sustain; }
                            }
                            break;
                        case Stage.Sustain:
                            break;
                        case Stage.Release:
                            _env *= _releaseCoef;
                            break;
                    }

                    if (_env <= 0.00015f && _stage != Stage.Attack) { Kill(); return; }

                    // --- sample fetch with linear interpolation ---
                    int i0 = (int)_pos;
                    if (i0 < 0) i0 = 0;
                    if (i0 >= last)
                    {
                        if (!_looping) { Kill(); return; }
                        i0 = (int)_loopStart;
                        _pos = _loopStart;
                    }

                    float frac = (float)(_pos - i0);
                    int i1 = i0 + 1;
                    if (_looping && i1 >= _loopEnd) i1 = (int)_loopStart;
                    else if (i1 > last) i1 = last;

                    float l, r;
                    if (srcCh == 1)
                    {
                        float a = src[i0];
                        l = a + (src[i1] - a) * frac;
                        r = l;
                    }
                    else
                    {
                        int a0 = i0 * srcCh, a1 = i1 * srcCh;
                        float la = src[a0], ra = src[a0 + 1];
                        l = la + (src[a1] - la) * frac;
                        r = ra + (src[a1 + 1] - ra) * frac;
                    }

                    // --- gain smoothing, avoids zipper noise on CC changes ---
                    _gainL += (_targetL - _gainL) * GainSmoothing;
                    _gainR += (_targetR - _gainR) * GainSmoothing;

                    float level = _env * _lfoGain;
                    int idx = offset + i * outChannels;
                    buf[idx] += l * level * _gainL;
                    if (stereoOut) buf[idx + 1] += r * level * _gainR;

                    // --- advance ---
                    _pos += _increment;
                    if (_looping)
                    {
                        while (_pos >= _loopEnd) _pos -= _loopLength;
                    }
                    else if (_pos >= last)
                    {
                        Kill();
                        return;
                    }
                }
            }

            void AdvanceEg2()
            {
                switch (_eg2Stage)
                {
                    case Stage.Attack:
                        _eg2Level += _eg2AttackInc;
                        if (_eg2Level >= 1f) { _eg2Level = 1f; _eg2Stage = Stage.Decay; }
                        break;
                    case Stage.Decay:
                        if (_eg2DecayCoef <= 0f) { _eg2Level = _eg2Sustain; _eg2Stage = Stage.Sustain; }
                        else
                        {
                            _eg2Level *= _eg2DecayCoef;
                            if (_eg2Level <= _eg2Sustain) { _eg2Level = _eg2Sustain; _eg2Stage = Stage.Sustain; }
                        }
                        break;
                    case Stage.Sustain:
                        break;
                    case Stage.Release:
                        _eg2Level *= _eg2ReleaseCoef;
                        break;
                }
            }

            static float Clamp01(float v) { return v < 0f ? 0f : (v > 1f ? 1f : v); }
            static float Clamp(float v, float lo, float hi) { return v < lo ? lo : (v > hi ? hi : v); }
        }
    }
}
