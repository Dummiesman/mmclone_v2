using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Angel.Archive
{
    public abstract class IFileEntry
    {
        public string Name { get; protected set; }
        public uint Size { get; protected set; }
        public uint Offset { get; protected set; }

        public abstract byte[] Extract(Stream input);
        public abstract Stream GetStream(Stream input);

        public IFileEntry(string name, uint size)
        {
            Name = name;
            Size = size;
        }
    }

    public abstract class IFileReader
    {
        public abstract string Identifier { get; }
        public abstract bool IsValid(BinaryReader input);
        public abstract IEnumerable<IFileEntry> Read(BinaryReader input);
    }

    public class RawFileEntry : IFileEntry
    {
        public RawFileEntry(string name, uint size, uint offset)
            : base(name, size)

        {
            Offset = offset;
        }

        public override byte[] Extract(Stream input)
        {
            input.Seek(Offset, SeekOrigin.Begin);

            var result = new byte[Size];

            if (input.Read(result, 0, result.Length) != Size)
            {
                return null;
            }

            return result;
        }

        public override Stream GetStream(Stream input)
        {
            var subStream = new SubStream(input, Offset, Size);
            return subStream;
        }
    }

    public class DeflatedFileEntry : IFileEntry
    {
        public uint CompressedSize { get; protected set; }

        public DeflatedFileEntry(string name, uint size, uint compressed_size, uint offset)
            : base(name, size)

        {
            CompressedSize = compressed_size;
            Offset = offset;
        }

        public override Stream GetStream(Stream input)
        {
            /*var subStream = new SubStream(input, Offset, CompressedSize);
            subStream.Seek(0, SeekOrigin.Begin);
            return new System.IO.Compression.DeflateStream(subStream, System.IO.Compression.CompressionMode.Decompress);*/

            return new MemoryStream(Extract(input));
        }

        public override byte[] Extract(Stream input)
        {
            input.Seek(Offset, SeekOrigin.Begin);

#if USE_NET_COMPRESSION
            /*if (Application.isEditor)
            {
                Log.Info($"Extracting from DeflatedFileEntry with USE_NET_COMPRESION");
            }*/
            input = new System.IO.Compression.DeflateStream(input, System.IO.Compression.CompressionMode.Decompress);
#elif USE_MANAGED_ZLIB
            /*if (Application.isEditor)
            {
                Log.Info($"Extracting from DeflatedFileEntry with USE_MANAGED_ZLIB");
            }*/
            input = new Zlib.DeflateStream(input, Zlib.CompressionMode.Decompress);
#else
            /*if (Application.isEditor)
            {
                Log.Info($"Extracting from DeflatedFileEntry with DEFAULT");
            }
            */
            input = new Ionic.Zlib.DeflateStream(input, Ionic.Zlib.CompressionMode.Decompress);
#endif

            var result = new byte[Size];

            if (input.Read(result, 0, result.Length) != Size)
            {
                return null;
            }

            return result;
        }
    }
}