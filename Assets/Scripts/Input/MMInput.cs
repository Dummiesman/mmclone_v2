using UnityEngine;

public class MMInput : MonoBehaviour
{
    public bool InvertPedals = false;
    public MMControllerType Mode { get; private set; } = MMControllerType.Keyboard;

    // Contro v alues
    public float SteeringInput { get; private set; }
    public float ThrottleInput { get; private set; }
    public float BrakeInput { get; private set; }
    public float HandbrakeInput { get; private set; }
    public bool Horn
    {
        get
        {
            return GetHorn();
        }
    }

    [Header("Mobile")]
    public MobileRacingUI MobileInputUI;
    public bool ForceMobileInput = false;
    public bool FilterMobileSteering = true;
    [Tooltip("1 = linear. Higher = softer near center, sharper near full lock.")]
    [SerializeField, Range(1f, 4f)] private float MobileSteerExponent = 2.2f;

    [Tooltip("Input below this is treated as zero.")]
    [SerializeField, Range(0f, 0.2f)] private float MobileSteerDeadzone = 0.05f;

    private VehCar vehicle;
    private PlayerConfig config;
    private PlayerInputConfig inputConfig => config.Input;

    // Input defs
    public static readonly MMInputDefinition[] InputDefinitions =
    {
        new(LocString.InputActionChangeCamera,     MMInputID.ChangeCamera,      InputDefinitionFlags.Trigger),
        new(LocString.InputActionThrillCamera,     MMInputID.ThrillCamera,      InputDefinitionFlags.Trigger),
        new(LocString.InputActionTransmisison,     MMInputID.Transmission,      InputDefinitionFlags.Trigger),
        new(LocString.InputActionHorn,             MMInputID.Horn,              InputDefinitionFlags.Analog),
        new(LocString.InputActionThrottle,         MMInputID.Throttle,          InputDefinitionFlags.Analog),
        new(LocString.InputActionBrakes,           MMInputID.Brakes,            InputDefinitionFlags.Analog),
        new(LocString.InputActionSteering,         MMInputID.Steering,          InputDefinitionFlags.FullAxis),
        new(LocString.InputActionSteerLeft,        MMInputID.SteerLeft,         InputDefinitionFlags.HalfAxis),
        new(LocString.InputActionSteerRight,       MMInputID.SteerRight,        InputDefinitionFlags.HalfAxis),
        new(LocString.InputActionLookRight,        MMInputID.LookRight,         InputDefinitionFlags.Analog),
        new(LocString.InputActionLookLeft,         MMInputID.LookLeft,          InputDefinitionFlags.Analog),
        new(LocString.InputActionLookBack,         MMInputID.LookBack,          InputDefinitionFlags.Analog),
        new(LocString.InputActionLookForward,      MMInputID.LookForward,       InputDefinitionFlags.Analog),
        new(LocString.InputActionWideAngle,        MMInputID.WideAngle,         InputDefinitionFlags.Trigger),
        new(LocString.InputActionDashboard,        MMInputID.Dashboard,         InputDefinitionFlags.Trigger),
        new(LocString.InputActionShiftUp,          MMInputID.ShiftUp,           InputDefinitionFlags.Trigger),
        new(LocString.InputActionShiftDown,        MMInputID.ShiftDown,         InputDefinitionFlags.Trigger),
        new(LocString.InputActionReverse,          MMInputID.Reverse,           InputDefinitionFlags.Trigger),
        new(LocString.InputActionNextCheckpoint,   MMInputID.NextCheckpoint,    InputDefinitionFlags.Trigger),
        new(LocString.InputActionPrevCheckpoint,   MMInputID.PrevCheckpoint,    InputDefinitionFlags.Trigger),
        new(LocString.InputActionMapToggle,        MMInputID.MapToggle,         InputDefinitionFlags.Trigger),
        new(LocString.InputActionHUDToggle,        MMInputID.HUDToggle,         InputDefinitionFlags.Trigger),
        new(LocString.InputActionFullScreenMap,    MMInputID.FullScreenMap,     InputDefinitionFlags.Trigger),
        new(LocString.InputActionMapZoom,          MMInputID.MapZoom,           InputDefinitionFlags.Trigger),
        new(LocString.InputActionRotatingMap,      MMInputID.RotatingMap,       InputDefinitionFlags.Trigger),
        new(LocString.InputActionToggleCDPlayer,   MMInputID.ToggleCDPlayer,    InputDefinitionFlags.Trigger),
        new(LocString.InputActionStartStopCD,      MMInputID.StartStopCD,       InputDefinitionFlags.Trigger),
        new(LocString.InputActionNextCDTrack,      MMInputID.NextCDTrack,       InputDefinitionFlags.Trigger),
        new(LocString.InputActionPrevCDTrack,      MMInputID.PrevCDTrack,       InputDefinitionFlags.Trigger),
        new(LocString.InputActionRearViewMirror,   MMInputID.RearViewMirror,    InputDefinitionFlags.Trigger),
        new(LocString.InputActionCameraPan,        MMInputID.CameraPan,         InputDefinitionFlags.FullAxis),
        new(LocString.InputActionHandbrake,        MMInputID.Handbrake,         InputDefinitionFlags.HalfAxis),
        new(LocString.InputActionOpponentPosition, MMInputID.OpponentPosition,  InputDefinitionFlags.Trigger),
        new(LocString.InputActionChatMessage,      MMInputID.ChatMessage,       InputDefinitionFlags.Trigger),
    };

