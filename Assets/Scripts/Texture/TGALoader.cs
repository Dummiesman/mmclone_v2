using System;
using System.Buffers;
using System.Buffers.Binary;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

namespace Dummiesman.TextureLoading
{
    public class TGALoader
    {
        private static readonly bool s_rgb24IsNative =
            SystemInfo.SupportsTextureFormat(TextureFormat.RGB24);

        /// <summary>
        /// Set to false to always decode into RGBA32, even where RGB24 is native.
        /// </summary>
        public static bool PreferRGB24 = true;

        private enum TGAImageType
        {
            None = 0,
            ColorMapped = 1,
            TrueColor = 2,
            Grayscale = 3,
            ColorMappedRLE = 9,
            TrueColorRLE = 10,
            GrayscaleRLE = 11
        }

        public static Texture2D Load(string fileName, bool mipChain = true, bool linear = false,
                                     bool makeNoLongerReadable = false)
        {
            using (var fs = new FileStream(fileName, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024))
                return Load(fs, mipChain, linear, makeNoLongerReadable);
        }

        public static Texture2D Load(byte[] bytes, bool mipChain = true, bool linear = false,
                                     bool makeNoLongerReadable = false)
        {
            using (var ms = new MemoryStream(bytes, writable: false))
                return Load(ms, mipChain, linear, makeNoLongerReadable);
        }

        /// <summary>
        /// Decodes a TGA from the supplied stream. The stream is NOT disposed and NOT seeked;
        /// for RLE images the reader buffers ahead, so the final stream position is unspecified.
        /// </summary>
        public static Texture2D Load(Stream tgaStream, bool mipChain = true, bool linear = false,
                                     bool makeNoLongerReadable = false)
        {
            Span<byte> header = stackalloc byte[18];
            ReadExactly(tgaStream, header);

            byte idLength = header[0];
            byte colorMapType = header[1];
            var imageType = (TGAImageType)header[2];
            int colorMapLength = BinaryPrimitives.ReadUInt16LittleEndian(header.Slice(5, 2));
            int colorMapEntrySize = header[7];
            int width = BinaryPrimitives.ReadUInt16LittleEndian(header.Slice(12, 2));
            int height = BinaryPrimitives.ReadUInt16LittleEndian(header.Slice(14, 2));
            int bitDepth = header[16];
            byte imageDescriptor = header[17];

            ValidateHeader(imageType, colorMapType, colorMapEntrySize, bitDepth, width, height);

            // Skip the image ID field.
            if (idLength > 0)
                SkipBytes(tgaStream, idLength);

            // Read or skip the colormap. It must be consumed either way to stay byte-aligned.
            Color32[] colorMap = null;
            if (colorMapType == 1)
            {
                if (imageType == TGAImageType.ColorMapped)
                    colorMap = ReadColorMap(tgaStream, colorMapLength, colorMapEntrySize);
                else
                    SkipBytes(tgaStream, colorMapLength * ((colorMapEntrySize + 7) / 8));
            }

            // 24-bit true-colour can use RGB24 to save a third of the VRAM, but only where the
            // GPU takes that format natively. Elsewhere RGBA32 is cheaper
            bool rgb24 = bitDepth == 24
                         && (imageType == TGAImageType.TrueColor || imageType == TGAImageType.TrueColorRLE)
                         && PreferRGB24
                         && s_rgb24IsNative;
            int stride = rgb24 ? 3 : 4;

            var tex = new Texture2D(width, height, rgb24 ? TextureFormat.RGB24 : TextureFormat.RGBA32,
                                    mipChain, linear);
            try
            {
                int count = width * height;
                NativeArray<byte> raw = tex.GetRawTextureData<byte>();

                // GetRawTextureData returns the whole mip chain; mip 0 comes first.
                Span<byte> px;
                unsafe
                {
                    px = new Span<byte>(NativeArrayUnsafeUtility.GetUnsafePtr(raw), count * stride);
                }

                switch (imageType)
                {
                    case TGAImageType.TrueColor:
                        LoadTrueColor(tgaStream, bitDepth, px, count, stride);
                        break;
                    case TGAImageType.Grayscale:
                        LoadGrayscale(tgaStream, px, count);
                        break;
                    case TGAImageType.ColorMapped:
                        LoadPaletted(tgaStream, bitDepth, px, count, colorMap);
                        break;
                    case TGAImageType.TrueColorRLE:
                        LoadRLE(tgaStream, bitDepth / 8, px, stride, count);
                        break;
                    case TGAImageType.GrayscaleRLE:
                        LoadRLE(tgaStream, 1, px, stride, count);
                        break;
                }

                // Flip in the pixel buffer instead of allocating a second Texture2D.
                if ((imageDescriptor & 0x20) != 0)
                    FlipVertical(px, width, height, stride);
                if ((imageDescriptor & 0x10) != 0)
                    FlipHorizontal(px, width, height, stride);

                tex.Apply(mipChain, makeNoLongerReadable);
                return tex;
            }
            catch
            {
                UnityEngine.Object.Destroy(tex);
                throw;
            }
        }

