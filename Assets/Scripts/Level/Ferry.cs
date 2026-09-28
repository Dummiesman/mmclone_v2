using MM2.AI;
using UnityEngine;

public class Ferry : MonoBehaviour
{
    private FerryInstance instance;
    private PathFollower follower;
    private new FerryAudio audio;

    public void Init(string name, SDLCity level, PathSet.Path path)
    {
        float speed = 0.75f;

        var ferryInstance = FerryInstance.RequestFerry(level, name, Vector3.zero, Quaternion.identity);
        instance = ferryInstance;

        ferryInstance.gameObject.transform.parent = this.transform;

        // add follower if the path is more than two points
        // if not, place this statically
        if (path.Type == PathSet.Path.PathType.Lines && path.Points.Count == 2)
        {
            Vector3 p0 = path.Points[0];
            Vector3 p1 = path.Points[1];

            transform.position = p0;
            transform.forward = (p1 - p0).normalized;

            var blp = ferryInstance.gameObject.transform.localPosition;
            blp.x = 0.0f;
            blp.z = 0.0f;
            ferryInstance.gameObject.transform.localPosition = blp;
        }
        else
        {
            follower = gameObject.AddComponent<PathFollower>();
            follower.FollowSpeed = speed;
            follower.InterpolateRotation = true;
            follower.RotationMaxDPS = 360.0f;
            follower.Path = path;
        }

        audio = gameObject.AddComponent<FerryAudio>();
        audio.Init(level);
    }
}
