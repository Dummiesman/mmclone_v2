using System;
using System.Collections.Generic;
using UnityEngine.SceneManagement;

public class CrashCourse : UIMenu
{
    private RaceData raceData;

    private readonly List<CCStatus> statusIndicators = new List<CCStatus>();
    private readonly TextDropdown crashNameDropdown;
    private readonly BMButton crashDropdownDec;
    private readonly BMButton crashDropdownInc;

    private readonly TextDropdown raceNameDropdown;
    private readonly BMButton raceDropdownDec;
    private readonly BMButton raceDropdownInc;

    private readonly BMButton crashRadioButton;
    private readonly BMButton blitzRadioButton;
    private readonly BMButton checkpointRadioButton;
    private BMButtonGroup modesButtonGroup = new BMButtonGroup();

    private BMLabel descLabel;

    private uint crashEnableMask;
    private uint blitzEnableMask;
    private uint checkpointEnableMask;

    private uint crashProgressMask;
    private uint blitzProgressMask;
    private uint checkpointProgressMask;

    private uint activeProgressMask;

    private MenuID nextMenu;
    private BMButton nextMenuButton;
    private bool useDefaultVehicle;

    private MMGameMode SelectedGameMode
    {
        get
        {
            switch (modesButtonGroup.ActiveButtonIndex)
            {
                case 0: return MMGameMode.CrashCourse;
                case 1: return MMGameMode.Blitz;
                case 2: return MMGameMode.Checkpoint;
            }
            return default;
        }
        set
        {
            switch (value)
            {
                case MMGameMode.CrashCourse:
                    modesButtonGroup.ActiveButtonIndex = 0;
                    break;

                case MMGameMode.Blitz:
                    modesButtonGroup.ActiveButtonIndex = 1;
                    break;

                case MMGameMode.Checkpoint:
                    modesButtonGroup.ActiveButtonIndex = 2;
                    break;
            }
            GameModeChange();
        }
    }

    private void NextButtonClicked()
    {
        SetupRaceState();
        if(nextMenu != MenuID.CrashCourse)
        {
            if (MenuManager.Instance != null) MenuManager.Instance.SwitchTo(nextMenu);
        }
        else
        {
            GameState.EnterGame();
        }
    }
    
    private void SetupRaceState()
    {
        int raceId = (SelectedGameMode == MMGameMode.CrashCourse) ? crashNameDropdown.SelectedItemIndex : raceNameDropdown.SelectedItemIndex;
        GameState.SelectedRace = raceId;
        GameState.SelectedGameMode = SelectedGameMode;

        if(raceData.TryGetData(GameState.SelectedGameMode, GameState.SelectedRace, GameState.SkillLevel, out var selectedRaceData))
        {
            GameState.PedestrianDensity = selectedRaceData.PedestrianDensity;
            GameState.TrafficDensity = selectedRaceData.TrafficDensity;
            GameState.CopDensity = selectedRaceData.NumCops;
            GameState.SelectedTimeOfDay = selectedRaceData.TimeOfDay;
            GameState.SelectedWeather = selectedRaceData.Weather;
            GameState.TimeLimit = selectedRaceData.TimeLimit;
            GameState.LapCount = selectedRaceData.NumLaps;
            GameState.Difficulty = selectedRaceData.Difficulty;
            GameState.OpponentCount = selectedRaceData.Opponents;
        }

        if (SelectedGameMode == MMGameMode.CrashCourse)
        {
            bool useDefaultVehicle = ((1 << GameState.SelectedRace) & activeProgressMask) == 0;
            if(useDefaultVehicle && GameState.SelectedCity == "sf")
            {
                GameState.SelectedVehicle = "vpbullet";
                GameState.SelectedPaintjob = 0;
            }
            else if(useDefaultVehicle && GameState.SelectedCity == "london")
            {
                GameState.SelectedVehicle = "vpcab";
                GameState.SelectedPaintjob = 0;
            }

            GameState.OpponentCount = 8;
            GameState.CopDensity = 1.0f;
            GameState.TrafficDensity = 0.0f;
        }
    }

    private void SetupStateFromGameState()
    {
        if(GameState.SelectedGameMode == MMGameMode.CrashCourse || 
            GameState.SelectedGameMode == MMGameMode.Blitz ||
            GameState.SelectedGameMode == MMGameMode.Checkpoint)
        {
            SelectedGameMode = GameState.SelectedGameMode;
            if(GameState.SelectedGameMode == MMGameMode.CrashCourse)
            {
                crashNameDropdown.SelectedItemIndex = GameState.SelectedRace;
            }
            else
            {
                raceNameDropdown.SelectedItemIndex = GameState.SelectedRace;
            }
        }
        else
        {
            SelectedGameMode = MMGameMode.CrashCourse;
        }
    }

