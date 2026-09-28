using System;
using UnityEngine;

public class FollowTarget : MonoBehaviour
{
    public Transform Target;
    public Vector3 Offset = new Vector3(0f, 7.5f, 0f);

    public bool FollowPosition = true;
    public bool FollowRotation = true;
    public Vector3 PositionMultiplier = new Vector3(1f, 1f, 1f);
    public Vector3 RotationMultiplier = new Vector3(1f, 1f, 1f);
    public Axes PositionAxes = new Axes();
    public Axes RotationAxes = new Axes();

    public class Axes
    {
        public bool x = true;
        public bool y = true;
        public bool z = true;
    }

    private void LateUpdate()
    {
        if (Target == null)
            return;


        if (FollowPosition)
        {
            Vector3 position = transform.position;
            if (PositionAxes.x)
                position.x = Target.position.x * PositionMultiplier.x;
            if (PositionAxes.y)
                position.y = Target.position.y * PositionMultiplier.y;
            if (PositionAxes.z)
                position.z = Target.position.z * PositionMultiplier.z;
            transform.position = position + Offset;
        }

        if (FollowRotation)
        {
            Vector3 rotation = transform.rotation.eulerAngles;
            if (RotationAxes.x)
                rotation.x = Target.rotation.x * RotationMultiplier.x;
            if (RotationAxes.y)
                rotation.y = Target.rotation.y * RotationMultiplier.y;
            if (RotationAxes.z)
                rotation.z = Target.rotation.z * RotationMultiplier.z;
            transform.eulerAngles = rotation;
        }
    }
}

