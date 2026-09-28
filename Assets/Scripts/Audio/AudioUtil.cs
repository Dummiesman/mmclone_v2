using UnityEngine;

public static class AudioUtils
{
    // Converts DirectSound audio volume to gain
    public static float AdjustVolumeCurve(float volume)
    {
        float dsVol = (volume - 1.0f) * 10000.0f;
        float dB = dsVol / 100.0f;
        float gain = Mathf.Pow(10.0f, dB / 20.0f);
        return gain;
    }
}