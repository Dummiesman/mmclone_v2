using System;
using System.Collections.Generic;
using UnityEngine;

public class AmbientAudioGroupMono : MonoBehaviour
{
    private AmbientAudio.AmbientAudioGroup _group;
    private AmbientAudio.AmbientAudioData _currentData;

    private AudioSource _myAudioSource;
    private int _lastAudioIndex = 0;
    private float _timeBeforeOneshot = 999f;
    private bool _oneShotPlayed = false;

    private float _referenceAudioVolume = 1f;

    private Vector3 _lastPlayedPoint = Vector3.zero;
    private bool _hasPlayed = false;


    public void Init(SDLCity city, AmbientAudio.AmbientAudioGroup group)
    {
        _group = group;

        // create my audiosource
        _myAudioSource = MMAudioMixer.CreateAudioSource(this.gameObject);
        _myAudioSource.minDistance = group.MinDistance;
        _myAudioSource.maxDistance = group.MaxDistance;

        _myAudioSource.loop = false;
        _myAudioSource.playOnAwake = false;
        _myAudioSource.Stop();

        city.AudioZoneManager.AddToManager(_myAudioSource, _group.Area);
        
        // setup first wave
        SetWaveDataIndex(0);
    }

    private void SetWaveDataIndex(int index)
    {
        if (index < 0 || index >= _group.WaveData.Count)
            return;

        _currentData = _group.WaveData[index];
        _timeBeforeOneshot = UnityEngine.Random.Range(_currentData.OneshotMinTime, _currentData.OneshotMaxTime);
        _myAudioSource.volume = AudioUtils.AdjustVolumeCurve(_currentData.Volume);
        _myAudioSource.dopplerLevel = _currentData.DopplerLevel;
        _myAudioSource.clip = _currentData.Clip;
        _myAudioSource.panStereo = 0f;
        _lastAudioIndex = index;
        _oneShotPlayed = false;

        // setup curernt type
        if (_currentData.Type == AmbientAudio.AmbientAudioData.AudioMode.PanVol2DOnce)
        {
            _myAudioSource.spatialBlend = 0f;
            CalculatePanVolume(UnityEngine.Random.Range(0f, 4f));
        }else if (_currentData.Type == AmbientAudio.AmbientAudioData.AudioMode.PanVol2D)
        {
            _myAudioSource.spatialBlend = 0f;
        }
        else
        {
            _myAudioSource.spatialBlend = 1f;
        }
    }

    private void PlaySound()
    {
        var activeCameraPos = ViewportManager.MainViewport.ActiveCamera != null
            ? ViewportManager.MainViewport.ActiveCamera.transform.position
            : Vector3.zero;

        float closestPointDistance = float.MaxValue;
        Vector3 closestPoint = Vector3.zero;

        // find closest point
        foreach (var point in _group.Points)
        {
            float distance = (point - activeCameraPos).sqrMagnitude;
            if (distance < closestPointDistance)
            {
                closestPoint = point;
                closestPointDistance = distance;
            }
        }

        // don't play if out of range
        closestPointDistance = Mathf.Sqrt(closestPointDistance);
        if (closestPointDistance > _group.MaxDistance)
            return;
        _referenceAudioVolume = 1f - Mathf.Clamp01((closestPointDistance - _group.MinDistance) / (_group.MaxDistance - _group.MinDistance));

        // go here and play
        _myAudioSource.gameObject.transform.position = closestPoint;
        _lastPlayedPoint = closestPoint;
        _hasPlayed = true;
        _myAudioSource.Play();
    }

    private int GetNextRandomIndex()
    {
        if (_group.WaveData.Count <= 1)
        {
            return 0;
        }

        int index = _lastAudioIndex;
        while (index == _lastAudioIndex)
        {
            index = UnityEngine.Random.Range(0, _group.WaveData.Count);
        }
        return index;
    }

