using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using static PackageObjectInstance;

public class SurfaceAudio : MonoBehaviour 
{
    private VehCar vehicle;
    private WobbleAudioSample wobble = null;
    private readonly List<SurfaceAudioGroup> audioGroups = new List<SurfaceAudioGroup>();
    private float wobbleDistanceAccum = 0f;

    class WobbleAudioSample
    {
        public float MinVol = 0.95f;
        public float MaxVol = 1.0f;
        public float MinPitch = 0.75f;
        public float MaxPitch = 0.95f;
        public float PitchChange = 0.063f;
        public AudioSource WobbleSource;
    }

    class SkidAudioSample
    {
        public float MinSlippage = 999f;
        public float MaxSlippage = 999f;
        public AudioSource SkidSource;

        public static SkidAudioSample InitFromCSV(CSVParser par, SurfaceAudio parent)
        {
            var skds = new SkidAudioSample();
            if (par.TokenCount < 3)
            {
                Debug.LogWarning("SkidAudioSample InitFromCsv, file has malformed data.");
                return skds;
            }

            //create object
            string audioName = par[0];

            GameObject skidSourceObj = new GameObject(audioName);
            skidSourceObj.transform.parent = parent.gameObject.transform;

            skds.SkidSource = MMAudioMixer.CreateAudioSource(skidSourceObj);
            skds.SkidSource.pitch = 1f;
            skds.SkidSource.volume = 1f;
            skds.SkidSource.spatialBlend = (parent.vehicle.Type == vehCarType.Player) ? 0f : 1f;
            skds.SkidSource.loop = true;
            skds.SkidSource.minDistance = 7.5f;
            skds.SkidSource.maxDistance = 25f;
            skds.SkidSource.priority = 100;

            skds.SkidSource.clip = AudioAssetManager.LoadClip("surfaces", audioName);

            //
            FastFloatParser.TryParse(par[1], out skds.MinSlippage);
            FastFloatParser.TryParse(par[2], out skds.MaxSlippage);

            return skds;
        }
    }

    class SurfaceAudioGroup
    {
        public float MaxSpeed;
        public float MinVolume;
        public float MaxVolume;
        public float MinPitch;
        public float MaxPitch;
        public float MinSkidVolume;
        public float MaxSkidVolume;
        public AudioSource SurfaceSource;

        public List<SkidAudioSample> SkidSamples = new List<SkidAudioSample>();

        public void Mute()
        {
            if (SurfaceSource != null)
            {
                SurfaceSource.Stop();
                SurfaceSource.volume = 0f;
            }

            foreach (var sample in SkidSamples)
            {
                if (sample.SkidSource != null)
                {
                    sample.SkidSource.Stop();
                    sample.SkidSource.volume = 0f;
                }
            }
        }

        public static SurfaceAudioGroup InitFromCSV(CSVParser par, SurfaceAudio parent)
        {
            var sfag = new SurfaceAudioGroup();
            if(par.TokenCount < 9)
            {
                Debug.LogWarning("SkidAudioGroup InitFromCsv, file has malformed data");
                return sfag;
            }

            //create surface audio
            string audioName = par[0];

            if(audioName.ToUpper() != "NOSOUND")
            {
                GameObject surfaceSourceObj = new GameObject(audioName);
                surfaceSourceObj.transform.parent = parent.gameObject.transform;

                sfag.SurfaceSource = MMAudioMixer.CreateAudioSource(surfaceSourceObj);
                sfag.SurfaceSource.pitch = 1f;
                sfag.SurfaceSource.volume = 1f;
                sfag.SurfaceSource.spatialBlend = (parent.vehicle.Type == vehCarType.Player) ? 0f : 1f;
                sfag.SurfaceSource.loop = true;
                sfag.SurfaceSource.minDistance = 7.5f;
                sfag.SurfaceSource.maxDistance = 25f;

                sfag.SurfaceSource.clip = AudioAssetManager.LoadClip("surfaces", audioName);
            }

            //
            FastFloatParser.TryParse(par[1], out sfag.MaxSpeed);
            FastFloatParser.TryParse(par[2], out sfag.MinVolume);
            FastFloatParser.TryParse(par[3], out sfag.MaxVolume);
            FastFloatParser.TryParse(par[4], out sfag.MinPitch);
            FastFloatParser.TryParse(par[5], out sfag.MaxPitch);
            FastFloatParser.TryParse(par[6], out sfag.MinSkidVolume);
            FastFloatParser.TryParse(par[7], out sfag.MaxSkidVolume);

            //read skid samples
            int numSkidSamples = int.Parse(par[8], CultureInfo.InvariantCulture);
            par.Seek(1, System.IO.SeekOrigin.Current);

            for(int i=0; i < numSkidSamples; i++)
            {
                par.PrepareLine();
                sfag.SkidSamples.Add(SkidAudioSample.InitFromCSV(par, parent));
            }

            //
            return sfag;
        }
    }

