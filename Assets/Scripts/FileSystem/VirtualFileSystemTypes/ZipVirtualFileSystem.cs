/*
using Pathfinding.Ionic.Zip;
using System.IO;

namespace Dummiesman.VFS
{
    public class ZipFilesystemEntry : VFSEntry
    {
        private ZipEntry backingEntry;

        public override Stream GetStream()
        {
            return backingEntry.OpenReader();
        }

        public ZipFilesystemEntry(string name, string path, ZipEntry backingEntry) : base(name, path)
        {
            this.backingEntry = backingEntry;
        }
    }

    public class ZipFilesystem : VFSSystem
    {
        /// <summary>
        /// The source path of the ZIP, may be null if loaded from stream
        /// </summary>
        public string SourcePath;
        private ZipFile file;

        public override void Dispose()
        {
            file.Dispose();
        }

        public ZipFilesystem(Stream stream)
        {
            var zipFile = ZipFile.Read(stream);
            foreach (var entry in zipFile.Entries)
            {
                string name = entry.FileName.EndsWith("/", System.StringComparison.Ordinal) ? Path.GetFileName(entry.FileName.Substring(0, entry.FileName.Length - 1))
                                                                                            : Path.GetFileName(entry.FileName);
                string path = VFSEntry.PreparePath(entry.FileName);

                var zEntry = new ZipFilesystemEntry(name, path, entry);
                AddEntry(zEntry);
            }
            this.file = zipFile;
        }

        public ZipFilesystem(string path) : this(File.OpenRead(path))
        {
            this.SourcePath = path;
        }
    }
}*/