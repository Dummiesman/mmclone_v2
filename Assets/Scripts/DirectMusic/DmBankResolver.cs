// DmBankResolver.cs — turns a band's DLS reference into a loaded collection.
//
// DirectMusic's loader resolves references through a search directory plus GUID
// matching against already-loaded collections. This keeps that shape, but splits it
// in two so the backing store is swappable:
//
//   * a subclass answers two questions — "where does this reference live?" and
//     "what are the bytes?" — and nothing else;
//   * this class does the stacking, de-duplication, default-collection handling
//     and reporting on top of those answers.
//
// DmFileBankResolver is the on-disk implementation and the default. For a virtual
// filesystem, either subclass this or hand two callbacks to DmDelegateBankResolver.
//
// Part of DirectMusicLite.

using System;
using System.Collections.Generic;

namespace DirectMusicLite
{
    /// <summary>
    /// The minimum a bank source has to provide. Implement this directly if you already
    /// have a class that can't inherit from DmBankResolver, then wrap it with
    /// DmBankResolver.Wrap to get the loading logic.
    /// </summary>
    public interface IDmBankResolver
    {
        /// <summary>Maps an authored reference onto an openable key, or null if absent.</summary>
        string Resolve(string reference);

        /// <summary>Reads the bytes behind a key that Resolve returned.</summary>
        byte[] Open(string resolvedKey);
    }

    public abstract class DmBankResolver : IDmBankResolver
    {
        /// <summary>
        /// Reference used by band instruments that carry no DLS reference of their own.
        /// It goes through Resolve like any other reference, so a VFS resolver can point
        /// this at a VFS entry. Leave it blank to fall back on ResolveDefaultCollection.
        /// </summary>
        public string DefaultCollectionReference;

        // ------------------------------------------------------------------ contract

        /// <summary>
        /// Maps an authored reference onto a key this resolver can open, or returns null
        /// when the reference can't be found.
        ///
        /// References are stored as authoring-time paths ("D:\Media\Bass.dls"), so the
        /// full path usually points at a machine that isn't yours. Matching on the bare
        /// filename is what normally succeeds — see ReferenceFileName.
        ///
        /// The key is opaque to this class: a filesystem path, a VFS handle string, an
        /// asset bundle entry, whatever Open understands. It is also used to skip banks
        /// that are already stacked and it shows up in Result.Describe, so prefer
        /// something stable and readable.
        /// </summary>
        public abstract string Resolve(string reference);

        /// <summary>
        /// Reads the bytes for a key Resolve returned. Return null or throw if the read
        /// fails; either way the reference is reported as missing with the reason.
        /// </summary>
        public abstract byte[] Open(string resolvedKey);

        /// <summary>
        /// Maps a segment's style reference onto a key, or null when this resolver can't
        /// supply styles. Styles are separate .sty files that segments name the same way
        /// they name collections, so a resolver that can find one can usually find both.
        ///
        /// Not abstract: a resolver that only deals in collections can ignore it, and
        /// LoadReferencedStyle will report that rather than guessing. Override it rather
        /// than forwarding to Resolve — implementations that supply a default extension
        /// would append .dls to a style name.
        /// </summary>
        public virtual string ResolveStyle(string reference) { return null; }

        // ------------------------------------------------------------------ hooks

        /// <summary>
        /// Last-resort default General MIDI collection, used when DefaultCollectionReference
        /// is blank or doesn't resolve. DmFileBankResolver returns the system gm.dls here.
        /// Return a key that Open accepts, or null.
        /// </summary>
        protected virtual string ResolveDefaultCollection() { return null; }

        /// <summary>
        /// True when a key points at a system-provided GM set rather than shipped content.
        /// Only drives Result.UsedSystemGeneralMidi, which exists so a build step can warn
        /// about depending on a file it isn't allowed to redistribute.
        /// </summary>
        protected virtual bool IsSystemGeneralMidi(string resolvedKey) { return false; }

