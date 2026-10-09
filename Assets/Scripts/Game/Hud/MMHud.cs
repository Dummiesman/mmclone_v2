using UnityEngine;
using static Hudmap;

public class MMHud : MonoBehaviour
{
    public Hudmap Map { get; private set; }
    public HudArrow Arrow { get; private set; }
    public MMHudTimer Timer => timer;
    public MMMirror Mirror => mirror;

    public ResultsMenu ResultsMenu => resultsMenu;

    private MMMirror mirror;
    private MMGamePlayer player;
    private MMHudTimer timer;

    private MMTextNode textUpper;
    private MMTextNode textLower;

    public bool DashboardActive => dashEnabled;
    private Dashboard dashboard;
    private bool dashEnabled = false;

    private MMDigiTach tach;
    private bool enableTach = true;

    // mobile ui
    public MobileRacingUI MobileUI => mobileUi;
    private MobileRacingUI mobileUi;

    // menus
    private MenuManager menuManager; // menu manager for pause, etc.
    private ResultsMenu resultsMenu;
    private PopupMain popupMain;
    private PopupOptions popupOptions;
    private PopupAudio popupAudio;
    private PopupGraphics popupGraphics;

    private float messageUpperY;
    private float messageLowerY;

    private float showMessageTime;
    private bool useAltMessagePosition;

    private const float SmallViewportThreshold = 0.4f;

    private void UpdateMessage()
    {
        var viewport = ViewportManager.MainViewport;
        var screenRect = viewport.ScreenRect;
        var pixelRect = viewport.PixelRect;
        float viewportY = screenRect.height;

        float upper, lower;
        if (viewportY < SmallViewportThreshold)
        {
            upper = 0.05f;
            lower = 0.1f;
        }
        else if (useAltMessagePosition)
        {
            upper = 0.2f;
            lower = 0.215f;
        }
        else
        {
            upper = 0.8f;
            lower = 0.875f;
        }

        messageUpperY = pixelRect.y + upper * pixelRect.height;
        messageLowerY = pixelRect.y + lower * pixelRect.height;

        if (showMessageTime > 0.0f)
        {
            showMessageTime -= Time.deltaTime;
            if(showMessageTime <= 0.0f)
            {
                textUpper.SetString(0, string.Empty);
                textLower.SetString(0, string.Empty);
                showMessageTime = 0.0f;
            }
        }
    }

    private void InitMenus(MMGame game)
    {
        var menuRoot = new GameObject("MenuManager");
        menuRoot.transform.parent = this.transform;

        menuManager = menuRoot.AddComponent<MenuManager>();
        menuManager.LoadInGameFonts();

        resultsMenu = new ResultsMenu(game, game.Level.Name, GameState.SelectedGameMode, GameState.SelectedRace);
        popupMain = new PopupMain(game);
        popupOptions = new PopupOptions();
        popupAudio = new PopupAudio();
        popupGraphics = new PopupGraphics(game);

        menuManager.enabled = false;
    }

    public void Init(MMGame game, MMGamePlayer player, VehCar car)
    {
        this.player = player;

        // init viewports
        var hudViewport = ViewportManager.AddViewport(
            "HUDMAP",
            new Rect(0.7794f, 0.0131f, 0.2022f, 0.2369f),
            LayerMask.GetMask("Hudmap"));
        hudViewport.DefaultDepth = 1;

        // init hudmap
        var mapRoot = new GameObject("Hudmap");
        mapRoot.transform.parent = this.transform;

        Map = mapRoot.AddComponent<Hudmap>();
        Map.Init(GameState.SelectedCity);
        Map.SetFollowRotation(true);

        // init arrow
        var arrowRoot = new GameObject("Arrow");
        arrowRoot.transform.parent = this.transform;

        Arrow = arrowRoot.AddComponent<HudArrow>();
        Arrow.Init();
        Arrow.enabled = false;

        // init mirror
        var mirrorRoot = new GameObject("Mirror");
        mirrorRoot.transform.parent = this.transform;

        mirror = mirrorRoot.AddComponent<MMMirror>();
        mirror.Init(car);

        mirror.enabled = !Application.isMobilePlatform;

        // init dashboard
        var dashRoot = new GameObject("Dash");
        dashRoot.transform.parent = this.transform;
        dashboard = dashRoot.AddComponent<Dashboard>();
        dashboard.Init(car);

        // init tach
        var tachRoot = new GameObject("Tachometer");
        tachRoot.transform.parent = this.transform;
        tach = tachRoot.AddComponent<MMDigiTach>();
        tach.Init(car);

        // init timer
        var timerRoot = new GameObject("Timer");
        timerRoot.transform.parent = this.transform;
        timer = timerRoot.AddComponent<MMHudTimer>();
        timer.Init();
        timer.enabled = false;

        // init hud text
        string fontDefinition = Localization.GetString(LocString.Font_Popup_ArialBold_16_24);
        var font = FontLoader.LoadFont(fontDefinition);

        textLower = new MMTextNode();
        textLower.AddText(font, string.Empty, TextNodeEffect.CenterBoth | TextNodeEffect.Shadow, 0.0f, 0.0f);

        textUpper = new MMTextNode();
        textUpper.AddText(font, string.Empty, TextNodeEffect.CenterBoth | TextNodeEffect.Shadow, 0.0f, 0.0f);

        textLower.ForegroundColor = new Color(1.0f, 1.0f, 0.0f);
        textUpper.ForegroundColor = new Color(1.0f, 1.0f, 0.0f);

        // init menus
        InitMenus(game);

        // init mobile ui
        if(Application.isMobilePlatform || Application.isEditor)
        {
            GameObject prefab = Resources.Load<GameObject>("Prefabs/MobileUI");
            var instantiated = Instantiate(prefab);
            mobileUi = instantiated.GetComponent<MobileRacingUI>();

            mobileUi.PausePressed += () =>
            {
                TogglePopup();
            };
            mobileUi.CameraPressed += () =>
            {
                player.CameraManager.NextCamera();
            };
            mobileUi.DashCamToggled += (enable) =>
            {
                player.ToggleDash();
            };
            mobileUi.MirrorToggled += (enable) =>
            {
                ToggleMirror();
            };
            mobileUi.MapPressed += () =>
            {
                NextMapMode();
            };

            player.Input.MobileInputUI = mobileUi; // gross, but works for now
        }
    }

