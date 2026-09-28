using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class RaceMenuBase : UIMenu
{
    private RaceData raceData;

    private BMButtonGroup gameModeButtonGroup;

    private readonly BMButton cruiseButton;
    private readonly BMButton blitzButton;
    private readonly BMButton checkpointButton;
    private readonly BMButton circuitButton;

    private readonly TextDropdown raceNameDropdown;
    private readonly BMButton raceDropdownInc;
    private readonly BMButton raceDropdownDec;

    private readonly TextDropdown cityDropdown;

    private readonly TextDropdown timeOfDayDropdown;
    private readonly BMButton timeOfDayDropdownInc;
    private readonly BMButton timeOfDayDropdownDec;

    private readonly TextDropdown weatherDropdown;
    private readonly BMButton weatherDropdownInc;
    private readonly BMButton weatherDropdownDec;

    private readonly UISlider sliderPedDensity;
    private readonly UISlider sliderTrafficDensity;
    private readonly UISlider sliderCopDensity;

    private readonly UIIcon racesCover;
    private readonly UIIcon raceThumbnail;

    private TextRoller lapsRoller;
    private TextRoller opponentsRoller;

    private uint blitzEnableMask;
    private uint checkpointEnableMask;
    private uint circuitEnableMask;

    private uint blitzProgressMask;
    private uint checkpointProgressMask;
    private uint circuitProgressMask;

    private MMGameMode SelectedGameMode
    {
        get
        {
            MMGameMode gameModeType = MMGameMode.Cruise;
            if (gameModeButtonGroup.ActiveButtonIndex == 1) gameModeType = MMGameMode.Blitz;
            if (gameModeButtonGroup.ActiveButtonIndex == 2) gameModeType = MMGameMode.Checkpoint;
            if (gameModeButtonGroup.ActiveButtonIndex == 3) gameModeType = MMGameMode.Circuit;
            if (gameModeButtonGroup.ActiveButtonIndex == 4) gameModeType = MMGameMode.CopsNRobbers;
            return gameModeType;
        }
        set
        {
            switch (value)
            {
                case MMGameMode.Cruise: gameModeButtonGroup.ActiveButtonIndex = 0; break;
                case MMGameMode.Blitz: gameModeButtonGroup.ActiveButtonIndex = 1; break;
                case MMGameMode.Checkpoint: gameModeButtonGroup.ActiveButtonIndex = 2; break;
                case MMGameMode.Circuit: gameModeButtonGroup.ActiveButtonIndex = 3; break;
                case MMGameMode.CopsNRobbers: gameModeButtonGroup.ActiveButtonIndex = 4; break;
            }
        }
    }

    public void SetGameState()
    {
        GameState.SelectedCity = CityList.Cities[cityDropdown.SelectedItemIndex].RaceDir;
        GameState.SelectedGameMode = SelectedGameMode;
        GameState.SelectedRace = raceNameDropdown.SelectedItemIndex;
        GameState.SelectedTimeOfDay = (MMTimeOfDay)timeOfDayDropdown.SelectedItemIndex;
        GameState.SelectedWeather = (MMWeather)weatherDropdown.SelectedItemIndex;
        GameState.PedestrianDensity = sliderPedDensity.Value;
        GameState.TrafficDensity = sliderTrafficDensity.Value;
        GameState.CopDensity = sliderCopDensity.Value;
        GameState.OpponentCount = opponentsRoller.Value;
        GameState.LapCount = lapsRoller.Value;

        if (SelectedGameMode != MMGameMode.Cruise && SelectedGameMode != MMGameMode.CrashCourse)
        {
            if(raceData.TryGetData(SelectedGameMode, raceNameDropdown.SelectedItemIndex, GameState.SkillLevel, out var selectedRaceData))
            {
                GameState.TimeLimit = selectedRaceData.TimeLimit;
                GameState.Difficulty = selectedRaceData.Difficulty;
                GameState.CopDensity = selectedRaceData.NumCops; // always overridden in base game
            }
        }
        if (SelectedGameMode == MMGameMode.Checkpoint)
        {
            if (raceData.TryGetData(SelectedGameMode, raceNameDropdown.SelectedItemIndex, GameState.SkillLevel, out var selectedRaceData))
            {
                // checkpoint doesn't allow changing opponent count
                GameState.OpponentCount = selectedRaceData.Opponents;
            }
        }
    }

    public void FromGameState()
    {
        int cityIndex = 0;
        for (int i = 0; i < CityList.Cities.Count; i++)
            if (CityList.Cities[i].RaceDir == GameState.SelectedCity) { cityIndex = i; break; }

        var gameMode = GameState.SelectedGameMode;
        if (gameMode == MMGameMode.CrashCourse)
            gameMode = MMGameMode.Cruise;

        // 1. mode first, so everything below sees the right value
        SelectedGameMode = gameMode;

        // 2. city data, done explicitly instead of via the change event
        cityDropdown.SelectedItemIndex = cityIndex;
        raceData = new RaceData();
        raceData.LoadAll(CityList.Cities[cityIndex].RaceDir);
        SetCityWidgetState(cityIndex);

        // 3. race list now built for the correct mode
        UpdateRaceList();

        int raceIndex = 0;
        if (gameMode != MMGameMode.Cruise && gameMode != MMGameMode.CopsNRobbers)
            raceIndex = Mathf.Clamp(GameState.SelectedRace, 0, raceNameDropdown.Items.Count - 1);

        raceNameDropdown.SelectedItemIndex = raceIndex;
        RaceChange(raceIndex);   // widget state, limits, defaults, map

        // 4. saved values overwrite the per-race defaults
        timeOfDayDropdown.SelectedItemIndex = (int)GameState.SelectedTimeOfDay;
        weatherDropdown.SelectedItemIndex = (int)GameState.SelectedWeather;
        sliderPedDensity.Value = GameState.PedestrianDensity;
        sliderTrafficDensity.Value = GameState.TrafficDensity;
        sliderCopDensity.Value = GameState.CopDensity;
        lapsRoller.Value = GameState.LapCount;
        opponentsRoller.Value = GameState.OpponentCount;

        UpdateMapImage();
    }

    private void UpdateMapImage()
    {
        var cityInfo = CityList.Cities[cityDropdown.SelectedItemIndex];
        string imageName;

        switch (SelectedGameMode)
        {
            case MMGameMode.Cruise:
            case MMGameMode.CopsNRobbers:
                imageName = $"{cityInfo.RaceDir}_maproam";
                break;

            case MMGameMode.Blitz:
                imageName = $"{cityInfo.RaceDir}_mapblitz{raceNameDropdown.SelectedItemIndex}";
                break;

            case MMGameMode.Checkpoint:
                imageName = $"{cityInfo.RaceDir}_maprace{raceNameDropdown.SelectedItemIndex}";
                break;

            case MMGameMode.Circuit:
                imageName = $"{cityInfo.RaceDir}_mapcircuit{raceNameDropdown.SelectedItemIndex}";
                break;

            default:
                return;
        }

        if (AssetManager.Exists("jpg", $"{imageName}.jpg"))
        {
            raceThumbnail.Visible = true;
            raceThumbnail.SetImage(imageName);
        }
        else
        {
            raceThumbnail.Visible = false;
        }
    }

    private void SetRaceWidgetState(MMGameMode selectedGameMode, int raceIndex)
    {
        // set dropdown disabled mask
        uint enableMask = 0xFFFFFFFF;
        switch (selectedGameMode)
        {
            case MMGameMode.Blitz: enableMask = blitzEnableMask; break;
            case MMGameMode.Checkpoint: enableMask = checkpointEnableMask; break;
            case MMGameMode.Circuit: enableMask = circuitEnableMask; break;
        }
        raceNameDropdown.SetDisabledMask(~enableMask);

        if (selectedGameMode == MMGameMode.Cruise || selectedGameMode == MMGameMode.CopsNRobbers)
        {
            timeOfDayDropdown.Enabled = true;
            timeOfDayDropdownDec.Visible = true;
            timeOfDayDropdownInc.Visible = true;

            weatherDropdown.Enabled = true;
            weatherDropdownInc.Visible = true;
            weatherDropdownDec.Visible = true;

            sliderPedDensity.ReadOnly = false;
            sliderTrafficDensity.ReadOnly = false;
            sliderCopDensity.ReadOnly = false;

            opponentsRoller.ReadOnly = false;
            lapsRoller.ReadOnly = false;
        }
        else
        {
            // update dropdown readonly states
            uint progressMask = 0xFFFFFFFF;
            switch (selectedGameMode)
            {
                case MMGameMode.Blitz: progressMask = blitzProgressMask; break;
                case MMGameMode.Checkpoint: progressMask = checkpointProgressMask; break;
                case MMGameMode.Circuit: progressMask = circuitProgressMask; break;
            }

            bool enableRaceSettingsChanges = ((1 << raceIndex) & progressMask) != 0;
            timeOfDayDropdown.Enabled = enableRaceSettingsChanges;
            timeOfDayDropdownDec.Visible = enableRaceSettingsChanges;
            timeOfDayDropdownInc.Visible = enableRaceSettingsChanges;

            weatherDropdown.Enabled = enableRaceSettingsChanges;
            weatherDropdownInc.Visible = enableRaceSettingsChanges;
            weatherDropdownDec.Visible = enableRaceSettingsChanges;

            sliderPedDensity.ReadOnly = !enableRaceSettingsChanges;
            sliderTrafficDensity.ReadOnly = (!enableRaceSettingsChanges || selectedGameMode == MMGameMode.Circuit);
            sliderCopDensity.ReadOnly = !enableRaceSettingsChanges;

            opponentsRoller.ReadOnly = !enableRaceSettingsChanges;
            lapsRoller.ReadOnly = !enableRaceSettingsChanges;
        }
    }

    private void SetCityWidgetState(int cityIndex)
    {
        var cityInfo = CityList.Cities[cityIndex];

        blitzButton.Enabled = (cityInfo.BlitzNames.Length > 0);
        checkpointButton.Enabled = (cityInfo.CheckpointNames.Length > 0);
        circuitButton.Enabled = (cityInfo.CircuitNames.Length > 0);

        var cityRecord = PlayerManager.OpenCityRecord(cityInfo.RaceDir);
        if (cityRecord != null)
        {
            blitzEnableMask = 0xFFFFFFFF;
            checkpointEnableMask = cityRecord.ResolveCheckpointProgress();
            circuitEnableMask = 0xFFFFFFFF;

            blitzProgressMask = (uint)cityRecord.GetPassedMask(MMGameMode.Blitz);
            checkpointProgressMask = (uint)cityRecord.GetPassedMask(MMGameMode.Checkpoint);
            circuitProgressMask = (uint)cityRecord.GetPassedMask(MMGameMode.Circuit);
        }
        else
        {
            blitzEnableMask = 0xFFFFFFFF;
            checkpointEnableMask = 0xFFFFFFFF;
            circuitEnableMask = 0xFFFFFFFF;

            blitzProgressMask = 0xFFFFFFFF;
            checkpointProgressMask = 0xFFFFFFFF;
            circuitProgressMask = 0xFFFFFFFF;
        }
    }

    private void UpdateRaceList()
    {
        var gameModeType = SelectedGameMode;
        bool showRaces = (gameModeType != MMGameMode.Cruise && gameModeType != MMGameMode.CopsNRobbers);
        raceNameDropdown.Visible = showRaces;
        raceDropdownDec.Visible = showRaces;
        raceDropdownInc.Visible = showRaces;
        racesCover.Visible = (gameModeType != MMGameMode.Circuit);

        lapsRoller.Visible = (gameModeType == MMGameMode.Circuit);
        opponentsRoller.Visible = (gameModeType == MMGameMode.Circuit);

        var cityInfo = CityList.Cities[cityDropdown.SelectedItemIndex];
        if (showRaces)
        {
            string items = string.Join("|", cityInfo.GetRaceNames(gameModeType));
            raceNameDropdown.SetItems(items);
        }
    }

    private void UpdateLimits()
    {
        int raceIndex = raceNameDropdown.SelectedItemIndex;
        switch (SelectedGameMode)
        {
            case MMGameMode.Cruise:
                break;
            case MMGameMode.Blitz:
            case MMGameMode.Checkpoint:
            case MMGameMode.Circuit:
                if (raceData.TryGetData(SelectedGameMode, raceIndex, GameState.SkillLevel, out var data))
                {
                    opponentsRoller.Max = data.Opponents;
                }
                else
                {
                    opponentsRoller.Max = 1;
                }
                break;
        }
    }

    private void SetRaceData()
    {
        int raceIndex = raceNameDropdown.SelectedItemIndex;
        switch(SelectedGameMode)
        {
            case MMGameMode.Cruise:
                sliderPedDensity.Value = 0.25f;
                sliderTrafficDensity.Value = 0.5f;
                sliderCopDensity.Value = 1.0f;

                timeOfDayDropdown.SelectedItemIndex = 1;
                weatherDropdown.SelectedItemIndex = 0;
                break;
            case MMGameMode.Blitz:
            case MMGameMode.Checkpoint:
            case MMGameMode.Circuit:
                if (raceData.TryGetData(SelectedGameMode, raceIndex, GameState.SkillLevel, out var data))
                {
                    sliderPedDensity.Value = data.PedestrianDensity;
                    sliderTrafficDensity.Value = data.TrafficDensity;
                    sliderCopDensity.Value = (data.NumCops > 0) ? 1.0f : 0.0f;

                    timeOfDayDropdown.SelectedItemIndex = (int)data.TimeOfDay;
                    weatherDropdown.SelectedItemIndex = (int)data.Weather;

                    lapsRoller.Value = data.NumLaps;
                    opponentsRoller.Value = data.Opponents;
                }
                break;
        }
    }

    private void GameModeChange()
    {
        UpdateRaceList();
        RaceChange(raceNameDropdown.SelectedItemIndex);
    }

    private void RaceChange(int obj)
    {
        SetRaceWidgetState(SelectedGameMode, obj);
        UpdateLimits();
        SetRaceData();
        UpdateMapImage();
    }

    private void CityChange(int index)
    {
        var cityInfo = CityList.Cities[index];
        
        raceData = new RaceData();
        raceData.LoadAll(cityInfo.RaceDir);

        SetCityWidgetState(index);
        UpdateRaceList();
        RaceChange(0);
    }

    private void WeatherChange(int obj)
    {
        
    }

    private void TimeOfDayChange(int obj)
    {
        
    }

    public override void Activate()
    {
        base.Activate();
        FromGameState();
    }

    public override void Deactivate()
    {
        SetGameState();
        base.Deactivate();
    }

    public RaceMenuBase(MenuID id, bool multiplayer) : base(id)
    {
        AssignSwitchAudio("UIraces");

        gameModeButtonGroup = new BMButtonGroup();

        // GAME MODES
        string cruiseButtonName = (multiplayer) ? "cruise_m" : "cruise";
        string blitzButtonName = (multiplayer) ? "blitz_m" : "blitz";
        string checkpointButtonName = (multiplayer) ? "cp_m" : "cp";
        string circuitButtonName = (multiplayer) ? "circt_m" : "circuit";

        cruiseButton = AddBMButton(cruiseButtonName, 0.078125f, 0.05f * 1, 5);
        cruiseButton.Group = gameModeButtonGroup;
        cruiseButton.Sound = MenuSound.BeepDouble;
        cruiseButton.OnClick += GameModeChange;

        blitzButton = AddBMButton(blitzButtonName, 0.078125f, 0.05f * 2, 5);
        blitzButton.Group = gameModeButtonGroup;
        blitzButton.Sound = MenuSound.BeepDouble;
        blitzButton.OnClick += GameModeChange;

        checkpointButton = AddBMButton(checkpointButtonName, 0.078125f, 0.05f * 3, 5);
        checkpointButton.Group = gameModeButtonGroup;
        checkpointButton.Sound = MenuSound.BeepDouble;
        checkpointButton.OnClick += GameModeChange;

        circuitButton = AddBMButton(circuitButtonName, 0.078125f, 0.05f * 4, 5);
        circuitButton.Group = gameModeButtonGroup;
        circuitButton.Sound = MenuSound.BeepDouble;
        circuitButton.OnClick += GameModeChange;

        gameModeButtonGroup.Buttons.AddRange(new[] { cruiseButton, blitzButton, checkpointButton, circuitButton});
        gameModeButtonGroup.ActiveButtonIndex = 0;

        // ROLLERS
        lapsRoller = AddTextRoller("LAPS", Rect.zero, 1, 10, 1);
        opponentsRoller = AddTextRoller("OPPONENTS", Rect.zero, 1, 7, 1);

        // RACE SELECT
        raceNameDropdown = AddTextDropdown("RACE NAME", 0.0f, .0f, 0.0f, 0.0f, string.Empty);
        raceDropdownDec = AddBMButton("roller_up", "RACE UP", raceNameDropdown.Rect.x, raceNameDropdown.Rect.y, 3);
        raceDropdownInc = AddBMButton("roller_down", "RACE DOWN", raceNameDropdown.Rect.x, raceNameDropdown.Rect.y, 3);

        raceNameDropdown.OnSelectedIndexChanged += RaceChange;
        raceDropdownDec.OnClick += raceNameDropdown.Decrement;
        raceDropdownInc.OnClick += raceNameDropdown.Increment;

        raceNameDropdown.Visible = false;
        raceDropdownDec.Visible = false;
        raceDropdownInc.Visible = false;

        racesCover = AddIcon("race_cov", 0.453125f, 0.19374999f);
        racesCover.Visible = true;

        // CITY SELECT
        string cityNames = string.Join("|", CityList.Cities.Select(x => x.LocalizedName));
        cityDropdown = AddTextDropdown("RACE LOCALE", 0.178125f, 0.1f, 0.375f, 0.06666667f, cityNames);
        var cityDropdownDec = AddBMButton("roller_up", "RACE LOCALE UP", cityDropdown.Rect.x, cityDropdown.Rect.y, 3);
        var cityDropdownInc = AddBMButton("roller_down", "RACE LOCALE DOWN", cityDropdown.Rect.x, cityDropdown.Rect.y, 3);

        cityDropdown.OnSelectedIndexChanged += CityChange;
        cityDropdownDec.OnClick += cityDropdown.Decrement;
        cityDropdownInc.OnClick += cityDropdown.Increment;

        // TIME WEATHER
        string timeNames = Localization.GetString(LocString.TimeMorning) + "|";
        timeNames += Localization.GetString(LocString.TimeNoon) + "|";
        timeNames += Localization.GetString(LocString.TimeEvening) + "|";
        timeNames += Localization.GetString(LocString.TimeNight);

        string weatherNames = Localization.GetString(LocString.WeatherClear) + "|";
        weatherNames += Localization.GetString(LocString.WeatherCloudy) + "|";
        weatherNames += Localization.GetString(LocString.WeatherFoggy) + "|";
        weatherNames += Localization.GetString(LocString.WeatherRainy);

        timeOfDayDropdown = AddTextDropdown("TIME", 0.178125f, 0.35f, cityDropdown.Rect.width, cityDropdown.Rect.height, timeNames, "drop_frame_bx_medium");
        timeOfDayDropdown.SelectedItemIndex = 1; // noon
        timeOfDayDropdownDec = AddBMButton("roller_up", "TIME UP", timeOfDayDropdown.Rect.x, timeOfDayDropdown.Rect.y, 3);
        timeOfDayDropdownInc = AddBMButton("roller_down", "TIME DOWN", timeOfDayDropdown.Rect.x, timeOfDayDropdown.Rect.y, 3);

        timeOfDayDropdown.OnSelectedIndexChanged += TimeOfDayChange;
        timeOfDayDropdownDec.OnClick += timeOfDayDropdown.Decrement;
        timeOfDayDropdownInc.OnClick += timeOfDayDropdown.Increment;

        weatherDropdown = AddTextDropdown("WEATHER", 0.178125f, 0.5f, cityDropdown.Rect.width, cityDropdown.Rect.height, weatherNames, "drop_frame_bx_medium");
        weatherDropdownDec = AddBMButton("roller_up", "WEATHER UP", weatherDropdown.Rect.x, weatherDropdown.Rect.y, 3);
        weatherDropdownInc = AddBMButton("roller_down", "WEATHER DOWN", weatherDropdown.Rect.x, weatherDropdown.Rect.y, 3);

        weatherDropdown.OnSelectedIndexChanged += WeatherChange;
        weatherDropdownDec.OnClick += weatherDropdown.Decrement;
        weatherDropdownInc.OnClick += weatherDropdown.Increment;

        // SLIDERS
        sliderPedDensity = AddSlider("PEDESTRIAN DENSITY", Rect.zero, false);
        sliderTrafficDensity = AddSlider("TRAFFIC DENSITY", Rect.zero, false);
        sliderCopDensity = AddSlider("COP DENSITY", Rect.zero, false);

        sliderPedDensity.Value = 0.25f;
        sliderTrafficDensity.Value = 0.5f;
        sliderCopDensity.Value = 1.0f;

        // setup race thumb
        raceThumbnail = AddIcon("race map", 0.1125f, 0.4515625f);
        raceThumbnail.SetImage("sf_maproam");

        // setup description labels
        var descLabel = AddBMLabel("race desc icons", 0.4844f, 0.25f, "race_btz|race_cir|race_cp|race_rom|race_cop|host_ffa|host_cvr|host_rt|host_n|host_t|host_p|host_gm");
        SetDescriptionLabel(descLabel);
        SetupDescriptionLabelEvents(blitzButton, circuitButton, checkpointButton, cruiseButton);

        // initial state
        CityChange(0);
    }
}