        /// <summary>
        /// Whether two keys name the same collection. Ordinal-ignore-case suits paths;
        /// override it if your VFS is case-sensitive.
        /// </summary>
        protected virtual bool KeysEqual(string a, string b)
        {
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>
        /// The bare filename from an authored reference, with separators normalised.
        /// Handy for VFS lookups keyed by name, and safe on references containing
        /// characters the running platform rejects in paths.
        /// </summary>
        public static string ReferenceFileName(string reference)
        {
            if (string.IsNullOrEmpty(reference)) return null;

            int cut = -1;
            for (int i = reference.Length - 1; i >= 0; i--)
            {
                char c = reference[i];
                if (c == '\\' || c == '/') { cut = i; break; }
            }
            return cut < 0 ? reference : reference.Substring(cut + 1);
        }

        /// <summary>
        /// The reference with the given extension (".sty", ".dls") applied if it hasn't
        /// got it already. References are inconsistent: some name a file, some name the
        /// object ("DLS Collection 1") and leave the extension off entirely.
        /// </summary>
        public static string EnsureExtension(string reference, string extension)
        {
            if (string.IsNullOrEmpty(reference)) return reference;
            if (string.IsNullOrEmpty(extension)) return reference;
            if (reference.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) return reference;
            return reference + extension;
        }

        /// <summary>
        /// Whether a reference names the General MIDI set by one of the spellings
        /// Producer content uses.
        /// </summary>
        public static bool LooksLikeGeneralMidi(string reference)
        {
            if (string.IsNullOrEmpty(reference)) return false;
            string lower = reference.ToLowerInvariant();
            return lower.Contains("gm.dls")
                || lower.Contains("general midi")
                || lower.Contains("generalmidi")
                || lower.Contains("roland gm");
        }

        /// <summary>
        /// Counts band instruments carrying no DLS reference. In DirectMusic those mean
        /// "use the default General MIDI collection" rather than "no instrument", so a
        /// non-zero count here means gm.dls (or a stand-in) has to be loaded too.
        /// </summary>
        public static int InstrumentsNeedingDefaultCollection(DmSegment segment, DmStyle style)
        {
            int count = 0;
            count += CountUnreferenced(segment != null ? segment.Bands : null);
            count += CountUnreferenced(style != null ? style.Bands : null);
            return count;
        }

        static int CountUnreferenced(List<DmBandEvent> bands)
        {
            if (bands == null) return 0;
            int count = 0;
            for (int i = 0; i < bands.Count; i++)
                for (int j = 0; j < bands[i].Instruments.Count; j++)
                    if (string.IsNullOrEmpty(bands[i].Instruments[j].DlsReference)) count++;
            return count;
        }

        /// <summary>Adapts a bare IDmBankResolver so it can be used for loading.</summary>
        public static DmBankResolver Wrap(IDmBankResolver resolver)
        {
            if (resolver == null) return null;

            DmBankResolver already = resolver as DmBankResolver;
            if (already != null) return already;

            return new Adapter(resolver);
        }

        sealed class Adapter : DmBankResolver
        {
            readonly IDmBankResolver _inner;
            public Adapter(IDmBankResolver inner) { _inner = inner; }
            public override string Resolve(string reference) { return _inner.Resolve(reference); }
            public override byte[] Open(string resolvedKey) { return _inner.Open(resolvedKey); }
        }

        // ------------------------------------------------------------------ result

        public sealed class Result
        {
            public readonly List<string> Loaded = new List<string>();
            public readonly List<string> Missing = new List<string>();
            public bool UsedSystemGeneralMidi;
            /// <summary>Where the default collection came from, if one was needed and found.</summary>
            public string DefaultCollection;
            public int InstrumentsNeedingDefault;

            public string Describe()
            {
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                sb.AppendLine("Referenced DLS resolution:");
                for (int i = 0; i < Loaded.Count; i++) sb.AppendLine("  loaded  " + Loaded[i]);
                for (int i = 0; i < Missing.Count; i++) sb.AppendLine("  MISSING " + Missing[i]);
                if (InstrumentsNeedingDefault > 0)
                {
                    sb.AppendLine("  " + InstrumentsNeedingDefault + " band instrument(s) carry no DLS reference, " +
                                  "meaning the default General MIDI collection.");
                    sb.AppendLine(DefaultCollection != null
                        ? "  default collection: " + DefaultCollection
                        : "  DEFAULT COLLECTION NOT FOUND - those channels will fall back to the wrong " +
                          "instrument. Point DefaultCollectionReference at a General MIDI .dls.");
                }
                if (Loaded.Count == 0 && Missing.Count == 0 && InstrumentsNeedingDefault == 0)
                    sb.AppendLine("  none - the bands carry no DLS references, so banks must be loaded manually.");
                return sb.ToString();
            }
        }

        /// <summary>Outcome of resolving the style a segment names.</summary>
        public sealed class StyleResult
        {
            public DmStyle Style;
            /// <summary>The reference that worked, or the first one tried.</summary>
            public string Reference;
            public string ResolvedKey;
            /// <summary>Every candidate attempted, for when nothing matched.</summary>
            public readonly List<string> Tried = new List<string>();
            public string Error;
            /// <summary>False when the resolver doesn't implement ResolveStyle at all.</summary>
            public bool Supported = true;
            /// <summary>True when the segment named no style, which is normal for some content.</summary>
            public bool NoneReferenced;

            public bool Loaded { get { return Style != null; } }

            public string Describe()
            {
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                sb.AppendLine("Style resolution:");
                if (NoneReferenced) { sb.AppendLine("  the segment references no style."); return sb.ToString(); }
                if (!Supported)
                {
                    sb.AppendLine("  this resolver doesn't supply styles - override ResolveStyle, or load " +
                                  "the .sty yourself with LoadStyle().");
                    for (int i = 0; i < Tried.Count; i++) sb.AppendLine("  referenced: " + Tried[i]);
                    return sb.ToString();
                }
                if (Loaded) { sb.AppendLine("  loaded  " + Reference + "  ->  " + ResolvedKey); return sb.ToString(); }

                sb.AppendLine("  NOT FOUND - tried:");
                for (int i = 0; i < Tried.Count; i++) sb.AppendLine("    " + Tried[i]);
                if (Error != null) sb.AppendLine("  last error: " + Error);
                return sb.ToString();
            }
        }

        /// <summary>
        /// Resolves and parses the style a segment references. Segments name their style
        /// by a DMRF reference carrying a filename, an object name, or both, so each
        /// candidate is tried in turn and the first that loads wins.
        ///
        /// Returns a result rather than throwing; check StyleResult.Loaded.
        /// </summary>
        public StyleResult LoadReferencedStyle(DmSegment segment)
        {
            StyleResult result = new StyleResult();
            if (segment == null || segment.StyleReferences.Count == 0)
            {
                result.NoneReferenced = true;
                return result;
            }

            for (int i = 0; i < segment.StyleReferences.Count; i++)
            {
                DmStyleRef reference = segment.StyleReferences[i];

                // The file chunk names a .sty; the name chunk names the style object and
                // is often all that survives. Either can be the one that matches.
                TryCandidate(reference.FileName, result);
                if (result.Loaded) return result;

                TryCandidate(reference.Name, result);
                if (result.Loaded) return result;
            }

            if (result.Reference == null && result.Tried.Count > 0) result.Reference = result.Tried[0];
            return result;
        }

        void TryCandidate(string reference, StyleResult result)
        {
            if (string.IsNullOrEmpty(reference)) return;
            if (result.Tried.Contains(reference)) return;
            result.Tried.Add(reference);

            string key;
            try { key = ResolveStyle(reference); }
            catch (Exception e) { result.Error = e.Message; return; }

            // A resolver that hasn't overridden ResolveStyle returns null for everything.
            // Distinguishing that from "looked, didn't find it" is the difference between
            // a setup mistake and missing content.
            if (string.IsNullOrEmpty(key))
            {
                if (!SuppliesStyles) result.Supported = false;
                return;
            }

            try
            {
                byte[] bytes = Open(key);
                if (bytes == null || bytes.Length == 0) { result.Error = "no data at " + key; return; }

                result.Style = DmStyle.Load(bytes);
                result.Reference = reference;
                result.ResolvedKey = key;
            }
            catch (Exception e)
            {
                result.Error = key + " (" + e.Message + ")";
            }
        }

        /// <summary>
        /// Whether this resolver can supply styles at all, as opposed to having looked and
        /// found nothing. Only affects the wording of StyleResult.Describe. The default
        /// answer is "yes if ResolveStyle was overridden"; override this property when a
        /// resolver overrides ResolveStyle but can still be configured not to use it.
        /// </summary>
        public virtual bool SuppliesStyles
        {
            get
            {
                try
                {
                    System.Reflection.MethodInfo method = GetType().GetMethod("ResolveStyle",
                        new Type[] { typeof(string) });
                    return method != null && method.DeclaringType != typeof(DmBankResolver);
                }
                catch (Exception)
                {
                    // Managed stripping can take method metadata away. Assuming support
                    // costs a slightly vaguer message, which beats a wrong one.
                    return true;
                }
            }
        }

        // ------------------------------------------------------------------ loading

        /// <summary>
        /// Resolves and loads every DLS a segment and style refer to, stacking them in
        /// reference order. Already-loaded collections are skipped by key and by DLSID,
        /// so calling this twice won't duplicate a bank.
        ///
        /// If any band instrument carries no reference, the default collection is loaded
        /// underneath as well, so explicit references still win.
        /// </summary>
        public Result LoadReferenced(DmSegment segment, DmStyle style, DlsBankStack into)
        {
            Result result = new Result();
            if (into == null) return result;

            List<string> references = new List<string>();
            if (segment != null) references.AddRange(segment.ReferencedDlsFiles());
            if (style != null)
                foreach (string reference in style.ReferencedDlsFiles())
                    if (!references.Contains(reference)) references.Add(reference);

            for (int i = 0; i < references.Count; i++)
            {
                string reference = references[i];

                string key;
                try { key = Resolve(reference); }
                catch (Exception e)
                {
                    result.Missing.Add(reference + " (resolve failed: " + e.Message + ")");
                    continue;
                }

                if (string.IsNullOrEmpty(key)) { result.Missing.Add(reference); continue; }
                if (AlreadyLoaded(into, key)) continue;

                DlsFile bank = TryLoad(key, reference, result);
                if (bank == null) continue;
                if (bank.Id != Guid.Empty && AlreadyLoadedById(into, bank.Id)) continue;

                into.Add(bank);
                result.Loaded.Add(reference + "  ->  " + key);
                if (IsSystemGeneralMidi(key)) result.UsedSystemGeneralMidi = true;
            }

            result.InstrumentsNeedingDefault = InstrumentsNeedingDefaultCollection(segment, style);
            if (result.InstrumentsNeedingDefault > 0) LoadDefaultCollection(into, result);

            return result;
        }

        void LoadDefaultCollection(DlsBankStack into, Result result)
        {
            string key = null;

            if (!string.IsNullOrEmpty(DefaultCollectionReference))
            {
                try { key = Resolve(DefaultCollectionReference); }
                catch (Exception) { key = null; }
            }
            if (string.IsNullOrEmpty(key))
            {
                try { key = ResolveDefaultCollection(); }
                catch (Exception) { key = null; }
            }
            if (string.IsNullOrEmpty(key)) return;

            if (AlreadyLoaded(into, key)) { result.DefaultCollection = key; return; }

            DlsFile bank = TryLoad(key, "(default collection)", result);
            if (bank == null) return;

            if (bank.Id != Guid.Empty && AlreadyLoadedById(into, bank.Id))
            {
                result.DefaultCollection = key;
                return;
            }

            // Underneath, so an explicitly referenced collection still wins.
            into.AddBase(bank);
            result.DefaultCollection = key;
            if (IsSystemGeneralMidi(key)) result.UsedSystemGeneralMidi = true;
        }

        DlsFile TryLoad(string key, string reference, Result result)
        {
            try
            {
                byte[] bytes = Open(key);
                if (bytes == null || bytes.Length == 0)
                {
                    result.Missing.Add(reference + "  ->  " + key + " (no data)");
                    return null;
                }

                DlsFile bank = DlsFile.Load(bytes);
                bank.SourcePath = key;
                return bank;
            }
            catch (Exception e)
            {
                result.Missing.Add(reference + "  ->  " + key + " (" + e.Message + ")");
                return null;
            }
        }

        bool AlreadyLoaded(DlsBankStack stack, string key)
        {
            for (int i = 0; i < stack.Layers.Count; i++)
            {
                string existing = stack.Layers[i].SourcePath;
                if (!string.IsNullOrEmpty(existing) && KeysEqual(existing, key)) return true;
            }
            return false;
        }

        static bool AlreadyLoadedById(DlsBankStack stack, Guid id)
        {
            for (int i = 0; i < stack.Layers.Count; i++)
                if (stack.Layers[i].Id == id) return true;
            return false;
        }
    }
}
