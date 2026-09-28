using MM2.AI;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public class AmbientVehicleAudio : MonoBehaviour
{
    private AITrafficCar Vehicle;
    private List<AmbCarAudioIncrement> Samples = new List<AmbCarAudioIncrement>();
    private AudioSource EngineSource;

    private int lastSample = -1;
    
    class AmbCarAudioIncrement
    {
        public float MinSpeed = 0;
        public float MaxSpeed = 1;
        public float MinPitch = 0;
        public float MaxPitch = 1;

        public static AmbCarAudioIncrement InitFromCSV(CSVParser par)
        {
            if (par.TokenCount < 4)
            {
                Debug.LogWarning("AmbCarAudioIncrement InitFromCsv, file has malformed data.");
                return null;
            }

            var casd = new AmbCarAudioIncrement();
            FastFloatParser.TryParse(par[0], out casd.MinSpeed);
            FastFloatParser.TryParse(par[1], out casd.MaxSpeed);
            FastFloatParser.TryParse(par[2], out casd.MinPitch);
            FastFloatParser.TryParse(par[3], out casd.MaxPitch);

            return casd;
        }
    }

    private void InitSource(CSVParser parser)
    {
        string sampleName = parser[0];
        float sampleVolume = FastFloatParser.Parse(parser[1]);

        GameObject engineSourceObj = new GameObject(sampleName);
        EngineSource = MMAudioMixer.CreateAudioSource(engineSourceObj);
        EngineSource.clip = AudioAssetManager.LoadClip("engines", sampleName);
        EngineSource.spatialBlend = 1f;
        EngineSource.volume = AudioUtils.AdjustVolumeCurve(sampleVolume);
        EngineSource.pitch = 1;
        EngineSource.loop = true;
        EngineSource.minDistance = 10.0f;
        EngineSource.maxDistance = 50.0f;
        EngineSource.dopplerLevel = 0.05f;
        EngineSource.Stop();
        engineSourceObj.transform.SetParent(this.transform, false);
    }

    public void SetSpatialBlend(float blend)
    {
        EngineSource.spatialBlend = blend;
    }

    public void Init(string basename, AITrafficCar vehicle)
    {
        //set references
        this.Vehicle = vehicle;

        //read in sample data
        CSVParser audReader = null;

        if (!AssetManager.Exists("aud", "cardata", "ambient", $"{basename}_engine.csv"))
        {
            if (AssetManager.Exists("aud", "cardata", "ambient", $"default_engine.csv"))
            {
                audReader = AssetManager.OpenCSV("aud", "cardata", "ambient", $"default_engine.csv");
            }
            else
            {
                Debug.LogWarning($"Amb audio cardata doesn't exist for {basename}");
                return;
            }
        }
        else
        {
            audReader = AssetManager.OpenCSV("aud", "cardata", "ambient", $"{basename}_engine.csv");
        }

        //open csv reader
        audReader.Seek(1, SeekOrigin.Begin);


        //read in source data
        audReader.PrepareLine();
        InitSource(audReader);

        //read in sample data
        audReader.Seek(1, SeekOrigin.Current);

        while (!audReader.EOF())
        {
            audReader.PrepareLine();
            var sample = AmbCarAudioIncrement.InitFromCSV(audReader);
            Samples.Add(sample);
        }

    }

    public void UpdateEngine(float speed)
    {
        if (!EngineSource.isPlaying)
            EngineSource.Play();

        for(int i= lastSample < 1 ? 0 : lastSample - 1; i < ((lastSample <= Samples.Count && lastSample > 1) ? lastSample + 1 : Samples.Count); i++)
        {
            if(i == Samples.Count - 1 || Samples[i].MinSpeed < speed)
            {
                var sample = Samples[i];
                EngineSource.pitch = Mathf.Lerp(sample.MinPitch, sample.MaxPitch, (speed - sample.MinSpeed) / (sample.MaxSpeed - sample.MinSpeed));
                lastSample = i;
                break;
            }
        }
    }

    private void OnEnable()
    {
        if (EngineSource != null) EngineSource.enabled = true;
    }

    private void OnDisable()
    {
        if (EngineSource != null) EngineSource.enabled = false; 
    }

    void Update()
    {
        if (Vehicle == null)
            return;

        float speed = (Vehicle.CurrentGoal == Vehicle.DriveGoal || Vehicle.CurrentGoal == Vehicle.RegainGoal) ? Vehicle.Speed : 0.0f;
        UpdateEngine(speed);
    }
}
