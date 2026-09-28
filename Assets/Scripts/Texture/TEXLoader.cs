using UnityEngine;
using Unity.Collections.LowLevel.Unsafe;
using System.IO;
using System.Buffers;
using System;
using System.Xml.Linq;

namespace Dummiesman.TextureLoading
{
    public static class TEXLoader
    {
        public enum TEXType : int
        {
            P8 = 1,
            P8A8 = 2,
            PA8 = 14,
            P4 = 15,
            PA4 = 16,
            RGB888 = 17,
            ARGB1555 = 6,
            RGBA8888 = 18,
            DXT1 = 22,
            // DXT3 = 14
            DXT5 = 26
        }

        /// <summary>
        /// BinaryReader.Read may return short reads on network/compressed streams.
        /// </summary>
        private static void ReadExact(BinaryReader reader, byte[] buffer, int count)
        {
            int read = 0;
            while (read < count)
            {
                int n = reader.Read(buffer, read, count - read);
                if (n <= 0)
                    throw new EndOfStreamException("Truncated TEX file.");
                read += n;
            }
        }

        /// <summary>
        /// BGRA (file) -> RGBA (Color32 layout), swizzled in place over the palette only.
        /// </summary>
        private static unsafe void SwizzlePalette(byte* palette, int entries, bool alpha)
        {
            for (int i = 0; i < entries; i++)
            {
                byte* e = palette + (i << 2);
                byte b = e[0];
                e[0] = e[2];
                e[2] = b;
                if (!alpha) e[3] = 255;
            }
        }

        /// <summary>
        /// 24bpp -> RGB24 raw data, rows flipped for DX->OpenGL coordinate space.
        /// </summary>
        private static unsafe void ReadRGBColorData(BinaryReader reader, Texture2D texture, int width, int height)
        {
            int rowBytes = width * 3;
            int total = rowBytes * height;

            var pool = ArrayPool<byte>.Shared;
            byte[] src = pool.Rent(total);
            try
            {
                ReadExact(reader, src, total);

                var raw = texture.GetRawTextureData<byte>();
                if (raw.Length < total)
                    throw new InvalidDataException("Texture buffer smaller than TEX pixel data.");

                fixed (byte* srcPtr = src)
                {
                    byte* dstBase = (byte*)NativeArrayUnsafeUtility.GetUnsafePtr(raw);
                    for (int y = 0; y < height; y++)
                    {
                        Buffer.MemoryCopy(
                            srcPtr + (y * rowBytes),
                            dstBase + ((height - 1 - y) * rowBytes),
                            rowBytes, rowBytes);
                    }
                }
            }
            finally { pool.Return(src); }
        }

        /// <summary>
        /// 32bpp -> RGBA32 raw data, rows flipped for DX->OpenGL coordinate space.
        /// </summary>
        private static unsafe void ReadRGBAColorData(BinaryReader reader, Texture2D texture, int width, int height)
        {
            int rowBytes = width * 4;
            int total = rowBytes * height;

            var pool = ArrayPool<byte>.Shared;
            byte[] src = pool.Rent(total);
            try
            {
                ReadExact(reader, src, total);

                var raw = texture.GetRawTextureData<byte>();
                if (raw.Length < total)
                    throw new InvalidDataException("Texture buffer smaller than TEX pixel data.");

                fixed (byte* srcPtr = src)
                {
                    byte* dstBase = (byte*)NativeArrayUnsafeUtility.GetUnsafePtr(raw);
                    for (int y = 0; y < height; y++)
                    {
                        Buffer.MemoryCopy(
                            srcPtr + (y * rowBytes),
                            dstBase + ((height - 1 - y) * rowBytes),
                            rowBytes, rowBytes);
                    }
                }
            }
            finally { pool.Return(src); }
        }

        /// <summary>
        /// 4bpp paletted -> RGBA32 raw data, rows flipped for DX->OpenGL coordinate space.
        /// </summary>
        public static unsafe void ReadPalettedColorData4bpp(BinaryReader reader, Texture2D texture, int width, int height, bool alpha = false)
        {
            const int palBytes = 4 * 16;
            int rowBytes = (width + 1) >> 1;          // handles odd widths
            int mapBytes = rowBytes * height;

            var pool = ArrayPool<byte>.Shared;
            byte[] pal = pool.Rent(palBytes);
            byte[] map = pool.Rent(mapBytes);
            try
            {
                ReadExact(reader, pal, palBytes);
                ReadExact(reader, map, mapBytes);

                var raw = texture.GetRawTextureData<Color32>();
                if (raw.Length < width * height)
                    throw new InvalidDataException("Texture buffer smaller than TEX pixel data.");

                fixed (byte* palPtr = pal)
                fixed (byte* mapPtr = map)
                {
                    SwizzlePalette(palPtr, 16, alpha);

                    Color32* palette = (Color32*)palPtr;
                    Color32* dstBase = (Color32*)NativeArrayUnsafeUtility.GetUnsafePtr(raw);

                    for (int y = 0; y < height; y++)
                    {
                        byte* src = mapPtr + (y * rowBytes);
                        Color32* dst = dstBase + ((height - 1 - y) * width);

                        int x = 0;
                        int pairs = width >> 1;
                        for (int p = 0; p < pairs; p++, x += 2)
                        {
                            byte nibbles = src[p];
                            dst[x + 0] = palette[nibbles & 0xF];
                            dst[x + 1] = palette[nibbles >> 4];
                        }
                        if ((width & 1) != 0)         // trailing half-byte
                            dst[x] = palette[src[pairs] & 0xF];
                    }
                }
            }
            finally
            {
                pool.Return(pal);
                pool.Return(map);
            }
        }

