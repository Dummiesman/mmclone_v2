using UnityEngine;

public class CableCarAudio : MonoBehaviour
{
    private AudioClip goBellClip;
    private AudioClip intersectionBellClip;

    private AudioClip stopClip;
    private AudioClip startClip;

    private AudioClip cablecarLoopClip;

    // Loop lives on its own source so one-shots (start/stop/bells) never affect isPlaying
    // or get cut off when the loop is stopped.
    private AudioSource loopSource;
    private AudioSource sfxSource;

    private bool running;

    /// True between Play() and Stop(), including while the loop is still pending
    /// behind the start clip.
    public bool IsPlaying => running;

    public void Init()
    {
        //load clips
        startClip = AudioAssetManager.LoadClip("cablecarstart");
        stopClip = AudioAssetManager.LoadClip("cablecarstop");

        cablecarLoopClip = AudioAssetManager.LoadClip("cablecar");

        intersectionBellClip = AudioAssetManager.LoadClip("cablecarbell2");
        goBellClip = AudioAssetManager.LoadClip("cablecargobell");

        //create sources
        loopSource = CreateSource("CableCarAudio_Loop");
        loopSource.volume = AudioUtils.AdjustVolumeCurve(0.98f);
        loopSource.clip = cablecarLoopClip;
        loopSource.loop = true;

        sfxSource = CreateSource("CableCarAudio_Sfx");
        sfxSource.volume = AudioUtils.AdjustVolumeCurve(0.98f);
        sfxSource.loop = false;

        loopSource.Stop();
        running = false;
    }

    private AudioSource CreateSource(string name)
    {
        GameObject soundObj = new GameObject(name);
        AudioSource source = MMAudioMixer.CreateAudioSource(soundObj);

        source.spatialBlend = 1f;
        source.volume = AudioUtils.AdjustVolumeCurve(0.98f);
        source.pitch = 1;
        source.minDistance = 20f;
        source.maxDistance = 50f;
        source.rolloffMode = AudioRolloffMode.Linear;
        source.dopplerLevel = 0.2f;

        soundObj.transform.SetParent(this.transform, false);
        return source;
    }

    public void Play()
    {
        if (running)
            return;
        running = true;

        if (startClip != null)
        {
            sfxSource.PlayOneShot(startClip);
            loopSource.PlayDelayed(startClip.length * 0.71f); // delay this so it blends smoothly
        }
        else
        {
            loopSource.Play(); // just play, start clip is null
        }
    }

    public void Stop()
    {
        if (!running)
            return;
        running = false;

        loopSource.Stop(); // also cancels a loop still pending from PlayDelayed
        if (stopClip != null)
            sfxSource.PlayOneShot(stopClip);
    }

    public void PlayStartBell()
    {
        if (goBellClip != null)
            sfxSource.PlayOneShot(goBellClip);
    }

    public void PlayIntersectionEntryBell()
    {
        if (intersectionBellClip != null)
            sfxSource.PlayOneShot(intersectionBellClip);
    }

    private void OnDisable()
    {
        // Deactivated/pooled: go silent without playing the stop clip.
        running = false;
        if (loopSource != null) loopSource.Stop();
        if (sfxSource != null) sfxSource.Stop();
    }
}