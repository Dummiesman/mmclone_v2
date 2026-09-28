using UnityEngine;

public class Sailboat : MonoBehaviour
{
    private GizmoInstance instance;
    private PathFollower follower; 
    
    public void Init(string name, SDLCity level, PathSet.Path path)
    {
        float speed = ((Random.value * 2f) - 1.0f) + path.Spacing; // spacing contains speed, 1.0f random variation

        follower = gameObject.AddComponent<PathFollower>();
        follower.FollowSpeed = speed;
        follower.InterpolateRotation = true;
        follower.RotationMaxDPS = 720.0f;
        follower.Path = path;

        var instanceObj = new GameObject("SailboatInstance");
        instanceObj.transform.parent = this.transform;
        instance = instanceObj.AddComponent<GizmoInstance>();

        instance.Init(level, name);
        instance.Init(level, name, Vector3.zero, Quaternion.identity, Vector3.one);
        instance.SetVariant(Random.Range(0, instance.VariantCount));

        follower.HeightOffset = 0.0f;
    }
}