    public void InitTireWobble()
    {
        string tireWobbleCsvName = "tirewobble.csv";
        if (AssetManager.Exists("aud", "cardata", "player", tireWobbleCsvName))
        {
            var surfaceParser = AssetManager.OpenCSV("aud", "cardata", "player", tireWobbleCsvName);
            surfaceParser.PrepareHeader();
            surfaceParser.PrepareLine();

            GameObject wobbleSourceObj = new GameObject("Tire Wobble");
            wobbleSourceObj.transform.parent = this.transform;

            wobble = new WobbleAudioSample();
            wobble.WobbleSource = MMAudioMixer.CreateAudioSource(wobbleSourceObj);
            wobble.WobbleSource.clip = AudioAssetManager.LoadClip(surfaceParser[0]);
            wobble.WobbleSource.spatialBlend = (vehicle.Type == vehCarType.Player) ? 0f : 1f;
            FastFloatParser.TryParse(surfaceParser[1], out wobble.MinVol);
            FastFloatParser.TryParse(surfaceParser[2], out wobble.MaxVol);
            FastFloatParser.TryParse(surfaceParser[3], out wobble.MinPitch);
            FastFloatParser.TryParse(surfaceParser[4], out wobble.MaxPitch);
            if (FastFloatParser.TryParse(surfaceParser[5], out var pitchDivisor))
            {
                wobble.PitchChange = (pitchDivisor == 0.0f) ? 0.0f : 1.0f / pitchDivisor;
            }
        }
    }

	public void Init(VehCar vehicle, MMWeather weather)
    {
        this.vehicle = vehicle;

        //get csv name
        string surfaceAudioCsv = "default_surfacedry.csv";
        if (AssetManager.Exists("aud", "cardata", "player", $"{vehicle.Basename}_surfacedry.csv"))
        {
            surfaceAudioCsv = $"{vehicle.Basename}_surfacedry.csv";
        }

        if(weather == MMWeather.Raining)
        {
            surfaceAudioCsv = "default_surfacewet.csv";
            if (AssetManager.Exists("aud", "cardata", "player", $"{vehicle.Basename}_surfacewet.csv"))
            {
                surfaceAudioCsv = $"{vehicle.Basename}_surfacewet.csv";
            }
        }

        //load surface samples
        if(!AssetManager.Exists("aud", "cardata", "player", surfaceAudioCsv))
        {
            Debug.LogError("SurfaceAudio::Init - Missing surface csv files??");
            return;
        }

        var surfaceParser = AssetManager.OpenCSV("aud", "cardata", "player", surfaceAudioCsv);
        surfaceParser.Seek(2, System.IO.SeekOrigin.Begin);

        while (!surfaceParser.EOF())
        {
            surfaceParser.PrepareLine();

            //check if we're at the "header" of a new group
            if(surfaceParser[0].ToLowerInvariant() == "surface wave")
            {
                surfaceParser.PrepareLine();
                audioGroups.Add(SurfaceAudioGroup.InitFromCSV(surfaceParser, this));
            }
        }

        // load tire wobble
        InitTireWobble();
    }

    private void MuteAllGroups()
    {
        foreach (var grp in audioGroups)
        {
            grp.Mute();
        }
    }

    private LevelPhysMaterial lastSurfaceMaterial = null;

