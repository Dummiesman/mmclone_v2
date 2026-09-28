using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

public class SirenSampleIncrement
{
    public float PlayTime;
    public int NextIndex;

    public SirenSampleIncrement(float playTime, int nextIndex)
    {
        this.PlayTime = playTime;
        this.NextIndex = nextIndex;
    }
}

public class SirenSampleData
{
    public string Name;
    public readonly List<SirenSampleIncrement> Increments = new List<SirenSampleIncrement>();

    public int CurrentIncrement = 0;
    public float currentPlayTime = 0f;

    public SirenSampleData(string name)
    {
        this.Name = name;
    }

    public void LoadClip()
    {
        clip = AudioAssetManager.LoadClip("Sirens", Name);
    }

    public AudioClip clip = null;
}

public class SirenAudio : MonoBehaviour
{
    //private vars
    private int lastIndex = 0;
    private List<SirenSampleData> sampleData;
    private bool isActive = false;

    private AudioSource sirenSource;
    private AudioSource explosionSource;

    public void Init(string desiredCsvName, bool is3D = false)
    {
        // if the csv for this city doesn't exist, fallback to sf
        if (!AssetManager.Exists("aud", "cardata", "player", $"{desiredCsvName}.csv"))
        {
            desiredCsvName = "sfpolicesiren";
        }

        //if the csv for this city doesn't exist, die
        if (!AssetManager.Exists("aud", "cardata", "player", $"{desiredCsvName}.csv"))
        {
            Debug.LogWarning($"Failed to load siren audio csv {desiredCsvName}.");
            Destroy(this);
            return;
        }

        // this csv exists, lets open it now
        var reader = AssetManager.OpenCSV("aud", "cardata", "player", $"{desiredCsvName}");
        var sampleDataList = new List<SirenSampleData>();
        string explosionSample = string.Empty;

        while (!reader.EOF())
        {
            reader.PrepareLine();
            if (reader.TokenCount < 1)
                continue;

            // possible cases
            if (reader.GetToken(0) == "Sample name")
            {
                reader.PrepareLine();
                sampleDataList.Add(new SirenSampleData(reader.GetToken(0)));
                continue;
            }
            if (reader.GetToken(0) == "play time")
            {
                reader.PrepareLine();
                    
                float playTime = FastFloatParser.Parse(reader.GetToken(0));
                int nextIndex = int.Parse(reader.GetToken(1), CultureInfo.InvariantCulture);

                sampleDataList[sampleDataList.Count - 1].Increments.Add(new SirenSampleIncrement(playTime, nextIndex));
                continue;
            }
            if (reader.GetToken(0) == "Explosion sample")
            {
                reader.PrepareLine();
                explosionSample = reader.GetToken(0);
                continue;
            }
        }

        sampleData = sampleDataList;

        // initialize siren source+samples
        var sirenSourceObj = new GameObject("Siren Source");
        sirenSourceObj.transform.parent = transform;

        sirenSource = MMAudioMixer.CreateAudioSource(sirenSourceObj);
        sirenSource.volume = AudioUtils.AdjustVolumeCurve(0.98f);
        sirenSource.pitch = 1f;
        sirenSource.loop = true;
        sirenSource.dopplerLevel = 0.2f;
        if (is3D)
        {
            sirenSource.spatialBlend = 1f;
            sirenSource.minDistance = 35f;
            sirenSource.maxDistance = 100f;
        }

        foreach (var sample in sampleData)
        {
            sample.LoadClip();
        }

        // initialize explosion souhnd
        if(!string.IsNullOrEmpty(explosionSample))
        {
            var explosionSourceObj = new GameObject("Explosion Source");
            explosionSourceObj.transform.parent = transform;

            explosionSource = MMAudioMixer.CreateAudioSource(explosionSourceObj);
            explosionSource.volume = AudioUtils.AdjustVolumeCurve(0.98f);
            explosionSource.pitch = 1f;
            explosionSource.clip = AudioAssetManager.LoadClip("Sirens", explosionSample);
            if (is3D)
            {
                explosionSource.spatialBlend = 1f;
                explosionSource.minDistance = 35f / 2.0f;
                explosionSource.maxDistance = 100f / 2.0f;
            }
        }

        // load first clip
        sirenSource.playOnAwake = false;
        sirenSource.clip = sampleData[0].clip;

        // deactivate
        Deactivate();
    }

    public void PlayExplosion()
    {
        if(explosionSource != null)
        {
            explosionSource.Play();
        }
    }

    public void Activate()
    {
        if (!isActive)
        {
            isActive = true;
            sirenSource.Play();
            sirenSource.time = 0f; //reset time to play from the start of the sample, just like MM
        }
    }

    public void Deactivate()
    {
        if (isActive)
        {
            isActive = false;
            sirenSource.Pause();
        }
    }

    public void Toggle()
    {
        if (isActive)
        {
            Deactivate();
        }
        else
        {
            Activate();
        }
    }

    void Update()
    {
        if (!isActive)
            return;

        var currentData = sampleData[lastIndex];
        var currentIncrement = currentData.Increments[currentData.CurrentIncrement];
        currentData.currentPlayTime += Time.deltaTime;

        //are we done playing this increment?
        if (currentData.currentPlayTime > currentIncrement.PlayTime)
        {
            currentData.CurrentIncrement++;
            if (currentData.CurrentIncrement == currentData.Increments.Count)
                currentData.CurrentIncrement = 0;

            //assign new clip
            lastIndex = currentIncrement.NextIndex;
            sirenSource.clip = sampleData[lastIndex].clip;
            sirenSource.Play();

            //rest play time
            currentData.currentPlayTime -= currentIncrement.PlayTime;
        }
    }

    public void Reset()
    {
        lastIndex = 0;
        foreach (var sd in sampleData)
        {
            sd.currentPlayTime = 0f;
            sd.CurrentIncrement = 0;
        }
    }
}
