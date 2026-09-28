using UnityEngine;
using UnityEngine.Audio;

public static class MMAudioMixer
{
    //statics
    private static AudioMixer mixer;
    private static AudioMixerGroup mixerGroup;
    private static AudioMixer musicMixer;

    //mute state
    private const string MasterVolumeParam = "MasterVolume";
    private const float MutedVolumeDb = -80f;
    private static float volumeBeforeMute = 0f;
    public static bool IsMuted { get; private set; }

    public static float MusicVolume
    {
        get
        {
            EnsureMusicMixerExists();
            if (musicMixer == null) return 0f;

            return musicMixer.GetFloat(MasterVolumeParam, out float db) ? DbToLinear(db) : 0f;
        }
        set
        {
            EnsureMusicMixerExists();
            if (musicMixer == null) return;

            musicMixer.SetFloat(MasterVolumeParam, LinearToDb(value));
        }
    }

    public static float Volume
    {
        get
        {
            EnsureMixerExists();
            if (mixer == null) return 0f;

            // while muted the mixer sits at -80dB, so report the cached value instead
            if (IsMuted) return DbToLinear(volumeBeforeMute);

            return mixer.GetFloat(MasterVolumeParam, out float db) ? DbToLinear(db) : 0f;
        }
        set
        {
            EnsureMixerExists();
            if (mixer == null) return;

            float db = LinearToDb(value);

            if (IsMuted)
            {
                volumeBeforeMute = db;   // applied on Unmute
                return;
            }

            mixer.SetFloat(MasterVolumeParam, db);
        }
    }

    private static float LinearToDb(float linear)
    {
        linear = Mathf.Clamp01(linear);
        return linear <= 0.0001f ? MutedVolumeDb : Mathf.Log10(linear) * 20f;
    }

    private static float DbToLinear(float db)
    {
        return db <= MutedVolumeDb ? 0f : Mathf.Clamp01(Mathf.Pow(10f, db / 20f));
    }

    public static void EchoOn()
    {
        EnsureMixerExists();
        if (mixer == null) return;

        mixer.SetFloat("EchoDryMix", 1.0f);
        mixer.SetFloat("EchoWetMix", 0.5f);
    }

    public static void EchoOff()
    {
        EnsureMixerExists();
        if (mixer == null) return;

        mixer.SetFloat("EchoDryMix", 1.0f);
        mixer.SetFloat("EchoWetMix", 0.0f);
    }

    public static void Mute()
    {
        EnsureMixerExists();
        if (mixer == null || IsMuted) return;

        if (mixer.GetFloat(MasterVolumeParam, out float current))
            volumeBeforeMute = current;

        mixer.SetFloat(MasterVolumeParam, MutedVolumeDb);
        IsMuted = true;
    }

    public static void Unmute()
    {
        EnsureMixerExists();
        if (mixer == null || !IsMuted) return;

        mixer.SetFloat(MasterVolumeParam, volumeBeforeMute);
        IsMuted = false;
    }

    public static void ToggleMute()
    {
        if (IsMuted) Unmute();
        else Mute();
    }

    public static void EnsureMusicMixerExists()
    {
        if (musicMixer != null)
            return;

        musicMixer = Resources.Load<AudioMixer>("Mixers/DirectMusicMixer");
        if (musicMixer == null)
            Debug.LogError("Failed to initialize music mixer!");
    }

    public static void EnsureMixerExists()
    {
        if (mixer != null)
            return;

        mixer = Resources.Load<AudioMixer>("Mixers/MainMixer");
        if (mixer == null)
        {
            Debug.LogError("Failed to initialize audio mixer!");
            return;
        }

        var groups = mixer.FindMatchingGroups("Master");
        if (groups.Length == 0)
        {
            Debug.LogError("Failed to find audio mixer group!");
            mixer = null;
            return;
        }

        mixerGroup = groups[0];
    }

    //Helpers
    public static AudioSource CreateAudioSource(GameObject onObject)
    {
        EnsureMixerExists();

        var source = onObject.AddComponent<AudioSource>();
        source.outputAudioMixerGroup = mixerGroup;
        return source;
    }
}