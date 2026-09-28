using UnityEngine;

namespace UnityStandardAssets.Utility
{
    public class SmoothFollow : MonoBehaviour
    {
        // The target we are following
        [SerializeField]
        public Transform target;

        // The distance in the x-z plane to the target
        [SerializeField]
        public float distance = 10.0f;

        // The height we want the camera to be above the target
        [SerializeField]
        public float height = 5.0f;

        [SerializeField]
        public float rotationDamping;

        [SerializeField]
        public float heightDamping;

        [SerializeField]
        public Vector3 constantRotation;

        // Follow from in front instead of behind
        [SerializeField]
        public bool invert = true;

        void Start() { }

        void Update()
        {
            // Early out if we don't have a target
            if (!target)
                return;

            // Calculate the current rotation angles
            var wantedRotationAngle = target.eulerAngles.y + (invert ? 180.0f : 0.0f);
            var wantedHeight = target.position.y + height;

            var currentRotationAngle = transform.eulerAngles.y;
            var currentHeight = transform.position.y;

            // Damp the rotation around the y-axis
            currentRotationAngle = Mathf.LerpAngle(
                currentRotationAngle,
                wantedRotationAngle,
                rotationDamping * Time.deltaTime
            );

            // Damp the height
            currentHeight = Mathf.Lerp(
                currentHeight,
                wantedHeight,
                heightDamping * Time.deltaTime
            );

            // Convert the angle into a rotation
            var currentRotation = Quaternion.Euler(0, currentRotationAngle, 0);

            // Set the position of the camera on the x-z plane
            transform.position = target.position;
            transform.position -= currentRotation * Vector3.forward * distance;

            // Set the height of the camera
            transform.position = new Vector3(
                transform.position.x,
                currentHeight,
                transform.position.z
            );

            // Always look at the target
            transform.LookAt(target);

            transform.RotateAround(
                target.transform.position,
                Vector3.up,
                constantRotation.y
            );
        }
    }
}