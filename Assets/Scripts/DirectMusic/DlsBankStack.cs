using System.Collections.Generic;
using System.Text;

namespace DirectMusicLite
{
    public sealed class DlsBankStack : IDlsBank
    {
        readonly List<DlsFile> _layers = new List<DlsFile>();
        readonly Dictionary<int, DlsInstrument> _cache = new Dictionary<int, DlsInstrument>();

        public int LayerCount { get { return _layers.Count; } }
        public IList<DlsFile> Layers { get { return _layers.AsReadOnly(); } }

        public DlsBankStack() { }

        public DlsBankStack(params DlsFile[] layers)
        {
            if (layers == null) return;
            for (int i = 0; i < layers.Length; i++) Add(layers[i]);
        }

        /// <summary>Adds a collection on top, so it overrides everything already stacked.</summary>
        public void Add(DlsFile bank)
        {
            if (bank == null) return;
            _layers.Add(bank);
            _cache.Clear();
        }

        /// <summary>Adds a collection at the bottom, as a fallback beneath the current stack.</summary>
        public void AddBase(DlsFile bank)
        {
            if (bank == null) return;
            _layers.Insert(0, bank);
            _cache.Clear();
        }

        public bool Remove(DlsFile bank)
        {
            bool removed = _layers.Remove(bank);
            if (removed) _cache.Clear();
            return removed;
        }

        public void Clear()
        {
            _layers.Clear();
            _cache.Clear();
        }

        /// <summary>
        /// Resolves a bank/program pair across every layer.
        /// </summary>
        public DlsInstrument FindInstrument(int bank, int program, bool drum)
        {
            int key = CacheKey(bank, program, drum);
            DlsInstrument cached;
            if (_cache.TryGetValue(key, out cached)) return cached;

            DlsInstrument result = Resolve(bank, program, drum);
            _cache[key] = result;
            return result;
        }

        DlsInstrument Resolve(int bank, int program, bool drum)
        {
            DlsInstrument found;

            // exact match.
            found = SearchLayers(bank, program, drum);
            if (found != null) return found;

            // same program in bank 0 — most banks ignore bank select entirely.
            found = SearchLayers(0, program, drum);
            if (found != null) return found;

            if (drum)
            {
                // the conventional GM drum slot, then any kit at all.
                found = SearchLayers(0, 0, true);
                if (found != null) return found;

                for (int i = _layers.Count - 1; i >= 0; i--)
                {
                    DlsInstrument kit = _layers[i].FirstDrumInstrument;
                    if (kit != null) return kit;
                }

                found = SearchLayers(bank, program, false);
                if (found != null) return found;
            }

            // fallback to any default
            found = SearchLayers(0, 0, false);
            if (found != null) return found;

            for (int i = _layers.Count - 1; i >= 0; i--)
            {
                DlsInstrument first = _layers[i].FirstInstrument;
                if (first != null) return first;
            }
            return null;
        }

        DlsInstrument SearchLayers(int bank, int program, bool drum)
        {
            for (int i = _layers.Count - 1; i >= 0; i--)
            {
                DlsInstrument ins = _layers[i].FindExact(bank, program, drum);
                if (ins != null) return ins;
            }
            return null;
        }

        static int CacheKey(int bank, int program, bool drum)
        {
            return (drum ? 1 << 24 : 0) | ((bank & 0x3FFF) << 8) | (program & 0x7F);
        }

        public IEnumerable<DlsInstrument> AllInstruments
        {
            get
            {
                for (int i = 0; i < _layers.Count; i++)
                    for (int j = 0; j < _layers[i].Instruments.Count; j++)
                        yield return _layers[i].Instruments[j];
            }
        }

        public string Dump()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("DLS bank stack: " + _layers.Count + " layer(s)");

            Dictionary<int, List<int>> owners = new Dictionary<int, List<int>>();
            for (int i = 0; i < _layers.Count; i++)
            {
                DlsFile layer = _layers[i];
                sb.AppendLine(string.Format("  [{0}] {1} — {2} instruments, {3} waves",
                    i, layer.Name ?? "<unnamed>", layer.Instruments.Count, layer.Waves.Count));

                for (int j = 0; j < layer.Instruments.Count; j++)
                {
                    DlsInstrument ins = layer.Instruments[j];
                    int key = CacheKey(ins.Bank, ins.Program, ins.IsDrum);
                    List<int> list;
                    if (!owners.TryGetValue(key, out list)) { list = new List<int>(); owners[key] = list; }
                    if (!list.Contains(i)) list.Add(i);
                }
            }

            int conflicts = 0;
            StringBuilder detail = new StringBuilder();
            foreach (KeyValuePair<int, List<int>> kv in owners)
            {
                if (kv.Value.Count < 2) continue;
                conflicts++;
                if (conflicts <= 16)
                {
                    int program = kv.Key & 0x7F;
                    int bank = (kv.Key >> 8) & 0x3FFF;
                    bool drum = (kv.Key & (1 << 24)) != 0;
                    detail.AppendLine(string.Format("    bank {0} program {1}{2}: layers {3} — layer {4} wins",
                        bank, program, drum ? " (drum)" : "",
                        string.Join(",", kv.Value.ConvertAll(delegate (int x) { return x.ToString(); }).ToArray()),
                        kv.Value[kv.Value.Count - 1]));
                }
            }

            sb.AppendLine("  overridden slots: " + conflicts);
            if (detail.Length > 0) sb.Append(detail.ToString());
            if (conflicts > 16) sb.AppendLine("    ...");
            return sb.ToString();
        }
    }
}