    public void ShowResults()
    {
        menuManager.enabled = true;
        menuManager.DrawSolidBackground = true;
        menuManager.SwitchTo(MenuID.Results);
    }

    public void Reset()
    {
        showMessageTime = 0.0f;
        textLower.SetString(0, string.Empty);
        textUpper.SetString(0, string.Empty);

        resultsMenu.ClearNames();
        menuManager.enabled = false;
        menuManager.DrawSolidBackground = false;
        Map.Reset();
    }

    private void OnGUI()
    {
        float textScale = Screen.height / UIConstants.ReferenceHeight;
        float heightPx = textScale * 32;

        textLower.Draw(new Rect(0, messageLowerY, Screen.width, heightPx), textScale);
        textUpper.Draw(new Rect(0, messageUpperY, Screen.width, heightPx), textScale);
    }

    private void Update()
    {
        UpdateMessage();
        if(Input.GetKeyDown(KeyCode.KeypadPlus))
        {
            SetMessage("Test message");
        }
    }

    // Interface
    public void SetMessage2(string message)
    {
        textUpper.SetString(0, message);
    }

    public void SetMessage(string message, float time, bool altPos)
    {
        textLower.SetString(0, message);
        SetMessage2(string.Empty);
        showMessageTime = time;
        useAltMessagePosition = altPos;
    }
    
    public void SetMessage(string message, float time)
    {
        SetMessage(message, time, false);
    }

    public void SetMessage(string message)
    {
        SetMessage(message, 3.0f, false);
    }

    public void TogglePopup()
    {
        // don't show if results active
        if (menuManager.enabled && menuManager.ActiveMenu != null && menuManager.ActiveMenu.ID == MenuID.Results)
            return;

        if (!menuManager.enabled)
        {
            MMAudioMixer.Mute();
            Time.timeScale = 0.0f;
            menuManager.enabled = true;
            menuManager.SwitchTo(MenuID.PopupMain);
        }
        else
        {
            MMAudioMixer.Unmute();
            Time.timeScale = 1.0f;
            menuManager.DeactivateMenu();
            menuManager.enabled = false;
        }
    }

    public void ToggleSpeedometer()
    {
        if(!dashEnabled)
        {
            enableTach = !enableTach;
            tach.enabled = enableTach;
        }
    }

    public void ToggleMapRotate()
    {
        Map.ToggleFollowRotation();
    }

    public void ToggleMapFullscreen()
    {
        Map.ToggleFullscreen();
    }

    public void ToggleMapZoom()
    {
        Map.ToggleZoom();
    }

    public void NextMapMode()
    {
        Map.CycleModeNext();
    }

    public void ToggleMirror()
    {
        mirror.enabled = !mirror.enabled;
    }

    public void ToggleDash()
    {
        if(dashEnabled)
        {
            // disable dash
            tach.enabled = enableTach;
            dashboard.Deactivate();
            Map.SetScreenSide(ScreenSide.Right);
        }
        else
        {
            // enable dash
            tach.enabled = false;
            dashboard.Activate();

            var vehicleInfo = VehicleList.GetVehicle(player.Car.Basename);
            Hudmap.ScreenSide screenSide = (vehicleInfo.Flags.HasFlag(VehicleInfoFlags.RightHandDrive)) ? Hudmap.ScreenSide.Left : Hudmap.ScreenSide.Right;
            Map.SetScreenSide(screenSide);
        }
        dashEnabled = !dashEnabled;
    }
}
