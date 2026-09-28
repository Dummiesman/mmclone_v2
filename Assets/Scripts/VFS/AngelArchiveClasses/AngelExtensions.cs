using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Angel.Archive
{
    public static class AngelExtensions
    {
        [ThreadStatic] private static char[] _buffer;

        public static string ReadASCII(this Stream stream)
        {
            var buf = _buffer ??= new char[256];
            int len = 0;

            int b;
            while ((b = stream.ReadByte()) > 0)
            {
                if (len == buf.Length)
                {
                    var grown = new char[buf.Length * 2];
                    Array.Copy(buf, grown, len);
                    _buffer = buf = grown;
                }
                buf[len++] = (char)b;
            }

            return len == 0 ? string.Empty : new string(buf, 0, len);
        }

        private static string DaveEncoding = "\0 #$()-./?0123456789_abcdefghijklmnopqrstuvwxyz~";

        private static string ReadDaveStringImpl(this Stream stream, int bit_index)
        {
            var builder = new StringBuilder();

            int bits = 0;

            var start_pos = stream.Position;

            while (true)
            {
                var byte_index = bit_index / 8;

                stream.Seek(start_pos + byte_index, SeekOrigin.Begin);

                switch (bit_index % 8)
                {
                    case 0: // Next: 6
                        {
                            bits = stream.ReadByte() & 0x3F;
                        }
                        break;

                    case 2: // Next: 0
                        {
                            bits = stream.ReadByte() >> 2;
                        }
                        break;

                    case 4: // Next: 2
                        {
                            bits = (stream.ReadByte() >> 4) | ((stream.ReadByte() & 0x3) << 4);
                        }
                        break;

                    case 6: // Next: 4
                        {
                            bits = (stream.ReadByte() >> 6) | ((stream.ReadByte() & 0xF) << 2);
                        }
                        break;
                }

                bit_index += 6;

                if (bits != 0)
                {
                    builder.Append(DaveEncoding[bits]);
                }
                else
                {
                    break;
                }
            };

            return builder.ToString();
        }

        public static string ReadDaveString(this Stream stream, string prev_name)
        {
            var start_pos = stream.Position;

            var first = stream.ReadByte();

            if ((first & 0x3F) < 0x38)
            {
                stream.Seek(start_pos, SeekOrigin.Begin);

                return ReadDaveStringImpl(stream, 0);
            }

            if (prev_name == null)
            {
                throw new Exception("Expected Previous Entry");
            }

            var second = stream.ReadByte();

            /*
             * index[0:3] = first[0:3]
             * index[3:5] = first[6:8]
             * index[5:8] = second[0:3]
             */

            var index = (first & 0x7) | ((first & 0xC0) >> 3) | ((second & 0x7) << 5);

            stream.Seek(start_pos, SeekOrigin.Begin);

            var replacement = ReadDaveStringImpl(stream, 12);

            return prev_name.Substring(0, index) + replacement;
        }

        public static string ReadASCII(this Stream stream, int size)
        {
            var buffer = new byte[size];

            stream.Read(buffer, 0, buffer.Length);

            return Encoding.ASCII.GetString(buffer, 0, buffer.TakeWhile(x => x > 0).Count());
        }
    }
}