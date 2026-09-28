using System.Collections.Generic;
using System.IO;
using UnityEngine;

public class PlayerCityRecord 
{
    private const float VERSION = 2.0f;
    private const int HEADER_SIZE = 44;

    public int TotalScore
    {
        get
        {
            int score = 0;
            for (int i = 0; i < numCircuitRaces; i++) score += circuitRecords[i].Score;
            for (int i = 0; i < numCheckpointRaces; i++) score += checkpointRecords[i].Score;
            for (int i = 0; i < numBlitzRaces; i++) score += blitzRecords[i].Score;
            return score;
        }
    }

    private float playerCreationTime;

    private int numCheckpointRaces;
    private int numCircuitRaces;
    private int numBlitzRaces;
    private int numCCRaces;
    
    private int checkpointPassMask;
    private int circuitPassMask;
    private int blitzPassMask;
    private int ccPassMask;

    private readonly List<PlayerRecord> checkpointRecords = new List<PlayerRecord>();
    private readonly List<PlayerRecord> circuitRecords = new List<PlayerRecord>();
    private readonly List<PlayerRecord> blitzRecords = new List<PlayerRecord>();
    private readonly List<PlayerRecord> ccRecords = new List<PlayerRecord>();

    private uint ComputeCRC()
    {
        var crc = new Crc32();
        crc.Reset();                    
        crc.Update(playerCreationTime);
        crc.Update(numCheckpointRaces);
        crc.Update(numCircuitRaces);
        crc.Update(numBlitzRaces);
        crc.Update(numCCRaces);
        crc.Update(checkpointPassMask);
        crc.Update(circuitPassMask);
        crc.Update(blitzPassMask);
        return crc.Update(ccPassMask);
    }

    private int GetFileOffset(MMGameMode gameMode)
    {
        int offset = HEADER_SIZE;
        switch(gameMode)
        {
            case MMGameMode.Circuit:
                offset += PlayerRecord.SizeOf * numCheckpointRaces;
                break;
            case MMGameMode.Blitz:
                offset += PlayerRecord.SizeOf * (numCheckpointRaces + numCircuitRaces);
                break;
            case MMGameMode.CrashCourse:
                offset += PlayerRecord.SizeOf * (numCheckpointRaces + numCircuitRaces + numBlitzRaces);
                break;
        }
        return offset;
    }

    public bool NewRecord(PlayerRecord record, MMGameMode gameMode, int raceIndex)
    {
        if (gameMode != MMGameMode.CrashCourse && gameMode != MMGameMode.Circuit &&
            gameMode != MMGameMode.Blitz && gameMode != MMGameMode.Checkpoint)
        {
            Debug.LogError($"PlayerCityRecord.NewRecord - invalid gameMode");
            return false;
        }

        int numRaces = GetNumRaces(gameMode);
        if(raceIndex < 0 || raceIndex >= numRaces)
        {
            Debug.LogError($"PlayerCityRecord.NewRecord - raceIndex out of bounds");
            return false;
        }

        if(record.Passed)
        {
            int passMask = 1 << raceIndex;
            switch(gameMode)
            {
                case MMGameMode.CrashCourse:
                    ccPassMask |= passMask;
                    break;
                case MMGameMode.Blitz:
                    blitzPassMask |= passMask;
                    break;
                case MMGameMode.Circuit:
                    circuitPassMask |= passMask;
                    break;
                case MMGameMode.Checkpoint:
                    checkpointPassMask |= passMask;
                    break;
            }
        }

        // Update the existing record
        List<PlayerRecord> records = null;
        switch(gameMode)
        {
            case MMGameMode.CrashCourse: records = ccRecords; break;
            case MMGameMode.Blitz: records = blitzRecords; break;
            case MMGameMode.Circuit: records = circuitRecords; break;
            case MMGameMode.Checkpoint: records = checkpointRecords; break;
        }

        var existing = records[raceIndex];
        if(existing.Time == 0.0f)
        {
            // this record has not been written to yet, update it
            records[raceIndex] = new PlayerRecord(record);
        }
        else
        {
            // this record has data, update conditionally
            var modifiedRecord = new PlayerRecord(existing);

            if(record.Time < modifiedRecord.Time)
            {
                modifiedRecord.Time = record.Time;
                modifiedRecord.VehicleName = record.VehicleName;
            }

            modifiedRecord.Score = Mathf.Max(modifiedRecord.Score, record.Score);
            modifiedRecord.Passed = (record.Passed | modifiedRecord.Passed);
            records[raceIndex] = modifiedRecord;
        }
        return true;
    }

