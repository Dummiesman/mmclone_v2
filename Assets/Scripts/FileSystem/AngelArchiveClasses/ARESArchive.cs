using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Angel.Archive
{
    public class VirtualFileInode
    {
        public string Name { get; protected set; }
        public uint DataOffset { get; protected set; }
        public uint Size { get; protected set; }
        public bool IsDirectory { get; protected set; }

        public VirtualFileInode(BinaryReader reader, Stream names_stream)
        {
            DataOffset = reader.ReadUInt32();

            var flags_4 = reader.ReadUInt32();
            var flags_8 = reader.ReadUInt32();

            Size = (flags_4 & 0x7FFFFF);
            IsDirectory = (flags_8 & 1) != 0;

            var name_offset = (flags_8 >> 14) & 0x3FFFF;
            var ext_offset = (flags_4 >> 23) & 0x1FF;
            var name_integer = ((flags_8 >> 1) & 0x1FFF).ToString();

            names_stream.Seek(name_offset, SeekOrigin.Begin);

            Name = names_stream.ReadASCII().Replace("\x01", name_integer);

            if (ext_offset != 0)
            {
                names_stream.Seek(ext_offset, SeekOrigin.Begin);

                Name += "." + names_stream.ReadASCII();
            }
        }

        public IFileEntry ToFileEntry(string parent = "")
        {
            if (!IsDirectory)
            {
                return new RawFileEntry(parent + Name, Size, DataOffset);
            }

            return null;
        }

        public IEnumerable<VirtualFileInode> GetChildren(VirtualFileInode[] nodes)
        {
            if (IsDirectory)
            {
                for (var i = DataOffset; i < DataOffset + Size; ++i)
                {
                    yield return nodes[i];
                }
            }
        }

        public IEnumerable<IFileEntry> GetFiles(VirtualFileInode[] nodes, string parent = "")
        {
            if (IsDirectory)
            {
                var name = parent + Name + "\\";

                foreach (var node in GetChildren(nodes))
                {
                    if (node.IsDirectory)
                    {
                        foreach (var file in node.GetFiles(nodes, name))
                        {
                            yield return file;
                        }
                    }
                    else
                    {
                        yield return node.ToFileEntry(name);
                    }
                }
            }
        }
    }


    public class ARESReader : IFileReader
    {
        public static uint ARES_MAGIC = 0x53455241;

        public override string Identifier => "Midtown Madness 1 / ARES";

        public override bool IsValid(BinaryReader input)
        {
            input.BaseStream.Seek(0, SeekOrigin.Begin);

            return input.ReadUInt32() == ARES_MAGIC;
        }

        public override IEnumerable<IFileEntry> Read(BinaryReader input)
        {
            input.BaseStream.Seek(0, SeekOrigin.Begin);

            if (input.ReadUInt32() != ARES_MAGIC)
            {
                yield break;
            }

            var file_count = input.ReadUInt32();
            var dir_count = input.ReadUInt32();
            var names_size = input.ReadUInt32();
            var names_offset = 16 + file_count * 12;

            input.BaseStream.Seek(names_offset, SeekOrigin.Begin);

            var names_stream = new MemoryStream(input.ReadBytes((int)names_size));

            var nodes = new VirtualFileInode[file_count];

            for (var i = 0; i < file_count; ++i)
            {
                input.BaseStream.Seek(16 + (i * 12), SeekOrigin.Begin);

                nodes[i] = new VirtualFileInode(input, names_stream);
            }

            for (var i = 0; i < dir_count; ++i)
            {
                foreach (var file in nodes[i].GetFiles(nodes, ""))
                {
                    yield return file;
                }
            }
        }
    }
}