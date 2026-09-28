using System;
using System.IO;
using UnityEngine;

namespace Dummiesman.Wave
{
    public class WAVLoader
    {
        private static string LastFileName = null;

        private static float[] Decode_DVI_ADPCM(BinaryReader reader, int dataLength, WaveFormatEx format)
        {
            if (format.cbSize !=2)
            {
                throw new InvalidDataException($"DVI_ADPCM read failure: bad cbSize.");
            }
            if(format.wChannels < 1 || format.wChannels > 2)
            {
                throw new InvalidDataException($"DVI_ADPCM read failure: bad channel count");
            }

            // parse extra header data
            int samplesPerBlock = BitConverter.ToInt16(format.pExtraData, 0);

            //int expectedDataSize = (((samplesPerBlock - 1) / 2) + (4 * format.wChannels)) * format.wBlockAlign;
            //if (samplesPerBlock == 0 || samplesPerBlock != expectedDataSize)
            //{
            //    throw new InvalidDataException("Unexpected nSamplesPerBlock");
            //}

            short decodeStep(byte code, ref DVI_ADPCM_State state)
            {
                int nStep = Common.DVI_ADPCM_StepTable[state.index];
                int nDiff = nStep >> 3;

                if ((code & 0x01) > 0) nDiff += nStep >> 2;
                if ((code & 0x02) > 0) nDiff += nStep >> 1;
                if ((code & 0x04) > 0) nDiff += nStep;
                if ((code & 0x08) > 0) nDiff = -nDiff;

                int nNewSample = Math.Clamp(state.sample + nDiff, -32768, 32767);
                state.index = Math.Clamp(state.index + Common.DVI_ADPCM_StepAdjust[code], 0, 88);

                state.sample = nNewSample;
                return (short)nNewSample;
            }

            
            int compressedSamplesPerBlock = samplesPerBlock - 1; // samplesPerBlock includes the coder state sample

            int nBlocks = dataLength / format.wBlockAlign;
            int blockSizeMinusHeader = format.wBlockAlign - (format.wChannels * 4);

            int samplesPerChannel = nBlocks * samplesPerBlock;
            DVI_ADPCM_State[] channelStates = new DVI_ADPCM_State[] { new DVI_ADPCM_State(), new DVI_ADPCM_State() };
            
            float[] samples = new float[samplesPerChannel * format.wChannels];
            int[] sampleIndices = new int[] { 0, 1 };

            for(int i=0; i < nBlocks; i++)
            {
                for(int ch=0; ch < format.wChannels; ch++)
                {
                    var state = channelStates[ch];
                    state.sample = reader.ReadInt16();
                    state.index = reader.ReadByte();
                    channelStates[ch] = state;

                    reader.ReadByte(); // pad byte
                    
                    samples[sampleIndices[ch]] = state.sample / 32767.0f;
                    sampleIndices[ch] += format.wChannels;
                }

                // samples are stored in groups of 4 bytes (8 samples) per channel
                // make this into interleaved data for Unity
                byte[] blockData = reader.ReadBytes(blockSizeMinusHeader);
                int nSubBlocksPerChannel = (compressedSamplesPerBlock / 4) / 2; // divide by sample count, then by two because each 2 samples is 1 byte
                int nSubBlocks = nSubBlocksPerChannel * format.wChannels;

                for (int subBlock = 0; subBlock < nSubBlocks; subBlock++)
                {
                    int channelIndex = (format.wChannels == 1) ? 0 : subBlock % 2;
                    
                    for(int sample = 0; sample < 4; sample++)
                    {
                        byte nibbles = blockData[(subBlock * 4) + sample];
                        
                        samples[sampleIndices[channelIndex]] = decodeStep((byte)(nibbles >> 4), ref channelStates[channelIndex]) / 32767.0f;
                        sampleIndices[channelIndex] += format.wChannels;

                        samples[sampleIndices[channelIndex]] = decodeStep((byte)(nibbles & 0xF), ref channelStates[channelIndex]) / 32767.0f;
                        sampleIndices[channelIndex] += format.wChannels;
                    }
                }
            }

            return samples;
        }