    // Filtering
    private readonly MMApproachInputFilter JoyFilter = new MMApproachInputFilter();
    private readonly MMApproachInputFilter WheelFilter = new MMApproachInputFilter();
    private readonly MMMouseInputFilter MouseFilter = new MMMouseInputFilter();
    private readonly MMDiscreteInputFilter DiscreteFilter = new MMDiscreteInputFilter();

    private bool inputEnabled = true;
    private bool lastHornState;
    private bool isMobile;

    private bool GetHorn()
    {
        return (MobileInputUI != null) ? MobileInputUI.HornHeld : Input.GetKey(KeyCode.Return);
    }

    public void SetMode(MMControllerType mode)
    {
        Mode = mode;
    }

    public void DisableVehicleInput()
    {
        inputEnabled = false;
    }

    public void EnableVehicleInput()
    {
        inputEnabled = true;
    }

    public void Disable()
    {
        inputEnabled = false;
    }

    public void Enable()
    {
        inputEnabled = true;
    }

    private void UpdateHorn()
    {
        bool currentHornState = Horn;

        // Legacy Input has no InputAction performed/released event,
        // so detect the transition manually.
        bool hornButtonReleased =
            !currentHornState && lastHornState;

        lastHornState = currentHornState;

        
        if (vehicle.Siren.LightCount > 0)
        {
            if (hornButtonReleased)
            {
                bool active = vehicle.Siren.enabled;
                if (!active)
                {
                    vehicle.Siren.Activate();
                    vehicle.Audio.ActivateSiren();
                }
                else
                {
                    vehicle.Siren.Deactivate();
                    vehicle.Audio.DeactivateSiren();
                }
            }
        }
        else
        {
            vehicle.Audio.UpdateHorn(currentHornState);
        }
    }

    private void ApplyInput()
    {
        // todo: move to MM model of pop event
        var sim = vehicle.VehCarSim;
        sim.SetInputs(
            SteeringInput,
            ThrottleInput,
            BrakeInput,
            HandbrakeInput);
    }

    /// <summary>
    /// Pushes the resolved inputs into the sim.
    /// horizontalValue uses the same convention as the desktop paths:
    /// positive == "steer right pressed", inverted below before it reaches the sim.
    /// </summary>
    private void SetInputs(
        float horizontalValue,
        float accel,
        float brake,
        bool handbrake)
    {
        if (InvertPedals)
        {
            float temp = accel;
            accel = brake;
            brake = temp;
        }

        SteeringInput = horizontalValue;
        ThrottleInput = accel;
        BrakeInput = brake;
        HandbrakeInput = handbrake ? 1.0f : 0.0f;
        ApplyInput();
    }

