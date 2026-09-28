using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;

namespace MM2.AI 
{
    public class AIMap 
    {
        public float SpeedLimit;
        public bool LeftSidedTraffic;
        public Dictionary<string, float> TrafficTypesAndDensity = new Dictionary<string, float>();
        public List<string> GoodWeatherPedModels = new List<string>();
        public List<string> BadWeatherPedModels = new List<string>();
        public string SingleTrafficLightModel = "sp_traflitsingle_f";
        public string DualTrafficLightModel = "sp_traflitdual_f";
        public int PedestrianPoolSize = 100;

        public List<OpponentData> Opponents = new List<OpponentData>();
        public List<PoliceData> Police = new List<PoliceData>();

        public void LoadRaceData(string locale, string name, MMSkillLevel skillLevel)
        {
            //find the aimap path
            string levelSuffix = (skillLevel == MMSkillLevel.Professional) ? "_p" : string.Empty;
            string aimapPath = null;
            if (AssetManager.Exists("race", locale, $"{name}.aimap{levelSuffix}"))
            {
                aimapPath = AssetManager.CombinePath("race", locale, $"{name}.aimap{levelSuffix}");
            }
            else if (AssetManager.Exists("city", $"{name}.aimap{levelSuffix}"))
            {
                aimapPath = AssetManager.CombinePath("city", $"{name}.aimap{levelSuffix}");
            }
            if (aimapPath == null)
            {
                Debug.LogError($"AIMAP {name} wasn't found in the race, or city folder.");
                return;
            }

            //get the lines
            string[] aimapLines = AssetManager.ReadAllLines(aimapPath)
                .Where(line => !line.StartsWith("#"))
                .ToArray();
            var aimapParser = new IniLikeParser(aimapLines);

            //read speed limit
            if (aimapParser.SkipToSection("Speed Limit"))
            {
                aimapParser.Seek(1, SeekOrigin.Current);
                SpeedLimit = FastFloatParser.Parse(aimapParser.GetLine().Clean());
            }

            //read exceptions
            if (aimapParser.SkipToSection("Exceptions"))
            {

            }

            //read police
            if (aimapParser.SkipToSection("Police"))
            {
                aimapParser.Seek(1, SeekOrigin.Current);
                int copCount = int.Parse(aimapParser.GetLine().Clean(), CultureInfo.InvariantCulture);

                for (int i = 0; i < copCount; i++)
                {
                    aimapParser.Seek(1, SeekOrigin.Current);
                    Police.Add(PoliceData.Parse(aimapParser.GetLine()));
                }
            }

            //read opps
            if (aimapParser.SkipToSection("Opponent"))
            {
                aimapParser.Seek(1, SeekOrigin.Current);
                int oppCount = int.Parse(aimapParser.GetLine().Clean(), CultureInfo.InvariantCulture);

                for (int i = 0; i < oppCount; i++)
                {
                    aimapParser.Seek(1, SeekOrigin.Current);
                    Opponents.Add(OpponentData.Parse(aimapParser.GetLine()));
                }
            }
        }

        public void Load(string locale, string name, MMSkillLevel skillLevel)
        {
            //find the aimap path
            string levelSuffix = (skillLevel == MMSkillLevel.Professional) ? "_p" : string.Empty;
            string aimapPath = null;
            if (AssetManager.Exists("race", locale, $"{name}.aimap{levelSuffix}"))
            {
                aimapPath = AssetManager.CombinePath("race", locale, $"{name}.aimap{levelSuffix}");
            }
            else if (AssetManager.Exists("city", $"{name}.aimap{levelSuffix}"))
            {
                aimapPath = AssetManager.CombinePath("city", $"{name}.aimap{levelSuffix}");
            }
            else if (AssetManager.Exists("city", $"{name}.aimap"))
            {
                aimapPath = AssetManager.CombinePath("city", $"{name}.aimap");
            }

            //haven't found it :(
            if (aimapPath == null)
            {
                Debug.LogError($"AIMAP {name} wasn't found in the race, or city folder.");
                return;
            }

            //get the lines
            string[] aimapLines = AssetManager.ReadAllLines(aimapPath)
                .Where(line => !line.StartsWith("#"))
                .ToArray();
            var aimapParser = new IniLikeParser(aimapLines);

            //read speed limit
            if (aimapParser.SkipToSection("Speed Limit"))
            {
                aimapParser.Seek(1, SeekOrigin.Current);
                SpeedLimit = FastFloatParser.Parse(aimapParser.GetLine().Clean());
            }

            //drive on the left??
            if (aimapParser.SkipToSection("Ambients Drive On The Left"))
            {
                aimapParser.Seek(1, SeekOrigin.Current);
                LeftSidedTraffic = aimapParser.GetLine().Clean() == "1";
            }

            //read ambient types
            if (aimapParser.SkipToSection("Ambient Types/Density"))
            {
                aimapParser.Seek(1, SeekOrigin.Current);
                int ambientTypeCount = int.Parse(aimapParser.GetLine().Clean(), CultureInfo.InvariantCulture);

                for (int i = 0; i < ambientTypeCount; i++)
                {
                    aimapParser.Seek(1, SeekOrigin.Current);

                    string[] typeTokens = aimapParser.GetTokens();
                    TrafficTypesAndDensity[typeTokens[0]] = FastFloatParser.Parse(typeTokens[1]);
                }
            }

            //read peds
            if (aimapParser.SkipToSection("GoodWeatherPedName / BadWeatherPedName"))
            {
                aimapParser.Seek(1, SeekOrigin.Current);
                int pedModelCount = int.Parse(aimapParser.GetLine().Clean(), CultureInfo.InvariantCulture);

                for (int i = 0; i < pedModelCount; i++)
                {
                    aimapParser.Seek(1, SeekOrigin.Current);

                    string[] modelTypes = aimapParser.GetTokens();
                    GoodWeatherPedModels.Add(modelTypes[0]);
                    BadWeatherPedModels.Add(modelTypes.Length > 1 ? modelTypes[1] : modelTypes[0]);
                }
            }

            //read traffic lights
            if (aimapParser.SkipToSection("Traffic Lights"))
            {
                aimapParser.Seek(1, SeekOrigin.Current);
                string[] trafLitNames = aimapParser.GetTokens();

                SingleTrafficLightModel = trafLitNames[0];
                DualTrafficLightModel = (trafLitNames.Length > 1) ? trafLitNames[1] : trafLitNames[0];
            }

            //read ped pool
            if (aimapParser.SkipToSection("Ped Pool"))
            {
                aimapParser.Seek(1, SeekOrigin.Current);
                PedestrianPoolSize = int.Parse(aimapParser.GetLine().Clean(), CultureInfo.InvariantCulture);
            }
        }
    }
}