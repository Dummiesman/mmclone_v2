using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Dummiesman.VFS
{
    public class VFSEntry
    {
        public string Name { protected set; get; }
        public string Path { protected set; get; }
        public VFSSystem System { private set; get; }

        public virtual Stream GetStream() { return null; }
        public virtual bool IsDirectory() { return Path.EndsWith("/", StringComparison.Ordinal); }

        public byte[] Read() 
        {
            var stream = this.GetStream();
            if (stream.Position > 0 && stream.CanSeek)
            {
                stream.Seek(0, SeekOrigin.Begin);
            }
            
            byte[] data = new byte[stream.Length - stream.Position];
            stream.Read(data, 0, data.Length);
            stream.Dispose();

            return data;
        }

        public static string PreparePath(string path)
        {
            string replaced = path.Replace("\\", "/");
            if (!replaced.StartsWith("/", StringComparison.Ordinal))
            {
                replaced = replaced.Insert(0, "/");
            }
            return replaced.ToLowerInvariant();
        }

        public static string CombinePath(params string[] pathComponents)
        {
            var builder = new System.Text.StringBuilder();
            for(int i=0; i < pathComponents.Length; i++)
            {
                string cmp = pathComponents[i];
                bool isFileEnding = false;
                bool isLastComponent = i == (pathComponents.Length - 1);

                if (isLastComponent)
                {
                    int dotIndex = cmp.LastIndexOf('.');
                    if (dotIndex >= 0)
                    {
                        int slashIndex = cmp.IndexOf('/', dotIndex);
                        isFileEnding = slashIndex < dotIndex;
                    }
                }

                if (isFileEnding)
                    builder.Append(cmp);
                else
                    builder.Append($"{cmp}/");
            }

            if (builder.Length > 0 && builder[0] != '/')
                builder.Insert(0, '/');

            return builder.ToString();
        }

        public void SetSystem(VFSSystem system)
        {
            if(system == null)
            {
                this.System = null;
                return;
            }

            if (this.System != null)
                throw new Exception($"File {Path} already belongs to a filesystem ({this.System.GetType().Name})");
            this.System = system;
        }

        public VFSEntry(string name, string path)
        {
            Name = name;
            Path = path;
        }
    }


    public class VFSSystem : IDisposable
    {
        protected Dictionary<string, VFSEntry> entries = new Dictionary<string, VFSEntry>(4096, StringComparer.OrdinalIgnoreCase);
        public virtual int EntryCount { get { return entries.Count; } }

        public virtual byte[] Read(string path) { if (Exists(path)) return GetEntry(path).Read(); return null; }
        public virtual bool Exists(string path) { return entries.ContainsKey(path); }
        public virtual VFSEntry GetEntry(string path) {
            VFSEntry entry;

            if (entries.TryGetValue(path, out entry))
                return entry;

            return null;
        }

        public virtual IEnumerable<VFSEntry> GetEntries() { return entries.Values; }
        public virtual IEnumerable<VFSEntry> GetEntries(string basePath) {
            foreach(var entry in this.entries.Values)
            {
                if (entry.Path.StartsWith(basePath, StringComparison.Ordinal))
                    yield return entry;
            }
        }

        public virtual IEnumerable<VFSEntry> GetEntriesAtLevel(string basePath)
        {
            int baseSepCount = basePath.Count(x => x == '/');
            foreach (var entry in this.entries)
            {
                int sepCount = entry.Key.Count(x => x == '/');
                if (entry.Key.StartsWith(basePath, StringComparison.Ordinal))
                {
                    if (entry.Value.IsDirectory() && sepCount == baseSepCount + 1)
                    {
                        yield return entry.Value;
                    }
                    else if(!entry.Value.IsDirectory() && sepCount == baseSepCount)
                    {
                        yield return entry.Value;
                    }
                }
            }
        }

        protected void AddEntry(VFSEntry entry)
        {
            entry.SetSystem(this);
            this.entries[entry.Path] = entry;
        }

        public virtual void Dispose() {}
    }

    //Draft
    public class DirectoryEntry : VFSEntry
    {
        public readonly List<VFSEntry> Entries = new List<VFSEntry>();

        public DirectoryEntry(string name, string path) : base(name, path)
        {
        }
    }
}