    /// <summary>
    /// Touch + tilt driving. Bypasses Mode entirely.
    /// Right half of the screen = accelerate, left half = brake, device roll = steer.
    /// </summary>
    private void UpdateMobile()
    {
        if (MobileInputUI == null) return;

        float accel = MobileInputUI.Accelerator;
        float brake = MobileInputUI.Brake;
        float steer = MobileInputUI.Steering;
        bool handbrake = MobileInputUI.Handbrake > 0.5f;

        if (FilterMobileSteering)
        {
            float sign = Mathf.Sign(steer);
            float mag = Mathf.Abs(steer);

            // Remove deadzone, then rescale so the remaining range still reaches 1.
            mag = Mathf.InverseLerp(MobileSteerDeadzone, 1f, mag);

            steer = sign * Mathf.Pow(mag, MobileSteerExponent);
        }

        SetInputs(
            steer,
            accel,
            brake,
            handbrake);
    }

    private void UpdateDrivingInputs()
    {
        float horizontalValue = 0f;
        float accel = 0.0f;
        float brake = 0.0f;

        bool handbrake = GetHandbrake();
        float steeringSensitivity = 1.0f; // placeholder
        float joyDeadZone = 0.1f; // placeholder
        float mouseSensitivity = 1.0f; // placeholder

        switch (Mode)
        {
            case MMControllerType.Keyboard:
            case MMControllerType.GamePad:
                {
                    float horizontalValueRaw = 0f;

                    if (Input.GetKey(KeyCode.LeftArrow))
                        horizontalValueRaw -= 1f;

                    if (Input.GetKey(KeyCode.RightArrow))
                        horizontalValueRaw += 1f;

                    if (Input.GetKey(KeyCode.DownArrow))
                        brake = 1.0f;

                    if (Input.GetKey(KeyCode.UpArrow))
                        accel = 1.0f;

                    DiscreteFilter.Update(
                        Mathf.Abs(vehicle.VehCarSim.Speed),
                        horizontalValueRaw,
                        steeringSensitivity);

                    horizontalValue = DiscreteFilter.Value;
                    break;
                }

            case MMControllerType.Mouse:
                {
                    float mouseXPercent = Input.mousePosition.x / Screen.width;
                    float rawMouseValue = 2f * (mouseXPercent - 0.5f);

                    MouseFilter.Update(
                        Mathf.Abs(vehicle.VehCarSim.Speed),
                        rawMouseValue,
                        steeringSensitivity);

                    horizontalValue = MouseFilter.Value;

                    if (Input.GetMouseButton(1))
                        brake = 1.0f;

                    if (Input.GetMouseButton(0))
                        accel = 1.0f;

                    break;
                }

            case MMControllerType.Joystick:
            case MMControllerType.SteeringWheel:
            default:
                {
                    float rawVertical = Input.GetAxis("Vertical");
                    if (Mathf.Abs(rawVertical) < joyDeadZone)
                        rawVertical = 0.0f;

                    if (rawVertical > 0.0f)
                    {
                        accel = rawVertical;
                    }
                    else if (rawVertical < 0.0f)
                    {
                        brake = -rawVertical;
                    }
                    float rawDeviceValue = Input.GetAxis("Horizontal");

                    if (Mode == MMControllerType.SteeringWheel)
                    {
                        WheelFilter.Update(
                            Mathf.Abs(vehicle.VehCarSim.Speed),
                            rawDeviceValue,
                            steeringSensitivity);

                        horizontalValue = WheelFilter.Value;
                    }
                    else
                    {
                        JoyFilter.Update(
                            Mathf.Abs(vehicle.VehCarSim.Speed),
                            rawDeviceValue,
                            steeringSensitivity);

                        horizontalValue = JoyFilter.Value;
                    }

                    if (Mathf.Abs(horizontalValue) < joyDeadZone)
                        horizontalValue = 0f;


                    break;
                }
        }

        SetInputs(horizontalValue, accel, brake, handbrake);
    }

    private bool GetHandbrake()
    {
        return Input.GetKey(KeyCode.Space);
    }

    private void Update()
    {
        if (vehicle == null || !inputEnabled)
            return;
        if (Time.timeScale == 0.0f)
            return;

        UpdateHorn();

        if (isMobile)
            UpdateMobile();
        else
            UpdateDrivingInputs();
    }