    public int GetNumRaces(MMGameMode mode)
    {
        switch(mode)
        {
            case MMGameMode.Blitz:
                return numBlitzRaces;
            case MMGameMode.Checkpoint:
                return numCheckpointRaces;
            case MMGameMode.Circuit:
                return numCircuitRaces;
            case MMGameMode.CrashCourse:
                return numCCRaces;
            default:
                return 0;
        }
    }

    public int GetPassedMask(MMGameMode mode)
    {
        switch (mode)
        {
            case MMGameMode.Blitz:
                return blitzPassMask;
            case MMGameMode.Checkpoint:
                return checkpointPassMask;
            case MMGameMode.Circuit:
                return circuitPassMask;
            case MMGameMode.CrashCourse:
                return ccPassMask;
            default:
                return 0;
        }
    }

    public bool GetPassed(MMGameMode mode, int index)
    {
        if (index < 0 || index >= GetNumRaces(mode))
        {
            return false;
        }
        return (GetPassedMask(mode) & (1 << index)) != 0;
    }

    public int GetNumPassed(MMGameMode mode)
    {
        int mask = GetPassedMask(mode);
        int numRaces = GetNumRaces(mode);
        int numPassed = 0;
        for (int i = 0; i < numRaces; i++)
        {
            if ((mask & (1 << i)) != 0)
            {
                numPassed++;
            }
        }
        return numPassed;
    }

    private bool LoadRecords(Stream stream, MMGameMode gameMode, List<PlayerRecord> records)
    {
        records.Clear();
        stream.Seek(GetFileOffset(gameMode), SeekOrigin.Begin);

        bool success = true;
        int numRaces = GetNumRaces(gameMode);
        for (int i = 0; i < numRaces; i++)
        {
            var record = new PlayerRecord();
            if (!record.Load(stream))
            {
                success = false;
                records.Add(new PlayerRecord());
            }
            else
            {
                records.Add(record);
            }
        }
        return success;
    }

    public uint ResolveCheckpointProgress()
    {
        uint progressMask = 7;
        var passedMask = GetPassedMask(MMGameMode.Checkpoint);
        if ((passedMask & 7) == 7)
            progressMask = 0x3F;
        if ((passedMask & 0x38) == 0x38)
            progressMask |= 0x1C0u;
        if ((passedMask & 0x1C0) == 0x1C0)
            progressMask |= 0xE00u;
        return progressMask;
    }

    public uint ResolveCrashProgress()
    {
        uint progressMask = 0x777;
        var passedMask = GetPassedMask(MMGameMode.CrashCourse);
        if ((passedMask & 7) == 7)
            progressMask = 0x77F;
        if ((passedMask & 0x70) == 0x70)
            progressMask |= 0x80u;
        if ((passedMask & 0x700) == 0x700)
            progressMask |= 0x800u;
        if ((passedMask & 0xFFF) == 0xFFF)
            progressMask = 0xFFFFFFFF;
        return progressMask;
    }

