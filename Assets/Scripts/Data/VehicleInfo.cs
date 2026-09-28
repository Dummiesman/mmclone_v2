using System;
using System.Globalization;
using System.IO;
using System.Text;

[Flags]
public enum VehicleInfoFlags
{
    Police = 8,
    LargeVehicle = 16,
    RightHandDrive = 64
}

public class VehicleInfo
{
    public string BaseName;
    public string Description = "Default";
    public string[] Colors = new string[] { "Default" };
    public VehicleInfoFlags Flags = 0;
    public int Order = -1;
    public float ScoringBias = 1.0f;
    public int UnlockScore = 0;
    public int UnlockFlags = 0;
    public int Horsepower = 100;
    public int TopSpeed = 100;
    public int Durability = 100;
    public float Mass = 100;
    public float UIDist = 6.0f;
    public float ForceFeedbackModifier = 1.0f;
    public float RoadForceModifier = 1.0f;

    public bool IsLocked = false;
    public int RewardFlags = 0;

    public void Load(Stream stream)
    {
        using (var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 1024, leaveOpen: true))
        {
            string ln = null;
            int i;
            float f;
            while ((ln = reader.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(ln))
                    continue;
                int eqIndex = ln.IndexOf('=');
                if (eqIndex < 0) continue;
                string key = ln.Substring(0, eqIndex).ToLowerInvariant();
                string value = ln.Substring(eqIndex + 1, ln.Length - eqIndex - 1).Trim();
                switch (key)
                {
                    case "basename":
                        BaseName = value;
                        break;
                    case "description":
                        Description = value;
                        break;
                    case "colors":
                        Colors = value.Split('|');
                        break;
                    case "flags":
                        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out i)) Flags = (VehicleInfoFlags)i;
                        break;
                    case "order":
                        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out i)) Order = i;
                        break;
                    case "scoringbias":
                        if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out f)) ScoringBias = f;
                        break;
                    case "unlockscore":
                        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out i)) UnlockScore = i;
                        break;
                    case "unlockflags":
                        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out i)) UnlockFlags = i;
                        break;
                    case "horsepower":
                        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out i)) Horsepower = i;
                        break;
                    case "top speed":
                        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out i)) TopSpeed = i;
                        break;
                    case "durability":
                        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out i)) Durability = i;
                        break;
                    case "mass":
                        if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out f)) Mass = f;
                        break;
                    case "uidist":
                        if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out f)) UIDist = f;
                        break;
                    case "forcefeedbackmodifier":
                        if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out f)) ForceFeedbackModifier = f;
                        break;
                    case "roadforcemodifier":
                        if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out f)) RoadForceModifier = f;
                        break;
                }
            }
        }
    }
}