using UnityEngine;

public class MainMenu : UIMenu
{
    public override bool CreatesHistoryEntry => false;

    private TextDropdown playersDropdown;
    private bool surpressPlayerChangeEvent = false;

    private MMTextNodeWidget driverInfoText;

    private void UpdatePlayerInformationLabel()
    {
        var playerData = PlayerManager.CurrentPlayer;
        var playerConfig = PlayerManager.CurrentPlayerConfig;

        // skill level
        LocString skillStringId = (playerData.SkillLevel == MMSkillLevel.Amateur) ? LocString.DifficultyAmateur 
                                                                                  : LocString.DifficultyProfessional;
        string skillString = Localization.GetString(skillStringId);

        // last race
        string lastRace = Localization.GetString(LocString.PlaceholderText);
        switch(playerData.GameMode)
        {
            case MMGameMode.CrashCourse:
                lastRace = Localization.GetString(LocString.DriverStats_GameModeCrashCourse);
                break;
            case MMGameMode.Cruise:
                lastRace = Localization.GetString(LocString.DriverStats_GameModeCruise);
                break;
            case MMGameMode.CopsNRobbers:
                lastRace = Localization.GetString(LocString.DriverStats_GameModeCnR);
                break;
            default:
                {
                    var city = CityList.GetCity(playerData.City);
                    if(city != null)
                    {
                        lastRace = city.GetRaceName(playerData.GameMode, playerData.RaceId);
                    }
                }
                break;
        }

        // last vehicle
        string vehicleName = Localization.GetString(LocString.PlaceholderText);
        if(!string.IsNullOrEmpty(playerData.VehicleName))
        {
            var info = VehicleList.GetVehicle(playerData.VehicleName);
            if(info != null)
            {
                vehicleName = info.Description;
            }
        }

        // controller
        string controllerName = Localization.GetString(LocString.ControllerMouse + (int)playerConfig.Input.inputDeviceType);

        // score
        int score = -1;
        if(playerData.SkillLevel == MMSkillLevel.Professional)
        {
            score = 0;
            score += PlayerManager.OpenCityRecord("sf")?.TotalScore ?? 0;
            score += PlayerManager.OpenCityRecord("london")?.TotalScore ?? 0;
        }

        // update texts
        driverInfoText.SetString(1, skillString);
        driverInfoText.SetString(3, lastRace);
        driverInfoText.SetString(5, vehicleName);
        driverInfoText.SetString(7, controllerName);

        if(score < 0)
        {
            driverInfoText.SetString(8, string.Empty);
            driverInfoText.SetString(9, string.Empty);
        }
        else
        {
            driverInfoText.SetString(8, Localization.GetString(LocString.DriverStatsScore));
            driverInfoText.SetString(9, score.ToString());
        }
    }

    private void OnPlayerIndexChanged(int index)
    {
        if (!surpressPlayerChangeEvent)
        {
            PlayerManager.SetActivePlayer(index);
        }
        if (driverInfoText != null)
        {
            UpdatePlayerInformationLabel();
        }
    }

    private void UpdateDriverList()
    {
        surpressPlayerChangeEvent = true;
        playersDropdown.SetItems(PlayerManager.GetAllPlayerNames());

        int activePlayerIndex = PlayerManager.FindPlayer(PlayerManager.CurrentPlayer.Name);
        playersDropdown.SelectedItemIndex = activePlayerIndex;
        surpressPlayerChangeEvent = false;
    }

    private void OnNewPlayerCreated(PlayerData data, PlayerConfig config)
    {
        UpdateDriverList();
    }

    private void OnPlayerDeleted(int index)
    {
        UpdateDriverList();
    }

    private void OnNewPlayerSelected(PlayerData data, PlayerConfig config)
    {
        int index = PlayerManager.FindPlayer(data.Name);
        if(index >=0 && index != playersDropdown.SelectedItemIndex)
        {
            surpressPlayerChangeEvent = true;
            playersDropdown.SelectedItemIndex = index;
            surpressPlayerChangeEvent = false;
        }
    }

    public override void Dispose()
    {
        base.Dispose();
        PlayerManager.OnNewPlayerCreated -= OnNewPlayerCreated;
        PlayerManager.OnActiveProfileChanged -= OnNewPlayerSelected;
        PlayerManager.OnPlayerDeleted -= OnPlayerDeleted;
    }