    private void UpdateWobble()
    {
        if (wobble == null || wobble.WobbleSource == null) return;

        var sim = vehicle.VehCarSim;
        float damageFactor = vehicle.Damage.MedMaxDamagePercentage;

        float wheelSpeedSum = 0f;
        foreach (var w in sim.Wheels)
        {
            wheelSpeedSum += Mathf.Abs(w.RotationRate) * w.Radius;
        }
        float speed = wheelSpeedSum / sim.Wheels.Length;

        if (damageFactor <= 0.05f) return;
        damageFactor = Mathf.Min(damageFactor, 1f);

        float distance = wobbleDistanceAccum + speed * Time.deltaTime;
        float wheelCircumference = 2f * Mathf.PI * sim.Wheels[0].Radius;

        if (distance >= wheelCircumference)
        {
            float volume = Mathf.Clamp(damageFactor, wobble.MinVol, wobble.MaxVol);
            float pitch = Mathf.Clamp(speed * wobble.PitchChange, wobble.MinPitch, wobble.MaxPitch);

            if (wobble.WobbleSource.isPlaying) return;

            wobble.WobbleSource.volume = AudioUtils.AdjustVolumeCurve(volume);
            wobble.WobbleSource.pitch = pitch;
            wobble.WobbleSource.Play();

            distance = 0f;
        }

        wobbleDistanceAccum = distance;
    }

    private void UpdateSurface()
    {
        // check for surface change
        bool newSurface = false;

        var wheels = vehicle.VehCarSim.Wheels;
        if (wheels[0].CurrentMaterial != lastSurfaceMaterial && wheels[1].CurrentMaterial != lastSurfaceMaterial)
        {
            newSurface = lastSurfaceMaterial != wheels[0].CurrentMaterial;
            lastSurfaceMaterial = wheels[0].CurrentMaterial;
        }


        // update surface sounds accordingly
        if (lastSurfaceMaterial == null)
        {
            MuteAllGroups();
            return;
        }

        // mute other sounds
        for (int i = 0; i < audioGroups.Count; i++)
        {
            if (i != lastSurfaceMaterial.Sound)
                audioGroups[i].Mute();
        }

        // out of range?
        if (lastSurfaceMaterial.Sound < 0 || lastSurfaceMaterial.Sound >= audioGroups.Count)
        {
            return;
        }

        //update this surfaces sounds
        var group = audioGroups[lastSurfaceMaterial.Sound];
        float speedFactor = Mathf.Clamp01(vehicle.VehCarSim.Speed / group.MaxSpeed);

        //stop playing at low speed
        float speed = vehicle.VehCarSim.Speed;
        if (speed < 2f)
        {
            speedFactor *= (speed / 2f);
        }

        float targetPitch = Mathf.Lerp(group.MinPitch, group.MaxPitch, speedFactor);
        float targetVolume = Mathf.Lerp(group.MinVolume, group.MaxVolume, speedFactor);

        if (group.SurfaceSource != null)
        {
            if (!group.SurfaceSource.isPlaying) group.SurfaceSource.Play();
            group.SurfaceSource.pitch = targetPitch;
            group.SurfaceSource.volume = AudioUtils.AdjustVolumeCurve(targetVolume);
        }

        //update skid sounds
        float slip = Mathf.Max(wheels[0].LastSlippage, wheels[1].LastSlippage, wheels[2].LastSlippage, wheels[3].LastSlippage);
        float volumeRange = (group.MaxSkidVolume - group.MinSkidVolume);
        float volume = slip * volumeRange + group.MinSkidVolume;

        if (slip <= 0.0f)
        {
            group.Mute();
        }
        else
        {
            foreach (var skidSound in group.SkidSamples)
            {
                if (skidSound.SkidSource == null)
                    continue;

                // Out of range: stop if needed.
                if (slip < skidSound.MinSlippage || slip > skidSound.MaxSlippage)
                {
                    if (skidSound.SkidSource.isPlaying)
                        skidSound.SkidSource.Stop();

                    continue;
                }

                // In range: update volume and play.
                skidSound.SkidSource.volume = AudioUtils.AdjustVolumeCurve(volume);

                if (!skidSound.SkidSource.isPlaying)
                {
                    skidSound.SkidSource.loop = true;
                    skidSound.SkidSource.Play();
                }
            }
        }
    }

    private void Update()
    {
        UpdateWobble();
        UpdateSurface();
    }
}