    private void LoadVehiclePlayerSettings()
    {
        var playerSettings = new CarPlayerSettings(vehicle.Basename);

        // DISCRETE
        DiscreteFilter.DiscreteSteeringDeltaOutLo =
            playerSettings.DiscreteSteeringDeltaOutLo;
        DiscreteFilter.DiscreteSteeringDeltaInLo =
            playerSettings.DiscreteSteeringDeltaInLo;
        DiscreteFilter.DiscreteSteeringFilterLo =
            playerSettings.DiscreteSteeringFilterLo;

        DiscreteFilter.DiscreteSteeringDeltaOutHi =
            playerSettings.DiscreteSteeringDeltaOutHi;
        DiscreteFilter.DiscreteSteeringDeltaInHi =
            playerSettings.DiscreteSteeringDeltaInHi;
        DiscreteFilter.DiscreteSteeringFilterHi =
            playerSettings.DiscreteSteeringFilterHi;

        // JOYSTICK
        JoyFilter.UseApproachValues =
            playerSettings.JoyApp != 0;
        JoyFilter.AppApp =
            playerSettings.JoySteerAppApp;
        JoyFilter.SensitivityHigh =
            playerSettings.JoySensitivityHi;
        JoyFilter.SensitivityLow =
            playerSettings.JoySensitivityLow;
        JoyFilter.SteerFilterHi =
            playerSettings.JoySteerFilterHi;
        JoyFilter.SteerFilterLow =
            playerSettings.JoySteerFilterLow;
        JoyFilter.ApproachInHi =
            playerSettings.JoySteerApproachInHi;
        JoyFilter.ApproachInLo =
            playerSettings.JoySteerApproachInLo;
        JoyFilter.ApproachOutHi =
            playerSettings.JoySteerApproachOutHi;
        JoyFilter.ApproachOutLo =
            playerSettings.JoySteerApproachOutLo;

        // WHEEL
        WheelFilter.UseApproachValues =
            playerSettings.WheelApp != 0;
        WheelFilter.AppApp =
            playerSettings.WheelSteerAppApp;
        WheelFilter.SensitivityHigh =
            playerSettings.WheelSensitivityHi;
        WheelFilter.SensitivityLow =
            playerSettings.WheelSensitivityLow;
        WheelFilter.SteerFilterHi =
            playerSettings.WheelSteerFilterHi;
        WheelFilter.SteerFilterLow =
            playerSettings.WheelSteerFilterLow;
        WheelFilter.ApproachInHi =
            playerSettings.WheelSteerApproachInHi;
        WheelFilter.ApproachInLo =
            playerSettings.WheelSteerApproachInLo;
        WheelFilter.ApproachOutHi =
            playerSettings.WheelSteerApproachOutHi;
        WheelFilter.ApproachOutLo =
            playerSettings.WheelSteerApproachOutLo;

        // MOUSE
        MouseFilter.SensitivityLow =
            playerSettings.MouseSensitivityLow;
        MouseFilter.SensitivityHigh =
            playerSettings.MouseSensitivityHi;
        MouseFilter.SteerFilterHi =
            playerSettings.MouseSteerFilterHi;
        MouseFilter.SteerFilterLow =
            playerSettings.MouseSteerFilterLow;

        // SPEED VALUES
        DiscreteFilter.SpeedBaseLow =
            playerSettings.SpeedBaseLow;
        DiscreteFilter.SpeedBaseHi =
            playerSettings.SpeedBaseHi;
        DiscreteFilter.Mode =
            (MMInputFilterBase.SpeedMode)playerSettings.SpeedSensitive;

        JoyFilter.CopySpeedVars(DiscreteFilter);
        WheelFilter.CopySpeedVars(DiscreteFilter);
        MouseFilter.CopySpeedVars(DiscreteFilter);
    }

    public void SetVehicle(VehCar vehicle)
    {
        this.vehicle = vehicle;

        if (vehicle != null)
            LoadVehiclePlayerSettings();
    }

    public void Reset()
    {
        DiscreteFilter.Reset();
        JoyFilter.Reset();
        MouseFilter.Reset();
        WheelFilter.Reset();

        lastHornState = false;
    }

    public void Init(VehCar car)
    {
        //this.config = config;
        SetVehicle(car);

        isMobile = Application.isMobilePlatform || ForceMobileInput;

        Enable();
        SetMode(GameState.ControllerType);
    }
}