        private static float[] Decode_MS_ADPCM(BinaryReader reader, int dataLength, WaveFormatEx format)
        {
            // parse extra header data
            if (format.cbSize < 4)
            {
                throw new Exception($"MS_ADPCM read failure: bad cbSize.");
            }

            short samplesPerBlock = BitConverter.ToInt16(format.pExtraData, 0);
            if(samplesPerBlock == 0 || samplesPerBlock > 2 * (format.wBlockAlign - 6))
            {
                throw new InvalidDataException("Unexpected nSamplesPerBlock");
            }

            ushort wNumCoef = BitConverter.ToUInt16(format.pExtraData, 2);
            var coefficients = new short[wNumCoef][];

            for(int i=0; i < wNumCoef; i++)
            {
                coefficients[i] = new short[] {(short)BitConverter.ToUInt16(format.pExtraData, 4 + (i*4)),
                                               (short)BitConverter.ToUInt16(format.pExtraData, 6 + (i*4)) };
            }

            // read data
            int blocks = dataLength / format.wBlockAlign;
            byte[] dataBytes = new byte[2 * blocks * format.wChannels * samplesPerBlock];
            int position = 0;

            for (int i = 0; i < blocks; i++)
            {
                unchecked
                {
                    MS_ADPCM_ChannelData[] channelData = new MS_ADPCM_ChannelData[format.wChannels];
                    for (int j = 0; j < format.wChannels; j++)
                    {
                        channelData[j].bPredictor = (int)reader.ReadByte();
                        if (channelData[j].bPredictor >= coefficients.Length)
                        {
                            throw new InvalidDataException("Invalid bPredictor");
                        }
                        else
                        {
                            channelData[j].iCoef1 = (int)coefficients[channelData[j].bPredictor][0];
                            channelData[j].iCoef2 = (int)coefficients[channelData[j].bPredictor][1];
                        }
                    }
                    for (int j = 0; j < format.wChannels; j++)
                    {
                        channelData[j].iDelta = (short)reader.ReadUInt16();
                    }
                    for (int j = 0; j < format.wChannels; j++)
                    {
                        channelData[j].iSamp1 = (short)reader.ReadUInt16();
                    }
                    for (int j = 0; j < format.wChannels; j++)
                    {
                        channelData[j].iSamp2 = (short)reader.ReadUInt16();
                    }
                    for (int j = 0; j < format.wChannels; j++)
                    {
                        dataBytes[position] = (byte)(ushort)channelData[j].iSamp2;
                        dataBytes[position + 1] = (byte)((ushort)channelData[j].iSamp2 >> 8);
                        position += 2;
                    }
                    for (int j = 0; j < format.wChannels; j++)
                    {
                        dataBytes[position] = (byte)(ushort)channelData[j].iSamp1;
                        dataBytes[position + 1] = (byte)((ushort)channelData[j].iSamp1 >> 8);
                        position += 2;
                    }
                    uint nibbleByte = 0;
                    bool nibbleFirst = true;
                    for (int j = 0; j < samplesPerBlock - 2; j++)
                    {
                        for (int k = 0; k < format.wChannels; k++)
                        {
                            int lPredSample =
                                (int)channelData[k].iSamp1 * channelData[k].iCoef1 +
                                (int)channelData[k].iSamp2 * channelData[k].iCoef2 >> 8;
                            int iErrorDeltaUnsigned;
                            if (nibbleFirst)
                            {
                                nibbleByte = (uint)reader.ReadByte();
                                iErrorDeltaUnsigned = (int)(nibbleByte >> 4);
                                nibbleFirst = false;
                            }
                            else
                            {
                                iErrorDeltaUnsigned = (int)(nibbleByte & 15);
                                nibbleFirst = true;
                            }
                            int iErrorDeltaSigned =
                                iErrorDeltaUnsigned >= 8 ? iErrorDeltaUnsigned - 16 : iErrorDeltaUnsigned;
                            int lNewSampInt =
                                lPredSample + (int)channelData[k].iDelta * iErrorDeltaSigned;
                            short lNewSamp =
                                lNewSampInt <= -32768 ? (short)-32768 :
                                lNewSampInt >= 32767 ? (short)32767 :
                                (short)lNewSampInt;
                            channelData[k].iDelta = (short)(
                                (int)channelData[k].iDelta *
                                (int)Common.MS_ADPCM_AdaptionTable[iErrorDeltaUnsigned] >> 8
                            );
                            if (channelData[k].iDelta < 16)
                            {
                                channelData[k].iDelta = 16;
                            }
                            channelData[k].iSamp2 = channelData[k].iSamp1;
                            channelData[k].iSamp1 = lNewSamp;
                            dataBytes[position] = (byte)(ushort)lNewSamp;
                            dataBytes[position + 1] = (byte)((ushort)lNewSamp >> 8);
                            position += 2;
                        }
                    }
                }
                
                reader.BaseStream.Position += format.wBlockAlign - (format.wChannels * (samplesPerBlock - 2) + 1 >> 1) - 7 * format.wChannels;
            }

            // now convert this into float data
            int sampleCountWChannels = dataBytes.Length / 2;
            var samples = new float[sampleCountWChannels];
            for (int i = 0; i < sampleCountWChannels; i++)
            {
                int sampleIndex = i * 2;
                short sample = (short)(dataBytes[sampleIndex] | (dataBytes[sampleIndex + 1] << 8));
                samples[i] = sample / 32767f;
            }

            return samples;
        }

