using System.Collections.Generic;
using System.IO;
using UnityEngine;

public class SuspensionAudio : MonoBehaviour 
{

    private VehCar vehicle;
    private List<SuspensionAudioSample> samples = new List<SuspensionAudioSample>();

    private AudioSource _oneShotSrc;

    public class SuspensionAudioSample
    {
        public AudioClip Clip;
        public float MinVelocity;
        public float MaxVelocity;
        public float MinVolume;
        public float MaxVolume;
        public float VolumeDivisor;

        public static SuspensionAudioSample InitFromCSV(CSVParser parser)
        {
            var sample = new SuspensionAudioSample();

            sample.Clip = AudioAssetManager.LoadClip("suspension", parser.GetToken(0));
            FastFloatParser.TryParse(parser.GetToken(1), out sample.MinVelocity);
            FastFloatParser.TryParse(parser.GetToken(2), out sample.MaxVelocity);
            FastFloatParser.TryParse(parser.GetToken(3), out sample.MinVolume);
            FastFloatParser.TryParse(parser.GetToken(4), out sample.MaxVolume);
            FastFloatParser.TryParse(parser.GetToken(5), out sample.VolumeDivisor);

            return sample;
        }
    }

    public void Init(VehCar vehicle)
    {
        this.vehicle = vehicle;

        if (!AssetManager.Exists("aud", "cardata", "player", "suspensionaudio.csv"))
        {
            Debug.LogWarning($"Suspension audio definitions missing!!!");
            return;
        }

        //open csv reader
        var audReader = AssetManager.OpenCSV("aud", "cardata", "player", "suspensionaudio.csv");
        audReader.Seek(1, SeekOrigin.Begin);

        while (!audReader.EOF())
        {
            audReader.PrepareLine();
            samples.Add(SuspensionAudioSample.InitFromCSV(audReader));
        }

        //create audio source
        _oneShotSrc = MMAudioMixer.CreateAudioSource(this.gameObject);
        _oneShotSrc.pitch = 1f;
        _oneShotSrc.volume = 1f;
        _oneShotSrc.spatialBlend = (vehicle.Type == vehCarType.Player) ? 0.0f : 1.0f;
    }
	
	void FixedUpdate () {
        //one at a time, please
        if (_oneShotSrc.isPlaying)
            return;

        //get largest suspension velocity
        float avg = (vehicle.VehCarSim.Wheels[0].SuspensionCompressionRate +
                     vehicle.VehCarSim.Wheels[1].SuspensionCompressionRate +
                     vehicle.VehCarSim.Wheels[2].SuspensionCompressionRate +
                     vehicle.VehCarSim.Wheels[3].SuspensionCompressionRate) * 0.25f;
        int numGrounded = vehicle.VehCarSim.OnGround();

        //play sounds
        if (numGrounded >= 2)
        {
            foreach (var sample in samples)
            {
                if (Mathf.Abs(avg) < sample.MinVelocity)
                    continue;

                float volume = (1.0f / sample.VolumeDivisor) * avg;
                if (volume < sample.MinVolume) volume = sample.MinVolume;
                else if (volume > sample.MaxVolume) volume = sample.MaxVolume;

                volume = AudioUtils.AdjustVolumeCurve(volume);
                _oneShotSrc.PlayOneShot(sample.Clip, volume);
            }
        }
    }
}
