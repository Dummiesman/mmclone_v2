using System.Collections.Generic;
using Dummiesman.Wave;
using MM2.AI;
using UnityEngine;

public class VoiceAudio : MonoBehaviour
{
    public List<AvoidanceAudioGroup> AvoidanceGroups = new List<AvoidanceAudioGroup>();
    public List<ImpactAudioGroup> ImpactGroups = new List<ImpactAudioGroup>();

    private AIEntity refEntity;
    private AudioSource VoiceSource;

    private AvoidanceAudioGroup.AvoidanceAudioSampleData lastAvoidSample;
    private ImpactAudioGroup.ImpactAudioSampleData lastImpactSample;

    private int lastCollisionGroupIndex = -1;
    private int lastAvoidGroupIndex = -1;
    private int curAvoidGroupIndex = -1;

    private float inRangeTimer = 0f;
    private float outRangeTimer = 0f;

    private float audioSourceStopTimer = 0f;

    public bool IsPlaying => (VoiceSource != null && VoiceSource.isPlaying);

    private static bool IsNewSectionHeader(string header)
    {
        string headerLower = header.ToLower();
        return headerLower == "min impact force" || headerLower == "min speed";
    }

    public class AvoidanceAudioGroup
    {
        public float MinSpeed = 0f;
        public float MaxSpeed = 999f;
        public float MinTimeInRange = 0.1f;
        public float MaxTimeOutOfRange = 0.1f;
        public readonly List<AvoidanceAudioSampleData> Samples = new List<AvoidanceAudioSampleData>();

        public class AvoidanceAudioSampleData
        {
            public string Name;
            public WAVStream Stream;
            public float Volume = 1f;

            public void SetupStream()
            {
                this.Stream = AudioAssetManager.LoadStream("Creature3D", Name);
            }

            public void DisposeStream()
            {
                this.Stream?.Dispose();
                Stream = null;
            }
        }

        public static AvoidanceAudioGroup LoadFromCsv(CSVParser parser)
        {
            if (parser[0].ToLower() != "min speed")
            {
                Debug.LogError($"AvoidanceAudioGroup LoadFromCSV: incorrect starting line.");
                return null;
            }

            //prepare speed data
            parser.PrepareLine();

            //
            var group = new AvoidanceAudioGroup()
            {
                MinSpeed = FastFloatParser.Parse(parser[0]),
                MaxSpeed = FastFloatParser.Parse(parser[1]),
                MinTimeInRange = FastFloatParser.Parse(parser[2]),
                MaxTimeOutOfRange = FastFloatParser.Parse(parser[3])
            };

            //check next headfer
            parser.PrepareLine();
            if (parser[0].ToLower() != "sample name")
            {
                Debug.LogError($"AvoidanceAudioGroup LoadFromCSV: Expected Sample Name. Were there multiple impact forces specified?");
                return null;
            }

            //read through until next header
            while (!parser.EOF())
            {
                parser.PrepareLine();
                if (IsNewSectionHeader(parser[0]))
                    break;

                group.Samples.Add(new AvoidanceAudioSampleData()
                {
                    Name = parser[0],
                    Volume = FastFloatParser.Parse(parser[1])
                });
            }

            return group;
        }
    }

    public class ImpactAudioGroup
    {
        public float MinImpactForce;
        public List<ImpactAudioSampleData> Samples = new List<ImpactAudioSampleData>();

        public class ImpactAudioSampleData
        {
            public string Name;
            public WAVStream Stream;
            public float Volume = 1f;
            public float PlayDelay = 0.5f;

            public void SetupStream()
            {
                this.Stream = AudioAssetManager.LoadStream("Creature3D", Name);
            }

            public void DisposeStream()
            {
                this.Stream?.Dispose();
                Stream = null;
            }
        }

        public static ImpactAudioGroup LoadFromCsv(CSVParser parser)
        {
            if (parser[0].ToLower() != "min impact force")
            {
                Debug.LogError($"ImpactAudioGroup LoadFromCSV: incorrect starting line.");
                return null;
            }

            //prepare force data
            parser.PrepareLine();

            //
            var group = new ImpactAudioGroup
            {
                MinImpactForce = FastFloatParser.Parse(parser[0])
            };

            //check next headfer
            parser.PrepareLine();
            if (parser[0].ToLower() != "sample name")
            {
                Debug.LogError($"ImpactAudioGroup LoadFromCSV: Expected Sample Name. Were there multiple impact forces specified?");
                return null;
            }

            //read through until next header
            while (!parser.EOF())
            {
                parser.PrepareLine();
                if (IsNewSectionHeader(parser[0]))
                    break;

                group.Samples.Add(new ImpactAudioSampleData()
                {
                    Name = parser[0],
                    Volume = FastFloatParser.Parse(parser[1]),
                    PlayDelay = FastFloatParser.Parse(parser[2])
                });
            }

            return group;
        }
    }

    public void SetEntity(AIEntity entity)
    {
        this.refEntity = entity;
    }