    public MainMenu() : base(MenuID.Main)
    {
        AssignBackground("main_bk");
        
        // main buttons
        var btnCrash = AddBMButton("dvrcc", 0.078125f, 0.25f, 4);
        var btnSingle = AddBMButton("main_sp", 0.078125f, 0.25f, 4);
        var btnMulti = AddBMButton("main_mp", 0.078125f, 0.4f, 4);
        var btnQuick = AddBMButton("main_qck", 0.078125f, 0.4f, 4);

        // driver
        playersDropdown = AddTextDropdown("DRIVER NAME", 1.0f, 1.0f, 0.375f, 1.0f, "Test|Test2");
        playersDropdown.OnSelectedIndexChanged += OnPlayerIndexChanged;
        UpdateDriverList();

        var btnDriverUp = AddBMButton("roller_up", "DRIVER UP", 1.0f, 1.0f, 3);
        var btnDriverDown = AddBMButton("roller_down", "DRIVER DOWN", 1.0f, 1.0f, 3);
        
        var btnDriverNew = AddBMButton("dvrnew", 1.0f, 1.0f, 4);
        var btnDriverDelete = AddBMButton("dvrdel", 1.0f, 1.0f, 4);
        var btnDriverStats = AddBMButton("dvrsts", 1.0f, 1.0f, 4);

        btnDriverNew.OnClick += () =>
        {
            if (MenuManager.Instance != null)
            {
                if (PlayerManager.PlayerCount < PlayerManager.MAX_PLAYERS)
                {
                    MenuManager.Instance.ShowDialog(MenuID.NewPlayerDialog);
                }
                else
                {
                    MenuManager.Instance.ShowDialog(MenuID.PlayerLimitReachedDialog);
                }
            }
        };
        btnDriverDelete.OnClick += () =>
        {
            if (MenuManager.Instance != null)
            {
                if (PlayerManager.PlayerCount > 1)
                {
                    MenuManager.Instance.ShowDialog(MenuID.DeletePlayerDialog);
                }
                else
                {
                    MenuManager.Instance.ShowDialog(MenuID.CannotDeleteLastPlayerDialog);
                }
            }
        };

        // replay/recods
        var btnReplay = AddBMButton("main_rpl", 0.078125f, 0.55f, 4);
        var btnRecords = AddBMButton("race_rec", 0.078125f, 0.7f, 4);

        // setup events
        btnDriverUp.OnClick += playersDropdown.Decrement;
        btnDriverDown.OnClick += playersDropdown.Increment;
        btnCrash.OnClick += () =>
        {
            if (MenuManager.Instance != null) MenuManager.Instance.SwitchTo(MenuID.CrashCourseIntro);
        };
        btnQuick.OnClick += () =>
        {
            if (MenuManager.Instance != null) MenuManager.Instance.SwitchTo(MenuID.VehicleSelect);
        };
        btnSingle.OnClick += () =>
        {
            if (MenuManager.Instance != null) MenuManager.Instance.SwitchTo(MenuID.RaceMenu);
        };


        // init driver label
        var driverInfoHeight = this.MenuToWidgetCoords(new Rect(0.0f, 0.0f, 0.0f, MenuManager.Instance.DropdownFontSize * 12)).height;
        driverInfoText = new MMTextNodeWidget(this, 9999, new Rect(playersDropdown.Rect.x, 0.34375f, 0.375f, driverInfoHeight));
        AddWidget(driverInfoText);

        driverInfoText.ForegroundColor = new Color(1.0f, 1.0f, 0.0f);
        var driverTextFont = MenuManager.Instance.GetFont(16);
        for(int i=0; i < 10; i+= 2)
        {
            float yPos = i * 0.033330001f;
            float yPosNext = (i + 1) * 0.033330001f;
            driverInfoText.AddText(driverTextFont, string.Empty, TextNodeEffect.None, 0.0f, yPos);
            driverInfoText.AddText(driverTextFont, string.Empty, TextNodeEffect.None, 0.05f, yPosNext);
        }

        driverInfoText.SetString(0, Localization.GetString(LocString.DriverStatsRanking));
        driverInfoText.SetString(2, Localization.GetString(LocString.DriverStatsLastRace));
        driverInfoText.SetString(4, Localization.GetString(LocString.DriverStatsLastVehicle));
        driverInfoText.SetString(6, Localization.GetString(LocString.DriverStatsController));
        UpdatePlayerInformationLabel();

        // setup sounds and labels
        btnDriverNew.Sound = MenuSound.BeepSingle;
        btnDriverDelete.Sound = MenuSound.BeepSingle;
        btnDriverStats.Sound = MenuSound.BeepSingle;

        var descLabel = AddBMLabel("desc icons", 0.4844f, 0.25f, "mn_cc|mn_sp|mn_mp|mn_qck|mn_new|mn_del|mn_sts|mn_rpl|mn_rec|mn_drv");
        SetDescriptionLabel(descLabel);
        SetupDescriptionLabelEvents(btnCrash, btnSingle, btnMulti, btnQuick,
                                    btnDriverNew, btnDriverDelete, btnDriverStats,
                                    btnReplay, btnRecords, playersDropdown);

        btnRecords.Enabled = false; // not implemented yet
        btnDriverStats.Enabled = false; // not implemented yet
        btnMulti.Enabled = false; // not implemented yet
        btnReplay.Visible = false; // overlaps by default, TODO

        // subscribe to events
        PlayerManager.OnNewPlayerCreated += OnNewPlayerCreated;
        PlayerManager.OnActiveProfileChanged += OnNewPlayerSelected;
        PlayerManager.OnPlayerDeleted += OnPlayerDeleted;
    }
}
