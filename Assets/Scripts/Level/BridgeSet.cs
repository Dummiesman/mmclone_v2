using MM2.AI;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class BridgeSet : MonoBehaviour 
{
    public static Vector3 BRIDGE_OFFSET = new Vector3(0, -0.3f, 0);

    public static readonly float MAX_ANGLE_DEG = -27.0f;
    private static readonly float MAX_STATE_TIME = 10.0f;
    private static readonly float MAX_OPEN_SPEED = 2.864789f;

    // public stuff
    public List<GameObject> BridgeObjects = new List<GameObject>();

    // private stuff
    private SDLCity level;

    private bool currentlyClosed = true;
    private float currentStateTime = 0f;

    private bool isMoving = false;
    private float moveDirection = -1f;
    private float xAngle = 0f;

    // audio things
    private AudioSource bridgeAudio;
    private AudioSource bellAudio;

    public void InitAudio()
    {
        if (!enabled)
            return;

        var bridgeAudioSettings = new AmbientAudio();
        bridgeAudioSettings.LoadAmbientFile("drawbridge");

        var bridgeAudioSettingsGroup = bridgeAudioSettings.AmbientGroups.FirstOrDefault();
        if (bridgeAudioSettingsGroup == null)
            return;

        if (bridgeAudioSettingsGroup.WaveData.Count > 0)
        {
            bridgeAudio = MMAudioMixer.CreateAudioSource(this.gameObject);
            bridgeAudio.loop = true;
            bridgeAudio.playOnAwake = false;
            bridgeAudio.clip = AudioAssetManager.LoadClip("Amb3D", $"{bridgeAudioSettingsGroup.WaveData[0].WaveName}");
            bridgeAudio.spatialBlend = 1.0f;
            bridgeAudio.minDistance = bridgeAudioSettingsGroup.MinDistance;
            bridgeAudio.maxDistance = bridgeAudioSettingsGroup.MaxDistance;
            bridgeAudio.dopplerLevel = 0;
            bridgeAudio.rolloffMode = AudioRolloffMode.Linear;
        }

        if (bridgeAudioSettingsGroup.WaveData.Count > 1)
        {
            bellAudio = MMAudioMixer.CreateAudioSource(this.gameObject);
            bellAudio.loop = false;
            bellAudio.playOnAwake = false;
            bellAudio.clip = AudioAssetManager.LoadClip("Amb3D", $"{bridgeAudioSettingsGroup.WaveData[1].WaveName}");
            bellAudio.spatialBlend = 1.0f;
            bellAudio.minDistance = bridgeAudioSettingsGroup.MinDistance;
            bellAudio.maxDistance = bridgeAudioSettingsGroup.MaxDistance;
            bellAudio.dopplerLevel = 0;
            bellAudio.rolloffMode = AudioRolloffMode.Linear;
        }

        if(level != null)
        {
            if (bridgeAudio != null) level.AudioZoneManager.AddToManager(bridgeAudio, bridgeAudioSettingsGroup.Area);
            if (bellAudio != null) level.AudioZoneManager.AddToManager(bellAudio, bridgeAudioSettingsGroup.Area);
            level.AudioZoneManager.OnZoneChanged += ZoneChangeCb;
        }
    }

    void OnDestroy()
    {
        if (level != null)
        {
            if (bridgeAudio != null) level.AudioZoneManager.RemoveFromManager(bridgeAudio);
            if (bellAudio != null) level.AudioZoneManager.RemoveFromManager(bellAudio);
            level.AudioZoneManager.OnZoneChanged -= ZoneChangeCb;
        }
    }

    private void ZoneChangeCb(AudioZoneManager.Zone newZone)
    {
        //re-trigger bridge audio if needed
        if(isMoving && bridgeAudio != null && bridgeAudio.clip != null && !bridgeAudio.isPlaying)
        {
            ActivateBridgeAudio();
        }
    }

    public void Init(SDLCity level, PathSet.Path path)
    {
        this.level = level;

        // offset points
        path.Offset(BRIDGE_OFFSET);

        // get bridge details
        string bridgeDescription = path.Name;
        string bridgeStartState = string.Empty;
        string bridgeModel = bridgeDescription;
        if (bridgeDescription.Contains(":"))
        {
            string[] splits = bridgeDescription.Split(':');
            bridgeStartState = splits[0].ToUpper();
            bridgeModel = splits[1];
        }

        // get a banger entry
        int bangerId = level.BangerDataManager.AddEntry(bridgeModel);
        if (bangerId < 0)
        {
            Debug.LogWarning($"BridgeSet.CreateFromPath failed, could not load create bridge from description \"{bridgeDescription}\".");
            return;
        }

        // get some info from the path
        Vector3 pathCenter = Vector3.Lerp(path.Points[0], path.Points[path.Points.Count - 1], 0.5f);

        // create parent + component
        GameObject bridgeParent = this.gameObject;
        BridgeSet bridgeComponent = this;
        bridgeParent.transform.position = pathCenter;

        // start placing bridges
        var bridgeBangerData = level.BangerDataManager.GetEntry(bangerId);
        bool flipDirection = false; //defines what way the bridge will face
        for (int i = 0; i < path.Points.Count - 1; i++)
        {
            Vector3 start = path.Points[i];
            Vector3 end = path.Points[i + 1];

            Vector3 bridgePos = flipDirection ? Vector3.MoveTowards(end, start, bridgeBangerData.Size.z / 2f) : 
                                                Vector3.MoveTowards(start, end, bridgeBangerData.Size.z / 2f);

            Debug.DrawLine(bridgePos, bridgePos + (Vector3.up * 10.0f), (flipDirection) ? Color.red : Color.yellow, 1000.0f);

            //create pivot
            GameObject bridgePivot = new GameObject("BridgePivot");
            bridgePivot.transform.parent = bridgeParent.transform;
            bridgePivot.transform.position = flipDirection ? end : start;
            bridgePivot.transform.LookAt(pathCenter);

            //create model instance
            Vector3 localPos = bridgePivot.transform.InverseTransformPoint(bridgePos) + new Vector3(bridgeBangerData.CG.x, -bridgeBangerData.CG.y, bridgeBangerData.CG.z);
            Vector3 propPos = bridgePivot.transform.TransformPoint(localPos);
            Quaternion propRot = bridgePivot.transform.rotation * Quaternion.Euler(0f, 180f, 0f);

            var bridgeBanger = BridgeInstance.RequestBridge(level, bridgeModel, propPos, propRot);
            level.MoveToRoom(bridgeBanger, level.FindRoomIdWithWarps(propPos));
            bridgeBanger.transform.parent = bridgePivot.transform;

            if (string.IsNullOrEmpty(bridgeStartState))
            {
                bridgeComponent.BridgeObjects.Add(bridgePivot);
            }
            else if (bridgeStartState == "OPEN")
            {
                bridgePivot.transform.Rotate(BridgeSet.MAX_ANGLE_DEG, 0, 0);
            }

            flipDirection = !flipDirection;
        }

        // remove offset in case something re-uses this
        path.Offset(-BRIDGE_OFFSET);

        // only animated bridges need Update/audio
        enabled = string.IsNullOrEmpty(bridgeStartState);
    }

    /// <summary>
    /// Turns on the bell audio, and sets it to loop
    /// </summary>
    private void ActivateBellAudio()
    {
        if (bellAudio == null)
            return;
        if (bellAudio.enabled)
        {
            bellAudio.loop = true;
            bellAudio.Play();
        }
    }

    /// <summary>
    /// Stops the bell audio at the end of it's current loop
    /// </summary>
    private void DeactivateBellAudio()
    {
        if (bellAudio == null)
            return;
        bellAudio.loop = false;
    }


    /// <summary>
    /// Turns on the bridge movement audio
    /// </summary>
    private void ActivateBridgeAudio()
    {
        if (bridgeAudio == null)
            return;
        if (bridgeAudio.enabled)
        {
            bridgeAudio.time = 0;
            bridgeAudio.Play();
        }
    }

    /// <summary>
    /// Stops the bridge movement audio
    /// </summary>
    private void DeactivateBridgeAudio()
    {
        if (bridgeAudio == null)
            return;
        bridgeAudio.Stop();
    }

    //update functions for different move states
    void UpdateInanimate()
    {
        currentStateTime += Time.deltaTime;
        if (currentStateTime >= MAX_STATE_TIME)
        {
            isMoving = true;
            moveDirection = currentlyClosed ? -1 : 1;
            ActivateBridgeAudio();
        }
    }

    void UpdateAnimate()
    {
        xAngle += (MAX_OPEN_SPEED * moveDirection) * Time.deltaTime;

        if (xAngle < MAX_ANGLE_DEG || xAngle >= 0)
        {
            //clamp back to one of our values
            xAngle = Mathf.Clamp(xAngle, MAX_ANGLE_DEG, 0);

            //stop!
            isMoving = false;
            currentStateTime = 0f;
            currentlyClosed = !currentlyClosed;
            DeactivateBridgeAudio();
            DeactivateBellAudio();
        }

        foreach (var bridgeObj in BridgeObjects)
        {
            var bridgeEuler = bridgeObj.transform.localEulerAngles;
            bridgeObj.transform.localEulerAngles = new Vector3(xAngle, bridgeEuler.y, bridgeEuler.z);
        }
    }

    void Update () {
        //add current st ate time, start moving if we have to
        if (!isMoving)
        {
            UpdateInanimate();
        }
        else
        {
            UpdateAnimate();
        }

        //bell audio trigger
        if (bellAudio == null || bellAudio.clip == null)
            return;
        if(currentStateTime >= (MAX_STATE_TIME - (bellAudio.clip.length * 1.5f)) && !bellAudio.isPlaying)
        {
            ActivateBellAudio();
        }
    }
}
