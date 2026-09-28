using System;
using System.IO;
using System.Linq;
using UnityEngine;

public class PlayerManager 
{
    public const int MAX_PLAYERS = 18;
    public static int PlayerCount => playerDirectory.Entries.Count;
    public static PlayerData CurrentPlayer => currentPlayerData;
    public static PlayerConfig CurrentPlayerConfig => currentPlayerConfig;

    private static PlayerDirectory playerDirectory;
    private static PlayerData currentPlayerData;
    private static PlayerConfig currentPlayerConfig;
    private static int currentPlayerIndex;

    public static Action<PlayerData, PlayerConfig> OnActiveProfileChanged; // Fired when the active player changes
    public static Action<PlayerData, PlayerConfig> OnNewPlayerCreated;
    public static Action<int> OnPlayerDeleted;

    // Private methods

    private static void CreateDefaultPlayer()
    {
        string playerName = Localization.GetString(LocString.DefaultPlayerName);
        playerDirectory.LastPlayerName = playerName;
        CreatePlayer(playerName, MMSkillLevel.Amateur);
    }

    private static string GetPlayerFilePath(int index, string extension)
    {
        var entry = playerDirectory.Entries[index];
        string fileName = $"{entry.SaveFileName}.{extension}";
        return Path.Combine(GetPlayersRootPath(), fileName);
    }

    private static string GetCityRecordPath(string cityName, int playerIndex)
    {
        string playersRoot = GetPlayersRootPath();
        string cityRoot = Path.Combine(playersRoot, cityName);
        var playerSaveName = playerDirectory.Entries[playerIndex].SaveFileName;
        return Path.Combine(cityRoot, $"{playerSaveName}.rec");
    }

    private static string GetPlayersRootPath()
    {
        string persistentRoot = (Application.isMobilePlatform) ? Application.persistentDataPath
                                                          : FileSystem.Root;
        string playersRoot = Path.Combine(persistentRoot, "players");
        return playersRoot;
    }

    private static void LoadPlayerDirectory()
    {
        playerDirectory = new PlayerDirectory();
        string playersRoot = GetPlayersRootPath();

        string directoryPath = Path.Combine(playersRoot, "players.dir");
        if (File.Exists(directoryPath))
        {
            using (var stream = File.OpenRead(directoryPath))
            {
                playerDirectory.Load(stream);
            }
            Debug.Log($"Loaded player profile directory from {directoryPath}, {playerDirectory.Entries.Count} players");
        }
    }

    private static void SavePlayerDirectory()
    {
        string playersRoot = GetPlayersRootPath();
        string directoryPath = Path.Combine(playersRoot, "players.dir");

        using (var stream = File.Create(directoryPath))
        {
            playerDirectory.Save(stream);
        }
    }

    private static PlayerData CreateDefaultPlayerData()
    {
        var data = new PlayerData();
        string defaultPlayerName = Localization.GetString(LocString.DefaultPlayerName);
        string defaultPlayerNetName = Localization.GetString(LocString.DefaultPlayerNetName);
        data.SkillLevel = MMSkillLevel.Amateur;
        data.City = CityList.Cities.FirstOrDefault()?.RaceDir ?? "sf";
        data.VehicleName = VehicleList.Vehicles.FirstOrDefault()?.BaseName ?? "vpcoop";
        data.Name = defaultPlayerName;
        data.NetName = defaultPlayerNetName;
        return data;
    }

