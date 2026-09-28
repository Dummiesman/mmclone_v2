using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

public enum LocString : int
{
    GameModeCircuit = 5,
    GameModeRace = 6,
    GameModeBlitz = 7,
    GameModeCnRFFASubTitle = 8,
    GameModeCnRFFATitle = 9,
    GameModeCnRTeamsSubTitle = 10,
    GameModeCnRTeamsTitle = 11,
    GameModeCrashCourse = 12,
    Opp1Name = 13,
    Opp2Name = 14,
    Opp3Name = 15,
    Opp4Name = 16,
    Opp5Name = 17,
    Opp6Name = 18,
    Opp7Name = 19,
    Opp8Name = 20,
    OppFinished1st = 21,
    OppFinished2nd = 22,
    OppFinished3rd = 23,
    OppFinished4th = 24,
    OppFinished5th = 25,
    OppFinished6th = 26,
    OppFinished7th = 27,
    OppFinished8th = 28,
    SleepWithTheFishes = 30,
    FinalLap = 62,
    LapTime = 63,
    PlaceholderText = 64,
    DefaultPlayerName = 65,
    DefaultPlayerNetName = 66,
    DriverStats_GameModeCrashCourse = 78,
    DriverStats_GameModeCnR = 79,
    DriverStats_GameModeCruise = 80,
    DifficultyAmateur = 82,
    DifficultyProfessional = 81,
    BlitzReady = 158,
    BlitzSet = 159,
    BlitzGo = 160,
    BlitzOutOfTime = 161,
    BlitzGameOver = 162,
    BlitzYouWon = 164,
    CircuitReady = 165,
    CircuitSet = 166,
    CircuitGo = 167,
    CircuitDamagePenalty = 168,
    RaceReady = 179,
    RaceSet = 180,
    RaceGo = 181,
    RaceGameOver = 182,
    RaceFinished1st = 183,
    RaceFinished2nd = 184,
    RaceFinished3rd = 185,
    RaceFinished4th = 186,
    RaceFinished5th = 187,
    RaceFinished6th = 188,
    RaceFinished7th = 189,
    RaceFinished8th = 190,
    RaceFinishedOutOfBounds = 191,
    StuntEvadeDescription = 193,
    StuntEvadeReady = 194,
    StuntEvadeGo = 195,
    StuntEvadeWin = 196,
    StuntEvadeStillPursued = 197,
    StuntEvadeTimeUp = 198,
    StuntEvadeGetGoing = 652,
    StuntJumpDescription = 206,
    StuntJumpReady = 207,
    StuntJumpGo = 208,
    StuntJumpFinished = 209,
    StuntChaseReady = 218,
    StuntChaseSet = 219,
    StuntChaseGo = 220,
    StuntChaseWin = 221,
    StuntChaseLose = 222,
    StuntFroggerReady = 225,
    StuntFroggerSet = 226,
    StuntFroggerGo = 227,
    StuntFroggerWin = 229,
    StuntFroggerFail = 230,
    StuntCornerMaintainXSpeedThroughWaypoints = 232,
    StuntCornerGo = 233,
    StuntCornerWin = 234,
    StuntCornerHitCheckpointBeforeSpeed1 = 235,
    StuntCornerHitCheckpointBeforeSpeed2 = 236,
    StuntCornerReachedMinSpeed = 237,
    StuntCornerHaveNotMaintainedSpeed = 238,
    StuntCornerYouLost = 239,
    StuntBlitzReady = 241,
    StuntBlitzSet = 242,
    StuntBlitzGo = 243,
    WpHudFont = 253,
    WpHudPlace = 254,
    WpHudCheck = 255,
    CircuitHudFont = 258,
    CircuitHudPlace = 259,
    CircuitHudCheck = 260,
    CircuitHudLap = 261,
    ControlUndefined = 271,
    InputActionChangeCamera = 276,
    InputActionThrillCamera = 277,
    InputActionTransmisison = 278,
    InputActionHorn = 279,
    InputActionThrottle = 280,
    InputActionBrakes = 281,
    InputActionSteering = 282,
    InputActionSteerLeft = 283,
    InputActionSteerRight = 284,
    InputActionLookRight = 285,
    InputActionLookLeft = 286,
    InputActionLookBack = 287,
    InputActionLookForward = 288,
    InputActionWideAngle = 289,
    InputActionDashboard = 290,
    InputActionShiftUp = 291,
    InputActionShiftDown = 292,
    InputActionReverse = 293,
    InputActionNextCheckpoint = 294,
    InputActionPrevCheckpoint = 295,
    InputActionMapToggle = 296,
    InputActionHUDToggle = 297,
    InputActionFullScreenMap = 298,
    InputActionMapZoom = 299,
    InputActionRotatingMap = 300,
    InputActionToggleCDPlayer = 301,
    InputActionStartStopCD = 302,
    InputActionNextCDTrack = 303,
    InputActionPrevCDTrack = 304,
    InputActionRearViewMirror = 305,
    InputActionCameraPan = 306,
    InputActionHandbrake = 307,
    InputActionOpponentPosition = 308,
    InputActionChatMessage = 309,
    StereoModeMono = 326,
    StereoModeStereo = 327,
    StereoModeSurround = 329,
    PopupAudioOptionsName = 442,
    PopupAudioSFX = 443,
    PopupAudioMusic = 444,
    PopupAudioBalance = 445,
    PopupMainRestart = 464,
    PopupMainReplay = 465,
    PopupMainOptions = 466,
    PopupMainHelp = 467,
    PopupMainRaceMenu = 468,
    PopupMainExit = 469,
    PopupPreviousMenu = 470,
    PopupCancel = 471,
    PopupOk = 472,
    PopupExit = 473,
    PopupOptionsTitle = 474,
    PopupOptionsAudio = 475,
    PopupOptionsControl = 476,
    PopupOptionsGraphics = 477,
    ResultsRestart = 492,
    ResultsRestartLesson = 653,
    ResultsNextRace = 493,
    ResultsNextLesson  = 654,
    ResultsShowRoster = 494,
    ResultsReplay = 495,
    ResultsBackToSchool = 496,
    ResultsRaceMenu = 497,
    ResultsExit = 498,
    ResultsDNF = 499,
    TransAutomatic = 579,
    TransManual = 578,
    SettingNone = 660,
    SettingLow = 574,
    SettingMed = 575,
    SettingHigh = 576,
    SettingHighest = 577,
    TexQualLow = 390,
    TexQualMed = 391,
    TexQualHigh = 392,
    TexQualHighest = 393,
    StuntTimeUp = 614,
    StuntGameOver = 615,
    StuntStopReady = 617,
    StuntStopSet = 618,
    StuntStopGo = 619,
    StuntStopYouDidIt = 620,
    StuntStopCarReachedDestination = 621,
    StuntStopYouLost = 622,
    WeatherClear = 625,
    WeatherCloudy = 626,
    WeatherFoggy = 627,
    WeatherRainy = 628,
    TimeMorning = 629,
    TimeNoon = 630,
    TimeEvening = 631,
    TimeNight = 632,
    TransmissionManual = 633,
    TransmissionAutomatic = 634,
    DriverStatsRanking = 635,
    DriverStatsLastRace = 636,
    DriverStatsLastVehicle = 637,
    DriverStatsController = 638,
    DriverStatsNetName = 639,
    DriverStatsScore = 640,
    SleepWithTheFishes_London = 642,
    SleepWithTheFishes_SF = 643,
    CnrFreeForAllDesc = 400,
    CnrCopsVsRobbersDesc = 401,
    CnrRobberTeamsDesc = 402,
    WeatherClearDesc = 408,
    WeatherCloudyDesc = 624,
    WeatherFoggyDesc = 409,
    WeatherRainDesc = 410,
    TimeMorningDesc = 412,
    TimeNoonDesc = 413,
    TimeEveningDesc = 414,
    TimeNightDesc = 415,
    LapsDesc = 418,
    GoldWeightDesc = 423,
    LimitNoneDesc = 426,
    LimitPointsDesc = 424,
    LimitMinutesDesc = 425,
    LimitDesc = 427,
    NoGoldMassDesc = 420,
    QuarterTonGoldMassDesc = 421,
    HalfTonGoldMassDesc = 422,
    NoGoldMass = 506,
    QuarterTonGoldMass = 507,
    HalfTonGoldMass = 508,
    TimeLimit5Min = 510,
    TimeLimit10Min = 511,
    TimeLimit20Min = 512,
    TimeLimit30Min = 513,
    PointLimit100 = 514,
    PointLimit250 = 515,
    PointLimit500 = 516,
    PointLimit1000 = 517,
    StuntGoodDriving = 609,
    PlayerStatsRanking = 635,
    PlayerStatsLastRace = 636,
    PlayerStatsLastVehicle = 637,
    PlayerStatsController = 638,
    PlayerStatsNetname = 639,
    PlayerStatsScore = 640,
    StuntResultsPass = 651,
    EventCruise = 588,
    EventBlitz = 585,
    EventCircuit = 586,
    EventCheckpoint = 587,
    EventCrashCourse = 78,
    EventCopsRobbers = 589,

