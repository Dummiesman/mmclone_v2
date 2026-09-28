using System.Collections.Generic;
using System.IO;

public struct PlayerDirectoryEntry
{
    public string Name;
    public string SaveFileName;
}

public class PlayerDirectory
{
    private const int VERSION = 6;

    public readonly List<PlayerDirectoryEntry> Entries = new List<PlayerDirectoryEntry>();
    public string LastPlayerName = string.Empty;

    public string MakeFileName()
    {
        for (int suffix = 0; ; suffix++)
        {
            string candidate = "player" + suffix;
            if (Entries.FindIndex(x => x.SaveFileName == candidate) < 0)
            {
                return candidate;
            }
        }
    }

    public void DeletePlayer(string name)
    {
        int index = FindPlayer(name);
        if(index >= 0)
        {
            Entries.RemoveAt(index);
        }
    }

    public int FindPlayer(string name)
    {
        return Entries.FindIndex(x => x.Name == name);
    }

    public void CreatePlayer(string name, string saveName)
    {
        Entries.Add(new PlayerDirectoryEntry()
        {
            Name = name,
            SaveFileName = saveName
        });
    }

    public bool AddPlayer(string name)
    {
        if (Entries.Count == 0)
        {
            Entries.Add(new PlayerDirectoryEntry()
            {
                Name = name,
                SaveFileName = MakeFileName(),
            });
            return true;
        }

        if (FindPlayer(name) != -1)
        {
            return false;
        }

        Entries.Add(new PlayerDirectoryEntry()
        {
            Name = name,
            SaveFileName = MakeFileName(),
        });
        return true;
    }

    public bool Load(Stream stream)
    {
        using (var reader = new BinaryReader(stream, System.Text.Encoding.Default, true))
        {
            Entries.Clear();

            int version = reader.ReadInt32();
            if (version != VERSION)
            {
                return false;
            }

            int playerCount = reader.ReadInt32();
            for(int i=0; i < playerCount; i++)
            {
                string name = reader.ReadFixedString(40);
                string saveName = reader.ReadFixedString(40);
                CreatePlayer(name, saveName);
            }
            LastPlayerName = reader.ReadFixedString(80);

            return true;
        }
    }

    public void Save(Stream stream)
    {
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.Default, true))
        {
            writer.Write(VERSION);

            writer.Write(Entries.Count);
            for (int i = 0; i < Entries.Count; i++)
            {
                writer.WriteFixedString(Entries[i].Name, 40);
                writer.WriteFixedString(Entries[i].SaveFileName, 40);
            }

            writer.WriteFixedString(LastPlayerName, 80);
        }
    }
}
