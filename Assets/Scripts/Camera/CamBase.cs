using UnityEngine;

namespace MM2.Camera
{
    public class camBaseCS : MonoBehaviour
    {
        public Vector3 Position
        {
            get { return internalCamera != null ? internalCamera.transform.position : Vector3.zero; }
            set { if (internalCamera != null) internalCamera.transform.position = value; }
        }

        public Quaternion Rotation
        {
            get { return internalCamera != null ? internalCamera.transform.rotation : Quaternion.identity; }
            set { if (internalCamera != null) internalCamera.transform.rotation = value; }
        }

        [SerializeField] protected UnityEngine.Camera internalCamera;

        public float BlendTime = 1.2f;
        public float BlendGoal = 1.0f;
        public float CameraFOV = 50.0f;
        public float CameraNear = 3.0f;
        public float CameraFar = 500.0f;

        public bool ApplyClipPlanes = false;

        protected virtual void Awake()
        {
        }

        public virtual void Init(UnityEngine.Camera cam)
        {
            internalCamera = cam;
            ApplySettings();
        }

        protected virtual void Update()
        {
            ApplySettings();
        }

        protected void ApplySettings()
        {
            if (internalCamera == null)
                return;

            if (ApplyClipPlanes)
            {
                internalCamera.nearClipPlane = CameraNear;
                internalCamera.farClipPlane = Mathf.Max(CameraFar, CameraNear + 0.01f);
            }
            internalCamera.fieldOfView = CameraFOV;
        }

        public virtual void ReadSettings(TokenFileParser parser)
        {
            BlendTime = parser.Read("BlendTime", BlendTime);
            BlendGoal = parser.Read("BlendGoal", BlendGoal);
            CameraFOV = parser.Read("CameraFOV", CameraFOV);
            CameraNear = parser.Read("CameraNear", CameraNear);
            CameraFar = parser.Read("CameraFar", CameraFar);
        }
    }
}