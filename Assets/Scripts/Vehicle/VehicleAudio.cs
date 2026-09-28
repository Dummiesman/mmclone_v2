using System.Collections.Generic;
using System.IO;
using UnityEngine;

public class VehicleAudio : MonoBehaviour
{
    private VehCar Vehicle;
    private bool Networked = false;
    private List<CarAudioSampleData> Samples = new List<CarAudioSampleData>();
    private AudioSource HornSource;
    private AudioSource ClutchSource;

    class CarAudioSampleData
    {
        public string Name = "null";
        public float MinVolume = 0;
        public float MaxVolume = 1;
        public float FadeInStartRPM = 0;
        public float FadeInEndRPM = 100;
        public float FadeOutStartRPM = 100;
        public float FadeOutEndRPM = 200;
        public float MinPitch = 0;
        public float MaxPitch = 1;
        public float PitchShiftStartRPM = 0;
        public float PitchShiftEndRPM = 200;
        public AudioSource Source;

        public static CarAudioSampleData InitFromCSV(CSVParser par)
        {
            var casd = new CarAudioSampleData { Name = par[0] };
            if (par.TokenCount < 11)
            {
                Debug.LogWarning("CarAudioSampleData InitFromCsv, file has malformed data.");
                return casd;
            }

            FastFloatParser.TryParse(par[1], out casd.MinVolume);
            FastFloatParser.TryParse(par[2], out casd.MaxVolume);
            FastFloatParser.TryParse(par[3], out casd.FadeInStartRPM);
            FastFloatParser.TryParse(par[4], out casd.FadeInEndRPM);
            FastFloatParser.TryParse(par[5], out casd.FadeOutStartRPM);
            FastFloatParser.TryParse(par[6], out casd.FadeOutEndRPM);
            FastFloatParser.TryParse(par[7], out casd.MinPitch);
            FastFloatParser.TryParse(par[8], out casd.MaxPitch);
            FastFloatParser.TryParse(par[9], out casd.PitchShiftStartRPM);
            FastFloatParser.TryParse(par[10], out casd.PitchShiftEndRPM);

            return casd;
        }
    }

    private void InitHorn(CSVParser par)
    {
        string hornWave = par.GetToken(0);
        float hornVolume = FastFloatParser.Parse(par.GetToken(1));

        bool isPlayer = (Vehicle.Type == vehCarType.Player);

        GameObject hornSoundObj = new GameObject(hornWave);
        HornSource = MMAudioMixer.CreateAudioSource(hornSoundObj);
        HornSource.clip = AudioAssetManager.LoadClip("horns", hornWave);
        HornSource.spatialBlend = (isPlayer) ? 0f : 1f;
        HornSource.volume = AudioUtils.AdjustVolumeCurve(hornVolume);
        HornSource.pitch = 1;
        HornSource.loop = true;
        HornSource.minDistance = 20f;
        HornSource.maxDistance = 50f;
        HornSource.priority = 99;
        HornSource.Stop();

        hornSoundObj.transform.parent = this.transform;
    }

    private void InitClutch(CSVParser par)
    {
        string clutchWave = par.GetToken(4);
        float clutchVolume = FastFloatParser.Parse(par.GetToken(5));

        bool isPlayer = (Vehicle.Type == vehCarType.Player);

        GameObject clutchSoundObj = new GameObject(clutchWave);
        ClutchSource = MMAudioMixer.CreateAudioSource(clutchSoundObj);
        ClutchSource.clip = AudioAssetManager.LoadClip(clutchWave);
        ClutchSource.spatialBlend = (isPlayer) ? 0f : 1f;
        ClutchSource.volume = clutchVolume;
        ClutchSource.pitch = 1;
        ClutchSource.loop = false;
        ClutchSource.priority = 99;
        ClutchSource.Stop();

        clutchSoundObj.transform.parent = this.transform;
    }

    public void SetSpatialBlend(float blend)
    {
        foreach (var source in Samples)
        {
            source.Source.spatialBlend = blend;
        }
    }

    public void Init(string basename, VehCar vehicle)
    {
        //set references
        this.Vehicle = vehicle;

        //destroy old samples
        if (Samples.Count > 0)
        {
            foreach (var sample in Samples)
                Destroy(sample.Source.gameObject);
            Samples.Clear();
        }

        //read in sample data
        bool isPlayer = (Vehicle.Type == vehCarType.Player);
        string carDataFolder = (isPlayer) ? "player" : "opponent";
        if (!AssetManager.Exists("aud", "cardata", carDataFolder, $"{basename}.csv"))
        {
            Debug.LogWarning($"Audio cardata doesn't exist for {basename}");
            return;
        }

        //open csv reader
        var audReader = AssetManager.OpenCSV("aud", "cardata", carDataFolder, $"{basename}.csv");
        audReader.Seek(1, SeekOrigin.Begin);

        //read in horn/clutch sample data, and immidiately create these audio sources
        audReader.PrepareLine();
        InitHorn(audReader);
        InitClutch(audReader);

        //read in sample data
        audReader.Seek(1, SeekOrigin.Current);

        while (!audReader.EOF())
        {
            audReader.PrepareLine();
            var sample = CarAudioSampleData.InitFromCSV(audReader);

            GameObject sampleSource = new GameObject(sample.Name);
            sampleSource.transform.parent = this.transform;
            sample.Source = MMAudioMixer.CreateAudioSource(sampleSource);
            sample.Source.spatialBlend = (isPlayer) ? 0f : 1f;
            sample.Source.volume = 0;
            sample.Source.pitch = 0;
            sample.Source.clip = AudioAssetManager.LoadClip("engines", sample.Name);
            sample.Source.loop = true;
            sample.Source.minDistance = 15f;
            sample.Source.maxDistance = 50f;
            sample.Source.Play();
            sample.Source.priority = 90;
            Samples.Add(sample);
        }

    }

