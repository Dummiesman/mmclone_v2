using UnityEngine;

public class RainAudio : MonoBehaviour
{
    private AudioSource interiorSource;
    private AudioSource exteriorSource;
    private AudioSource thunder1source;
    private AudioSource thunder2source;

    private int flashState; // 0 = no flash, 1 = flash triggered this frame, 2 = flash held
    private float thunderTimer;
    private bool thunderPlayed;

    private bool sheltered = false;
    private bool interior = false;

    public void SetInterior(bool value)
    {
        if (interior == value)
            return;

        interior = value;
        ApplyInterior();
    }

    private void ApplyInterior()
    {
        if (exteriorSource == null || interiorSource == null)
            return;

        if (interior)
        {
            if (!interiorSource.isPlaying)
                interiorSource.Play();
            if (exteriorSource.isPlaying)
                exteriorSource.Stop();
        }
        else
        {
            if (!exteriorSource.isPlaying)
                exteriorSource.Play();
            if (interiorSource.isPlaying)
                interiorSource.Stop();
        }
    }

    public void ShelterOff()
    {
        if (sheltered)
        {
            exteriorSource.volume = AudioUtils.AdjustVolumeCurve(0.76f);
            interiorSource.volume = AudioUtils.AdjustVolumeCurve(0.83f);

            if (thunder1source != null)
            {
                thunder1source.volume = AudioUtils.AdjustVolumeCurve(0.85f);
                thunder1source.panStereo = -0.2f;
            }
            if (thunder2source != null)
            {
                thunder2source.volume = AudioUtils.AdjustVolumeCurve(0.85f);
                thunder2source.panStereo = 0.2f;
            }
        }
        sheltered = false;
    }

    public void ShelterOn()
    {
        if (!sheltered)
        {
            exteriorSource.volume = AudioUtils.AdjustVolumeCurve(0.65f);
            interiorSource.volume = AudioUtils.AdjustVolumeCurve(0.0f);

            if (thunder1source != null)
            {
                thunder1source.volume = AudioUtils.AdjustVolumeCurve(0.85f);
                thunder1source.panStereo = 0.0f;
            }
            if (thunder2source != null)
            {
                thunder2source.volume = AudioUtils.AdjustVolumeCurve(0.85f);
                thunder2source.panStereo = 0.0f;
            }
        }
        sheltered = true;
    }

    public void Init()
    {
        exteriorSource = MMAudioMixer.CreateAudioSource(this.gameObject);
        exteriorSource.clip = AudioAssetManager.LoadClip("Rainexterior");
        exteriorSource.spatialBlend = 0.0f;
        exteriorSource.volume = AudioUtils.AdjustVolumeCurve(0.82f);
        exteriorSource.loop = true;

        interiorSource = MMAudioMixer.CreateAudioSource(this.gameObject);
        interiorSource.clip = AudioAssetManager.LoadClip("Raininterior");
        interiorSource.spatialBlend = 0.0f;
        interiorSource.volume = AudioUtils.AdjustVolumeCurve(0.85f);
        interiorSource.loop = true;

        if (GameState.SelectedTimeOfDay == MMTimeOfDay.Night)
        {
            thunder1source = MMAudioMixer.CreateAudioSource(this.gameObject);
            thunder1source.clip = AudioAssetManager.LoadClip("Thunder");
            thunder1source.spatialBlend = 0.0f;
            thunder1source.panStereo = -0.2f;
            thunder1source.volume = AudioUtils.AdjustVolumeCurve(0.85f);
            thunder1source.Stop();

            thunder2source = MMAudioMixer.CreateAudioSource(this.gameObject);
            thunder2source.clip = AudioAssetManager.LoadClip("Thunder");
            thunder2source.spatialBlend = 0.0f;
            thunder2source.panStereo = 0.2f;
            thunder2source.volume = AudioUtils.AdjustVolumeCurve(0.85f);
            thunder2source.Stop();
        }

        ApplyInterior();
    }

    void Update()
    {
        if (thunder1source == null || thunder2source == null)
        {
            thunderTimer = 0.0f;
            flashState = 0;
            return;
        }

        if (flashState == 0 && thunderTimer > 13.0f)
            flashState = 1;
        else if (flashState == 1)
            flashState = 2;

        if (thunderTimer >= 15.0f && !thunderPlayed)
        {
            if (!thunder1source.isPlaying)
                thunder1source.Play();

            thunderPlayed = true;
            thunderTimer = 0.0f;
        }

        if (thunderTimer > 1.0f && thunderPlayed)
        {
            if (!thunder2source.isPlaying)
                thunder2source.Play();

            thunderPlayed = false;
            thunderTimer = 0.0f;
            flashState = 0;
        }

        thunderTimer += Time.deltaTime;
    }
}
