using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Dummiesman.VFS
{
    public class MultiFilesystem : VFSSystem
    {
        public IReadOnlyList<VFSSystem> MountedSystems => mountedSystems;
        private List<VFSSystem> mountedSystems = new List<VFSSystem>();

        //Events
        public Action<VFSSystem> OnSystemMounted;
        public Action<VFSSystem> OnSystemUnMounted;

        //Internal MFS class
        private class MultiFilesystemEntry : VFSEntry
        {
            private LinkedList<VFSEntry> EntryStack = new LinkedList<VFSEntry>();

            public bool IsEntryStackEmpty => EntryStack.Count == 0;
            public VFSEntry Entry => EntryStack.Last?.Value;

            public void RemoveEntriesFromSystem(VFSSystem system)
            {
                var entry = EntryStack.Last;
                while (entry != null)
                {
                    if (entry.Value.System == system)
                    {
                        EntryStack.Remove(entry);
                    }
                    entry = entry.Previous;
                }
            }

            public void AddEntryToStack(VFSEntry entry)
            {
                EntryStack.AddLast(entry);
            }

            public override Stream GetStream() { return Entry.GetStream(); }

            public MultiFilesystemEntry(string name, string path) : base(name, path)
            {
            }
        }


        //We override GetEntry etc to return the base entry. A MultiFilesystem entry is for internal use only
        public override VFSEntry GetEntry(string path)
        {
            if (base.GetEntry(path) is MultiFilesystemEntry entry)
            {
                return entry.Entry;
            }
            return null;
        }

        public override IEnumerable<VFSEntry> GetEntries()
        {
            return entries.Values.Where(x => x is MultiFilesystemEntry).Cast<MultiFilesystemEntry>().Select(x => x.Entry);
        }

        public override IEnumerable<VFSEntry> GetEntries(string basePath)
        {
            foreach (var entry in this.entries.Values.Where(x => x is MultiFilesystemEntry).Cast<MultiFilesystemEntry>())
            {
                if (entry.Path.StartsWith(basePath, StringComparison.Ordinal))
                    yield return entry.Entry;
            }
        }

        public void Unmount(VFSSystem system)
        {
            if (!mountedSystems.Contains(system))
                throw new Exception("This filesystem isn't mounted.");
            foreach (var entry in system.GetEntries())
            {
                var mEntry = base.GetEntry(entry.Path) as MultiFilesystemEntry;
                if (mEntry != null)
                {
                    mEntry.RemoveEntriesFromSystem(system);
                    if (mEntry.IsEntryStackEmpty)
                    {
                        this.entries.Remove(mEntry.Path);
                    }
                }
            }
            mountedSystems.Remove(system);
            OnSystemUnMounted?.Invoke(system);
        }

        public void Mount(VFSSystem system)
        {
            if (mountedSystems.Contains(system))
                throw new Exception("This filesystem is already mounted.");

            foreach (var entry in system.GetEntries())
            {
                var mEntry = base.GetEntry(entry.Path) as MultiFilesystemEntry;
                if (mEntry == null)
                {
                    mEntry = new MultiFilesystemEntry(entry.Name, entry.Path);
                    AddEntry(mEntry);
                }

                mEntry.AddEntryToStack(entry);
            }
            mountedSystems.Add(system);
            OnSystemMounted?.Invoke(system);
        }

        public void Mount(IEnumerable<VFSSystem> systems)
        {
            foreach (var system in systems)
            {
                Mount(system);
            }
        }

        public override void Dispose()
        {
            foreach (var system in this.mountedSystems)
                system.Dispose();
        }

        public MultiFilesystem() { }
        public MultiFilesystem(IEnumerable<VFSSystem> systems)
        {
            Mount(systems);
        }
    }
}