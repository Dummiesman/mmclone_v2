using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Angel.Archive
{
    public class DAVEReader : IFileReader
    {
        public static uint DAVE_MAGIC_V1 = 0x45564144; // DAVE
        public static uint DAVE_MAGIC_V2 = 0x65766144; // Dave

        public override string Identifier => "Midtown Madness 2 / DAVE";

        public override bool IsValid(BinaryReader input)
        {
            input.BaseStream.Seek(0, SeekOrigin.Begin);

            var magic = input.ReadUInt32();

            return GetDaveVersion(magic) != -1;
        }

        protected int GetDaveVersion(uint magic)
        {
            if (magic == DAVE_MAGIC_V1)
            {
                return 1;
            }
            else if (magic == DAVE_MAGIC_V2)
            {
                return 2;
            }

            return -1;
        }

        public override IEnumerable<IFileEntry> Read(BinaryReader input)
        {
            input.BaseStream.Seek(0, SeekOrigin.Begin);

            var magic = input.ReadUInt32();

            var version = GetDaveVersion(magic);

            if (version == -1)
            {
                throw new Exception("Invalid Archive");
            }

            var file_count = input.ReadUInt32();
            var names_offset = input.ReadUInt32();
            var names_size = input.ReadUInt32();

            input.BaseStream.Seek(2048 + names_offset, SeekOrigin.Begin);
            var names_stream = new MemoryStream(input.ReadBytes((int)names_size));

            string prev_name = null;

            for (var i = 0; i < file_count; ++i)
            {
                input.BaseStream.Seek(2048 + (i * 16), SeekOrigin.Begin);

                var name_offset = input.ReadUInt32();
                var data_offset = input.ReadUInt32();

                var size = input.ReadUInt32();
                var compressed_size = input.ReadUInt32();

                names_stream.Seek(name_offset, SeekOrigin.Begin);

                string name;
                switch (version)
                {
                    case 1:
                        name = names_stream.ReadASCII();
                        break;
                    case 2:
                        name = names_stream.ReadDaveString(prev_name);
                        prev_name = name;
                        break;
                    default:
                        throw new Exception("Invalid Version");

                }

                if (size != compressed_size)
                {
                    yield return new DeflatedFileEntry(name, size, compressed_size, data_offset);
                }
                else
                {
                    yield return new RawFileEntry(name, size, data_offset);
                }
            }
        }
    }
}
