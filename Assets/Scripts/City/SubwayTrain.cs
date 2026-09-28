using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SubwayTrain : MonoBehaviour
{
    public PathFollower EndCarFollower => _trainCarFollowers[_trainCarFollowers.Count - 1];
    public PathFollower StartCarFollower => _trainCarFollowers[0];

    public bool IsEnteringStation => Mathf.Sign(StartCarFollower.Progress - 0.5f) == Mathf.Sign(StartCarFollower.FollowSpeed);

    public bool AtEndOfPath => EndCarFollower.AtEndOfPath || StartCarFollower.AtEndOfPath;
    public float DecelerationDistance = 30f;
    public float DecelerationMinSpeedPercentage = 0.05f;

    private const float TrainSpeed = 40f;

    private List<PathFollower> _trainCarFollowers = new List<PathFollower>();
    private float _trainRatio = 0f;
    private float _trainCarGap = 0.5f;

    private float _resumeCounter = 10f;

    public void PlaceAtStart()
    {
        float seperation = _trainRatio + _trainCarGap;
        for (int i = 0; i < _trainCarFollowers.Count; i++)
        {
            float progress = seperation * i;
            _trainCarFollowers[i].SetProgress(progress);
        }
    }

    public void PlaceAtEnd()
    {
        float seperation =  _trainRatio + _trainCarGap;
        float baseProgress = 1f - (seperation * (_trainCarFollowers.Count - 1));
        for (int i = 0; i < _trainCarFollowers.Count; i++)
        {
            float progress = seperation * i;
            _trainCarFollowers[i].SetProgress(progress + baseProgress);
        }
    }

    public void Init(SDLCity level, string modelName, int carCount, PathSet.Path path)
    {
        //remove unneeded control points from our path
        //and adjust the new first/last point
        path.Points.RemoveAt(0);
        path.Points.RemoveAt(path.Points.Count - 1);

        path.Points[0] += (path.Points[1] - path.Points[0]).normalized * 5f;
        path.Points[path.Points.Count - 1] -=
            (path.Points[path.Points.Count - 1] - path.Points[path.Points.Count - 2]).normalized * 5f;

        path.RecalculateLength();

        //init models
        float trainSize = 1.0f;
        modelName = AssetManager.Exists("geometry", $"{modelName}.pkg") ? modelName : "va_ug_l";
        for (int i = 0; i < carCount; i++)
        {
            var trainObject = new GameObject($"TrainCar{i}");

            //create model
            var bangerInstance = UnhitBangerInstance.RequestBanger(level, modelName, Vector3.zero, Quaternion.identity);
            if (bangerInstance.Data != null)
            {
                trainSize = bangerInstance.Data.Size.z;
            }

            bangerInstance.Flags &= ~(LevelInstanceFlags.Static | LevelInstanceFlags.DisableWhenRoomHidden);
            bangerInstance.Unbreakable = true;
            bangerInstance.gameObject.SetLayer(LayerMask.NameToLayer("Default"), true);

            bangerInstance.gameObject.transform.parent = trainObject.transform;
            
            //add audio if needed
            if (i == carCount / 2)
            {
                trainObject.gameObject.AddComponent<SubwayAudio>().Init(this);
            }

            //add follower
            var modelFollower = trainObject.AddComponent<PathFollower>();
            modelFollower.Path = path;
            modelFollower.FollowSpeed = TrainSpeed;
            modelFollower.InterpolateRotation = true;
            modelFollower.RotationMaxDPS = 22f;
            modelFollower.Looped = false;

            //create rb
            var modelRb = trainObject.AddComponent<Rigidbody>();
            modelRb.isKinematic = true;

            modelFollower.Rigidbody = modelRb;
            modelFollower.UseRigidbody = true;
            
            _trainCarFollowers.Add(modelFollower);
        }

        //precompute ratio stuffs
        _trainRatio = trainSize / path.Length;
        _trainCarGap = _trainCarGap / path.Length;
        foreach (var follower in _trainCarFollowers)
        {
            follower.HeightOffset = 0.0f;
            follower.RotationLookAheadTarget = trainSize / 2.0f;
        }

        //place model
        PlaceAtStart();        
    }

    private void SetTrainEnabled(bool enabled)
    {
        foreach (var car in _trainCarFollowers)
            car.enabled = enabled;
    }

    void SetFollowDirection(float direction)
    {
        float speed = Mathf.Sign(direction);
        foreach (var car in _trainCarFollowers)
        {
            car.FollowSpeed = speed;
        }
    }

    void UpdateCarSpeeds()
    {
        var endCar = StartCarFollower.Progress < 0.5f ? StartCarFollower : EndCarFollower;
        float metersFromEnd = endCar.CalculateMetersFromEnd();

        float speed = Mathf.Clamp(metersFromEnd / DecelerationDistance, DecelerationMinSpeedPercentage, 1f) * TrainSpeed * Mathf.Sign(endCar.FollowSpeed);
        foreach (var car in _trainCarFollowers)
        {
            car.FollowSpeed = speed;
        }
    }

    void Update()
    {
        //update car speeds
        UpdateCarSpeeds();

        //are we at the end of the path?
        if (StartCarFollower.AtEndOfPath || EndCarFollower.AtEndOfPath)
        {
            //snap back to start or end in case we overshot
            if (StartCarFollower.AtEndOfPath)
            {
                PlaceAtStart();
            }
            else
            {
                PlaceAtEnd();
            }

            //disable cars
            if (StartCarFollower.enabled || EndCarFollower.enabled)
                SetTrainEnabled(false);

            //decrement counter
            _resumeCounter -= Time.deltaTime;

            //reenable?
            if (_resumeCounter <= 0)
            {
                SetTrainEnabled(true);
                SetFollowDirection(StartCarFollower.AtEndOfPath ? 1f : -1f);
                _resumeCounter = 10f;
            }
        }
    }
}
