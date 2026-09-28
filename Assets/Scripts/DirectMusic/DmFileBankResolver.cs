// DmFileBankResolver.cs — the on-disk DmBankResolver, and the default one.
//
// This is the behaviour DmBankResolver had before it was split: try the reference as
// authored, then its bare filename in each search directory, then the system General
// MIDI set for references that name it.
//
// Part of DirectMusicLite.

using System;
using System.Collections.Generic;
using System.IO;

namespace DirectMusicLite
{
    public class DmFileBankResolver : DmBankResolver
    {
        /// <summary>
        /// Folders searched for a reference's bare filename, in order. Mutable, so a host
        /// can keep appending as it discovers where content lives.
        /// </summary>
        public readonly List<string> SearchDirectories = new List<string>();

        /// <summary>
        /// Fall back to the Windows GM set when a reference names General MIDI and nothing
        /// in SearchDirectories matches, and for the default collection.
        /// </summary>
        public bool AllowSystemGeneralMidi = true;

        /// <summary>Extension added to a style reference that doesn't carry one.</summary>
        public string StyleExtension = ".sty";

        public DmFileBankResolver() { }

        public DmFileBankResolver(IList<string> searchDirectories)
        {
            AddSearchDirectories(searchDirectories);
        }

        public void AddSearchDirectory(string directory)
        {
            if (string.IsNullOrEmpty(directory)) return;
            if (!SearchDirectories.Contains(directory)) SearchDirectories.Add(directory);
        }

        public void AddSearchDirectories(IList<string> directories)
        {
            if (directories == null) return;
            for (int i = 0; i < directories.Count; i++) AddSearchDirectory(directories[i]);
        }

        /// <summary>Adds the folder holding a file, for content that sits beside its banks.</summary>
        public void AddSearchDirectoryForFile(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return;
            try { AddSearchDirectory(Path.GetDirectoryName(Path.GetFullPath(filePath))); }
            catch (Exception) { }
        }

        // ------------------------------------------------------------------ contract

        public override string Resolve(string reference)
        {
            return Resolve(reference, SearchDirectories, AllowSystemGeneralMidi);
        }

        public override byte[] Open(string resolvedKey)
        {
            return File.ReadAllBytes(resolvedKey);
        }

        public override string ResolveStyle(string reference)
        {
            if (string.IsNullOrEmpty(reference)) return null;

            // As authored first, then with .sty appended: a style reference may name the
            // file or just the style object, and there is no GM fallback for styles.
            string direct = Resolve(reference, SearchDirectories, false);
            if (direct != null) return direct;

            string named = EnsureExtension(reference, StyleExtension);
            return string.Equals(named, reference, StringComparison.Ordinal)
                ? null
                : Resolve(named, SearchDirectories, false);
        }

        protected override string ResolveDefaultCollection()
        {
            // A relative path that exists as authored is accepted here but not in Resolve:
            // the host sets this one deliberately, whereas references come out of content
            // and shouldn't be matched against the working directory by accident.
            if (!string.IsNullOrEmpty(DefaultCollectionReference))
            {
                try { if (File.Exists(DefaultCollectionReference)) return DefaultCollectionReference; }
                catch (Exception) { }
            }
            return AllowSystemGeneralMidi ? SystemGeneralMidiPath() : null;
        }

        protected override bool IsSystemGeneralMidi(string resolvedKey)
        {
            if (string.IsNullOrEmpty(resolvedKey)) return false;
            string system = SystemGeneralMidiPath();
            return system != null && string.Equals(system, resolvedKey, StringComparison.OrdinalIgnoreCase);
        }

        // ------------------------------------------------------------------ statics

        /// <summary>
        /// Windows ships the Roland GM set here. Present on Windows only, and it is a
        /// system file — check licensing before copying it into a shipped build.
        /// </summary>
        public static string SystemGeneralMidiPath()
        {
            try
            {
                string system = Environment.GetFolderPath(Environment.SpecialFolder.System);
                if (string.IsNullOrEmpty(system)) return null;
                string path = Path.Combine(Path.Combine(system, "drivers"), "gm.dls");
                return File.Exists(path) ? path : null;
            }
            catch (Exception) { return null; }
        }

        /// <summary>
        /// Resolves one reference to an existing file, or null.
        ///
        /// References are stored as authoring-time paths, so the full path usually points
        /// at a machine that isn't yours. The bare filename matched against the search
        /// directories is what normally succeeds.
        /// </summary>
        public static string Resolve(string reference, IList<string> searchDirectories)
        {
            return Resolve(reference, searchDirectories, true);
        }

        public static string Resolve(string reference, IList<string> searchDirectories,
                                     bool allowSystemGeneralMidi)
        {
            if (string.IsNullOrEmpty(reference)) return null;

            string normalised = reference.Replace('\\', Path.DirectorySeparatorChar)
                                         .Replace('/', Path.DirectorySeparatorChar);

            try
            {
                if (Path.IsPathRooted(normalised) && File.Exists(normalised)) return normalised;
            }
            catch (ArgumentException) { /* authoring path with characters this platform rejects */ }

            string fileName;
            try { fileName = Path.GetFileName(normalised); }
            catch (ArgumentException) { fileName = normalised; }

            if (!string.IsNullOrEmpty(fileName) && searchDirectories != null)
            {
                for (int i = 0; i < searchDirectories.Count; i++)
                {
                    string dir = searchDirectories[i];
                    if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) continue;

                    string direct = Path.Combine(dir, fileName);
                    if (File.Exists(direct)) return direct;

                    // Case-insensitive fallback: Producer content is inconsistent about case,
                    // and Linux/Android builds care even though Windows doesn't.
                    string found = FindIgnoringCase(dir, fileName);
                    if (found != null) return found;
                }
            }

            if (allowSystemGeneralMidi && LooksLikeGeneralMidi(reference))
            {
                string gm = SystemGeneralMidiPath();
                if (gm != null) return gm;
            }

            return null;
        }

        static string FindIgnoringCase(string directory, string fileName)
        {
            try
            {
                string[] files = Directory.GetFiles(directory);
                for (int i = 0; i < files.Length; i++)
                    if (string.Equals(Path.GetFileName(files[i]), fileName, StringComparison.OrdinalIgnoreCase))
                        return files[i];
            }
            catch (Exception) { }
            return null;
        }
    }
}