    public void UpdateHorn(bool state)
    {
        if (HornSource == null)
            return;

        bool hornRequested = state;
        if (!HornSource.isPlaying && hornRequested)
        {
            HornSource.Play();
            return;
        }
        if (HornSource.isPlaying && !hornRequested)
        {
            HornSource.Stop();
            return;
        }
    }

    private int lastRecordedGear = 1;
    void UpdateClutch()
    {
        if (ClutchSource == null)
            return;

        int currentGear = Vehicle.VehCarSim.Transmission.CurrentGear;
        if (currentGear != lastRecordedGear)
        {
            if (lastRecordedGear >= 1 && currentGear == 0)
            {
                ClutchSource.PlayOneShot(ClutchSource.clip);
            }
            if (lastRecordedGear == 0 && currentGear >= 1)
            {
                ClutchSource.PlayOneShot(ClutchSource.clip);
            }
            lastRecordedGear = currentGear;
        }
    }

    void UpdateEngine(float rpm)
    {
        foreach (var sample in Samples)
        {
            var source = sample.Source;

            // volume — mirrors calculateBlendVolume
            float targetVolume;
            if (rpm <= sample.FadeInStartRPM || rpm >= sample.FadeOutEndRPM)
            {
                targetVolume = sample.MinVolume;
            }
            else if (rpm >= sample.FadeInEndRPM && rpm <= sample.FadeOutStartRPM)
            {
                targetVolume = sample.MaxVolume;
            }
            else if (rpm > sample.FadeInStartRPM && rpm < sample.FadeInEndRPM)
            {
                float fadeInScale = (sample.FadeInEndRPM != sample.FadeInStartRPM)
                    ? (sample.MaxVolume - sample.MinVolume) * (1f / (sample.FadeInEndRPM - sample.FadeInStartRPM))
                    : 0f;
                targetVolume = ((rpm - sample.FadeInStartRPM) * fadeInScale) + sample.MinVolume;
            }
            else // rpm > FadeOutStartRPM && rpm < FadeOutEndRPM
            {
                float fadeOutScale = (sample.FadeOutEndRPM != sample.FadeOutStartRPM)
                    ? (sample.MaxVolume - sample.MinVolume) * (1f / (sample.FadeOutEndRPM - sample.FadeOutStartRPM))
                    : 0f;
                targetVolume = ((sample.FadeOutEndRPM - rpm) * fadeOutScale) + sample.MinVolume;
            }

            // pitch — mirrors calculateBlendPitch (the "broken but matches the game" branch)
            float targetPitch;
            if (rpm <= sample.PitchShiftStartRPM)
            {
                targetPitch = sample.MinPitch;
            }
            else if (rpm >= sample.PitchShiftEndRPM)
            {
                targetPitch = sample.MaxPitch;
            }
            else
            {
                float pitchScale = (sample.PitchShiftEndRPM != sample.PitchShiftStartRPM)
                    ? (sample.MaxPitch - sample.MinPitch) * (1f / (sample.PitchShiftEndRPM - sample.PitchShiftStartRPM))
                    : 0f;
                targetPitch = (rpm * pitchScale) + sample.MinPitch;
            }

            // cutoff, then clamp — order matters
            if (targetVolume < 0.25f) targetVolume = 0f;

            targetVolume = Mathf.Clamp(targetVolume, 0f, 1f);
            targetPitch = Mathf.Clamp(targetPitch, 0f, 2f);

            source.volume = AudioUtils.AdjustVolumeCurve(targetVolume);
            source.pitch = targetPitch;
        }
    }

    public void UpdateEngineNetworked(float throttlePercent)
    {
        //get the largest RPM value of the samples
        float highestRpm = 0f;
        foreach(var sample in Samples)
        {
            if (sample.FadeOutEndRPM > highestRpm)
                highestRpm = sample.FadeOutEndRPM;
        }

        //update engine pitch with it
        UpdateEngine(throttlePercent * highestRpm);
    }

    void Update()
    {
        if (Vehicle == null)
            return;
        if (Networked)
            return;

        UpdateClutch();
        UpdateEngine(Vehicle.VehCarSim.Engine.CurrentRPM);
    }
}