        private static void ValidateHeader(TGAImageType imageType, byte colorMapType,
                                           int colorMapEntrySize, int bitDepth, int width, int height)
        {
            if (width <= 0 || height <= 0)
                throw new InvalidDataException($"TGA has invalid dimensions ({width}x{height}).");

            switch (imageType)
            {
                case TGAImageType.TrueColor:
                case TGAImageType.TrueColorRLE:
                    if (bitDepth != 24 && bitDepth != 32)
                        throw new NotSupportedException($"Unsupported true-colour TGA bit depth: {bitDepth}.");
                    break;

                case TGAImageType.Grayscale:
                case TGAImageType.GrayscaleRLE:
                    if (bitDepth != 8)
                        throw new NotSupportedException($"Unsupported grayscale TGA bit depth: {bitDepth}.");
                    break;

                case TGAImageType.ColorMapped:
                    if (colorMapType != 1)
                        throw new InvalidDataException("Paletted TGA has no colormap.");
                    if (bitDepth != 8 && bitDepth != 16)
                        throw new NotSupportedException($"Unsupported paletted TGA index size: {bitDepth}.");
                    if (colorMapEntrySize != 24 && colorMapEntrySize != 32)
                        throw new NotSupportedException($"Unsupported TGA colormap entry size: {colorMapEntrySize}.");
                    break;

                default:
                    throw new NotSupportedException($"Unsupported TGA image type: {imageType}.");
            }
        }

        private static Color32[] ReadColorMap(Stream s, int length, int entrySize)
        {
            int entryBytes = entrySize / 8;
            var map = new Color32[length];

            byte[] rented = ArrayPool<byte>.Shared.Rent(length * entryBytes);
            try
            {
                Span<byte> buf = rented.AsSpan(0, length * entryBytes);
                ReadExactly(s, buf);

                for (int i = 0; i < length; i++)
                {
                    int o = i * entryBytes;
                    map[i] = new Color32(buf[o + 2], buf[o + 1], buf[o],
                                         entryBytes == 4 ? buf[o + 3] : (byte)255);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rented);
            }

            return map;
        }

        private static void LoadTrueColor(Stream s, int bitDepth, Span<byte> dst, int count, int stride)
        {
            if (bitDepth == 32)
            {
                // RGBA32 destination, BGRA source - same size, swap in place.
                ReadExactly(s, dst);
                SwizzleBgraToRgba(dst);
            }
            else if (stride == 3)
            {
                // RGB24 destination, BGR source - same size, swap in place.
                ReadExactly(s, dst);
                for (int o = 0; o < dst.Length; o += 3)
                {
                    byte b = dst[o];
                    dst[o] = dst[o + 2];
                    dst[o + 2] = b;
                }
            }
            else
            {
                // RGBA32 destination, BGR source - tail-load and expand forward,
                // fusing the channel swap and the alpha fill into a single pass.
                ReadExactly(s, dst.Slice(count));
                for (int i = 0; i < count; i++)
                {
                    int o = count + i * 3;
                    byte b = dst[o], g = dst[o + 1], r = dst[o + 2];

                    int d = i * 4;
                    dst[d] = r;
                    dst[d + 1] = g;
                    dst[d + 2] = b;
                    dst[d + 3] = 255;
                }
            }
        }

        private static void SwizzleBgraToRgba(Span<byte> dst)
        {
            Span<uint> p = MemoryMarshal.Cast<byte, uint>(dst);
            for (int i = 0; i < p.Length; i++)
            {
                uint u = p[i];
                p[i] = (u & 0xFF00FF00u)
                     | ((u & 0x00FF0000u) >> 16)
                     | ((u & 0x000000FFu) << 16);
            }
        }

        private static void LoadGrayscale(Stream s, Span<byte> dst, int count)
        {
            int offset = count * 3; // 1 source byte per pixel, 4 destination bytes
            ReadExactly(s, dst.Slice(offset));

            for (int i = 0; i < count; i++)
            {
                byte v = dst[offset + i];
                int d = i * 4;
                dst[d] = v;
                dst[d + 1] = v;
                dst[d + 2] = v;
                dst[d + 3] = 255;
            }
        }

        private static void LoadPaletted(Stream s, int indexBits, Span<byte> dst, int count, Color32[] palette)
        {
            int idxSize = indexBits > 8 ? 2 : 1;
            int offset = count * (4 - idxSize);
            ReadExactly(s, dst.Slice(offset));

            for (int i = 0; i < count; i++)
            {
                int o = offset + i * idxSize;
                int idx = idxSize == 1 ? dst[o] : (dst[o] | (dst[o + 1] << 8));

                if ((uint)idx >= (uint)palette.Length)
                    throw new InvalidDataException($"TGA palette index {idx} is out of range.");

                Color32 c = palette[idx];
                int d = i * 4;
                dst[d] = c.r;
                dst[d + 1] = c.g;
                dst[d + 2] = c.b;
                dst[d + 3] = c.a;
            }
        }

