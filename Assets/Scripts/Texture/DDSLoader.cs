using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Dummiesman.TextureLoading
{
    public class DDSLoader
    {
        public enum PixelFormatFlags
        {
            DDPF_ALPHAPIXELS = 0x1,
            DDPF_ALPHA = 0x2,
            DDPF_FOURCC = 0x4,
            DDPF_RGB = 0x40,
            DDPF_YUV = 0x200,
            DDPF_LUMINANCE = 0x20000
        }

        private static int GetShiftValue(int mask)
        {
            for (int i = 0; i < 32; i++)
            {
                if ((mask & (1 << i)) != 0)
                    return i;
            }
            return 0;
        }

        private static float GetShiftMultiplier(int mask)
        {
            int numSetBits = 0;
            for (int i = 0; i < 32; i++)
            {
                if ((mask & (1 << i)) != 0)
                    numSetBits++;
            }
            return (numSetBits > 0) ? 255f / ((1 << numSetBits) - 1): 0f;
        }

        private static int ConvertLittleEndian(byte[] array, int offset, int amount)
        {
            int result = 0;
            for(int i=offset; i < offset + amount; i++)
            {
                result |= array[i] << ((i - offset) * 8);
            }
            return result;
        }

        private static int GetNumBitsSet(int mask)
        {
            int numSetBits = 0;
            for (int i = 0; i < 32; i++)
            {
                if ((mask & (1 << i)) != 0)
                    numSetBits++;
            }
            return numSetBits;
        }

        public static Texture2D Load(Stream ddsStream)
        {
            var reader = new BinaryReader(ddsStream);
            var magic = new string(reader.ReadChars(4));
            if(magic != "DDS ")
            {
                throw new Exception("DDS header incorrect magic.");
            }

            int headerSize = reader.ReadInt32();
            if(headerSize != 124)
            {
                throw new Exception("DDS header incorrect size.");
            }

            int flags = reader.ReadInt32();
            int height = reader.ReadInt32();
            int width = reader.ReadInt32();

            reader.BaseStream.Seek(8, SeekOrigin.Current);

            int mipCount = reader.ReadInt32();
            byte[] reserved = reader.ReadBytes(11*4);

            int pixelFormatSize = reader.ReadInt32();
            if (pixelFormatSize != 32)
            {
                throw new Exception($"DDS header incorrect pixelFormat size.");
            }

            PixelFormatFlags pixelFormatFlags = (PixelFormatFlags)reader.ReadInt32();
            string pixelFormatFourCC = new string(reader.ReadChars(4));
            int rgbBitCount = reader.ReadInt32();
            int rBitMask = reader.ReadInt32();
            int gBitMask = reader.ReadInt32();
            int bBitMask = reader.ReadInt32();
            int aBitMask = reader.ReadInt32();

            int[] caps = new int[4];
            for (int i = 0; i < caps.Length; i++)
                caps[i] = reader.ReadInt32();
            int reserved2 = reader.ReadInt32();
            
            //finally, read pixel data
            if((pixelFormatFlags & PixelFormatFlags.DDPF_FOURCC) != 0)
            {
                //FOURCC, Check DXT1 and DXT5
                bool supported = pixelFormatFourCC[3] == '5' || pixelFormatFourCC[3] == '1';
                if (!supported)
                    throw new Exception($"Cannot load DDS: Format is FOURCC but not supported ({pixelFormatFourCC}).");
                
                TextureFormat textureFormat = pixelFormatFourCC[3] == '1' ? TextureFormat.DXT1 : TextureFormat.DXT5;
                byte[] dxtBytes = reader.ReadBytes((int)(reader.BaseStream.Length - reader.BaseStream.Position));

                Texture2D texture = new Texture2D(width, height, textureFormat, mipCount > 0);
                texture.LoadRawTextureData(dxtBytes);
                texture.Apply();
                return texture;
            }
            else
            {
                //'CLASSIC' DDS
                int bytesPerPixel = rgbBitCount >> 3;
                byte[] data = reader.ReadBytes((int)(reader.BaseStream.Length - reader.BaseStream.Position));
                Color32[] pixels = new Color32[width * height];

                int[] shiftAmounts = new[] { GetShiftValue(rBitMask), GetShiftValue(gBitMask), GetShiftValue(bBitMask), GetShiftValue(aBitMask) };
                float[] shiftMultipliers = new[] { GetShiftMultiplier(rBitMask), GetShiftMultiplier(gBitMask), GetShiftMultiplier(bBitMask), GetShiftMultiplier(aBitMask) };

                for(int y = 0; y < height; y++)
                {
                    for(int x = 0; x < width; x++)
                    {
                        int dataOrigin = (y * width * bytesPerPixel) + (x * bytesPerPixel);
                        int pixelData = ConvertLittleEndian(data, dataOrigin, bytesPerPixel);

                        byte rColor = (byte)(((pixelData & rBitMask) >> shiftAmounts[0]) * shiftMultipliers[0]);
                        byte gColor = (byte)(((pixelData & gBitMask) >> shiftAmounts[1]) * shiftMultipliers[1]);
                        byte bColor = (byte)(((pixelData & bBitMask) >> shiftAmounts[2]) * shiftMultipliers[2]);
                        byte aColor = ((pixelFormatFlags &  PixelFormatFlags.DDPF_ALPHAPIXELS) == 0) ? byte.MaxValue : (byte)(((pixelData & aBitMask) >> shiftAmounts[3]) * shiftMultipliers[3]);
                        pixels[(y * width) + x] = (new Color32(rColor, gColor, bColor, aColor));
                    }
                }

                var texture = new Texture2D(width, height, TextureFormat.ARGB32, false);
                texture.SetPixels32(pixels);
                texture.Apply();
                return texture;
            }
        }

        public static Texture2D Load(string ddsPath)
        {
            using (var stream = File.OpenRead(ddsPath))
            {
                return Load(stream);
            }
        }
    }
}
