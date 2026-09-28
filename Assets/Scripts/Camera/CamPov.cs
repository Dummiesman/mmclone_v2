using MM2.Camera;
using UnityEngine;

/// <summary>
/// First-person / bumper (POV) camera. Port of camPovCS.
/// Places the camera at a fixed offset in the car's local space, applies pitch
/// and an optional yaw (look-back / look-around), then hands off to ApproachIt().
/// </summary>
public class camPovCS : camCarCS
{
    // ---- Settings (FileIO) ------------------------------------------------
    public Vector3 Offset = new Vector3(0f, 1.6f, 0.7f);
    public Vector3 ReverseOffset = new Vector3(0f, 1.7f, 0.75f);
    public float Pitch;          // radians
    public float POVJitterAmp;

    // ---- Runtime state ----------------------------------------------------
    public Vector3 ResetTargetPosition; // 0x130-0x138: target position captured on Reset()
    public float YawOffset;           // 0x13C: yaw applied when not reversing (radians)
    public float LookYawOverride;     // 0x144: if non-zero, overrides the yaw entirely (radians)

    private bool IsReversing => ReverseMode == -1; // original: ReverseMode == 0xFFFFFFFF

    // Was the C++ constructor. MonoBehaviours shouldn't use constructors.
    protected override void Awake()
    {
        base.Awake();

        // camBaseCS / camAppCS / camCarCS fields overridden by this camera
        BlendTime = 1.2f;
        BlendGoal = 1.0f;
        CameraFOV = 60f;
        CameraNear = 3f;
        CameraFar = 1600f;      // static sm_cameraFar in the original

        AppRot = 28f;
        AppXZPos = 28f;
        AppYPos = 28f;
        ApproachOn = true;
        AppAppOn = true;
        AppRotMin = 0f;
        AppPosMin = 0f;
        AppApp = 0.7f;
        MinDist = 1.74f;
        MaxDist = 1.8f;
        LookAt = 0f;
    }

    public void UpdatePOV()
    {
        bool reversing = IsReversing;

        // Offset in car space -> world space.
        Vector3 offset = reversing ? ReverseOffset : Offset;
        Vector3 position = Target.TransformPoint(offset);
        Quaternion rotation = Target.rotation;

        // Pitch about the camera's own X axis.
        float pitch = reversing ? -Pitch : Pitch;
        if (pitch != 0f)
            rotation *= Quaternion.AngleAxis(pitch * Mathf.Rad2Deg, Vector3.right);

        // Yaw about the (already pitched) Y axis: 180 when reversing, else YawOffset,
        // with LookYawOverride taking priority when set.
        float yaw = reversing ? Mathf.PI : YawOffset;
        if (LookYawOverride != 0f)
            yaw = LookYawOverride;
        if (yaw != 0f)
            rotation *= Quaternion.AngleAxis(yaw * Mathf.Rad2Deg, Vector3.up);

        Position = position;
        Rotation = rotation;
        ApproachIt();
    }

    // Was camPovCS::Update. Driven from camBaseCS.LateUpdate so it runs after
    // all movement/animation for the frame.
    protected override void Update()
    {
        UpdatePOV();
    }

    public override void ReadSettings(TokenFileParser parser)
    {
        Offset = parser.Read("Offset", Offset);
        ReverseOffset = parser.Read("ReverseOffset", ReverseOffset);
        Pitch = parser.Read("Pitch", Pitch);
        POVJitterAmp = parser.Read("POVJitterAmp", POVJitterAmp);
        base.ReadSettings(parser);
    }
}