    public bool Load(Stream stream)
    {
        using (var reader = new BinaryReader(stream, System.Text.Encoding.Default, true))
        {
            float version = reader.ReadSingle();
            if (version != VERSION)
            {
                return false;
            }

            uint crc = reader.ReadUInt32();
            float playerTagId = reader.ReadSingle(); //* time since game start when the profile was made

            numCheckpointRaces = reader.ReadInt32();
            numCircuitRaces = reader.ReadInt32();
            numBlitzRaces = reader.ReadInt32();
            numCCRaces = reader.ReadInt32();
            checkpointPassMask = reader.ReadInt32();
            circuitPassMask = reader.ReadInt32();
            blitzPassMask = reader.ReadInt32();
            ccPassMask = reader.ReadInt32();

            bool overallSuccess = (crc == ComputeCRC() && playerCreationTime == playerTagId);

            // load records
            if (!LoadRecords(stream, MMGameMode.Checkpoint, checkpointRecords)) overallSuccess = false;
            if (!LoadRecords(stream, MMGameMode.Circuit, circuitRecords)) overallSuccess = false;
            if (!LoadRecords(stream, MMGameMode.Blitz, blitzRecords)) overallSuccess = false;
            if (!LoadRecords(stream, MMGameMode.CrashCourse, ccRecords)) overallSuccess = false;

            return overallSuccess;
        }
    }

    private void SaveRecords(Stream stream, MMGameMode gameMode, List<PlayerRecord> records)
    {
        stream.Seek(GetFileOffset(gameMode), SeekOrigin.Begin);

        int numRaces = GetNumRaces(gameMode);
        for (int i = 0; i < numRaces; i++)
        {
            if (i >= records.Count)
            {
                Debug.LogError($"PlayerCityRecord.SaveRecords - missing record {i} for {gameMode}");
                break;
            }
            records[i].Save(stream);
        }
    }

    public PlayerRecord GetRecord(MMGameMode mode, int index)
    {
        List<PlayerRecord> records = null;
        switch(mode)
        {
            case MMGameMode.CrashCourse:
                records = ccRecords; break;
            case MMGameMode.Blitz:
                records = blitzRecords; break;
            case MMGameMode.Checkpoint:
                records = checkpointRecords; break;
            case MMGameMode.Circuit:
                records = circuitRecords; break;
        }

        if(index < 0 || records == null || index > records.Count)
        {
            return new PlayerRecord();
        }
        else
        {
            return records[index];
        }
    }

    public void Save(Stream stream)
    {
        stream.Seek(0, SeekOrigin.Begin);
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.Default, true))
        {
            writer.Write(VERSION);
            writer.Write(ComputeCRC());
            writer.Write(playerCreationTime);
            writer.Write(numCheckpointRaces);
            writer.Write(numCircuitRaces);
            writer.Write(numBlitzRaces);
            writer.Write(numCCRaces);
            writer.Write(checkpointPassMask);
            writer.Write(circuitPassMask);
            writer.Write(blitzPassMask);
            writer.Write(ccPassMask);

            SaveRecords(stream, MMGameMode.Checkpoint, checkpointRecords);
            SaveRecords(stream, MMGameMode.Circuit, circuitRecords);
            SaveRecords(stream, MMGameMode.Blitz, blitzRecords);
            SaveRecords(stream, MMGameMode.CrashCourse, ccRecords);
        }
    }

    public void Init(int numBlitzRaces, int numCircuitRaces, int numCheckpointRaces, int numCrashCourse)
    {
        this.numCCRaces = numCrashCourse;
        this.numBlitzRaces = numBlitzRaces;
        this.numCircuitRaces = numCircuitRaces;
        this.numCheckpointRaces = numCheckpointRaces;

        ccPassMask = 0;
        blitzPassMask = 0;
        circuitPassMask = 0;
        checkpointPassMask = 0;

        // add entries
        for (int i = 0; i < numBlitzRaces; i++) blitzRecords.Add(new PlayerRecord());
        for (int i = 0; i < numCircuitRaces; i++) circuitRecords.Add(new PlayerRecord());
        for (int i = 0; i < numCheckpointRaces; i++) checkpointRecords.Add(new PlayerRecord());
        for (int i = 0; i < numCrashCourse; i++) ccRecords.Add(new PlayerRecord());
    }

    public PlayerCityRecord(float playerCreationTime)
    {
        this.playerCreationTime = playerCreationTime;
    }
}