        private static void LoadRLE(Stream s, int srcBytesPerPixel, Span<byte> dst, int dstStride, int count)
        {
            byte[] scratch = ArrayPool<byte>.Shared.Rent(16 * 1024);
            try
            {
                var src = new ByteSource(s, scratch);
                int i = 0;

                while (i < count)
                {
                    byte packet = src.ReadByte();
                    int run = (packet & 0x7F) + 1;
                    if (run > count - i)
                        run = count - i; // tolerate a run that overshoots the image

                    if ((packet & 0x80) != 0)
                    {
                        ReadPixel(ref src, srcBytesPerPixel, out byte r, out byte g, out byte b, out byte a);
                        for (int k = 0; k < run; k++)
                            Store(dst, (i + k) * dstStride, dstStride, r, g, b, a);
                        i += run;
                    }
                    else
                    {
                        for (int k = 0; k < run; k++)
                        {
                            ReadPixel(ref src, srcBytesPerPixel, out byte r, out byte g, out byte b, out byte a);
                            Store(dst, i * dstStride, dstStride, r, g, b, a);
                            i++;
                        }
                    }
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(scratch);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ReadPixel(ref ByteSource src, int bpp,
                                      out byte r, out byte g, out byte b, out byte a)
        {
            switch (bpp)
            {
                case 1:
                    r = g = b = src.ReadByte();
                    a = 255;
                    break;
                case 3:
                    b = src.ReadByte();
                    g = src.ReadByte();
                    r = src.ReadByte();
                    a = 255;
                    break;
                default:
                    b = src.ReadByte();
                    g = src.ReadByte();
                    r = src.ReadByte();
                    a = src.ReadByte();
                    break;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Store(Span<byte> dst, int o, int stride, byte r, byte g, byte b, byte a)
        {
            dst[o] = r;
            dst[o + 1] = g;
            dst[o + 2] = b;
            if (stride == 4)
                dst[o + 3] = a;
        }

        /// <summary>
        /// Pull-buffered byte reader. Removes the per-byte virtual call and, on an
        /// unbuffered stream, the per-byte syscall. Reads ahead past the image data.
        /// </summary>
        private struct ByteSource
        {
            private readonly Stream _stream;
            private readonly byte[] _buf;
            private int _pos;
            private int _len;

            public ByteSource(Stream stream, byte[] buffer)
            {
                _stream = stream;
                _buf = buffer;
                _pos = 0;
                _len = 0;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public byte ReadByte()
            {
                if (_pos == _len)
                    Refill();
                return _buf[_pos++];
            }

            private void Refill()
            {
                _len = _stream.Read(_buf, 0, _buf.Length);
                _pos = 0;
                if (_len <= 0)
                    throw new EndOfStreamException("Truncated TGA pixel data.");
            }
        }


        private static void FlipVertical(Span<byte> px, int width, int height, int stride)
        {
            int rowBytes = width * stride;
            byte[] rented = ArrayPool<byte>.Shared.Rent(rowBytes);
            try
            {
                Span<byte> tmp = rented.AsSpan(0, rowBytes);
                for (int y = 0; y < height / 2; y++)
                {
                    Span<byte> top = px.Slice(y * rowBytes, rowBytes);
                    Span<byte> bottom = px.Slice((height - 1 - y) * rowBytes, rowBytes);
                    top.CopyTo(tmp);
                    bottom.CopyTo(top);
                    tmp.CopyTo(bottom);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }

        private static void FlipHorizontal(Span<byte> px, int width, int height, int stride)
        {
            int rowBytes = width * stride;
            Span<byte> tmp = stackalloc byte[4];

            for (int y = 0; y < height; y++)
            {
                Span<byte> row = px.Slice(y * rowBytes, rowBytes);
                for (int x = 0; x < width / 2; x++)
                {
                    Span<byte> a = row.Slice(x * stride, stride);
                    Span<byte> b = row.Slice((width - 1 - x) * stride, stride);
                    a.CopyTo(tmp);
                    b.CopyTo(a);
                    tmp.Slice(0, stride).CopyTo(b);
                }
            }
        }

        private static void ReadExactly(Stream s, Span<byte> buffer)
        {
            int read = 0;
            while (read < buffer.Length)
            {
                int n = s.Read(buffer.Slice(read));
                if (n <= 0)
                    throw new EndOfStreamException("Truncated TGA data.");
                read += n;
            }
        }

        private static void SkipBytes(Stream s, int count)
        {
            if (count <= 0)
                return;

            if (s.CanSeek)
            {
                s.Seek(count, SeekOrigin.Current);
                return;
            }

            Span<byte> scratch = stackalloc byte[256];
            while (count > 0)
            {
                int n = s.Read(scratch.Slice(0, Math.Min(count, scratch.Length)));
                if (n <= 0)
                    throw new EndOfStreamException("Truncated TGA data.");
                count -= n;
            }
        }
    }
}