using System.Collections.Generic;
using System.Globalization;
public struct RewardData
{
    public bool IsHalfUnlock => RaceNumber == 254;
    public bool IsFullUnlock => RaceNumber == 255;
    public bool IsVehicleUnlock => VariantNumber == 0;
    public bool IsTextureUnlock => !IsVehicleUnlock;

    public string VehicleBasename;
    public MMGameMode GameMode;
    public int RaceNumber;
    public int VariantNumber;
    public string RewardMessage;
}

public class RewardsList
{
    public IReadOnlyList<RewardData> Rewards => rewardList;
    private List<RewardData> rewardList = new List<RewardData>();

    public bool CheckReward(MMGameMode gameMode, PlayerCityRecord cityRecord, out RewardData outReward)
    {
        outReward = default;
        foreach (var reward in rewardList)
        {
            if (reward.GameMode != gameMode)
                continue;

            var vehicleInfo = VehicleList.GetVehicle(reward.VehicleBasename);
            if (vehicleInfo == null)
                continue; // original just ran with a garbage pointer here

            bool requirementMet;
            if (reward.IsHalfUnlock)
            {
                requirementMet = cityRecord.GetNumPassed(reward.GameMode)
                              == cityRecord.GetNumRaces(reward.GameMode) / 2;
            }
            else if (reward.IsFullUnlock)
            {
                requirementMet = cityRecord.GetNumPassed(reward.GameMode)
                              == cityRecord.GetNumRaces(reward.GameMode);
            }
            else
            {
                requirementMet = (cityRecord.GetPassedMask(reward.GameMode)
                                  & (1 << reward.RaceNumber)) != 0;
            }

            if (!requirementMet)
                continue;

            // already handed out?
            bool isLocked = reward.IsTextureUnlock
                ? (vehicleInfo.RewardFlags & (1 << reward.VariantNumber)) != 0
                : vehicleInfo.IsLocked;

            if (!isLocked)
                continue;

            // consume the unlock
            if (reward.IsTextureUnlock)
                vehicleInfo.RewardFlags &= ~(1 << reward.VariantNumber);
            else
                vehicleInfo.IsLocked = false;

            outReward = reward;
            return true;
        }

        return false;
    }

    public bool Init(string city)
    {
        var basename = city;
        if (!AssetManager.Exists("race/" + basename, basename + "_rewards.csv"))
        {
            return false;
        }

        var csvReader = AssetManager.OpenCSV("race/" + basename, basename + "_rewards.csv");
        csvReader.Seek(1, System.IO.SeekOrigin.Current); //seek past header

        while (!csvReader.EOF())
        {
            var rewardData = new RewardData();
            csvReader.PrepareLine();

            // comments
            if (csvReader.GetToken(0).StartsWith("#", System.StringComparison.Ordinal))
            {
                continue;
            }

            // parse raceType
            string raceType = csvReader.GetToken(0).ToLowerInvariant();
            if (raceType == "blitz")
            {
                rewardData.GameMode = MMGameMode.Blitz;
            }
            else if (raceType == "crash")
            {
                rewardData.GameMode = MMGameMode.CrashCourse;
            }
            else if (raceType == "circuit")
            {
                rewardData.GameMode = MMGameMode.Circuit;
            }
            else if (raceType == "race")
            {
                rewardData.GameMode = MMGameMode.Checkpoint;
            }

            // parse raceNum 
            string raceNum = csvReader.GetToken(1).ToLowerInvariant();
            if (raceNum == "half")
            {
                rewardData.RaceNumber = 254;
            }
            else if (raceNum == "all")
            {
                rewardData.RaceNumber = 255;
            }
            else
            {
                int.TryParse(raceNum, NumberStyles.Integer, CultureInfo.InvariantCulture, out rewardData.RaceNumber);
            }

            //parse the rest
            rewardData.VehicleBasename = csvReader.GetToken(2);

            string variantNum = csvReader.GetToken(3); // beta compatibility, beta1 has nothing for no variant
            rewardData.VariantNumber = string.IsNullOrEmpty(variantNum) ? 0 : int.Parse(variantNum, CultureInfo.InvariantCulture);

            rewardData.RewardMessage = csvReader.GetToken(4);

            // add to list
            rewardList.Add(rewardData);
        }
        return true;
    }
}
