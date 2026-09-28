
using System.IO;
using System;

public class CPVS
{
    public int Length => roomOffsets.Length - 1;

    private int[] roomOffsets = null;
    private byte[] listsData = null;

    private byte[] decompressBuffer = null;
    private bool[] resultBuffer = null;

    public bool[] Decompress(int index)
    {
        if (decompressBuffer == null)
        {
            int decompressBufferLength = (roomOffsets.Length + 4) / 4;
            decompressBuffer = new byte[decompressBufferLength];
            resultBuffer = new bool[decompressBufferLength * 4];
        }
        if (index < 0 || index >= roomOffsets.Length)
        {
            for (int i = 0; i < resultBuffer.Length; i++) resultBuffer[i] = false;
            return resultBuffer;
        }

        int dataStart = roomOffsets[index];
        int dataEnd = roomOffsets[index + 1];
        int dataPtr = dataStart;

        Array.Clear(decompressBuffer, 0, decompressBuffer.Length);

        // decompress
        int decompressBufferPtr = 0;
        while (dataPtr < dataEnd && decompressBufferPtr < decompressBuffer.Length)
        {
            byte code = listsData[dataPtr++];
            if ((code & 0x80) != 0)
            {
                int ncopy = code - 127;
                for (int i = 0; i < ncopy && decompressBufferPtr < decompressBuffer.Length; i++)
                {
                    byte value = listsData[dataPtr++];
                    decompressBuffer[decompressBufferPtr++] = value;
                }
            }
            else
            {
                int nrepeat = code;
                byte value = listsData[dataPtr++];
                for (int i = 0; i < nrepeat && decompressBufferPtr < decompressBuffer.Length; i++)
                {
                    decompressBuffer[decompressBufferPtr++] = value;
                }
            }
        }

        // now setup visibility list
        int resultPtr = 0;
        for (int i = 0; i < decompressBuffer.Length; i++)
        {
            byte room4 = decompressBuffer[i];
            for (int j = 0; j < 4; j++)
            {
                resultBuffer[resultPtr++] = (room4 >> j * 2 & 3) == 3;
            }
        }

        return resultBuffer;
    }

    public void Read(Stream stream)
    {
        var reader = new BinaryReader(stream);
        if (reader.ReadUInt32() != 0x30535650) /*PVS0*/
        {
            throw new Exception("Invalid Magic");
        }

        roomOffsets = new int[reader.ReadUInt32() - 1];
        for (int i = 0; i < roomOffsets.Length; i++)
            roomOffsets[i] = reader.ReadInt32();

        listsData = reader.ReadBytes((int)(reader.BaseStream.Length - reader.BaseStream.Position));
    }

    public void Write(Stream stream)
    {
        var writer = new BinaryWriter(stream);
        writer.Write(0x30535650); /*PVS0*/

        writer.Write(roomOffsets.Length + 1);
        for (int i = 0; i < roomOffsets.Length; i++)
            writer.Write(roomOffsets[i]);

        writer.Write(listsData);
    }

    public CPVS(int[] roomOffsets, byte[] data)
    {
        this.roomOffsets = new int[roomOffsets.Length];
        Array.Copy(roomOffsets, this.roomOffsets, roomOffsets.Length);
            
        this.listsData = new byte[data.Length];
        Array.Copy(data, this.listsData, data.Length);
    }

    public CPVS(string path)
    {
        using (var file = File.OpenRead(path))
        {
            Read(file);
        }
    }

    public CPVS(Stream stream)
    {
        Read(stream);
    }
}