    public void PlayAvoidReaction()
    {
        if (refEntity == null || curAvoidGroupIndex < 0 || IsPlaying)
            return;
        if (AvoidanceGroups[curAvoidGroupIndex].MinTimeInRange > inRangeTimer)
            return;

        //get sample
        var samples = AvoidanceGroups[curAvoidGroupIndex].Samples;
        var chosenSample = samples[Random.Range(0, samples.Count - 1)];

        //kill last stream
        if (lastAvoidSample != chosenSample)
        {
            lastAvoidSample?.DisposeStream();
            chosenSample.SetupStream();
        }

        //play it
        VoiceSource.clip = chosenSample.Stream.Clip;
        VoiceSource.volume = AudioUtils.AdjustVolumeCurve(chosenSample.Volume);
        VoiceSource.Play();
        audioSourceStopTimer = chosenSample.Stream.Clip.length + Time.deltaTime;

        lastAvoidSample = chosenSample;
    }

    public void PlayCollisionReaction(float collisionForce)
    {
        if (ImpactGroups.Count == 0 || IsPlaying)
            return;

        //find group
        int collisionGroupIdx = -1;
        for (int i = 0; i < ImpactGroups.Count; i++)
        {
            if (collisionForce > ImpactGroups[i].MinImpactForce)
                collisionGroupIdx = i;
        }

        //cant find group
        if (collisionGroupIdx < 0)
            return;

        //get sample
        var samples = ImpactGroups[collisionGroupIdx].Samples;
        var chosenSample = samples[Random.Range(0, samples.Count - 1)];

        //kill last stream
        if (lastImpactSample != chosenSample)
        {
            lastImpactSample?.DisposeStream();
            chosenSample.SetupStream();
        }

        //play it
        VoiceSource.clip = chosenSample.Stream.Clip;
        VoiceSource.volume = AudioUtils.AdjustVolumeCurve(chosenSample.Volume);
        VoiceSource.PlayDelayed(chosenSample.PlayDelay);
        audioSourceStopTimer = chosenSample.Stream.Clip.length + chosenSample.PlayDelay + Time.deltaTime;

        lastCollisionGroupIndex = collisionGroupIdx;
        lastImpactSample = chosenSample;
    }

    public void StopAllSounds()
    {
        lastAvoidSample?.DisposeStream();
        lastImpactSample?.DisposeStream();
        if (IsPlaying)
            VoiceSource.Stop();
    }

    private void InitAudioSource()
    {
        GameObject voiceSourceObject = new GameObject("VoiceAudioAudio");
        VoiceSource = MMAudioMixer.CreateAudioSource(voiceSourceObject);
        VoiceSource.spatialBlend = 1f;
        VoiceSource.volume = 0f;
        VoiceSource.pitch = 1;
        VoiceSource.loop = true;
        VoiceSource.minDistance = 20f;
        VoiceSource.maxDistance = 50f;
        VoiceSource.rolloffMode = AudioRolloffMode.Linear;
        VoiceSource.dopplerLevel = 0f;
        VoiceSource.Stop();
        voiceSourceObject.transform.SetParent(this.transform, false);
    }

    private void InitFromParser(CSVParser parser)
    {
        parser.PrepareLine();
        while (!parser.EOF())
        {
            string header = parser[0].ToLower();
            if (header == "min speed")
            {
                AvoidanceGroups.Add(AvoidanceAudioGroup.LoadFromCsv(parser));
            }
            else if (header == "min impact force")
            {
                var group = ImpactAudioGroup.LoadFromCsv(parser);
                ImpactGroups.Add(group);
            }
            else
            {
                Debug.LogError($"VoiceAudio InitFromParser: invalid header {header}");
                break;
            }
        }
    }

    public void InitPedestrian(string name)
    {
        CSVParser parser = null;
        bool isMale = name.ToLower() != "pedmodel_woman" && name.ToLower() != "pedmodel_womanw";

        if (isMale)
        {
            if (AssetManager.Exists("aud", "creaturedata", $"nummalepedvoicefiles.csv"))
            {
                string[] voiceFileData = AssetManager.ReadAllLines($"aud/creaturedata/nummalepedvoicefiles.csv");
                int numVoiceFiles = int.Parse(voiceFileData[1]);

                int fileNumber = Random.Range(1, numVoiceFiles);
                parser = AssetManager.OpenCSV("aud", "creaturedata", $"default_mpedvoice{fileNumber}.csv");
            }
        }
        else
        {
            if (AssetManager.Exists("aud", "creaturedata", $"numfemalepedvoicefiles.csv"))
            {
                string[] voiceFileData = AssetManager.ReadAllLines($"aud/creaturedata/numfemalepedvoicefiles.csv");
                int numVoiceFiles = int.Parse(voiceFileData[1]);

                int fileNumber = Random.Range(1, numVoiceFiles);
                parser = AssetManager.OpenCSV("aud", "creaturedata", $"default_fpedvoice{fileNumber}.csv");
            }
        }

        //
        if (parser == null)
        {
            Debug.LogError($"VoiceAudio InitPedestrian failure! Trying to init {name}.");
        }

        //read things in!
        InitFromParser(parser);

        //done
        InitAudioSource();
    }

