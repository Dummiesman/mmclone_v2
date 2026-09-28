using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class AmbientHornAudio : MonoBehaviour
{
    public bool IsPlaying { get; private set; }

    private AudioSource hornSource;
    private float hornStuckImpactForce = 9999f;

    public List<HornPattern> Patterns = new List<HornPattern>();

    public class HornPattern
    {
        public List<HornPatternIncrement> Increments = new List<HornPatternIncrement>();

        public class HornPatternIncrement
        {
            public float PlayDuration = 0f;
            public float PauseDuration = 0f;

            public static HornPatternIncrement InitFromCsv(CSVParser parser)
            {
                return new HornPatternIncrement { 
                    PlayDuration = FastFloatParser.Parse(parser[0]), 
                    PauseDuration = FastFloatParser.Parse(parser[1]) 
                };
            }
        }

        public static HornPattern InitFromCsv(CSVParser parser)
        {
            if(parser[0].ToLowerInvariant() != "horn play duration")
            {
                Debug.LogError($"HornPattern InitFromCsv: wrong header.");
                return null;
            }

            var pattern = new HornPattern();

            while (!parser.EOF())
            {
                parser.PrepareLine();
                //reached end of pattern
                if (parser[0].ToLowerInvariant() == "horn play duration")
                    break;

                pattern.Increments.Add(HornPatternIncrement.InitFromCsv(parser));
            }

            return pattern;
        }

        public IEnumerator Play(AudioSource source)
        {
            //stop first
            source.enabled = true;
            source.Stop();

            for (int i = 0; i < Increments.Count; i++)
            {
                var increment = Increments[i];
                if (increment.PlayDuration >= Mathf.Epsilon)
                {
                    source.Play();
                    yield return new WaitForSeconds(increment.PlayDuration);
                }

                source.Pause();
                if (increment.PauseDuration >= Mathf.Epsilon)
                {
                    yield return new WaitForSeconds(increment.PauseDuration);
                }
            }

            //stop again, we're done
            source.Stop();
            source.enabled = false;
        }
    }

    //
    private IEnumerator PlayHornEnumerator(int index)
    {
        IsPlaying = true;
        yield return StartCoroutine(Patterns[index].Play(hornSource));
        IsPlaying = false;
    }

    public void PlayHorn(int index)
    {
        if (IsPlaying)
            return;
        StartCoroutine(PlayHornEnumerator(index));
    }

    public void PlayRandomHorn()
    {
        if (IsPlaying)
            return;
        PlayHorn(Random.Range(0, Patterns.Count - 1));
    }

    public void StopAllSounds()
    {
        hornSource.Stop();
    }

    public void Init(string vehicleName)
    {
        //find file
        CSVParser parser = null;
        if(AssetManager.Exists("aud", "cardata", "ambient", $"{vehicleName}_horn.csv"))
        {
            parser = AssetManager.OpenCSV("aud", "cardata", "ambient", $"{vehicleName}_horn.csv");
        }
        else if(AssetManager.Exists("aud", "cardata", "ambient", $"default_horn.csv"))
        {
            parser = AssetManager.OpenCSV("aud", "cardata", "ambient", $"default_horn.csv");
        }

        //file existed?
        if(parser == null)
        {
            Debug.LogError($"AmbientHornAudio Init failure! Trying to init {vehicleName}");
            return;
        }

        //read things
        parser.PrepareLine();
        if(parser[0].ToLower() != "horn sample")
        {
            Debug.LogError($"AmbientHornAudio Init failure: incorrect header. Trying to init {vehicleName}");
            return;
        }

        //read in horn clip info
        parser.PrepareLine();
        string sampleName = parser[0];
        float sampleVolume = FastFloatParser.Parse(parser[1]);
        float samplePitch  = FastFloatParser.Parse(parser[2]);
        hornStuckImpactForce = FastFloatParser.Parse(parser[3]);

        //create audio source
        GameObject hornSourceObj = new GameObject(sampleName);
        hornSource = MMAudioMixer.CreateAudioSource(hornSourceObj);
        hornSource.clip = AudioAssetManager.LoadClip("horns", sampleName);
        hornSource.spatialBlend = 1f;
        hornSource.volume = sampleVolume;
        hornSource.pitch = samplePitch;
        hornSource.loop = true;
        hornSource.minDistance = 20f;
        hornSource.maxDistance = 100f;
        hornSource.rolloffMode = AudioRolloffMode.Linear;
        hornSource.dopplerLevel = 0.1f;
        hornSource.enabled = false;
        hornSourceObj.transform.SetParent(this.transform, false);

        //read in patterns
        parser.PrepareLine();
        while (!parser.EOF())
        {
            if(parser[0].ToLowerInvariant() == "horn play duration")
            {
                Patterns.Add(HornPattern.InitFromCsv(parser));
            }
            else
            {
                Debug.LogError($"Unexpected line while loading AmbientHornAudio: {string.Join(",", parser.GetTokens())}");
                break;
            }
        }

        //DONE :)
    }

    private void OnDisable()
    {
        StopAllSounds();
    }
}