    ControllerMouse = 580,
    ControllerKeyboard = 581,
    ControllerJoystick = 582,
    ControllerGamepad = 583,
    ControllerSteeringWheel = 584,

    // Fonts
    Font_Main_ArialBold_12_12 = 558,
    Font_Main_ArialBold_14_14 = 559,
    Font_Main_ArialBold_16_16 = 560,
    Font_Main_ArialBold_18_18 = 561,
    Font_Main_ArialBold_18_24 = 562,
    Font_Main_ArialBold_12_16 = 563,
    Font_Main_ArialBold_24_48 = 564,
    Font_Main_ArialBold_32_64 = 565,

    Font_Popup_ArialBold_12_12 = 566,
    Font_Popup_ArialBold_12_14 = 567,
    Font_Popup_ArialBold_14_16 = 568,
    Font_Popup_ArialBold_16_20 = 569,
    Font_Popup_ArialBold_16_24 = 570,
    Font_Popup_ArialBold_20_32 = 571,
    Font_Popup_ArialBold_20_40 = 572,
    Font_Popup_ArialBold_32_64 = 573
}

public class Localization
{
    private static readonly List<string> LocStrings = new List<string>();
    private static bool initialized = false;

    public static void Init()
    {
        if (initialized)
            return;

        // open up a reader on the DLL
        // first check for an embedded copy
        BinaryReader rsrcReader = null;

        TextAsset asset = Resources.Load("mmlang") as TextAsset;
        if (asset != null)
        {
            rsrcReader = new BinaryReader(new MemoryStream(asset.bytes));
        }
        else
        {
            string mmLangPath = Path.Combine(FileSystem.Root, "mmlang.dll");
            if (File.Exists(mmLangPath))
            {
                rsrcReader = new BinaryReader(File.OpenRead(mmLangPath));
            }
            else
            {
                Debug.LogError($"No mmlang @ {mmLangPath}");
            }
        }

        // did we find the DLL?
        if (rsrcReader == null)
        {
            Debug.LogError("Could not find MMLANG.DLL");
            return;
        }

        var rsrcLoader = new WinRsrcLoader();
        try
        {
            rsrcLoader.Read(rsrcReader);
            rsrcReader.Close();
        }
        catch(Exception ex)
        {
            Debug.LogError($"Failed to load localization. {ex}");
            return;
        }
        LocStrings.AddRange(rsrcLoader.StringResources);

        var mmLangCulture = new CultureInfo(rsrcLoader.LanguageId);
        Debug.Log($"MMLANG loaded. Detected language is {mmLangCulture}");
        initialized = true;
    }

