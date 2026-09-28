using Dummiesman.Wave;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public class SpeechAudio : MonoBehaviour
{
    /// <summary>
    /// Voice ID used for speech folders that have no numbered variants
    /// (Crash Course "ccs" / "ccl").
    /// </summary>
    public const int NoVoice = -1;

    private class SpeechCategory
    {
        public string name;
        public List<WAVStream> clips = new List<WAVStream>();

        private int[] clipOrderShuffled;
        private int currentShuffledClipIndex = 0;

        private void InitShuffleArray()
        {
            clipOrderShuffled = new int[clips.Count];

            for (int i = 0; i < clips.Count; i++)
            {
                clipOrderShuffled[i] = i;
            }

            ShuffleArray();
        }

        private void ShuffleArray()
        {
            for (int i = 0; i < clipOrderShuffled.Length; i++)
            {
                int idx0 = Random.Range(0, clips.Count);
                int idx1 = Random.Range(0, clips.Count);

                int value0 = clipOrderShuffled[idx0];
                int value1 = clipOrderShuffled[idx1];

                clipOrderShuffled[idx1] = value0;
                clipOrderShuffled[idx0] = value1;
            }
        }

        public int GetRandomUnplayedClipIndex()
        {
            if (clipOrderShuffled == null)
            {
                InitShuffleArray();
            }

            int index = clipOrderShuffled[currentShuffledClipIndex++];

            if (currentShuffledClipIndex >= clips.Count)
            {
                currentShuffledClipIndex = 0;
                ShuffleArray();
            }

            return index;
        }

        public void DisposeStreams()
        {
            foreach (var clip in clips)
            {
                clip?.Dispose();
            }

            clips.Clear();
            clipOrderShuffled = null;
            currentShuffledClipIndex = 0;
        }
    }

    private AudioSource speechSource;

    private readonly Dictionary<string, SpeechCategory> speechCategories =
        new Dictionary<string, SpeechCategory>();

    private string initCity;
    private string initVehicle;
    private string initPrefix;
    private int initVoice = NoVoice;
    private MMGameMode initMode;
    private MMTimeOfDay initTime;
    private MMWeather initWeather;
    private int initRace = -1;

    private void Awake()
    {
        speechSource = gameObject.AddComponent<AudioSource>();
        speechSource.spatialBlend = 0f;
        speechSource.pitch = 1.0f;
        speechSource.volume = 1.0f;
    }

    private void OnDestroy()
    {
        Unload();
    }

    public void PlayCat(string cat)
    {
        PlayCat(cat, -1);
    }

    public void PlayCat(string cat, int index)
    {
        SpeechCategory spchCat;

        if (!speechCategories.TryGetValue(cat, out spchCat))
        {
            Debug.LogWarning($"Tried to play nonexistant speech category {cat}.. Voice {this.initPrefix}{this.initVoice}");
            return;
        }

        if (spchCat.clips.Count == 0)
        {
            Debug.LogWarning($"Tried to play an empty speech category {cat}");
            return;
        }

        // Pick a random clip to play if -1 is set.
        if (index < 0)
        {
            index = spchCat.GetRandomUnplayedClipIndex();
        }

        if (index >= spchCat.clips.Count)
        {
            Debug.LogWarning(
                $"Speech clip index {index} is out of range for category {cat}."
            );
            return;
        }

        // Stop if we're already playing something.
        if (speechSource.isPlaying)
            speechSource.Stop();

        // Play.
        speechSource.PlayOneShot(spchCat.clips[index].Clip);
    }

    public void PlayResults(int finishPosition, int numOpponents)
    {
        if (finishPosition == 1)
        {
            PlayResultsWin();
        }
        else if (finishPosition - 1 <= 0 || finishPosition - 1 >= numOpponents / 2)
        {
            PlayResultsPoor();
        }
        else
        {
            PlayResultsMid();
        }
    }

    public void PlayResultsMid()
    {
        PlayResultsCategory("RESULTSMID", "VEHICLERESULTSMID");
    }

    public void PlayResultsPoor()
    {
        PlayResultsCategory("RESULTSPOOR", "VEHICLERESULTSPOOR");
    }

    public void PlayResultsWin()
    {
        PlayResultsCategory("RESULTSWIN", "VEHICLERESULTSWIN");
    }

    private void PlayResultsCategory(string normalCategory, string vehicleCategory)
    {
        string category = normalCategory;

        if (Random.Range(0f, 10f) > 5f)
        {
            SpeechCategory spchCat;

            if (speechCategories.TryGetValue(vehicleCategory, out spchCat) &&
                spchCat.clips.Count > 0)
            {
                category = vehicleCategory;
            }
        }

        PlayCat(category);
    }

    public void PlayFinalCheckpoint()
    {
        PlayCat("FINALCHECKPOINT");
    }

    public void PlayPreRace()
    {
        string category;
        float roll = Random.Range(0f, 11.5f);

        if (roll <= 7.5f)
        {
            category = "PRERACE";
        }
        else if (roll <= 8.5f)
        {
            category = "TIMEOFDAY";
        }
        else if (roll <= 9.5f)
        {
            category = "WEATHER";
        }
        else
        {
            category = "VEHICLEPRERACE";

            // Fall back to normal pre-race speech if there
            // are no vehicle-specific pre-race clips.
            SpeechCategory spchCat;
            if (!speechCategories.TryGetValue(category, out spchCat) ||
                spchCat.clips.Count == 0)
            {
                category = "PRERACE";
            }
        }

        PlayCat(category);
    }

    public void PlayVehicleUnlock()
    {
        PlayCat("UNLOCKVEHICLE");
    }

    public void PlayTextureUnlock()
    {
        PlayCat("UNLOCKTEXTURE");
    }

    public void PlayDamagePenalty()
    {
        PlayCat("DAMAGEPENALTY");
    }

    public int GetNumVoiceVariants(string city)
    {
        string[] info = AssetManager.ReadAllLines("aud/spchdata/" + city + ".csv");

        if (info == null || info.Length < 2)
            return 0;

        return FastIntParser.Parse(info[1]);
    }

    public string GetCityPrefix(string city)
    {
        if (!AssetManager.Exists("aud", "spchdata", $"{city}.csv"))
        {
            Debug.LogWarning("Could not GetCityPrefix for " + city + ".");
            return string.Empty;
        }

        string[] info = AssetManager.ReadAllLines($"aud/spchdata/{city}.csv");
        return info[3].Clean();
    }

    /// <summary>
    /// Crash Course uses its own instructor voice, stored in an unnumbered
    /// folder: "ccs" for San Francisco, "ccl" for London.
    /// Returns an empty string for cities with no Crash Course voice.
    /// </summary>
    public static string GetCrashCoursePrefix(string city)
    {
        if (string.IsNullOrEmpty(city))
            return string.Empty;

        switch (city.Trim().ToLowerInvariant())
        {
            case "sf":
                return "ccs";

            case "london":
                return "ccl";

            default:
                return string.Empty;
        }
    }

    /// <summary>
    /// Csv name for a single Crash Course lesson, e.g. ("ccs", 1) -> "ccs1".
    /// </summary>
    public static string GetCrashCourseCsv(string prefix, int raceNumber)
    {
        return $"{prefix}{raceNumber}";
    }

    /// <summary>
    /// Folder name under aud/spchdata for a given prefix + voice.
    /// Voices below zero (Crash Course) have no number appended.
    /// </summary>
    private static string GetVoiceFolder(string prefix, int voice)
    {
        return voice < 0 ? prefix : $"{prefix}{voice}";
    }

    public bool LoadTextureUnlock(string vehicleName)
    {
        return LoadGroup($"{vehicleName}_texture");
    }

    public bool LoadVehicleUnlock(string vehicleName)
    {
        return LoadGroup($"{vehicleName}_unlock");
    }

    public bool LoadGroup(string csv)
    {
        return LoadGroup(csv, initVoice);
    }

    public bool LoadGroup(string csv, int voice)
    {
        return TryLoadCatData(initPrefix, voice, csv);
    }

    /// <summary>
    /// Loads a category csv only if it exists. Returns false instead of
    /// logging an error when it doesn't, so optional data (weather, vehicle,
    /// Crash Course extras) can be probed safely.
    /// </summary>
    public bool TryLoadCatData(string prefix, int voice, string csv)
    {
        if (string.IsNullOrEmpty(prefix))
            return false;

        string audioFolder = GetVoiceFolder(prefix, voice);

        if (!AssetManager.Exists("aud", "spchdata", audioFolder, $"{csv}.csv"))
            return false;

        LoadCatData(prefix, voice, csv);
        return true;
    }

    public void LoadCatData(string prefix, int voice, string csv)
    {
        string audioFolder = GetVoiceFolder(prefix, voice);
        string csvPath = $"aud/spchdata/{audioFolder}";
        string csvFullPath = $"{csvPath}/{csv}.csv";

        if (!AssetManager.Exists(csvFullPath))
        {
            Debug.LogError(
                $"LoadCatData failed because {csvFullPath} doesn't exist."
            );
            return;
        }

        var csvReader = AssetManager.OpenCSV(csvPath, csv);

        string currentCategory = string.Empty;

        // Skip header.
        csvReader.Seek(1, SeekOrigin.Current);

        while (!csvReader.EOF())
        {
            csvReader.PrepareLine();

            string category = csvReader.GetToken(0);

            if (category.EndsWith(" header", System.StringComparison.Ordinal))
            {
                // New category.
                currentCategory =
                    category.Substring(0, category.LastIndexOf(' '));

                // Add to dictionary if it isn't there already.
                if (!speechCategories.ContainsKey(currentCategory))
                {
                    speechCategories[currentCategory] =
                        new SpeechCategory()
                        {
                            name = currentCategory
                        };
                }

                continue;
            }

            // We don't have a category header, we have data.
            string wavPrefix = csvReader.GetToken(0);

            SpeechCategory catData =
                speechCategories[currentCategory];

            int numSamples =
                FastIntParser.Parse(csvReader.GetToken(1));

            for (int i = 1; i <= numSamples; i++)
            {
                // Numbered voices repeat the folder name in the file name
                // (al2/AL2Pre01). Unnumbered folders (ccs, ccl) usually
                // don't, so try the likely name first and fall back.
                string plainName = $"{wavPrefix}{i:00}";
                string folderName = $"{audioFolder}{wavPrefix}{i:00}";

                string firstTry = voice < 0 ? plainName : folderName;
                string secondTry = voice < 0 ? folderName : plainName;

                string audioPath = $"{audioFolder}/{firstTry}";

                if (Application.isEditor || Debug.isDebugBuild)
                {
                    Debug.Log("SpeechAudio loading:" + audioPath);
                }

                WAVStream clip = AudioAssetManager.LoadStream(audioPath);

                if (clip == null)
                {
                    clip = AudioAssetManager.LoadStream(
                        $"{audioFolder}/{secondTry}"
                    );
                }

                if (clip != null)
                {
                    catData.clips.Add(clip);
                }
            }
        }
    }

    public bool SpeechDataExists(string prefix, int voice)
    {
        return AssetManager.Exists(
            "aud",
            "spchdata",
            $"{GetVoiceFolder(prefix, voice)}/"
        );
    }

    public void Unload()
    {
        // Stop playback.
        if (speechSource != null)
        {
            speechSource.Stop();
        }

        // Release streams.
        foreach (var cat in speechCategories.Values)
        {
            cat.DisposeStreams();
        }

        speechCategories.Clear();
    }

    public void Init(
        string city,
        string currentVehicle,
        MMGameMode mode,
        MMTimeOfDay time,
        MMWeather weather,
        int voice = NoVoice,
        int raceNumber = -1)
    {
        // Release current resources if they exist.
        Unload();

        string prefix = string.Empty;
        bool crashCourse = mode == MMGameMode.CrashCourse;

        if (crashCourse)
        {
            // Crash Course has a single dedicated instructor voice per city
            // in an unnumbered folder ("ccs" / "ccl"), so ignore the
            // requested voice entirely.
            prefix = GetCrashCoursePrefix(city);
            voice = NoVoice;

            if (raceNumber < 0)
            {
                Debug.LogWarning(
                    "Crash Course speech needs a race number; none was passed."
                );
                return;
            }

            if (string.IsNullOrEmpty(prefix) ||
                !SpeechDataExists(prefix, NoVoice))
            {
                Debug.LogWarning(
                    $"No Crash Course speech data for {city} " +
                    $"(prefix \"{prefix}\"), falling back to city voices."
                );

                prefix = string.Empty;
                crashCourse = false;
            }
        }

        if (!crashCourse)
        {
            // Get prefix and voice ID.
            prefix = GetCityPrefix(city);

            if (string.IsNullOrEmpty(prefix))
            {
                // No speech.
                Debug.LogWarning(
                    "GetCityPrefix returned NULL for " + city + "."
                );
                return;
            }

            // Pick random voice if specified.
            if (voice < 0)
            {
                bool isMinInstall = !AssetManager.Exists("aud", "aud11", "al2", "AL2Pre01.11k.wav");
                if (isMinInstall)
                {
                    // without audex only one voice is installed
                    voice = 1;
                }
                else
                {
                    bool foundVoice = false;
                    while (!foundVoice)
                    {
                        int numVoices = GetNumVoiceVariants(city);

                        if (numVoices <= 0)
                            return;

                        voice = Random.Range(0, numVoices) + 1;
                        foundVoice = SpeechDataExists(prefix, voice);
                    }
                }
            }
        }

        // Store for later
        initCity = city;
        initVehicle = currentVehicle;
        initPrefix = prefix;
        initVoice = voice;
        initMode = mode;
        initTime = time;
        initWeather = weather;
        initRace = crashCourse ? raceNumber : -1;

        if (crashCourse)
        {
            // Crash Course has one csv per lesson (ccs/ccs01, ccl/ccl04, ...)
            // and nothing else: no vehicle, weather or time-of-day chatter.
            string raceCsv = GetCrashCourseCsv(prefix, raceNumber);

            if (!TryLoadCatData(prefix, voice, raceCsv))
            {
                Debug.LogWarning(
                    $"Crash Course speech csv {prefix}/{raceCsv}.csv is missing."
                );
            }

            return;
        }

        // Vehicle specific.
        TryLoadCatData(prefix, voice, $"{currentVehicle}_prerace");
        TryLoadCatData(prefix, voice, $"{currentVehicle}_results");

        // Weather specific.
        switch (weather)
        {
            case MMWeather.Clear:
                LoadCatData(prefix, voice, "weaclr_prerace");
                break;

            case MMWeather.Cloudy:
                LoadCatData(prefix, voice, "weacldy_prerace");
                break;

            case MMWeather.Foggy:
                LoadCatData(prefix, voice, "weafog_prerace");
                break;

            case MMWeather.Raining:
                LoadCatData(prefix, voice, "wearain_prerace");
                break;
        }

        // Time specific.
        switch (time)
        {
            case MMTimeOfDay.Morning:
                LoadCatData(prefix, voice, "timemorn_prerace");
                break;

            case MMTimeOfDay.Noon:
                LoadCatData(prefix, voice, "timenoon_prerace");
                break;

            case MMTimeOfDay.Evening:
                LoadCatData(prefix, voice, "timeeve_prerace");
                break;

            case MMTimeOfDay.Night:
                LoadCatData(prefix, voice, "timenight_prerace");
                break;
        }

        // Mode specific.
        if (mode == MMGameMode.Blitz)
        {
            LoadCatData(prefix, voice, "blitz");
            LoadCatData(prefix, voice, "racedamage");
        }
        else if (mode == MMGameMode.Circuit)
        {
            LoadCatData(prefix, voice, "circuit");
            LoadCatData(prefix, voice, "racedamage");
        }
        else if (mode == MMGameMode.Checkpoint)
        {
            LoadCatData(prefix, voice, "checkpoint");
            LoadCatData(prefix, voice, "racedamage");
        }
        else
        {
            LoadCatData(prefix, voice, "cruise");
            LoadCatData(prefix, voice, "cruisedamage");
        }
    }
}