using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

public class ImpactAudioData
{
    public AudioClip Clip;
    public float MinVolume = 0;
    public float MaxVolume = 1;
    public float MinForce = 0;
    public float MaxForce = 1000;
    public float Frequency = 1;
    public double LastPlayTime = -9999;
}

public class ImpactAudioDataManager
{
    private static Dictionary<int, ImpactAudioData[]> colliderIdData = new Dictionary<int, ImpactAudioData[]>();
    private static bool impactsLoaded = false;

    public static bool TryGetData(int colliderId, out ImpactAudioData[] data)
    {
        return colliderIdData.TryGetValue(colliderId, out data);
    }

    public static void Unload()
    {
        foreach (var dataArray in colliderIdData.Values)
        {
            foreach (var data in dataArray)
            {
                UnityEngine.Object.Destroy(data.Clip);
            }
        }
        colliderIdData.Clear();
        impactsLoaded = false;
    }

    public static void LoadImpacts()
    { 
        if (impactsLoaded)
        {
            return;
        }

        //
        List<ImpactAudioData> currentIdInfos = new List<ImpactAudioData>();

        //parse csv and load infos
        var impactParser = AssetManager.OpenCSV("aud", "cardata", "player", "default_impacts");
        while (!impactParser.EOF())
        {
            impactParser.PrepareLine();
            if (impactParser.TokenCount < 3)
                continue;

            // if we've hit a new id, load it
            if (impactParser[2].Trim().ToUpperInvariant() != "ID")
                continue;

            currentIdInfos.Clear();

            // prepare info line
            impactParser.PrepareLine();
            if (impactParser[0].Trim().ToUpperInvariant() == "ENDOFDATA")
                break;

            //read info
            int id = int.Parse(impactParser[2], CultureInfo.InvariantCulture);
            int numSamples = int.Parse(impactParser[1], CultureInfo.InvariantCulture);

            impactParser.Seek(1, SeekOrigin.Current);
            for (int i = 0; i < numSamples; i++)
            {
                impactParser.PrepareLine();
                string sampleName = impactParser[0];
                var audioData = new ImpactAudioData
                {
                    MinVolume = FastFloatParser.Parse(impactParser[1]),
                    MaxVolume = FastFloatParser.Parse(impactParser[2]),
                    MinForce = FastFloatParser.Parse(impactParser[3]),
                    MaxForce = FastFloatParser.Parse(impactParser[4]),
                    Frequency = FastFloatParser.Parse(impactParser[5])
                };
                if (Math.Abs(audioData.MaxForce - 999999) <= 0.001f) //the game uses 999999 as 'infinite' in these files
                    audioData.MaxForce = float.MaxValue;
                audioData.Clip = AudioAssetManager.LoadClip("impacts", sampleName);
                currentIdInfos.Add(audioData);
            }

            colliderIdData[id] = currentIdInfos.ToArray();
        }
        impactsLoaded = true;
    }
}
