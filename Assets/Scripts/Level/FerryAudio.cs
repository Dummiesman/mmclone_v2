using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class FerryAudio : MonoBehaviour
{
    private float timeBeforeHorn;
    private AudioSource mySource;
    private AudioClip hornClip;

    private void GetNewHornTiming()
    {
        timeBeforeHorn = Random.Range(5f, 10f);
    }

    public void Init(SDLCity level)
    {
        //init audio source
        mySource = MMAudioMixer.CreateAudioSource(this.gameObject);
        mySource.spatialBlend = 1f;
        mySource.minDistance = 0f;
        mySource.maxDistance = 225f;
        mySource.rolloffMode = AudioRolloffMode.Linear;
        mySource.loop = true;
        mySource.clip = AudioAssetManager.LoadClip("Amb3D", "ferryengine");
        mySource.Play();

        //load horn
        hornClip = AudioAssetManager.LoadClip("Amb3D", "ferryhorn");

        //add to manager
        if(level != null)
            level.AudioZoneManager.AddToManager(mySource, AudioZoneManager.Zone.AboveGround);

        //get new timings
        GetNewHornTiming();
    }

    void UpdateHornTimer()
    {
        timeBeforeHorn -= Time.deltaTime;
    }

    void Update()
    {
        //in case our zone manager killed us
        if (mySource.enabled && !mySource.isPlaying)
            mySource.Play();

        //horn
        UpdateHornTimer();
        if (timeBeforeHorn < 0f)
        {
            if(mySource.enabled)
                mySource.PlayOneShot(hornClip);
            GetNewHornTiming();
        }
    }
}