        private static float[] Decode_PCM(BinaryReader reader, int dataLength, WaveFormatEx format)
        {
            int sampleCount = dataLength / format.wChannels / (format.wBitsPerSample / 8);
            int sampleCountWChannels = sampleCount * format.wChannels;
            var samples = new float[sampleCountWChannels];

            switch (format.wBitsPerSample)
            {
                case 8:
                    {
                        byte[] byteSamples = reader.ReadBytes(sampleCountWChannels);
                        for (int i = 0; i < byteSamples.Length; i++)
                        {
                            float sample = Wave.Common.ByteToFloatTable[byteSamples[i]];
                            samples[i] = sample;
                        }
                    }
                    break;
                case 16:
                    {
                        byte[] sampleData = reader.ReadBytes((sampleCountWChannels) * 2);
                        for (int i = 0; i < sampleCountWChannels; i++)
                        {
                            int sampleIndex = i * 2;
                            short sample = (short)(sampleData[sampleIndex] | (sampleData[sampleIndex + 1] << 8));
                            samples[i] = sample / 32767f;
                        }
                    }
                    break;
                case 24:
                    {
                        byte[] sampleData = reader.ReadBytes((sampleCountWChannels) * 3);
                        for (int i = 0; i < sampleCountWChannels; i++)
                        {
                            int sampleIndex = i * 3;
                            float sample = (sampleData[sampleIndex] << 8 | sampleData[sampleIndex + 1] << 16 | sampleData[sampleIndex + 2] << 24) / 2147483648f;
                            samples[i] = sample;
                        }
                    }
                    break;
                case 32:
                    {
                        if (format.wFormatTag == WaveFormatTag.IEEE_FLOAT)
                        {
                            for (int i = 0; i < sampleCountWChannels; i++)
                                samples[i] = reader.ReadSingle();
                        }
                        else
                        {
                            byte[] sampleData = reader.ReadBytes((sampleCountWChannels) * 4);
                            for (int i = 0; i < sampleCountWChannels; i++)
                            {
                                int sampleIndex = i * 4;
                                float sample = (sampleData[sampleIndex] | sampleData[sampleIndex + 1] << 8 | sampleData[sampleIndex + 2] << 16 | sampleData[sampleIndex + 3] << 24) / 2147483648f;
                                samples[i] = sample;
                            }
                        }
                    }
                    break;
                default:
                    Debug.LogError($"Unsupported bits per sample {format.wBitsPerSample}.");
                    return null;
            }
            return samples;
        }

        private static float[] ReadAudioData(BinaryReader reader, int dataLength, WaveFormatEx format)
        {
            if(format.wFormatTag == WaveFormatTag.DVI_ADPCM)
            {
                return Decode_DVI_ADPCM(reader, dataLength, format);
            }
            else if(format.wFormatTag == WaveFormatTag.ADPCM)
            {
                return Decode_MS_ADPCM(reader, dataLength, format);
            }
            else
            {
                return Decode_PCM(reader, dataLength, format);
            }
        }

        public static AudioClip Load(Stream stream)
        {
            var wavLoader = new RIFF.RiffReader(stream);
            var rootChunk = wavLoader.NextChunk();
            if (!rootChunk.IsRIFF || !rootChunk.FormType.Equals("wave", StringComparison.OrdinalIgnoreCase))
            {
                throw new Exception($"Not a wave file!");
            }

            // read in wav data
            WaveFormatEx format = default;
            float[] samples = null;
            foreach (var subchunk in rootChunk.GetSubchunks())
            {
                if (subchunk.FourCCOrForm.Equals("fmt ", StringComparison.OrdinalIgnoreCase))
                {
                    format = new WaveFormatEx(subchunk.GetData().CreateReader());
                    if(format.wFormatTag != WaveFormatTag.PCM && format.wFormatTag != WaveFormatTag.IEEE_FLOAT 
                        && format.wFormatTag != WaveFormatTag.DVI_ADPCM && format.wFormatTag != WaveFormatTag.ADPCM)
                    {
                        throw new Exception($"Unsupported wave wFormatTag {format.wFormatTag}.");
                    }
                }
                else if (subchunk.FourCCOrForm.Equals("data", StringComparison.OrdinalIgnoreCase))
                {
                    // read in samples
                    var rawSampleData = subchunk.GetData();
                    var sampleDataReader = rawSampleData.CreateReader();
                    samples = ReadAudioData(sampleDataReader, rawSampleData.Length, format);
                }
            }

            // return read wave data as audioclip
            if (samples == null)
                throw new Exception("File had no data chunk / failed to load wave data!");

            string name = string.IsNullOrWhiteSpace(LastFileName)  ? string.Empty : Path.GetFileNameWithoutExtension(LastFileName);
            AudioClip clip = AudioClip.Create(name, samples.Length / format.wChannels, format.wChannels, (int)format.dwSamplesPerSec, false);
            clip.SetData(samples, 0);
            return clip;
        }

        public static AudioClip Load(string path)
        {
            LastFileName = path;
            var returnVal = Load(File.OpenRead(path));
            LastFileName = null;
            return returnVal;
        }
    }
}