        /// <summary>
        /// 8bpp paletted -> RGBA32 raw data, rows flipped for DX->OpenGL coordinate space.
        /// </summary>
        private static unsafe void ReadPalettedColorData8bpp(BinaryReader reader, Texture2D texture, int width, int height, bool alpha = false)
        {
            const int palBytes = 4 * 256;
            int mapBytes = width * height;

            var pool = ArrayPool<byte>.Shared;
            byte[] pal = pool.Rent(palBytes);
            byte[] map = pool.Rent(mapBytes);
            try
            {
                ReadExact(reader, pal, palBytes);
                ReadExact(reader, map, mapBytes);

                var raw = texture.GetRawTextureData<Color32>();
                if (raw.Length < mapBytes)
                    throw new InvalidDataException("Texture buffer smaller than TEX pixel data.");

                fixed (byte* palPtr = pal)
                fixed (byte* mapPtr = map)
                {
                    SwizzlePalette(palPtr, 256, alpha);

                    Color32* palette = (Color32*)palPtr;
                    Color32* dstBase = (Color32*)NativeArrayUnsafeUtility.GetUnsafePtr(raw);

                    for (int y = 0; y < height; y++)
                    {
                        byte* src = mapPtr + (y * width);
                        Color32* dst = dstBase + ((height - 1 - y) * width);
                        for (int x = 0; x < width; x++)
                            dst[x] = palette[src[x]];
                    }
                }
            }
            finally
            {
                pool.Return(pal);
                pool.Return(map);
            }
        }

        public static AGETexture Load(Stream stream)
        {
            Texture2D returnTexture = null;
            int width, height, mips, frames;
            AGETexFlags flags;
            TEXType type;
            bool compressed = false;

            using (var r = new BinaryReader(stream, System.Text.Encoding.UTF8, true))
            {
                width = r.ReadUInt16();
                height = r.ReadUInt16();
                type = (TEXType)r.ReadUInt16();
                mips = r.ReadUInt16();
                frames = r.ReadUInt16();
                flags = (AGETexFlags)r.ReadInt32();

                if (frames > 1)
                    Debug.LogWarning($"TEX file has {frames} frames, only the first will be loaded.");

                switch (type)
                {
                    case TEXType.DXT5:
                    case TEXType.DXT1:
                        {
                            compressed = true;
                            TextureFormat format = (type == TEXType.DXT5) ? TextureFormat.DXT5 : TextureFormat.DXT1;
                            returnTexture = new Texture2D(width, height, format, (mips > 1));

                            var raw = returnTexture.GetRawTextureData<byte>();
                            int available = (int)(r.BaseStream.Length - r.BaseStream.Position);
                            if (available < raw.Length)
                            {
                                Debug.LogWarning($"TEX has {available} bytes of DXT data, expected {raw.Length}.");
                                UnityEngine.Object.Destroy(returnTexture);
                                returnTexture = null;
                                break;
                            }

                            var pool = ArrayPool<byte>.Shared;
                            byte[] src = pool.Rent(raw.Length);
                            try
                            {
                                ReadExact(r, src, raw.Length);
                                unsafe
                                {
                                    fixed (byte* srcPtr = src)
                                    {
                                        Buffer.MemoryCopy(srcPtr,
                                            NativeArrayUnsafeUtility.GetUnsafePtr(raw),
                                            raw.Length, raw.Length);
                                    }
                                }
                            }
                            finally { pool.Return(src); }
                        }
                        break;
                    case TEXType.RGB888:
                        returnTexture = new Texture2D(width, height, TextureFormat.RGB24, true);
                        ReadRGBColorData(r, returnTexture, width, height);
                        break;
                    case TEXType.RGBA8888:
                        returnTexture = new Texture2D(width, height, TextureFormat.RGBA32, true);
                        ReadRGBAColorData(r, returnTexture, width, height);
                        break;
                    case TEXType.PA8:
                    case TEXType.P8:
                    case TEXType.P4:
                    case TEXType.PA4:
                        {
                            bool hasAlpha = (type == TEXType.PA8 || type == TEXType.PA4);
                            returnTexture = new Texture2D(width, height, TextureFormat.RGBA32, true);

                            // MC or MM2 type paletted texture?
                            if (type == TEXType.PA8 || type == TEXType.P8)
                                ReadPalettedColorData8bpp(r, returnTexture, width, height, hasAlpha);
                            else
                                ReadPalettedColorData4bpp(r, returnTexture, width, height, hasAlpha);
                        }
                        break;
                    default:
                        Debug.LogWarning($"TEX file failed to load, unknown or unsupported type {type}");
                        break;
                }

                if (returnTexture != null)
                {
                    returnTexture.wrapModeU = (flags & AGETexFlags.ClampU) != 0
                        ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
                    returnTexture.wrapModeV = (flags & AGETexFlags.ClampV) != 0
                        ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
                }
            }

            if (returnTexture != null)
                returnTexture.Apply(!compressed, false);

            return new AGETexture(returnTexture, flags);
        }

        public static AGETexture Load(byte[] data)
        {
            using (var ms = new MemoryStream(data))
                return Load(ms);
        }

        public static AGETexture Load(string path)
        {
            using (var fs = new FileStream(path, FileMode.Open))
                return Load(fs);
        }
    }
}