    // Public methods
    public static void SavePlayer()
    {
        if (currentPlayerData == null || playerDirectory == null ||
            currentPlayerIndex < 0 || currentPlayerIndex >= playerDirectory.Entries.Count)
        {
            Debug.LogWarning("SavePlayer: no active player to save.");
            return;
        }

        string playersRoot = GetPlayersRootPath();
        if (!Directory.Exists(playersRoot))
        {
            Directory.CreateDirectory(playersRoot);
        }

        if (currentPlayerData.Cheating)
        {
#if UNITY_EDITOR
            Debug.LogWarning($"SavePlayer: '{currentPlayerData.Name}' is a cheating bastard.");
#endif
        }
        else
        {
            string dataPath = GetPlayerFilePath(currentPlayerIndex, "sav");
            try
            {
                using (var stream = File.Create(dataPath))
                {
                    currentPlayerData.Save(stream);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to save player data file: {dataPath}. {ex}");
            }
        }

        if (currentPlayerConfig != null)
        {
            string configPath = GetPlayerFilePath(currentPlayerIndex, "cfg");
            try
            {
                using (var stream = File.Create(configPath))
                {
                    currentPlayerConfig.Save(stream);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to save player config file: {configPath}. {ex}");
            }
        }
    }

    public static bool CreatePlayer(string name, MMSkillLevel skillLevel)
    {
        var config = new PlayerConfig();

        var data = CreateDefaultPlayerData();
        data.Name = name;
        data.SkillLevel = skillLevel;

        if(!playerDirectory.AddPlayer(name))
        {
            // may already exist
            return false;
        }

        int playerIndex = playerDirectory.FindPlayer(name);
        var entry = playerDirectory.Entries[playerIndex];

        // persist the config/data files to disk
        string playersRoot = GetPlayersRootPath();
        if (!Directory.Exists(playersRoot))
        {
            Directory.CreateDirectory(playersRoot);
        }

        string configPath = Path.Combine(playersRoot, $"{entry.SaveFileName}.cfg");
        string dataPath = Path.Combine(playersRoot, $"{entry.SaveFileName}.sav");

        using (var stream = File.Create(configPath))
        {
            config.Save(stream);
        }
        using (var stream = File.Create(dataPath))
        {
            data.Save(stream);
        }

        SavePlayerDirectory();
        OnNewPlayerCreated?.Invoke(data, config);
        return true;
    }

    public static PlayerCityRecord OpenCityRecord(string cityName)
    {
        string recordPath = GetCityRecordPath(cityName, currentPlayerIndex);
        if (File.Exists(recordPath))
        {
            var cityRecord = new PlayerCityRecord(currentPlayerData.CreationTime);
            using(var stream = File.OpenRead(recordPath))
            {
                cityRecord.Load(stream);
            }
            return cityRecord;
        }
        else
        {
            var cityInfo = CityList.GetCity(cityName);
            if (CityList.IsDefaultCity(cityName) && cityInfo != null)
            {
                // need to create it
                string playersRoot = GetPlayersRootPath();
                string cityRoot = Path.Combine(playersRoot, cityName);
                Directory.CreateDirectory(cityRoot);

                var record = new PlayerCityRecord(currentPlayerData.CreationTime);
                record.Init(cityInfo.BlitzNames.Length, cityInfo.CircuitNames.Length, cityInfo.CheckpointNames.Length, 13);

                using(var file = File.OpenWrite(recordPath))
                {
                    record.Save(file);
                }

                // and return it
                return record;
            }
            else
            {
                return null;
            }
        }
    }

    public static void SaveCityRecord(string cityName, PlayerCityRecord record)
    {
        if (record == null)
            throw new ArgumentNullException(nameof(record));

        string recordPath = GetCityRecordPath(cityName, currentPlayerIndex);

        // Make sure the folder the record lives in exists
        string directory = Path.GetDirectoryName(recordPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        using (var file = File.Create(recordPath))
        {
            record.Save(file);
        }
    }

    public static string GetAllPlayerNames()
    {
        return string.Join("|", playerDirectory.Entries.Select(x => x.Name));
    }

    public static int FindPlayer(string name)
    {
        return playerDirectory.FindPlayer(name);
    }

    public static string GetPlayerName(int index)
    {
        return playerDirectory.Entries[index].Name;
    }

    public static void SetActivePlayer(int index)
    {
        string configPath = GetPlayerFilePath(index, "cfg");
        string dataPath = GetPlayerFilePath(index, "sav");

        currentPlayerConfig = new PlayerConfig(); // fallback for when file does not exist
        if (File.Exists(configPath))
        {
            try
            {
                using (var stream = File.OpenRead(configPath))
                {
                    currentPlayerConfig.Load(stream);
                }
            }
            catch(Exception ex)
            {
                Debug.LogError($"Failed to load player config file: {configPath}. {ex.Message}");
                currentPlayerConfig = new PlayerConfig(); // fallback to default values
            }
        }
        
        currentPlayerData = CreateDefaultPlayerData(); // fallback for when file does not exist
        currentPlayerData.Name = playerDirectory.Entries[index].Name;
        if (File.Exists(dataPath))
        {
            try
            {
                using (var stream = File.OpenRead(dataPath))
                {
                    if(!currentPlayerData.Load(stream))
                    {
#if UNITY_EDITOR
                        Debug.Log("Cheating bastard");
#endif
                        currentPlayerData.Cheating = true;                        
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to load player data file: {dataPath}. {ex.Message}");
                currentPlayerData = CreateDefaultPlayerData(); // fallback to sane values
                currentPlayerData.Name = playerDirectory.Entries[index].Name;
            }
        }


        playerDirectory.LastPlayerName = playerDirectory.Entries[index].Name;
        currentPlayerIndex = index;

        SavePlayerDirectory();
        OnActiveProfileChanged?.Invoke(currentPlayerData, currentPlayerConfig);
    }

    public static void DeletePlayer(int index)
    {
        var entry = playerDirectory.Entries[index];

        string savePath = GetPlayerFilePath(index, "sav");
        string configPath = GetPlayerFilePath(index, "cfg");
 
        if (File.Exists(savePath)) File.Delete(savePath);
        if (File.Exists(configPath)) File.Delete(configPath);

        string sfCityRecordPath = GetCityRecordPath("sf", index);
        string londonCityRecordPath = GetCityRecordPath("london", index);
        if (File.Exists(sfCityRecordPath)) File.Delete(sfCityRecordPath);
        if (File.Exists(londonCityRecordPath)) File.Delete(londonCityRecordPath);

        OnPlayerDeleted?.Invoke(index);

        playerDirectory.DeletePlayer(entry.Name);
        SavePlayerDirectory();

        if(currentPlayerIndex == index)
        {
            if (currentPlayerIndex >= playerDirectory.Entries.Count)
                currentPlayerIndex--;
            SetActivePlayer(currentPlayerIndex);
        }
        else if (index < currentPlayerIndex)
        {
            currentPlayerIndex--;
        }
    }

    public static void LoadPlayers()
    {
        // create dir if nonexistant
        string playersRoot = GetPlayersRootPath();
        if (!Directory.Exists(playersRoot))
        {
            Directory.CreateDirectory(playersRoot);
        }

        // load player database
        LoadPlayerDirectory();

        // set active player
        if(playerDirectory.Entries.Count == 0)
        {
            // no players: default player
            CreateDefaultPlayer();
            SetActivePlayer(0);
        }
        else if(string.IsNullOrEmpty(playerDirectory.LastPlayerName))
        {
            // last player wasn't set, set to the first one
            SetActivePlayer(0);
        }
        else
        {
            int lastPlayerIndex = playerDirectory.FindPlayer(playerDirectory.LastPlayerName);
            if(lastPlayerIndex >= 0)
            {
                SetActivePlayer(lastPlayerIndex);
            }
            else
            {
                // couldn't find this player
                SetActivePlayer(0);
            }
        }
    }
}
