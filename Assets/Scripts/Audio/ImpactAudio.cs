using UnityEngine;

public class ImpactAudio : MonoBehaviour
{
    private AudioSource oneShotSource;

    public void Init(bool isThreeD)
    {
        var go = new GameObject("CollisionAudioSource");
        go.transform.parent = this.transform;
        oneShotSource = MMAudioMixer.CreateAudioSource(go);
        oneShotSource.volume = 1.0f;
        oneShotSource.pitch = 1.0f;
        oneShotSource.transform.parent = this.transform;
        oneShotSource.spatialBlend = (isThreeD) ? 1.0f : 0.0f;

        ImpactAudioDataManager.LoadImpacts();
    }

    private void Set3D(Vector3 location)
    {
        oneShotSource.transform.position = location;
        oneShotSource.minDistance = 10f;
        oneShotSource.maxDistance = 50f;
        oneShotSource.spatialBlend = 1f;
    }

    private void Set2D()
    {
        oneShotSource.spatialBlend = 0f;
    }

    public void Collision3D(int colliderId, float magnitude, Vector3 position)
    {
        bool returnTo2D = oneShotSource.spatialBlend == 0f;
        Set3D(position);

        Collision(colliderId, magnitude);

        if (returnTo2D)
            Set2D();
    }

    public void Collision(int colliderId, float magnitude)
    {
        if (!ImpactAudioDataManager.TryGetData(colliderId, out var collisionSounds))
        {
            Debug.LogWarning($"CollisionAudioManager: ColliderId {colliderId} hasn't been loaded, or failed to load. Sound was not played");
            return;
        }

        //
        foreach (var impactSound in collisionSounds)
        {
            //can we make a sound?
            double timeSinceLastPlayed = Time.timeAsDouble - impactSound.LastPlayTime;
            if (magnitude < impactSound.MinForce || magnitude > impactSound.MaxForce || timeSinceLastPlayed < impactSound.Frequency)
            {
                continue;
            }

            //we can make a sound!
            float volume = Mathf.Lerp(impactSound.MinVolume, impactSound.MaxVolume, (magnitude - impactSound.MinForce) / (impactSound.MaxForce - impactSound.MinForce));
            volume = AudioUtils.AdjustVolumeCurve(volume);
            oneShotSource.PlayOneShot(impactSound.Clip, volume);

            //reset timer
            impactSound.LastPlayTime = Time.timeAsDouble;
        }
    }

    public void Collision(int colliderId, Collision collision)
    {
        float impulseMag = collision.impulse.magnitude;
        Collision(colliderId, impulseMag);
    }
}
