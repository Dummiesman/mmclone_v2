using UnityEngine;
using static Unity.VisualScripting.Member;

public class SemiAudio : MonoBehaviour
{
    private AudioSource reverseAudioSource;
    private AudioSource airBlowAudioSource;

    private float airBlowVolume = 1f;
    private AudioClip airBlowSample;
    private bool canPlayAirBlow = false;

    private VehCar vehicle;

    public void Init(VehCar vehicle, bool is3D = false)
    {
        this.vehicle = vehicle;

        //check for custom semidata, then shared
        CSVParser reader = null;
        if (AssetManager.Exists("aud", "cardata", "shared", $"{vehicle.Basename}_semidata.csv"))
        {
            reader = AssetManager.OpenCSV("aud", "cardata", "shared", $"{vehicle.Basename}_semidata.csv");
        }
        else if (AssetManager.Exists("aud", "cardata", "shared", "semidata.csv"))
        {
            reader = AssetManager.OpenCSV("aud", "cardata", "shared", "semidata.csv");
        }

        //initialize sources
        reverseAudioSource = CreateSource(is3D);
        reverseAudioSource.loop = true;
        reverseAudioSource.volume = AudioUtils.AdjustVolumeCurve(0.87f);

        airBlowAudioSource = CreateSource(is3D);
        airBlowAudioSource.loop = false;
        airBlowAudioSource.volume = AudioUtils.AdjustVolumeCurve(0.9f);

        //read data
        if (reader != null)
        {
            reader.Seek(1, System.IO.SeekOrigin.Begin);
            reader.PrepareLine(); //read data from 2nd line

            var lineData = reader.GetTokens();
            if (lineData.Length >= 1)
            {
                string reverseSampleName = lineData[0];
                reverseAudioSource.clip = AudioAssetManager.LoadClip(reverseSampleName);
            }
            if (lineData.Length >= 2)
            {
                string airBlowSampleName = lineData[1];
                airBlowSample = AudioAssetManager.LoadClip(airBlowSampleName);
                airBlowAudioSource.clip = airBlowSample;
            }
            if (lineData.Length >= 3)
            {
                float reverseVolume;
                if (FastFloatParser.TryParse(lineData[2], out reverseVolume))
                {
                    reverseAudioSource.volume = AudioUtils.AdjustVolumeCurve(reverseVolume);
                }
            }
            if (lineData.Length >= 4)
            {
                if(FastFloatParser.TryParse(lineData[3], out airBlowVolume))
                {
                    airBlowAudioSource.volume = AudioUtils.AdjustVolumeCurve(airBlowVolume);
                }
            }
        }

    }

    private AudioSource CreateSource(bool is3D)
    {
        AudioSource source = MMAudioMixer.CreateAudioSource(this.gameObject);
        source.pitch = 1f;
        if (is3D)
        {
            source.spatialBlend = 1f;
            source.minDistance = 20f;
            source.maxDistance = 45f;
        }
        source.Stop();
        return source;
    }

    public void Reset()
    {
        reverseAudioSource.Stop();
        airBlowAudioSource.Stop();
        canPlayAirBlow = false;
    }

    private void Update()
    {
        int gear = vehicle.VehCarSim.Transmission.CurrentGear;
        float brakeInput = vehicle.VehCarSim.BrakeInput;
        float speed = Mathf.Abs(vehicle.VehCarSim.Speed);

        // reverse
        if (gear == 0 && !reverseAudioSource.isPlaying)
        {
            reverseAudioSource.time = 0f;
            reverseAudioSource.Play();
        }
        else if (gear > 0 && reverseAudioSource.isPlaying)
        {
            reverseAudioSource.Stop();
        }

        // air blow
        if (speed > 2.2f)
        {
            canPlayAirBlow = true;
        }
        if (speed < 0.5f)
        {
            if (brakeInput > 0.1f && canPlayAirBlow && airBlowSample != null)
            {
                airBlowAudioSource.time = 0f;
                airBlowAudioSource.Play();
            }
            canPlayAirBlow = false;
        }
    }
}