using UnityEngine;
using System.IO;
using SharpFileSystem.IO;
using Unity.Collections;
using UnityEngine.Experimental.Rendering;

namespace Dummiesman.TextureLoading
{
    public class ImageLoader
    {
        public enum TextureFormat
        {
            DDS,
            TGA,
            BMP,
            PNG,
            JPG
        }


        /// <summary>
        /// Loads a texture from a stream
        /// </summary>
        /// <param name="stream">The stream</param>
        /// <param name="format">The format the stream contains</param>
        /// <returns></returns>
        public static AGETexture LoadTexture(Stream stream, TextureFormat format)
        {
            if (format == TextureFormat.BMP)
            {
                return new AGETexture(BMPLoader.Load(stream), AGETexFlags.Default);
            }
            else if (format == TextureFormat.DDS)
            {
                var texture = DDSLoader.Load(stream);
                return new AGETexture(texture, AGETexFlags.Default);
            }
            else if (format == TextureFormat.JPG || format == TextureFormat.PNG)
            {
                byte[] buffer = new byte[stream.Length];
                stream.Read(buffer, 0, (int)stream.Length);

                Texture2D texture;
                if(format == TextureFormat.PNG)
                {
                    texture = new Texture2D(1, 1, UnityEngine.TextureFormat.ARGB32, true);
                }
                else
                {
                    texture = new Texture2D(1, 1);
                }
                texture.LoadImage(buffer);
                texture = VerifyFormat(texture);

                return new AGETexture(texture, AGETexFlags.Default);
            }
            else if (format == TextureFormat.TGA)
            {
                var texture = TGALoader.Load(stream);
                if(texture != null)
                {
                    texture = VerifyFormat(texture);
                }
                return new AGETexture(texture, AGETexFlags.Default);
            }
            else
            {
                return null;
            }
        }

        private static Texture2D VerifyFormat(Texture2D tex)
        {
            var fmt = tex.format;
            if (fmt != UnityEngine.TextureFormat.ARGB32 && fmt != UnityEngine.TextureFormat.RGBA32)
                return tex;

            bool isArgb = fmt == UnityEngine.TextureFormat.ARGB32;

            // memory order is A,R,G,B for ARGB32 and R,G,B,A for RGBA32,
            // so on little-endian the alpha byte sits at opposite ends of the uint
            uint mask = isArgb ? 0x000000FFu : 0xFF000000u;
            uint limit = isArgb ? 250u : 250u << 24;

            int count = tex.width * tex.height;   // mip 0; raw data holds the whole chain
            var px = tex.GetRawTextureData<uint>();

            if (HasAlpha(px, count, mask, limit))
                return tex;

            bool mips = tex.mipmapCount > 1;
            bool linear = QualitySettings.activeColorSpace == ColorSpace.Linear
                          && tex.graphicsFormat != GraphicsFormatUtility.GetGraphicsFormat(fmt, true);

            var tex24 = new Texture2D(tex.width, tex.height, UnityEngine.TextureFormat.RGB24, mips, linear);
            tex24.wrapMode = tex.wrapMode;
            tex24.filterMode = tex.filterMode;
            tex24.anisoLevel = tex.anisoLevel;

            var src = tex.GetRawTextureData<byte>();
            var dst = tex24.GetRawTextureData<byte>();
            int rOff = isArgb ? 1 : 0;

            for (int i = 0, o = 0; i < count; i++)
            {
                int s = i * 4 + rOff;
                dst[o++] = src[s];
                dst[o++] = src[s + 1];
                dst[o++] = src[s + 2];
            }

            tex24.Apply(mips, false);   // updateMipmaps, makeNoLongerReadable
            return tex24;
        }

        private static bool HasAlpha(NativeArray<uint> px, int count, uint mask, uint limit)
        {
            // strided pass first — transparency is rarely one isolated pixel,
            // so the common "has alpha" case usually exits after ~1/64th of the reads
            for (int i = 0; i < count; i += 64)
                if ((px[i] & mask) < limit) return true;

            for (int i = 0; i < count; i++)
                if ((px[i] & mask) < limit) return true;

            return false;
        }

        private static byte[] StreamToByteArray(Stream stream)
        {
            return stream.ReadAllBytes();
        }

        /// <summary>
        /// Loads a texture from a file
        /// </summary>
        /// <param name="fn"></param>
        /// <param name="normalMap"></param>
        /// <returns></returns>
        public static AGETexture LoadTexture(string fn)
        {
            var textureStream = AssetManager.Open(fn);
            if (textureStream == null)
                return null;

            string ext = Path.GetExtension(fn).ToLowerInvariant();
            string name = Path.GetFileName(fn);
            AGETexture returnTex = null;

            switch (ext)
            {
                case ".png":
                case ".jpg":
                case ".jpeg":
                    {
                        var texture = new Texture2D(1, 1, UnityEngine.TextureFormat.ARGB32, true);
                        texture.LoadImage(StreamToByteArray(textureStream));
                        returnTex = new AGETexture(texture, AGETexFlags.Default);
                    }
                    break;
                case ".dds":
                    {
                        var texture  = DDSLoader.Load(textureStream);
                        returnTex = new AGETexture(texture, AGETexFlags.Default);
                    }
                    break;
                case ".tga":
                    {
                        var texture = TGALoader.Load(textureStream);
                        returnTex = new AGETexture(texture, AGETexFlags.Default);
                    }
                    break;
                case ".bmp":
                    {
                        var texture = BMPLoader.Load(textureStream);
                        returnTex = new AGETexture(texture, AGETexFlags.Default);
                    }
                    break;
                case ".tex":
                    returnTex = TEXLoader.Load(textureStream);
                    break;
                default:
                    Debug.LogError("Could not load texture " + name + " because its format is not supported : " + fn);
                    break;
            }
            
            if (returnTex != null)
            {
                if (!ArgParser.GetFlag("noverifyformat"))
                {
                    var verified = VerifyFormat(returnTex.Texture);
                    if(verified != returnTex.Texture)
                    {
                        // format differed, re-init
                        returnTex.Destroy();
                        returnTex = new AGETexture(verified, returnTex.Flags);
                    }
                }

                var format = returnTex.Texture.format;
                if (format == UnityEngine.TextureFormat.ARGB32 ||
                    format == UnityEngine.TextureFormat.RGBA32 ||
                    format == UnityEngine.TextureFormat.BGRA32 ||                
                    format == UnityEngine.TextureFormat.DXT5)
                {
                    returnTex.Flags |= AGETexFlags.Transparent;
                }
                returnTex.Texture.name = Path.GetFileNameWithoutExtension(fn);
            }

            return returnTex;
        }

    }
}