    private void CalculatePanVolume(float timeBase)
    {
        float timeOffset = Mathf.Floor((timeBase % 4f)) / 4f;
        _myAudioSource.panStereo = (timeOffset - 0.5f) * -2f;

        float pan01 = 1f - Mathf.Abs(_myAudioSource.panStereo);
        float amp = Mathf.Lerp(0.5f, 1f, pan01);

        _myAudioSource.volume = AudioUtils.AdjustVolumeCurve(_currentData.Volume) * _referenceAudioVolume * amp;
    }

    void Update()
    {
        _timeBeforeOneshot -= Time.deltaTime;

        // stuff
        if (_currentData.Type == AmbientAudio.AmbientAudioData.AudioMode.PanVol2D)
        {
            CalculatePanVolume(Time.timeSinceLevelLoad);
        }

        if (_myAudioSource.isPlaying)
        {
            var activeCameraPos = ViewportManager.MainViewport.ActiveCamera != null
                ? ViewportManager.MainViewport.ActiveCamera.transform.position
                : Vector3.zero;

            // stop playing if out of range
            if (Vector3.Distance(activeCameraPos,
                    this.transform.position) > _group.MaxDistance)
            {
                _myAudioSource.Stop();
            }
        }

        // play
        if (_timeBeforeOneshot < 0f && !_oneShotPlayed)
        {
            if(_myAudioSource.enabled)
                PlaySound();
            _oneShotPlayed = true;
        }

        // schedule next switch
        if (_myAudioSource.clip == null || _timeBeforeOneshot <= -_myAudioSource.clip.length)
        {
            SetWaveDataIndex(GetNextRandomIndex());
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (_group == null)
            return;

        //  all points + ranges
        foreach (var point in _group.Points)
        {
            Gizmos.color = new Color(0f, 1f, 1f, 1f);
            Gizmos.DrawSphere(point, 1f);

            Gizmos.color = new Color(0f, 1f, 0f, 0.35f);
            Gizmos.DrawWireSphere(point, _group.MinDistance);

            Gizmos.color = new Color(1f, 0.5f, 0f, 0.25f);
            Gizmos.DrawWireSphere(point, _group.MaxDistance);
        }

        //  last fired point
        if (_hasPlayed)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawSphere(_lastPlayedPoint, 2f);
            Gizmos.DrawWireSphere(_lastPlayedPoint, _group.MinDistance);
            Gizmos.DrawWireSphere(_lastPlayedPoint, _group.MaxDistance);

            var cam = ViewportManager.MainViewport != null && ViewportManager.MainViewport.ActiveCamera != null
                ? ViewportManager.MainViewport.ActiveCamera.transform.position
                : Vector3.zero;
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(_lastPlayedPoint, cam);

            string label = _currentData != null ? _currentData.WaveName : "?";
#if UNITY_EDITOR
            UnityEditor.Handles.color = Color.white;
            UnityEditor.Handles.Label(_lastPlayedPoint + Vector3.up * 3f,
                $"{_group.FileName}\n{label} [{_currentData?.Type}]\nvol {_myAudioSource?.volume:F2} pan {_myAudioSource?.panStereo:F2}\nnext in {_timeBeforeOneshot:F2}s\nplaying: {(_myAudioSource != null && _myAudioSource.isPlaying)}");
#endif
        }
    }
}

public class AmbientAudio : IDisposable
{ 
   
    public readonly List<AmbientAudioGroup> AmbientGroups = new List<AmbientAudioGroup>();

    public class AmbientAudioData
    {
        public enum AudioMode
        {
            Normal,
            PanVol2D,
            PanVol2DOnce
        }

        public string WaveName;
        public float Volume;
        public AudioMode Type;
        public float OneshotMinTime;
        public float OneshotMaxTime;
        public bool Active;
        public float MinSpeed;
        public float MaxSpeed;
        public float DopplerLevel;
        public AudioClip Clip;
    }

    public class AmbientAudioGroup
    {
        public string FileName;
        public float MinDistance;
        public float MaxDistance;
        public AudioZoneManager.Zone Area;
        public List<AmbientAudioData> WaveData = new List<AmbientAudioData>();
        public List<Vector3> Points = new List<Vector3>();
    }
	
