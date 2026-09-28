using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

public class RaceData 
{
    public class Data 
    {
        public string Description;
        public int CarType;
        public MMTimeOfDay TimeOfDay;
        public MMWeather Weather;
        public int NumCops;
        public float TrafficDensity;
        public float PedestrianDensity;
        public int Opponents;
        public int TimeLimit;
        public int NumLaps;
        public float Difficulty;
        public MMSkillLevel Skill;
    }

    public bool HasBlitzData { get; private set; }
    public bool HasRaceData { get; private set; }
    public bool HasCircuitData { get; private set; }
    public bool HasCrashData { get; private set; }

    private Dictionary<MMGameMode, List<Data>> amateurData = new Dictionary<MMGameMode, List<Data>>();
    private Dictionary<MMGameMode, List<Data>> professionalData = new Dictionary<MMGameMode, List<Data>>();
    
    public bool TryGetData(MMGameMode set, int raceID, MMSkillLevel skill, out Data data)
    {
        var dataDict = (skill == MMSkillLevel.Amateur) ? amateurData : professionalData;
        bool haveList = dataDict.TryGetValue(set, out var dataList);
        if (haveList && raceID >= 0 && raceID < dataList.Count)
        {
            data = dataList[raceID];
            return true;
        }
        else
        {
            data = null;
            return false;
        }
    }

    public Data GetData(MMGameMode set, int raceID, MMSkillLevel skill)
    {
        if (skill == MMSkillLevel.Amateur)
        {
            return amateurData[set][raceID];
        }
        else
        {
            return professionalData[set][raceID];
        }
    }
	
    public void Clear()
    {
        amateurData.Clear();
        professionalData.Clear();
    }

    public void LoadAll(string city)
    {
        Clear();
        HasBlitzData = Load(MMGameMode.Blitz, city, "mmblitzdata");
        HasRaceData = Load(MMGameMode.Checkpoint, city, "mmracedata");
        HasCircuitData = Load(MMGameMode.Circuit, city, "mmcircuitdata");
        HasCrashData = Load(MMGameMode.CrashCourse, city, "mmcrashdata");
    }

    public bool Load(MMGameMode set, string city, string csv)
    {
        // check if this exists
        if(!AssetManager.Exists("race", city, $"{csv}.csv"))
        {
            Debug.LogWarning($"Failed to load {csv} race data for {city}.");
            return false;
        }

        // create lists if they don't exist
        if (!amateurData.ContainsKey(set))
        {
            amateurData[set] = new List<Data>();
            professionalData[set] = new List<Data>();
        }

        // load CSV
        var csvReader = AssetManager.OpenCSV("race/" + city, csv);
        csvReader.Seek(1, System.IO.SeekOrigin.Current); //skip header

        while (!csvReader.EOF())
        {
            csvReader.PrepareLine();
            string description = csvReader.GetToken(0);

            for (int i = 0; i <= 1; i++)
            {
                var data = new Data();
                int tokenStart = (i * 10) + 1;

                data.Description = description;

                int.TryParse(csvReader.GetToken(tokenStart), NumberStyles.Integer, CultureInfo.InvariantCulture, out data.CarType);

                if (int.TryParse(csvReader.GetToken(tokenStart + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int timeOfDay))
                    data.TimeOfDay = (MMTimeOfDay)timeOfDay;

                if (int.TryParse(csvReader.GetToken(tokenStart + 2), NumberStyles.Integer, CultureInfo.InvariantCulture, out int weather))
                    data.Weather = (MMWeather)weather;

                int.TryParse(csvReader.GetToken(tokenStart + 3), NumberStyles.Integer, CultureInfo.InvariantCulture, out data.Opponents);
                int.TryParse(csvReader.GetToken(tokenStart + 4), NumberStyles.Integer, CultureInfo.InvariantCulture, out data.NumCops);

                FastFloatParser.TryParse(csvReader.GetToken(tokenStart + 5), out data.TrafficDensity);
                FastFloatParser.TryParse(csvReader.GetToken(tokenStart + 6), out data.PedestrianDensity);

                int.TryParse(csvReader.GetToken(tokenStart + 7), NumberStyles.Integer, CultureInfo.InvariantCulture, out data.NumLaps);
                int.TryParse(csvReader.GetToken(tokenStart + 8), NumberStyles.Integer, CultureInfo.InvariantCulture, out data.TimeLimit);

                FastFloatParser.TryParse(csvReader.GetToken(tokenStart + 9), out data.Difficulty);

                if (i == 0)
                    amateurData[set].Add(data);
                else
                    professionalData[set].Add(data);
            }
        }

        // load success
        return true;
    }
}