    public void InitVehicle(string cityName, string name)
    {
        CSVParser parser = null;

        // get our parser
        string citySuffix = cityName[0].ToString();
        string fallbackCitySuffix = "s";

        if(!AssetManager.Exists("aud", "creaturedata", $"{name}_ambcarvoice_{citySuffix}.csv")
            && !AssetManager.Exists("aud", "creaturedata", $"numambcarvoicefiles_{citySuffix}.csv"))
        {
            citySuffix = fallbackCitySuffix;
        }

        if (AssetManager.Exists("aud", "creaturedata", $"{name}_ambcarvoice_{citySuffix}.csv"))
        {
            parser = AssetManager.OpenCSV("aud", "creaturedata", $"{name}_ambcarvoice_{citySuffix}.csv");
        }
        else if (AssetManager.Exists("aud", "creaturedata", $"numambcarvoicefiles_{citySuffix}.csv"))
        {
            string[] voiceFileData = AssetManager.ReadAllLines($"aud/creaturedata/numambcarvoicefiles_{citySuffix}.csv");
            int numVoiceFiles = int.Parse(voiceFileData[1]);

            int fileNumber = Random.Range(1, numVoiceFiles);
            parser = AssetManager.OpenCSV("aud", "creaturedata", $"default_ambcarvoice_s{fileNumber}.csv");
        }

        //
        if (parser == null)
        {
            Debug.LogError($"VoiceAudio InitVehicle failure! Trying to init {name} with city {cityName}");
            this.enabled = false;
            return;
        }

        //read things in!
        InitFromParser(parser);

        //done
        InitAudioSource();
    }

    private void Dump()
    {
        Debug.Log("==== VOICEAUDIO DUMP ====");
        Debug.Log($"Avoidance Groups {AvoidanceGroups.Count}");
        foreach (var ag in AvoidanceGroups)
        {
            Debug.Log($"\tAvoidance Group MinSpeed:{ag.MinSpeed} MaxSpeed:{ag.MaxSpeed} MinTimeInRange:{ag.MinTimeInRange} MaxTimeOutOfRange:{ag.MaxTimeOutOfRange}");
            foreach (var sample in ag.Samples)
            {
                Debug.Log($"\t\tSample Clip:{(sample.Stream == null ? "null" : sample.Stream.Clip.name)} Volume:{sample.Volume}");
            }
        }

        Debug.Log($"Impact Groups {ImpactGroups.Count}");
        foreach (var ag in ImpactGroups)
        {
            Debug.Log($"\tImpact Group MinForce:{ag.MinImpactForce}");
            foreach (var sample in ag.Samples)
            {
                Debug.Log($"\t\tSample Clip:{(sample.Stream == null ? "null" : sample.Stream.Clip.name)} Volume:{sample.Volume} Delay:{sample.PlayDelay}");
            }
        }
    }

    //
    private int DetermineCurrentGroup()
    {
        if (refEntity == null)
            return -1;
        for (int i = 0; i < AvoidanceGroups.Count; i++)
        {
            if (refEntity.Speed >= AvoidanceGroups[i].MinSpeed && refEntity.Speed <= AvoidanceGroups[i].MaxSpeed)
                return i;
        }
        return -1;
    }

    public void Reset()
    {
        StopAllSounds();
    }

    void Update()
    {
        //update source
        if (IsPlaying && audioSourceStopTimer > 0f)
        {
            audioSourceStopTimer -= Time.unscaledDeltaTime;
            if (audioSourceStopTimer <= 0f)
            {
                VoiceSource.Stop();
            }
        }

        //update current group
        if (curAvoidGroupIndex >= 0)
        {
            bool inRange = refEntity.Speed >= AvoidanceGroups[curAvoidGroupIndex].MinSpeed && refEntity.Speed <= AvoidanceGroups[curAvoidGroupIndex].MaxSpeed;
            if (inRange)
                inRangeTimer += Time.deltaTime;
            if (!inRange)
                outRangeTimer += Time.deltaTime;

            if (inRange || (!inRange && outRangeTimer < AvoidanceGroups[curAvoidGroupIndex].MaxTimeOutOfRange))
                return;
        }

        //find new group
        int curGroup = DetermineCurrentGroup();
        if (curGroup != lastAvoidGroupIndex)
        {
            inRangeTimer = 0f;
            outRangeTimer = 0f;
            lastAvoidGroupIndex = curGroup;
            curAvoidGroupIndex = curGroup;
        }
    }

    private void OnDestroy()
    {
        foreach (var avoidGroup in this.AvoidanceGroups)
        {
            foreach (var sample in avoidGroup.Samples)
                sample.Stream?.Dispose();
        }
        foreach (var impactGroup in this.ImpactGroups)
        {
            foreach (var sample in impactGroup.Samples)
                sample.Stream?.Dispose();
        }
    }

}
