using UnityEngine;

public class MMGamePlayer : MonoBehaviour
{
    [Header("Automatic Reverse")]
    public float AutoReverseSpeedMph = 4.0f;
    public float AutoReverseInputThreshold = 0.1f;

    private bool isReversing;
    private bool waitForBrakeRelease;

    private const int ReverseGear = 0;
    private const int FirstGear = 2;

    public VehCar Car => car;
    public MMHud HUD => hud;
    public VehicleCameraManager CameraManager => cameraMgr;
    public MMInput Input => input;

    public bool DamagedOut
    {
        get
        {
            return (car != null && car.Damage.DamagePercentage >= 1.0f);
        }
    }

    private VehCar car;
    private MMInput input;
    private MMHud hud;
    private VehicleCameraManager cameraMgr;
    private RainAudio rainAudio;
    
    private bool inWater = false;
    private bool hitWaterHandlerCalled = false;
    private float timeInWater = 0.0f;

    private MMGame game;
    private bool postRaceInput;

    private SDLCity city;
    private bool inTunnel = false;
    private int curRoom = -1;
    private bool firstFrame = true;

    private void ApplyViewSettings()
    {
        var config = PlayerManager.CurrentPlayerConfig;
        var viewConfig = config.View;

        if(!Application.isMobilePlatform)
        {
            hud.Mirror.enabled = viewConfig.showMirror;
        }
        cameraMgr.SetCamIndex(viewConfig.viewModeIndex);
    }

    public void Init(MMGame game, string vehicleName, int vehiclePaintjob)
    {
        this.game = game;
        this.city = game.Level;

        var carRoot = new GameObject("Car");
        carRoot.transform.parent = transform;

        car = carRoot.AddComponent<VehCar>();
        car.Init(
            this.city,
            vehicleName,
            vehiclePaintjob,
            vehCarType.Player);
        car.VehCarSim.Transmission.Mode = (GameState.TransmissionType == MMTransmissionType.Manual) ? VehTransmission.TransmissionMode.Manual :
                                                                                                      VehTransmission.TransmissionMode.Automatic;

        input = gameObject.AddComponent<MMInput>();
        input.Init(car);

        hud = gameObject.AddComponent<MMHud>();
        hud.Init(game, this, car);
        hud.Map.SetTargetObject(car.Model.transform);

        cameraMgr = gameObject.AddComponent<VehicleCameraManager>();
        cameraMgr.Init(
            car,
            ViewportManager.MainViewport);

        cameraMgr.ActivateCurrentCamera();

        //if (Application.isEditor)
          //  car.Debug.enabled = true;

        // add ourselves to the ai
        if(city != null && city.AINetwork != null)
        {
            city.AINetwork.VehicleProxies.Add(new AIVehicleProxy(city.AINetwork, car));
        }

        // add rain audio if it's raining
        if(GameState.SelectedWeather == MMWeather.Raining)
        {
            var rainRoot = new GameObject("Rain Audio");
            rainRoot.transform.parent = this.transform;

            rainAudio = rainRoot.AddComponent<RainAudio>();
            rainAudio.Init();
        }
    }

    public void Reset()
    {
        hud.Reset();
        car.Reset();
        input.Reset();
        input.enabled = true;
        postRaceInput = false;
        timeInWater = 0.0f;
        inWater = false;
        hitWaterHandlerCalled = false;
        car.Model.gameObject.SetActive(!hud.DashboardActive);
    }

    public void EnterPostRaceMode()
    {
        input.enabled = false;
        postRaceInput = true;
    }