    /// <summary>
    /// MM2 has numbers 1 to 10 in the MMLANG
    /// </summary>
    public static string GetNumber(int num)
    {
        if (num > 10 || num < 1)
            return null;
        int langId = 590 + (num - 1);
        return GetString(langId);
    }

    public static string[] GetRange(int start, int end)
    {
        int count = end - start;
        string[] returnArray = new string[count + 1];

        int curIndex = 0;
        for (int i = (int)start; i <= (int)end; i++)
        {
            returnArray[curIndex] = GetString((LocString)i);
            curIndex++;
        }

        return returnArray;
    }

    public static string[] GetRange(LocString start, LocString end)
    {
        return GetRange((int)start, (int)end);
    }

    public static string GetString(LocString id)
    {
        int intId = (int) id;
        if (intId >= LocStrings.Count)
            return id.ToString();
        return LocStrings[intId];
    }

    public static string GetString(int id)
    {
        if (id >= LocStrings.Count)
            return $"UnkLocId_{id}";
        return LocStrings[id];
    }

    public static string GetOpponentName(int id)
    {
        return GetString(LocString.Opp1Name + id);
    }

    public static string GetOpponentFinishSuffix(int finishPos)
    {
        return GetString(LocString.OppFinished1st + (finishPos - 1));
    }

    public static void Dump()
    {
        for (int i = 0; i < LocStrings.Count; i++)
        {
            Debug.Log($"{i}: {LocStrings[i]}");
        }
    }
}