    private void UpdateNextMenuButton()
    {
        int raceId = (SelectedGameMode == MMGameMode.CrashCourse) ? crashNameDropdown.SelectedItemIndex : raceNameDropdown.SelectedItemIndex;
        bool showVehNext =  (SelectedGameMode != MMGameMode.CrashCourse) || ((1 << raceId) & activeProgressMask) != 0;
        if(showVehNext)
        {
            nextMenuButton.SetTexture("race_veh");
            nextMenu = MenuID.VehicleSelect;
            nextMenuButton.Sound = MenuSound.BeepDouble;
        }
        else
        {
            nextMenuButton.SetTexture("veh_go");
            nextMenu = MenuID.CrashCourse;
            nextMenuButton.Sound = MenuSound.GoDrive;
        }
    }

    private void CrashChange(int index)
    {
        // change desc label
        SetDescriptionLabelIndex(index);
        UpdateNextMenuButton();
    }

    private void RaceChange(int index)
    {
        UpdateNextMenuButton();
    }

    private void GameModeChange()
    {
        // hide dropdowns and buttons
        bool showCrashDropdown = (SelectedGameMode == MMGameMode.CrashCourse);
        
        crashNameDropdown.Visible = showCrashDropdown;
        crashDropdownInc.Visible = showCrashDropdown;
        crashDropdownDec.Visible = showCrashDropdown;
        
        raceNameDropdown.Visible = !showCrashDropdown;
        raceDropdownDec.Visible = !showCrashDropdown;
        raceDropdownInc.Visible = !showCrashDropdown;

        // update list
        var cityInfo = CityList.GetCity(GameState.SelectedCity);
        switch (SelectedGameMode)
        {
            case MMGameMode.CrashCourse:
                activeProgressMask = crashProgressMask;
                break;
            case MMGameMode.Blitz:
                {
                    raceNameDropdown.SetItems(cityInfo.GetRaceNames(MMGameMode.Blitz));
                    raceNameDropdown.SetDisabledMask(~blitzEnableMask);
                    activeProgressMask = blitzProgressMask;
                    break;
                }
            case MMGameMode.Checkpoint:
                {
                    raceNameDropdown.SetItems(cityInfo.GetRaceNames(MMGameMode.Checkpoint));
                    raceNameDropdown.SetDisabledMask(~checkpointEnableMask);
                    activeProgressMask = checkpointProgressMask;
                    break;
                }
        }

        // update desc label if needed
        if (SelectedGameMode == MMGameMode.CrashCourse)
        {
            SetDescriptionLabelIndex(crashNameDropdown.SelectedItemIndex);
        }
        else
        {
            ClearDescriptionLabel();
        }
        UpdateNextMenuButton();
    }

    private void CityChange()
    {
        UpdateBackground(GameState.SelectedCity);
        UpdateStatusIndicators(GameState.SelectedCity);

        // update crash list
        var cityInfo = CityList.GetCity(GameState.SelectedCity);
        crashNameDropdown.SetItems(cityInfo.GetRaceNames(MMGameMode.CrashCourse));

        // get disabled masks
        var cityRecord = PlayerManager.OpenCityRecord(cityInfo.RaceDir);
        if (cityRecord != null)
        {
            crashEnableMask = cityRecord.ResolveCrashProgress();
            blitzEnableMask = 0xFFFFFFFF;
            checkpointEnableMask = cityRecord.ResolveCheckpointProgress();

            crashProgressMask = (uint)cityRecord.GetPassedMask(MMGameMode.CrashCourse);
            blitzProgressMask = (uint)cityRecord.GetPassedMask(MMGameMode.Blitz);
            checkpointProgressMask = (uint)cityRecord.GetPassedMask(MMGameMode.Checkpoint);
        }
        else
        {
            crashEnableMask = 0xFFFFFFFF;
            blitzEnableMask = 0xFFFFFFFF;
            checkpointEnableMask = 0xFFFFFFFF;

            crashProgressMask = 0xFFFFFFFF;
            blitzProgressMask = 0xFFFFFFFF;
            checkpointProgressMask = 0xFFFFFFFF;
        }

        // set crash list disabled
        crashNameDropdown.SetDisabledMask(~crashEnableMask);

        // setup desc labels
        if(cityInfo.RaceDir == "sf")
        {
            descLabel.SetImages("sf_cc0|sf_cc1|sf_cc2|sf_cc3|sf_cc4|sf_cc5|sf_cc6|sf_cc7|sf_cc8|sf_cc9|sf_cc10|sf_cc11|sf_cc12");
        }
        else
        {
            descLabel.SetImages("lon_cc0|lon_cc1|lon_cc2|lon_cc3|lon_cc4|lon_cc5|lon_cc6|lon_cc7|lon_cc8|lon_cc9|lon_cc10|lon_cc11|lon_cc12");
        }

        // load race data
        raceData = new RaceData();
        raceData.LoadAll(cityInfo.RaceDir);

        UpdateNextMenuButton();
    }

