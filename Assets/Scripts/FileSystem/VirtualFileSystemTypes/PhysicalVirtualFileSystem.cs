using System.IO;

namespace Dummiesman.VFS
{
    public class PhysicalFilesystemEntry : VFSEntry
    {
        private string sourcePath;

        public override Stream GetStream() { return File.OpenRead(sourcePath); }

        public PhysicalFilesystemEntry(string name, string sourcePath, string path) : base(name, path)
        {
            this.sourcePath = sourcePath;
        }
    }

    public class PhysicalFilesystem : VFSSystem
    {
        public PhysicalFilesystem(string origin)
        {
            //get origin info
            var originDirectoryInfo = new DirectoryInfo(origin);

            //loop through files
            foreach (var file in Directory.GetFiles(origin, "*", System.IO.SearchOption.AllDirectories))
            {
                var fileInfo = new FileInfo(file);
                var entry = new PhysicalFilesystemEntry(fileInfo.Name, file, VFSEntry.PreparePath(fileInfo.FullName.Substring(originDirectoryInfo.FullName.Length)));
                AddEntry(entry);
            }

            //loop through directories
            foreach (var directory in Directory.GetDirectories(origin, "*", SearchOption.AllDirectories))
            {
                var directInfo = new DirectoryInfo(directory);
                var entry = new PhysicalFilesystemEntry(directInfo.Name, directory, VFSEntry.PreparePath(directInfo.FullName.Substring(originDirectoryInfo.FullName.Length)));
                AddEntry(entry);
            }
        }
    }
}
