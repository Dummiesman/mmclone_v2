using System;
using System.IO;
using UnityEngine;

namespace Dummiesman.Wave
{
    public class WAVStream : IDisposable
    {
        public AudioClip Clip { get; private set; } = null;

        /// <summary>
        /// The number of non disposed streams.
        /// </summary>
        public static int ActiveStreams { get; private set; }

        private long sampleDataPosition = -1;
        private int sampleCount = 0;          // total samples, all channels interleaved
        private WaveFormatEx format;
        private BinaryReader reader;
        private int currentSample = 0;        // index into interleaved samples
        private bool disposed = false;

        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;

            if (reader != null)
            {
                reader.Dispose();
                reader = null;
                ActiveStreams--;
            }
            if (Clip != null)
            {
                UnityEngine.Object.Destroy(Clip);
                Clip = null;
            }
        }

        private void PCMReaderCallback(float[] samples)
        {
            int samplesToRead = samples.Length;
            int remaining = sampleCount - currentSample;
            if (remaining <= 0)
            {
                Array.Clear(samples, 0, samples.Length);
                return;
            }
            if (samplesToRead > remaining)
                samplesToRead = remaining;

            switch (format.wBitsPerSample)
            {
                case 8:
                    {
                        byte[] sampleData = reader.ReadBytes(samplesToRead);
                        samplesToRead = sampleData.Length;
                        for (int i = 0; i < samplesToRead; i++)
                            samples[i] = Common.ByteToFloatTable[sampleData[i]];
                    }
                    break;

                case 16:
                    {
                        byte[] sampleData = reader.ReadBytes(samplesToRead * 2);
                        samplesToRead = sampleData.Length / 2;
                        for (int i = 0; i < samplesToRead; i++)
                        {
                            int b = i * 2;
                            short sample = (short)(sampleData[b] | (sampleData[b + 1] << 8));
                            samples[i] = sample / 32768f;
                        }
                    }
                    break;

                case 24:
                    {
                        byte[] sampleData = reader.ReadBytes(samplesToRead * 3);
                        samplesToRead = sampleData.Length / 3;
                        for (int i = 0; i < samplesToRead; i++)
                        {
                            int b = i * 3;
                            // little endian: LSB, mid, MSB -> place MSB in the top byte
                            int sample = (sampleData[b] << 8) | (sampleData[b + 1] << 16) | (sampleData[b + 2] << 24);
                            samples[i] = sample / 2147483648f;
                        }
                    }
                    break;

                case 32:
                    {
                        if (format.wFormatTag == WaveFormatTag.IEEE_FLOAT)
                        {
                            byte[] sampleData = reader.ReadBytes(samplesToRead * 4);
                            samplesToRead = sampleData.Length / 4;
                            for (int i = 0; i < samplesToRead; i++)
                                samples[i] = BitConverter.ToSingle(sampleData, i * 4);
                        }
                        else
                        {
                            byte[] sampleData = reader.ReadBytes(samplesToRead * 4);
                            samplesToRead = sampleData.Length / 4;
                            for (int i = 0; i < samplesToRead; i++)
                            {
                                int b = i * 4;
                                int sample = sampleData[b] | (sampleData[b + 1] << 8) | (sampleData[b + 2] << 16) | (sampleData[b + 3] << 24);
                                samples[i] = sample / 2147483648f;
                            }
                        }
                    }
                    break;

                default:
                    Debug.LogError($"Unsupported bits per sample {format.wBitsPerSample}.");
                    Array.Clear(samples, 0, samples.Length);
                    return;
            }

            currentSample += samplesToRead;

            // zero fill the tail so the end of the clip is silence, not stale data
            if (samplesToRead < samples.Length)
                Array.Clear(samples, samplesToRead, samples.Length - samplesToRead);
        }

        private void PCMSetPositionCallback(int position)
        {
            // Unity gives a per-channel frame index
            int frameCount = sampleCount / format.wChannels;
            position = Mathf.Clamp(position, 0, frameCount);

            int bytesPerFrame = (format.wBitsPerSample / 8) * format.wChannels;
            currentSample = position * format.wChannels;
            reader.BaseStream.Seek(sampleDataPosition + ((long)bytesPerFrame * position), SeekOrigin.Begin);
        }

        private void Load(Stream stream, string name)
        {
            var wavLoader = new RIFF.RiffReader(stream);
            var rootChunk = wavLoader.NextChunk();
            if (!rootChunk.IsRIFF || rootChunk.FormType.ToLowerInvariant() != "wave")
            {
                throw new Exception("Not a wave file!");
            }

            bool haveFormat = false;

            foreach (var subchunk in rootChunk.GetSubchunks())
            {
                if (subchunk.FourCCOrForm.Equals("fmt ", StringComparison.InvariantCultureIgnoreCase))
                {
                    format = new WaveFormatEx(subchunk.GetData().CreateReader());
                    if (format.wFormatTag != WaveFormatTag.PCM && format.wFormatTag != WaveFormatTag.IEEE_FLOAT)
                    {
                        throw new Exception($"Unsupported wave wFormatTag {format.wFormatTag}.");
                    }
                    if (format.wChannels <= 0 || format.wBitsPerSample <= 0 || (format.wBitsPerSample % 8) != 0)
                    {
                        throw new Exception("Invalid wave format header.");
                    }
                    haveFormat = true;
                }
                else if (subchunk.FourCCOrForm.Equals("data", StringComparison.InvariantCultureIgnoreCase))
                {
                    if (!haveFormat)
                        throw new Exception("Wave file has a data chunk before its fmt chunk.");

                    sampleDataPosition = wavLoader.Position;
                    sampleCount = subchunk.Length / (format.wBitsPerSample / 8);

                    // must be a whole number of frames
                    sampleCount -= sampleCount % format.wChannels;
                }
            }

            if (!haveFormat || sampleCount <= 0 || sampleDataPosition < 0)
                throw new Exception("Cannot stream this wave file. Failed to find valid samples.");

            reader = new BinaryReader(stream);
            reader.BaseStream.Seek(sampleDataPosition, SeekOrigin.Begin);
            currentSample = 0;

            Clip = AudioClip.Create(
                string.IsNullOrEmpty(name) ? "wav" : name,
                sampleCount / format.wChannels,
                format.wChannels,
                (int)format.dwSamplesPerSec,
                true,
                PCMReaderCallback,
                PCMSetPositionCallback);

            ActiveStreams++;
        }

        public WAVStream(Stream stream) : this(stream, null) { }

        public WAVStream(Stream stream, string name)
        {
            Load(stream, name);
        }

        public WAVStream(string path)
        {
            Stream stream = File.OpenRead(path);
            try
            {
                Load(stream, Path.GetFileNameWithoutExtension(path));
            }
            catch
            {
                stream.Dispose();
                throw;
            }
        }
    }
}