using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Angel.Archive
{
    public class ZipReader : IFileReader
    {
        public static uint ZIPENDLOCATOR_MAGIC = 0x06054B50;

        public override string Identifier => "ZIP / PK";

        public override bool IsValid(BinaryReader input)
        {
            input.BaseStream.Seek(-22, SeekOrigin.End);

            return input.ReadUInt32() == ZIPENDLOCATOR_MAGIC; // ZIPENDLOCATOR
        }

        public override IEnumerable<IFileEntry> Read(BinaryReader input)
        {
            input.BaseStream.Seek(-22, SeekOrigin.End);

            if (input.ReadUInt32() != ZIPENDLOCATOR_MAGIC)
            {
                yield break;
            }

            var diskNumber = input.ReadUInt16();
            var startDiskNumber = input.ReadUInt16();

            if (diskNumber != startDiskNumber)
            {
                throw new Exception("Incomplete Archive");
            }

            var fileCount = input.ReadUInt16();
            var filesInDirectory = input.ReadUInt16();

            var directorySize = input.ReadUInt32();
            var directoryOffset = input.ReadUInt32();

            var fileCommentLength = input.ReadUInt16();

            var currentOffset = directoryOffset;

            while (true)
            {
                input.BaseStream.Seek(currentOffset, SeekOrigin.Begin);

                if (input.ReadUInt32() != 0x02014B50) // ZIPDIRENTRY
                {
                    break;
                }

                var versionMadeBy = input.ReadUInt16();
                var versionToExtract = input.ReadUInt16();
                var flags = input.ReadUInt16();

                var compressionMethod = input.ReadUInt16();

                var fileTime = input.ReadUInt16();
                var fileDate = input.ReadUInt16();
                var crc = input.ReadUInt32();

                var compressedSize = input.ReadUInt32();
                var uncompressedSize = input.ReadUInt32();

                var nameLength = input.ReadUInt16();
                var extraLength = input.ReadUInt16();
                var commentLength = input.ReadUInt16();

                var diskNumberStart = input.ReadUInt16();

                var internalAttributes = input.ReadUInt16();
                var externalAttributes = input.ReadUInt32();

                var recordOffset = input.ReadUInt32(); // ZIPFILERECORD

                var name = input.BaseStream.ReadASCII(nameLength);

                currentOffset = (uint)input.BaseStream.Position + extraLength + commentLength;

                var dataOffset = recordOffset + 30 + nameLength;

                if (compressionMethod == 0)
                {
                    yield return new RawFileEntry(name, uncompressedSize, dataOffset);
                }
                else if (compressionMethod == 8)
                {
                    yield return new DeflatedFileEntry(name, uncompressedSize, compressedSize, dataOffset);
                }
                else
                {
                    throw new Exception($"Invalid compression method: {compressionMethod} (expected 0 or 8)");
                }
            }
        }
    }
}