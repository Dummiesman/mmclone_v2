using System.Globalization;
using System.IO;
using System.Text;

public class CityInfo
{
    public string LocalizedName;
    public string MapName;
    public string RaceDir;
    public string[] BlitzNames;
    public string[] CircuitNames;
    public string[] CheckpointNames;
    public int MustPlace;
    public int UnlockGroup;

    public override string ToString()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"LocalizedName={LocalizedName}");
        sb.AppendLine($"MapName={MapName}");
        sb.AppendLine($"RaceDir={RaceDir}");

        sb.AppendLine($"BlitzCount={(BlitzNames == null ? 0 : BlitzNames.Length)}");
        sb.AppendLine($"CircuitCount={(CircuitNames == null ? 0 : CircuitNames.Length)}");
        sb.AppendLine($"CheckpointCount={(CheckpointNames == null ? 0 : CheckpointNames.Length)}");

        sb.Append("BlitzNames=");
        if (BlitzNames != null && BlitzNames.Length > 0)
        {
            sb.Append(string.Join("|", BlitzNames));
        }
        sb.AppendLine();

        sb.Append("CircuitNames=");
        if (CircuitNames != null && CircuitNames.Length > 0)
        {
            sb.Append(string.Join("|", CircuitNames));
        }
        sb.AppendLine();

        sb.Append("CheckpointNames=");
        if (CheckpointNames != null && CheckpointNames.Length > 0)
        {
            sb.Append(string.Join("|", CheckpointNames));
        }
        sb.AppendLine();

        sb.AppendLine($"MustPlace={MustPlace}");
        sb.AppendLine($"UnlockGroup={UnlockGroup}");

        return sb.ToString();
    }

    public string[] GetCrashCourseNames()
    {
        if (MapName == "sf" || MapName == "london")
        {
            int startIndex = (MapName.Length == 2) ? 545 : 532;
            int endIndex = (MapName.Length == 2) ? 557 : 544;
            return Localization.GetRange(startIndex, endIndex);
        }
        return new string[] { };
    }

    public string[] GetRaceNames(MMGameMode gameMode)
    {
        string[] sourceArray = null;
        switch (gameMode)
        {
            case MMGameMode.CrashCourse:
                sourceArray = GetCrashCourseNames();
                break;
            case MMGameMode.Blitz:
                sourceArray = BlitzNames;
                break;
            case MMGameMode.Checkpoint:
                sourceArray = CheckpointNames;
                break;
            case MMGameMode.Circuit:
                sourceArray = CircuitNames;
                break;
        }
        return sourceArray;
    }

    public string GetRaceName(MMGameMode gameMode, int raceIndex)
    {
        string[] sourceArray = GetRaceNames(gameMode);

        // out of bounds
        if (sourceArray == null || raceIndex < 0 || raceIndex >= sourceArray.Length)
        {
            return "Invalid Race";
        }
        return sourceArray[raceIndex];
    }


    public void Load(Stream stream)
    {
        int blitzCount = -1;
        int circuitCount = -1;
        int checkpointCount = -1;

        using (var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 1024, leaveOpen: true))
        {
            string ln = null;
            while ((ln = reader.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(ln))
                    continue;

                int eqIndex = ln.IndexOf('=');
                if (eqIndex <= 0)
                    continue;

                string key = ln.Substring(0, eqIndex).ToLowerInvariant();
                string value = ln.Substring(eqIndex + 1, ln.Length - eqIndex - 1).Trim();

                switch (key)
                {
                    case "localizedname":
                        LocalizedName = value;
                        break;
                    case "mapname":
                        MapName = value;
                        break;
                    case "racedir":
                        RaceDir = value;
                        break;
                    case "blitzcount":
                        blitzCount = int.Parse(value, CultureInfo.InvariantCulture);
                        break;
                    case "circuitcount":
                        circuitCount = int.Parse(value, CultureInfo.InvariantCulture);
                        break;
                    case "checkpointcount":
                        checkpointCount = int.Parse(value, CultureInfo.InvariantCulture);
                        break;
                    case "blitznames":
                        BlitzNames = value.Split('|');
                        break;
                    case "circuitnames":
                        CircuitNames = value.Split('|');
                        break;
                    case "checkpointnames":
                        CheckpointNames = value.Split('|');
                        break;
                    case "mustplace":
                        MustPlace = int.Parse(value, CultureInfo.InvariantCulture);
                        break;
                    case "unlockgroup":
                        UnlockGroup = int.Parse(value, CultureInfo.InvariantCulture);
                        break;
                }
            }
        }

        // count of zero clears these arrays unconditionally
        if (blitzCount == 0)
            BlitzNames = new string[] { };
        if (circuitCount == 0)
            CircuitNames = new string[] { };
        if (checkpointCount == 0)
            CheckpointNames = new string[] { };
    }
}