#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace DirectMusicLite
{
    [CustomEditor(typeof(DirectMusicSegmentPlayer))]
    public class DirectMusicSegmentPlayerEditor : Editor
    {
        static readonly string[] NoteNames = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };

        struct Row
        {
            public int Channel;
            public int PChannel;
            public int Voices;
            public float Level;
            public float Peak;
            public string Instrument;
            public string Part;
            public string Notes;
            public bool Muted;
            public bool Solo;
            public bool Audible;
            public float TrimDb;
            public int Variation;
        }

        readonly List<Row> _rows = new List<Row>();
        DirectMusicSegmentPlayer _player;

        bool _showInactive = true;
        bool _showOnlyAssigned = false;
        bool _showNotes = true;
        bool _compact = false;
        bool _anySolo;
        int _grooveDraft = -1;

        // Snapshotted transport state, for the same reason as the rows.
        bool _playing;
        string _patternName = "-";
        int _measure;
        double _tempo;
        int _voices, _maxVoices;
        int _positionTicks, _loops;

        public override bool RequiresConstantRepaint()
        {
            return Application.isPlaying;
        }

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            DirectMusicSegmentPlayer player = (DirectMusicSegmentPlayer)target;
            _player = player;

            EditorGUILayout.Space();
            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("Channel activity appears here in play mode.", MessageType.None);
                return;
            }

            if (Event.current.type == EventType.Layout)
            {
                Snapshot(player);
            }

            DrawTransport(player);
            EditorGUILayout.Space();
            DrawChannels();
            EditorGUILayout.Space();
            DrawDiagnosticButtons(player);
        }

        void Snapshot(DirectMusicSegmentPlayer player)
        {
            _playing = player.IsPlaying;
            DmPattern pattern = player.CurrentPattern;
            // A sequence-only segment composes nothing, so there is no pattern to
            // name. Saying so beats a bare dash, which reads as something failing.
            _patternName = pattern != null ? (pattern.Name ?? "<unnamed>")
                         : (player.IsSequenceOnly ? "sequence track" : "-");
            _measure = player.CurrentMeasure;
            _tempo = player.CurrentTempo;
            _voices = player.ActiveVoiceCount;
            _maxVoices = player.MaxVoices;
            _anySolo = player.AnySolo;
            _positionTicks = player.SegmentPositionTicks;

            // While the segment owns groove, keep the slider showing the live value so it
            // doesn't sit stale and then fight the command track when nudged.
            if (player.followSegmentCommands) _grooveDraft = player.CurrentGrooveLevel;
            _loops = player.LoopsPlayed;

            _rows.Clear();
            for (int channel = 0; channel < player.SynthChannelCount; channel++)
            {
                DlsSynth.ChannelMeter meter = player.GetChannelMeter(channel);
                if (meter == null) continue;

                int pchannel;
                string instrument, part;
                bool mapped = player.TryDescribeChannel(channel, out pchannel, out instrument, out part);
                string detail = player.PartDetail;


                bool show = _showInactive ? (mapped || meter.IsActive) : meter.IsActive;
                if (_showOnlyAssigned && string.IsNullOrEmpty(part)) show = false;
                if (!show) continue;

                Row row = new Row
                {
                    Channel = channel,
                    PChannel = pchannel,
                    Voices = meter.Voices,
                    Level = meter.Level,
                    Peak = meter.Peak,
                    Instrument = instrument ?? "(unassigned)",
                    Part = part == null ? "" : (string.IsNullOrEmpty(detail) ? part : part + "  -  " + detail),
                    Notes = _showNotes ? DescribeNotes(meter) : "",
                    Muted = player.IsChannelMuted(channel),
                    Solo = player.IsChannelSolo(channel),
                    Audible = player.IsChannelAudible(channel),
                    TrimDb = player.GetChannelTrim(channel),
                    Variation = player.GetChannelVariation(channel)
                };
                _rows.Add(row);
            }
        }

        void DrawTransport(DirectMusicSegmentPlayer player)
        {
            EditorGUILayout.LabelField("Transport", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(_playing ? "Playing" : "Stopped", GUILayout.Width(60));
            EditorGUILayout.LabelField("pattern: " + _patternName);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("measure " + _measure, GUILayout.Width(90));
            EditorGUILayout.LabelField(_tempo.ToString("F1") + " bpm", GUILayout.Width(90));
            EditorGUILayout.LabelField("voices " + _voices + "/" + _maxVoices);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("tick " + _positionTicks, GUILayout.Width(90));
            EditorGUILayout.LabelField("loops " + _loops, GUILayout.Width(90));
            EditorGUILayout.LabelField("");
            EditorGUILayout.EndHorizontal();

            // Voice headroom: at the ceiling, notes are being stolen.
            float fraction = _maxVoices > 0 ? Mathf.Clamp01(_voices / (float)_maxVoices) : 0f;
            Rect voiceRect = GUILayoutUtility.GetRect(10, 6, GUILayout.ExpandWidth(true));
            DrawBar(voiceRect, fraction, 0f,
                    fraction > 0.9f ? new Color(0.9f, 0.4f, 0.3f) : new Color(0.4f, 0.6f, 0.9f));

            if (_grooveDraft < 0) _grooveDraft = player.CurrentGrooveLevel;
            int groove = EditorGUILayout.IntSlider(
                player.followSegmentCommands ? "Groove (from segment)" : "Groove", _grooveDraft, 1, 100);
            if (groove != _grooveDraft)
            {
                _grooveDraft = groove;
                player.SetGrooveLevel(groove);
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(_playing ? "Stop" : "Play"))
            {
                if (player.IsPlaying) player.Stop(); else player.Play();
            }
            if (GUILayout.Button("At start")) player.PlayAtStart();
            if (GUILayout.Button("Fill")) player.Fill();
            if (GUILayout.Button("Break")) player.Break();
            if (GUILayout.Button("End")) player.End();
            EditorGUILayout.EndHorizontal();
        }

        void DrawChannels()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Channels", EditorStyles.boldLabel, GUILayout.Width(70));
            _showInactive = GUILayout.Toggle(_showInactive, "show idle", EditorStyles.miniButton, GUILayout.Width(64));
            _showOnlyAssigned = GUILayout.Toggle(_showOnlyAssigned, "only assigned",
                                                 EditorStyles.miniButton, GUILayout.Width(88));
            _showNotes = GUILayout.Toggle(_showNotes, "notes", EditorStyles.miniButton, GUILayout.Width(46));
            _compact = GUILayout.Toggle(_compact, "compact", EditorStyles.miniButton, GUILayout.Width(64));
            if (GUILayout.Button(_anySolo ? "clear solo" : "unmute all", EditorStyles.miniButton,
                                 GUILayout.Width(76)))
                _player.ClearMutesAndSolos();
            EditorGUILayout.EndHorizontal();

            if (_rows.Count == 0)
            {
                string reason;
                if (_showOnlyAssigned)
                    reason = "No channels have a style part assigned. Untick \"only assigned\" to " +
                             "see band entries too, or check DumpChannelMap().";
                else if (!_showInactive)
                    reason = "Nothing sounding right now. Tick \"show idle\" to list mapped channels too.";
                else
                    reason = "No channels mapped yet. Press Play, or check DumpChannelMap().";
                EditorGUILayout.HelpBox(reason, MessageType.Info);
                return;
            }

            for (int i = 0; i < _rows.Count; i++) DrawChannelRow(_rows[i]);
        }

        // Fixed control count: two lines of three controls, always, whatever the state.
        void DrawChannelRow(Row row)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.BeginHorizontal();
            string label = row.PChannel >= 0
                ? string.Format("{0,2}  P{1}", row.Channel, row.PChannel)
                : string.Format("{0,2}", row.Channel);
            EditorGUILayout.LabelField(label, GUILayout.Width(54));

            bool mute = GUILayout.Toggle(row.Muted, "M", EditorStyles.miniButton, GUILayout.Width(22));
            if (mute != row.Muted) _player.SetChannelMute(row.Channel, mute);

            bool solo = GUILayout.Toggle(row.Solo, "S", EditorStyles.miniButton, GUILayout.Width(22));
            if (solo != row.Solo) _player.SetChannelSolo(row.Channel, solo);

            EditorGUILayout.LabelField(row.Instrument, GUILayout.Width(112));

            Rect bar = GUILayoutUtility.GetRect(60, 14, GUILayout.ExpandWidth(true));
            Color colour = !row.Audible ? new Color(0.35f, 0.35f, 0.38f)
                : (row.Voices > 0 ? new Color(0.35f, 0.8f, 0.45f) : new Color(0.35f, 0.55f, 0.75f));
            DrawBar(bar, row.Level, row.Peak, colour);

            EditorGUILayout.LabelField(row.Voices > 0 ? row.Voices.ToString() : "-", GUILayout.Width(24));
            EditorGUILayout.EndHorizontal();

            if (!_compact)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(58);
                EditorGUILayout.LabelField("trim", EditorStyles.miniLabel, GUILayout.Width(28));
                float trim = EditorGUILayout.Slider(row.TrimDb, -24f, 6f);
                if (Mathf.Abs(trim - row.TrimDb) > 0.01f) _player.SetChannelTrim(row.Channel, trim);
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(58);
                string detail = row.Part;
                if (row.Variation >= 0)
                    detail = string.IsNullOrEmpty(detail) ? "var " + row.Variation
                                                          : detail + "  -  var " + row.Variation;
                if (!string.IsNullOrEmpty(row.Notes))
                    detail = string.IsNullOrEmpty(detail) ? row.Notes : detail + "   " + row.Notes;
                EditorGUILayout.LabelField(detail, EditorStyles.miniLabel, GUILayout.ExpandWidth(true));
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndVertical();
        }

        static string DescribeNotes(DlsSynth.ChannelMeter meter)
        {
            if (meter.Voices <= 0 || meter.LowestNote < 0) return "";

            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            int listed = 0;
            for (int note = meter.LowestNote; note <= meter.HighestNote && listed < 8; note++)
            {
                if (!meter.IsNoteOn(note)) continue;
                if (listed > 0) sb.Append(' ');
                sb.Append(NoteNames[note % 12]).Append(note / 12 - 1);
                listed++;
            }
            if (listed == 8) sb.Append(" ...");
            return sb.ToString();
        }

        static void DrawBar(Rect rect, float level, float peak, Color colour)
        {
            EditorGUI.DrawRect(rect, new Color(0.15f, 0.15f, 0.15f, 0.6f));

            if (level > 0f)
            {
                Rect fill = new Rect(rect.x, rect.y, rect.width * Mathf.Clamp01(level), rect.height);
                EditorGUI.DrawRect(fill, colour);
            }

            if (peak > 0.01f)
            {
                float x = rect.x + rect.width * Mathf.Clamp01(peak);
                Rect tick = new Rect(Mathf.Min(x, rect.xMax - 2f), rect.y, 2f, rect.height);
                EditorGUI.DrawRect(tick, new Color(0.95f, 0.95f, 0.95f, 0.8f));
            }
        }

        void DrawDiagnosticButtons(DirectMusicSegmentPlayer player)
        {
            EditorGUILayout.LabelField("Diagnostics", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Channel map")) player.DumpChannelMap();
            if (GUILayout.Button("Notes")) player.DumpNoteResolution();
            if (GUILayout.Button("Groove map")) player.DumpGrooveMap();
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Banks")) player.DumpBankResolution();
            if (GUILayout.Button("Articulation")) player.DumpArticulation();
            if (GUILayout.Button("Gain")) player.DumpGainChain();
            if (GUILayout.Button("Envelopes")) player.DumpEnvelopes();
            if (GUILayout.Button("Pitch")) player.DumpPitchChain();
            if (GUILayout.Button("Segment")) player.DumpSegment();
            if (GUILayout.Button("Style")) player.DumpStyle();
            EditorGUILayout.EndHorizontal();
        }
    }
}
#endif