    private void Update()
    {
        if (firstFrame)
        {
            // hack
            MMAudioMixer.EchoOff();
            firstFrame = false; 
        }
        if (car == null)
            return;

        VehCarSim sim = car.VehCarSim;
        if(postRaceInput)
        {
            sim.Engine.ThrottleInput = 0.0f;
            sim.BrakeInput = 1.0f;
            sim.SteeringInput = -1.0f;
        }

        // cull test
        if(game != null && game.Level != null)
        {
            game.Level.Culler.UpdateCameraPosition(ViewportManager.MainViewport.ActiveCamera.transform.position);
        }

        // water
        if(car.Splash != null)
        {
            if(car.Splash.enabled)
            {
                if(!inWater)
                {
                    timeInWater = 0.0f;
                    inWater = true;
                    Car.Audio.PlaySplash();

                    // show water message
                    LocString message = LocString.SleepWithTheFishes;
                    if(city.Name == "sf")
                    {
                        message = LocString.SleepWithTheFishes_SF;
                    }
                    else if(city.Name == "london")
                    {
                        message = LocString.SleepWithTheFishes_London;
                    }
                    HUD.SetMessage(Localization.GetString(message), 3.0f, true);
                }

                timeInWater += Time.deltaTime;
                if(timeInWater >= 5.0f)
                {
                    if (!hitWaterHandlerCalled)
                    {
                        hitWaterHandlerCalled = true;   
                        game.PlayerHitWaterHandler();
                    }
                }
            }
        }

        // debug splash
        if(UnityEngine.Input.GetKeyDown(KeyCode.Alpha5))
        {
            car.Splash.Activate(car.Model.transform.position.y);
        }
        if (UnityEngine.Input.GetKeyDown(KeyCode.Alpha6))
        {
            car.Splash.Deactivate();
        }

        // TEST:  update audio mixer
        bool roomChanged = false;
        if(car != null  && game.Level != null)
        {
            int rid = game.Level.FindRoomIdWithWarpsCheckMiss(car.Model.transform.position, curRoom);
            if (rid != curRoom)
            {
                roomChanged = true;
                curRoom = rid;
            }

        }

        if (roomChanged && game != null && game.Level != null && game.Level.AINetwork != null)
        {
            game.Level.AINetwork.SetCullRoom(curRoom);
        }
        if (roomChanged && game != null && game.Level != null && game.Level.AmbientAudio != null)
        {
            var zoneMgr = game.Level.AudioZoneManager;
            if(curRoom == 0)
            {
                MMAudioMixer.EchoOff();
                zoneMgr.SetZone(AudioZoneManager.Zone.AboveGround);
                if (rainAudio != null) rainAudio.ShelterOff();
            }
            else
            {
                var info = game.Level.GetRoom(curRoom);
                if (info.Flags.HasFlag(PSDL.RoomFlags.Subterranean))
                {
                    MMAudioMixer.EchoOn();
                    zoneMgr.SetZone(AudioZoneManager.Zone.Subterranean);
                    if (rainAudio != null) rainAudio.ShelterOn();
                }
                else
                {
                    MMAudioMixer.EchoOff();
                    zoneMgr.SetZone(AudioZoneManager.Zone.AboveGround);
                    if (rainAudio != null) rainAudio.ShelterOff();
                }
            }
        }


        // Stop slow car.
        if (sim.SpeedInMph < 4.0f &&
            sim.Engine.ThrottleInput == 0.0f)
        {
            sim.HandBrakeInput = 1.0f;
        }

        // Reset if fallen out of world.
        float carHeight = car.Model.transform.position.y;

        if (carHeight < -50.0f)
        {
            car.Reset();
        }

        // Camera.
        if (UnityEngine.Input.GetKeyDown(KeyCode.C))
        {
            cameraMgr.NextCamera();
        }
        if (UnityEngine.Input.GetKeyDown(KeyCode.V))
        {
            cameraMgr.ActivateOrbitCamera();
        }

        // Automatic reverse.
        UpdateAutomaticReverse(sim);

        // Transmission controls.
        if (UnityEngine.Input.GetKeyDown(KeyCode.T))
        {
            var curMode = sim.Transmission.Mode;

            var nextMode =
                curMode == VehTransmission.TransmissionMode.Manual
                    ? VehTransmission.TransmissionMode.Automatic
                    : VehTransmission.TransmissionMode.Manual;

            sim.Transmission.Mode = nextMode;
        }

        if (UnityEngine.Input.GetKeyDown(KeyCode.A))
        {
            sim.Transmission.Upshift();
        }

        if (UnityEngine.Input.GetKeyDown(KeyCode.Z))
        {
            sim.Transmission.Downshift();
        }

        // hud
        if(UnityEngine.Input.GetKeyDown(KeyCode.Backspace))
        {
            hud.ToggleMirror();
        }
        if(UnityEngine.Input.GetKeyDown(KeyCode.Q))
        {
            hud.ToggleMapFullscreen();
        }
        if(UnityEngine.Input.GetKeyDown(KeyCode.E))
        {
            hud.ToggleMapZoom();
        }
        if (UnityEngine.Input.GetKeyDown(KeyCode.F))
        {
            hud.ToggleMapRotate();
        }
        if(UnityEngine.Input.GetKeyDown(KeyCode.H))
        {
            hud.ToggleSpeedometer();
        }
        if (UnityEngine.Input.GetKeyDown(KeyCode.Tab))
        {
            hud.NextMapMode();
        }
        if(UnityEngine.Input.GetKeyDown(KeyCode.D))
        {
            ToggleDash();
        }
    }

    public void ToggleDash()
    {
        hud.ToggleDash();
        if (hud.DashboardActive)
        {
            cameraMgr.ActivateDash();
            if (rainAudio != null) rainAudio.SetInterior(true);
        }
        else
        {
            cameraMgr.DeactivateDash();
            if (rainAudio != null) rainAudio.SetInterior(false);
        }
        car.Model.gameObject.SetActive(!hud.DashboardActive);
    }

    private void UpdateAutomaticReverse(VehCarSim sim)
    {
        if (input == null)
            return;

        // Brake is being forced by something other than the player.
        bool inputOverridden =
            postRaceInput ||
            !input.enabled ||
            !car.IsDrivable;

        if (inputOverridden ||
            sim.Transmission.Mode != VehTransmission.TransmissionMode.Automatic)
        {
            if (isReversing)
                SetReverse(sim, false);

            input.InvertPedals = false;
            waitForBrakeRelease = false;
            return;
        }

        float brake = input.BrakeInput;
        float accel = input.ThrottleInput;

        bool brakePressed = brake > AutoReverseInputThreshold;
        bool accelPressed = accel > AutoReverseInputThreshold;
        float speed = sim.SpeedInMph;

        // Clear the latch once the brake is let go.
        if (!brakePressed)
            waitForBrakeRelease = false;

        if (brakePressed && accelPressed)
            return;

        if (brakePressed &&
            !isReversing &&
            !waitForBrakeRelease &&
            speed < AutoReverseSpeedMph)
        {
            SetReverse(sim, true);
        }
        else if (!accelPressed &&
                 isReversing &&
                 speed > -AutoReverseSpeedMph)
        {
            SetReverse(sim, false);

            // If we left reverse while brake is held, don't re-enter
            // until it's released.
            if (brakePressed)
                waitForBrakeRelease = true;
        }
    }

    private void SetReverse(
        VehCarSim sim,
        bool reverse)
    {
        isReversing = reverse;
        input.InvertPedals = reverse;

        sim.Transmission.SetCurrentGear(
            reverse ? ReverseGear : FirstGear);
    }
}