    private void UpdateStatusIndicators(string city)
    {
        var cityRecord = PlayerManager.OpenCityRecord(city);
        if (cityRecord == null)
        {
            foreach (var statusIndicator in statusIndicators)
            {
                statusIndicator.State = CCStatusState.None;
            }
        }
        else
        {
            for(int i=0; i < statusIndicators.Count; i++)
            {
                var record = cityRecord.GetRecord(MMGameMode.CrashCourse, i);
                if(record.Score == 0)
                {
                    statusIndicators[i].State = CCStatusState.None;
                }
                else
                {
                    bool passed = cityRecord.GetPassed(MMGameMode.CrashCourse, i);
                    statusIndicators[i].State = (passed) ? CCStatusState.Passed : CCStatusState.Failed;
                }
            }
        }
    }

    private void UpdateBackground(string city)
    {
        if(city == "sf")
        {
            AssignBackground("ccsf_bk");
        }
        else
        {
            AssignBackground("cclon_bk");
        }
    }

    public override void Activate()
    {
        base.Activate();
        CityChange();
        SetupStateFromGameState();
    }

    public CrashCourse() : base(MenuID.CrashCourse)
    {
        // create status indicators
        int yPos = 98;
        int xPosPassed = 179;
        int xPosFailed = 225;

        // default values are too far left
        xPosPassed += 12;
        xPosFailed += 12;

        for(int i=0; i < 13; i++)
        {
            switch (i)
            {
                case 3:
                    yPos = 153;
                    break;
                case 4:
                    yPos = 183;
                    break;
                case 7:
                    yPos = 240;
                    break;
                case 8:
                    yPos = 267;
                    break;
                case 0xB:
                    yPos = 321;
                    break;
                case 0xC:
                    yPos = 351;
                    break;
            }

            var statusIndicator = new CCStatus(this, $"CCStatus{i}", 10000 + i, "cc_smchk", xPosPassed, xPosFailed, yPos);
            statusIndicators.Add(statusIndicator);
            AddWidget(statusIndicator);
            yPos += 15;
        }

        // crash course button
        crashRadioButton = AddBMButton("cc_train", 0.078125f, 0.05f * 1, 5);
        crashRadioButton.Group = modesButtonGroup; ;
        crashRadioButton.Sound = MenuSound.BeepDouble;
        crashRadioButton.OnClick += GameModeChange;

        // crash name
        crashNameDropdown = AddTextDropdown("CRASH NAME", 0.0f, .0f, 0.0f, 0.0f, string.Empty);
        crashNameDropdown.OnSelectedIndexChanged += CrashChange;

        crashDropdownDec = AddBMButton("roller_up", "CRASH NAME UP", crashNameDropdown.Rect.x, crashNameDropdown.Rect.y, 3);
        crashDropdownInc = AddBMButton("roller_down", "CRASH NAME DOWN", crashNameDropdown.Rect.x, crashNameDropdown.Rect.y, 3);

        crashDropdownDec.OnClick += crashNameDropdown.Decrement;
        crashDropdownInc.OnClick += crashNameDropdown.Increment;

        // races button
        blitzRadioButton = AddBMButton("cc_blitz", 0.078125f, 0.05f * 1, 5);
        blitzRadioButton.Group = modesButtonGroup; ;
        blitzRadioButton.Sound = MenuSound.BeepDouble;
        blitzRadioButton.OnClick += GameModeChange;

        checkpointRadioButton = AddBMButton("cc_cp", 0.078125f, 0.05f * 1, 5);
        checkpointRadioButton.Group = modesButtonGroup; ;
        checkpointRadioButton.Sound = MenuSound.BeepDouble;
        checkpointRadioButton.OnClick += GameModeChange;

        // race area
        raceNameDropdown = AddTextDropdown("RACE NAME", 0.0f, .0f, 0.0f, 0.0f, string.Empty);
        raceNameDropdown.OnSelectedIndexChanged += RaceChange;

        raceDropdownDec = AddBMButton("roller_up", "RACE NAME UP", raceNameDropdown.Rect.x, raceNameDropdown.Rect.y, 3);
        raceDropdownInc = AddBMButton("roller_down", "RACE NAME DOWN", raceNameDropdown.Rect.x, raceNameDropdown.Rect.y, 3);

        raceDropdownDec.OnClick += raceNameDropdown.Decrement;
        raceDropdownInc.OnClick += raceNameDropdown.Increment;

        // setup next button
        nextMenuButton = AddBMButton("veh_go", "host_dn", 0.0f, 0.0f, 4);
        nextMenuButton.OnClick += NextButtonClicked;

        // setup button group
        modesButtonGroup.Buttons.Add(crashRadioButton);
        modesButtonGroup.Buttons.Add(blitzRadioButton);
        modesButtonGroup.Buttons.Add(checkpointRadioButton);

        // setup description labels
        descLabel = AddBMLabel("desc icons", 0.4844f, 0.25f, null);
        SetDescriptionLabel(descLabel);
    }
}
