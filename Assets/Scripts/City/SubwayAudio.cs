using UnityEngine;

public class SubwayAudio : MonoBehaviour
{
    private SubwayTrain train;
    private AudioSource trackSource;
    private AudioSource squealSource;

    public void Init(SubwayTrain train)
    {
        //set train ref
        this.train = train;

        //init audio sources
        trackSource = MMAudioMixer.CreateAudioSource(this.gameObject);
        trackSource.spatialBlend = 1f;
        trackSource.minDistance = 0f;
        trackSource.maxDistance = 150f;
        trackSource.rolloffMode = AudioRolloffMode.Linear;
        trackSource.loop = true;
        trackSource.clip = AudioAssetManager.LoadClip("Amb3D", "LondonTube");
        trackSource.Play();

        squealSource = MMAudioMixer.CreateAudioSource(this.gameObject);
        squealSource.spatialBlend = 1f;
        squealSource.minDistance = 0f;
        squealSource.maxDistance = 150f;
        squealSource.rolloffMode = AudioRolloffMode.Linear;
        squealSource.loop = true;
        squealSource.clip = AudioAssetManager.LoadClip("Amb3D", "TubeSquel");
        squealSource.volume = 0f;
        squealSource.Play();

        //add to audio zone manager
        var city = SDLCity.Instance;
        if (city != null)
        {
            city.AudioZoneManager.AddToManager(trackSource, AudioZoneManager.Zone.Subterranean);
            city.AudioZoneManager.AddToManager(squealSource, AudioZoneManager.Zone.Subterranean);
            city.AudioZoneManager.OnZoneChanged += ZoneChangeCb;
        }                                                                           
    }

    void OnDestroy()
    {
        var city = SDLCity.Instance;
        if (city != null)
        {
            city.AudioZoneManager.RemoveFromManager(trackSource);
            city.AudioZoneManager.RemoveFromManager(squealSource);
            city.AudioZoneManager.OnZoneChanged -= ZoneChangeCb;
        }
    }

    void ZoneChangeCb(AudioZoneManager.Zone newZone)
    {
        //in case our zone manager killed us
        if (trackSource.enabled && !trackSource.isPlaying)
        {
            trackSource.Play();
            squealSource.Play();
        }
    }

    // Update is called once per frame
    void Update()
    {
        //volumes
        float squealVolume = 0f;
        float trackVolume = 1f;

        float trainSpeed = Mathf.Abs(train.StartCarFollower.FollowSpeed);
        if (train.AtEndOfPath)
        {
            trackVolume = 0f;
        }
        else if (trainSpeed < 40f && train.IsEnteringStation)
        {
            if (trainSpeed < 5f)
            {
                squealVolume = trainSpeed / 5f;
                trackVolume = squealVolume;
            }
            else if (trainSpeed > 35f)
            {
                squealVolume = 1f - (Mathf.Max(0f, trainSpeed - 35) / 5f);
                trackVolume = 1f - squealVolume;
            }
            else
            {
                squealVolume = 1f;
                trackVolume = 1f - squealVolume;
            }
        }

        //set volumes
        if(squealSource.volume != squealVolume)
            squealSource.volume = squealVolume;
        if(trackSource.volume != trackVolume)
            trackSource.volume = trackVolume;
    }
}
