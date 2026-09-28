using UnityEngine;

// Contains the menu manager, and menus
public class MMInterface : MonoBehaviour
{
    public static MMInterface Instance => instance;
    private static MMInterface instance;

    private PlayerDirectory playerDirectory;

    private MenuManager manager;
    private MainMenuMusic music;

    // menus
    private MainMenu mainMenu;
    private CrashCourseIntro crashCourseIntro;
    private CrashCourse crashCourse;
    private VehicleSelect vehicleSelect;
    private VehicleShowcase vehicleShowcase;
    private RaceMenu racesMenu;
    private OptionsMenu optionsMenu;
    private AboutMenu aboutMenu;

    // dialogs
    private DialogQuit quitDialog;
    private DialogNewPlayer newPlayerDialog;
    private DialogDuplicatePlayerName duplicatePlayerNameDialog;
    private DeletePlayerDialog deletePlayerDialog;
    private CannotDeletePlayerDialog cannotDeletePlayerDialog;
    private PlayerLimitReachedDialog playerLimitReachedDialog;
    private CannotChooseLockedVehicleDialog cannotChooseLockedVehicleDialog;

    private void PlayerResolveCars(PlayerData data, PlayerConfig config)
    {
        foreach(var vehicle in VehicleList.Vehicles)
        {
            vehicle.RewardFlags = 0;
            vehicle.IsLocked = false;
        }

        ResolveRewards(data, config, "sf");
        ResolveRewards(data, config, "london");
    }

    private void ResolveRewards(PlayerData data, PlayerConfig config, string city)
    {
        var list = new RewardsList();
        if(list.Init(city))
        {
            var cityRecord = PlayerManager.OpenCityRecord(city);
            if(cityRecord == null)
            {
                return;
            }

            foreach(var entry in list.Rewards)
            {
                var vehicleInfo = VehicleList.GetVehicle(entry.VehicleBasename);
                if(vehicleInfo == null)
                {
                    continue;
                }

                if(entry.IsHalfUnlock)
                {
                    int numRacesHalf = cityRecord.GetNumRaces(entry.GameMode) / 2;
                    int numPassed = cityRecord.GetNumPassed(entry.GameMode);
                    if(numPassed  < numRacesHalf)
                    {
                        if(entry.IsTextureUnlock)
                            vehicleInfo.RewardFlags |= (1 << entry.VariantNumber);
                        else
                            vehicleInfo.IsLocked = true;
                    }
                }
                else if(entry.IsFullUnlock)
                {
                    int numRaces = cityRecord.GetNumRaces(entry.GameMode);
                    int numPassed = cityRecord.GetNumPassed(entry.GameMode);
                    if (numPassed < numRaces)
                    {
                        if (entry.IsTextureUnlock)
                            vehicleInfo.RewardFlags |= (1 << entry.VariantNumber);
                        else
                            vehicleInfo.IsLocked = true;
                    }
                }
                else
                {
                    int passedMask = cityRecord.GetPassedMask(entry.GameMode);
                    bool passed = (passedMask & (1 << entry.RaceNumber)) != 0;
                    if(!passed)
                    {
                        if (entry.IsTextureUnlock)
                            vehicleInfo.RewardFlags |= (1 << entry.VariantNumber);
                        else
                            vehicleInfo.IsLocked = true;
                    }
                }
            }
        }
    }

    private void OnPlayerProfileChanged(PlayerData data, PlayerConfig config)
    {
        data.SetState();
        config.SetAudio();

        MMAudioMixer.Volume = GameState.AudioVolume;
        MMAudioMixer.MusicVolume = GameState.MusicVolme;

        var audioConfig = config.Audio;
        float balance = audioConfig.audioBalance;

        if(audioConfig.audioFlags.HasFlag(MMAudioFlags.MusicEnabled))
        {
            if (!music.isPlaying)
            {
                music.StartPlayback();
            }
            music.balance = balance;
        }
        else if(music.isPlaying)
        {
            music.StopPlayback();
        }

        PlayerResolveCars(data, config);
    }

    private void OnDestroy()
    {
        PlayerManager.OnActiveProfileChanged -= OnPlayerProfileChanged;
        instance = null;
    }

    public void Init()
    {
        // fix timescale if we loaded from a paused game
        Time.timeScale = 1.0f;
        MMAudioMixer.Unmute();

        // load lists
        CityList.LoadAll();
        VehicleList.LoadAll();

        // load player directory
        PlayerManager.LoadPlayers();

        // load manager
        manager = gameObject.AddComponent<MenuManager>();
        manager.LoadInterfaceFonts();

        // load individual menus
        mainMenu = new MainMenu();
        crashCourseIntro = new CrashCourseIntro();
        crashCourse = new CrashCourse();
        vehicleSelect = new VehicleSelect();
        vehicleShowcase = new VehicleShowcase();
        racesMenu = new RaceMenu();
        optionsMenu = new OptionsMenu();
        aboutMenu = new AboutMenu();

        quitDialog = new DialogQuit();
        newPlayerDialog = new DialogNewPlayer();
        duplicatePlayerNameDialog = new DialogDuplicatePlayerName();
        deletePlayerDialog = new DeletePlayerDialog();
        cannotDeletePlayerDialog = new CannotDeletePlayerDialog();
        playerLimitReachedDialog = new PlayerLimitReachedDialog();
        cannotChooseLockedVehicleDialog = new CannotChooseLockedVehicleDialog();

        // and then switch to the main
        manager.SwitchTo(mainMenu.ID);

        // add music
        var musicObj = new GameObject("Music");
        musicObj.transform.parent = transform;
        music = musicObj.AddComponent<MainMenuMusic>();
        music.Init();

        // setup things based on the player profile and subscribe to future changes
        PlayerManager.OnActiveProfileChanged += OnPlayerProfileChanged;
        OnPlayerProfileChanged(PlayerManager.CurrentPlayer, PlayerManager.CurrentPlayerConfig);
    }

    private void Awake()
    {
        instance = this;
    }
}
