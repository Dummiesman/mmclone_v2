using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

public class PlayerData 
{
    private const int VERSION = 3;

    public float CreationTime => creationTime;
    private float creationTime;

    public MMSkillLevel SkillLevel;
    public string VehicleName = string.Empty;
    public int VehiclePaintjob;
    public MMGameMode GameMode;
    public int RaceId;
    public string City = string.Empty;
    public string Name = string.Empty;
    public string NetName = string.Empty;
    public string FileName = string.Empty;
    public string LastConnectedIP = string.Empty;

    public bool Cheating = false;

    public void SetState()
    {
        GameState.SelectedCity = City;
        GameState.SelectedVehicle = VehicleName;
        GameState.SelectedPaintjob = VehiclePaintjob;
        GameState.SkillLevel = SkillLevel;
        GameState.SelectedRace = RaceId;
        GameState.SelectedGameMode = GameMode;

        // city failsafes
        var cityInfo = CityList.GetCity(City);
        if(cityInfo == null)
        {
            GameState.SelectedCity = CityList.Cities.First().RaceDir;
        }

        // vehicle failsafes
        var vehicleInfo = VehicleList.GetVehicle(VehicleName);
        if(vehicleInfo == null)
        {
            GameState.SelectedVehicle = VehicleList.Vehicles.First().BaseName;
            GameState.SelectedPaintjob = 0;
        }
        else
        {
            GameState.SelectedPaintjob = Mathf.Min(GameState.SelectedPaintjob, vehicleInfo.Colors.Length - 1);
        }

        // default to cruise for some game modes so quick race doesn't do unexpected things
        if (GameMode == MMGameMode.CrashCourse || GameMode == MMGameMode.Cruise || GameMode == MMGameMode.CopsNRobbers)
        {
            GameState.SelectedGameMode = MMGameMode.Cruise;
            GameState.TrafficDensity = 0.5f;
            GameState.PedestrianDensity = 0.25f;
            GameState.CopDensity = 1.0f;
        }
        else if(GameMode == MMGameMode.Blitz || GameMode == MMGameMode.Checkpoint || GameMode == MMGameMode.Circuit)
        {
            // setup race data so time limits etc are correct
            var raceData = new RaceData();
            raceData.LoadAll(City);

            if (raceData.TryGetData(GameState.SelectedGameMode, RaceId, SkillLevel, out var selectedRaceData))
            {
                GameState.PedestrianDensity = selectedRaceData.PedestrianDensity;
                GameState.TrafficDensity = selectedRaceData.TrafficDensity;
                GameState.CopDensity = selectedRaceData.NumCops;
                GameState.SelectedTimeOfDay = selectedRaceData.TimeOfDay;
                GameState.SelectedWeather = selectedRaceData.Weather;
                GameState.TimeLimit = selectedRaceData.TimeLimit;
                GameState.LapCount = selectedRaceData.NumLaps;
                GameState.Difficulty = selectedRaceData.Difficulty;
                GameState.OpponentCount = selectedRaceData.Opponents;
            }
        }
        
    }

    private static string DecodeFixedString(byte[] buffer)
    {
        int end = Array.IndexOf<byte>(buffer, 0);
        if (end < 0)
        {
            end = buffer.Length;
        }
        return Encoding.ASCII.GetString(buffer, 0, end);
    }

    private uint ComputeCRC()
    {
        var crc = new Crc32();
        crc.Reset();                                
        crc.Update(creationTime);                   
        crc.Update((byte)SkillLevel);                     
        crc.UpdateFixedString(VehicleName, 80);     
        crc.Update(VehiclePaintjob);                
        crc.Update((int)GameMode);                       
        crc.Update(RaceId);                         
        crc.UpdateFixedString(City, 80);            
        crc.UpdateFixedString(Name, 40);            
        crc.UpdateFixedString(NetName, 40);         
        crc.UpdateFixedString(FileName, 40);        
        return crc.UpdateFixedString(LastConnectedIP, 40);
    }

    public bool Load(Stream stream)
    {
        using(var reader = new BinaryReader(stream, System.Text.Encoding.Default, true))
        {
            int version = reader.ReadInt32();
            if (version != 2 && version != 3)
            {
                return false;
            }

            // load data as bytes initially because we need to use all the data to compute a crc
            uint crc = reader.ReadUInt32();
            float creationTime = reader.ReadSingle(); //* time since game start when this profile was made
            byte skillLevel = reader.ReadByte();
            byte[] rawVehicle = reader.ReadBytes(80);
            int lastChosenPaintjob = reader.ReadInt32();
            int lastGameMode = reader.ReadInt32();
            int lastRaceId = reader.ReadInt32();
            byte[] rawName = reader.ReadBytes(40);
            byte[] rawNetName = reader.ReadBytes(40);
            byte[] rawFileName = reader.ReadBytes(40);
            byte[] rawCity = reader.ReadBytes(80);
            // technically V2 and V3 read this differently (sizeof(field) vs fixed 40), but both are the same
            byte[] rawLastConnectedIP = reader.ReadBytes(40);

            // sanity check sizes of read fixed arrays
            if (rawVehicle.Length != 80 || rawName.Length != 40 || rawNetName.Length != 40 ||
                rawFileName.Length != 40 || rawCity.Length != 80 || rawLastConnectedIP.Length != 40)
            {
                return false;
            }

            // calculate the raw data crc
            // this mirrors ComputeCRC, but it is purposefully duplicated logic as
            // we need to compute it on the raw byte values of the file, and that's lost 
            // when converting to C# types like string
            var check = new Crc32();
            check.Reset();
            check.Update(creationTime);
            check.Update(skillLevel);
            check.Update(rawVehicle);
            check.Update(lastChosenPaintjob);
            check.Update(lastGameMode);
            check.Update(lastRaceId);
            check.Update(rawCity);
            check.Update(rawName);
            check.Update(rawNetName);
            check.Update(rawFileName);
            uint computed = check.Update(rawLastConnectedIP);

            this.creationTime = creationTime;
            this.SkillLevel = (MMSkillLevel)skillLevel;
            this.VehicleName = DecodeFixedString(rawVehicle);
            this.VehiclePaintjob = lastChosenPaintjob;
            this.GameMode = (MMGameMode)lastGameMode;
            this.RaceId = lastRaceId;
            this.Name = DecodeFixedString(rawName);
            this.NetName = DecodeFixedString(rawNetName);
            this.FileName = DecodeFixedString(rawFileName);
            this.City = DecodeFixedString(rawCity);
            this.LastConnectedIP = DecodeFixedString(rawLastConnectedIP);

            return crc == computed;
        }
    }

    public void Save(Stream stream)
    {
        using (var writer = new BinaryWriter(stream, Encoding.Default, true))
        {
            writer.Write(VERSION);
            writer.Write(ComputeCRC());
            writer.Write(creationTime);
            writer.Write((byte)SkillLevel);
            writer.WriteFixedString(VehicleName, 80);
            writer.Write(VehiclePaintjob);
            writer.Write((int)GameMode);
            writer.Write(RaceId);
            writer.WriteFixedString(Name, 40);
            writer.WriteFixedString(NetName, 40);
            writer.WriteFixedString(FileName, 40);
            writer.WriteFixedString(City, 80);
            writer.WriteFixedString(LastConnectedIP, 40);
        }
    }

    public PlayerData()
    {
        creationTime = Time.time;
    }
}
