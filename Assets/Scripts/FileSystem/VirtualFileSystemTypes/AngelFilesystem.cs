using Angel.Archive;
using Dummiesman.VFS;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace BarebonesFileSystem
{
    public class AngelFilesystemEntry : VFSEntry
    {
        private IFileEntry backingEntry;
        private Stream parentStream;

        public override Stream GetStream()
        {
            return backingEntry.GetStream(parentStream);
            //return new MemoryStream(backingEntry.Extract(parentStream));
        }

        public AngelFilesystemEntry(string name, string path, Stream parentStream, IFileEntry backingEntry) : base(name, path)
        {
            this.backingEntry = backingEntry;
            this.parentStream = parentStream;
        }
    }

    public class AngelFilesystem  : VFSSystem
    {
        //statics
        private static bool staticSystemsInitialized = false;
        private static IFileReader[] fileReaders;
        private static void InitializeStaticSystems()
        {
            fileReaders = new IFileReader[]
            {
                new DAVEReader(),
                new ZipReader(),
                new ARESReader()
            };
            staticSystemsInitialized = true;
        }

        //
        private Stream baseStream;

        public override void Dispose()
        {
            baseStream.Dispose();
        }

        public AngelFilesystem(Stream stream)
        {
            if (!staticSystemsInitialized)
                InitializeStaticSystems();

            BinaryReader input = new BinaryReader(stream);
            foreach (IFileReader fileReader in fileReaders)
            {
                if (fileReader.IsValid(input))
                {
                    IEnumerable<IFileEntry> enumerable = fileReader.Read(input);
                    foreach (IFileEntry fileEntry in enumerable)
                    {
                        string fileName = Path.GetFileName(fileEntry.Name);
                        string path = VFSEntry.PreparePath(fileEntry.Name);
                        this.entries[path] = new AngelFilesystemEntry(fileName, path, stream, fileEntry);
                    }
                }
            }
            this.baseStream = stream;
        }

        public AngelFilesystem(string path) : this(File.OpenRead(path)) { }
    }
}