    //  Use this for initialization
    public void LoadAmbientFile(string file)
    {
        if (!AssetManager.Exists("aud/ambient", $"{file}.csv"))
            return;

        var ambientFile = AssetManager.OpenCSV("aud/ambient", file);
        ambientFile.Seek(1, System.IO.SeekOrigin.Current);
        ambientFile.PrepareLine();

        var group = new AmbientAudioGroup();
        group.FileName = file;
        group.MinDistance = FastFloatParser.Parse(ambientFile[0]);
        group.MaxDistance = FastFloatParser.Parse(ambientFile[1]);
        group.Area = (AudioZoneManager.Zone)FastIntParser.Parse(ambientFile.GetToken(3));

        // read wavedata's
        ambientFile.Seek(1, System.IO.SeekOrigin.Current);
        while (!ambientFile.EOF())
        {
            ambientFile.PrepareLine();
            if (ambientFile.GetToken(0).ToUpper() == "VECTORPOINTS")
                break;

            var data = new AmbientAudioData
            {
                WaveName = ambientFile.GetToken(0),
                Volume = FastFloatParser.Parse(ambientFile.GetToken(1)),
                Type = (AmbientAudioData.AudioMode)FastIntParser.Parse(ambientFile.GetToken(2)),
                OneshotMinTime = FastFloatParser.Parse(ambientFile.GetToken(3)),
                OneshotMaxTime = FastFloatParser.Parse(ambientFile.GetToken(4)),
                Active = FastIntParser.Parse(ambientFile.GetToken(5)) > 0,
                MinSpeed = FastFloatParser.Parse(ambientFile.GetToken(6)),
                MaxSpeed = FastFloatParser.Parse(ambientFile.GetToken(7)),
                DopplerLevel = FastFloatParser.Parse(ambientFile.GetToken(8))
            };

            data.Clip = AudioAssetManager.LoadClip("Amb3D", data.WaveName);
            group.WaveData.Add(data);
        }

        // read points
        ambientFile.Seek(1, System.IO.SeekOrigin.Current); // seek past x,y,z  header
        while (!ambientFile.EOF())
        {
            ambientFile.PrepareLine();
            float x = FastFloatParser.Parse(ambientFile.GetToken(0));
            float y = FastFloatParser.Parse(ambientFile.GetToken(1));
            float z = FastFloatParser.Parse(ambientFile.GetToken(2));
            group.Points.Add(new Vector3(x, y, z).ConvertCoordinateSpace());
        }

        // add to list
        AmbientGroups.Add(group);
    }

	public void Init (SDLCity city, string cityName) 
    {
        // load ambient container
        if(!AssetManager.Exists("aud", "ambient", $"{cityName}ambientcontainer.csv")){
            Debug.LogWarning($"No ambient audio file for {cityName} exists.");
	        return;
	    }

	    var ambientContainer = AssetManager.OpenCSV("aud/ambient", cityName + "ambientcontainer");
        ambientContainer.Seek(1, System.IO.SeekOrigin.Current); // skip header

        while (!ambientContainer.EOF())
        {
            ambientContainer.PrepareLine();
            string fileName = ambientContainer[0];

            if(!string.IsNullOrEmpty(fileName))
                LoadAmbientFile(fileName);
        }

        // initialize gameobjects
        foreach(var group in AmbientGroups)
        {
            var groupObject = new GameObject($"AmbientAud:{group.FileName}");
            groupObject.AddComponent<AmbientAudioGroupMono>().Init(city, group);
        }
	}

    public void Dispose()
    {
        foreach (var group in AmbientGroups)
        {
            foreach (var data in group.WaveData)
            {
                if (data.Clip != null)
                {
                    UnityEngine.Object.Destroy(data.Clip);
                    data.Clip = null;
                }
            }
        }

        AmbientGroups.Clear();
    }
}