using UnityEngine;

public class MMMirror : MonoBehaviour
{
    public Vector3 Position = new Vector3(0.0f, 1.4f, 1.0f);
    public Vector2 Size = new Vector2(0.3f, 0.16f);
    public float Fov = 10.0f;
    public float Aspect = 2.0f;
    public float NearClip = 1.2f;
    public float FarClip = 100.0f;

    private ViewportManager.Viewport viewport;
    private new Camera camera;
    private VehCar car;

    private void OnDestroy()
    {
        if(viewport != null)
        {
            ViewportManager.DeleteViewport(viewport);
        }
    }

    private void OnEnable()
    {
        if (viewport != null && camera != null)
        {
            viewport.SetActiveCamera(camera);
        }
    }

    private void OnDisable()
    {
        if(viewport != null)
        {
            viewport.DeactivateAllCameras();
        }
    }

    public void Init(VehCar playerCar)
    {
        viewport = ViewportManager.AddViewport("Mirror", Rect.zero, LayerMask.GetMask("Default", "Sky", "Banger", "BangerStatic", "VehicleBody"));
        viewport.ClearColor = Color.black;
        viewport.DefaultDepth = 1;

        car = playerCar;
        camera = viewport.AddCamera();
        camera.gameObject.transform.parent = this.transform;
        camera.gameObject.AddComponent<MirrorCullingFlip>();
        viewport.SetActiveCamera(camera);

        // load the settings
        var node = AssetManager.OpenNode("tune", $"{playerCar.Basename}.mmMirror");
        if(node != null)
        {
            ReadSettings(node);
        }
    }

    private void LateUpdate()
    {
        Transform carTransform = car.transform;

        camera.transform.position = carTransform.position + carTransform.TransformDirection(Position);
        camera.transform.rotation = carTransform.rotation * Quaternion.Euler(0f, 180f, 0f);

        viewport.ScreenRect = new Rect(
            1f - Size.x,
            1f - Size.y,
            Size.x,
            Size.y
        );

        viewport.Aspect = Aspect;
        viewport.NearClip = NearClip;
        viewport.FarClip = FarClip;

        viewport.ApplySettingsToAllCameras(
            ViewportManager.ViewportApplyFlags.Rect |
            ViewportManager.ViewportApplyFlags.Clip |
            ViewportManager.ViewportApplyFlags.Aspect
        );

        camera.fieldOfView = Fov;
        camera.ResetProjectionMatrix();
        camera.projectionMatrix = Matrix4x4.Scale(new Vector3(-1f, 1f, 1f)) * camera.projectionMatrix;
    }

    public virtual void ReadSettings(TokenFileParser parser)
    {
        Position = parser.Read("Position", Position);
        Position.z = -Position.z;

        Size = parser.Read("Size", Size);
        Fov = parser.Read("Fov", Fov);
        Aspect = parser.Read("Aspect", Aspect);
        NearClip = parser.Read("NearClip", NearClip);
        FarClip = parser.Read("FarClip", FarClip);
    }
}