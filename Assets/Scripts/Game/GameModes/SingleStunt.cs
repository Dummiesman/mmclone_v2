using MM2;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.Experimental.AI;
using UnityEngine.Playables;

enum StuntState
{
    Init,
    DescribeEvent,
    GetReady,
    Playing,
    Finishing,
    Finished
}


public enum StuntEventType
{
    Jump,
    Collide,
    Chase,
    Evade,
    Corner,
    Frogger,
    Accel,
    Blitz1,
    Stop,
    Blitz2,
}

struct StuntEvent
{
    public string Filename;
    public StuntEventType Type;
    public int Checkpoints;
    public float TimeLimit;
    public float AmbDensity;
    public float Extra1;
    public int Extra2;
    public int Extra3;
}

public class SingleStunt : SingleGame
{
    private readonly List<StuntEvent> events = new List<StuntEvent>();
    private int currentEvent = 0;
    private bool currentEventIsFirst => currentEvent == 0;
    private bool currentEventIsLast => (currentEvent == events.Count - 1) || events.Count <= 1;

    private Vector3 playerStartPos;
    private float playerStartRot;

    private WPHud waypointHud;

    private float stateTimer = 0.0f;
    private StuntState state = StuntState.Init;
    private float timeSinceTimerWarning = 0.0f;

    // hudmap
    private List<HudmapItem> hudmapWaypoints = new List<HudmapItem>();

    // stop
    private float stopOpponentMaxDamage = 150000.0f;

    // corner
    private bool cornerMinSpeedReached = false;
    private float timeUnderMinSpeed = 0.0f;
    private float maxTimeUnderMinSpeed = 1.0f;
    private float minCornerSpeed = 0.0f; // mph

    private List<string> raceNames = new List<string>();
    private MMGameMode gameMode => MMGameMode.CrashCourse;

    private void PlayTimerWarning(float timeLeft)
    {
        float waitTimeBetweenPlays = (timeLeft <= 3.0f) ? 0.25f : 1.0f;
        if (timeSinceTimerWarning >= waitTimeBetweenPlays)
        {
            PlaySound(GameSound.TimeWarning);
            timeSinceTimerWarning -= waitTimeBetweenPlays;
        }
        timeSinceTimerWarning += Time.deltaTime;
    }

    private bool NextRaceAvailable()
    {
        // only allow next race when it's not a midterm because the results screen says "next LESSON"
        return GameState.SelectedRace != 2 && GameState.SelectedRace != 6 && GameState.SelectedRace <= 9;
    }

    public override void NextRace()
    {
        // load race data
        var raceData = new RaceData();
        raceData.Load(gameMode, Level.Name, "mmcrashdata");

        GameState.SelectedRace++;
        if (raceData.TryGetData(gameMode, GameState.SelectedRace, GameState.SkillLevel, out var data))
        {
            GameState.PedestrianDensity = data.PedestrianDensity;
            GameState.SelectedTimeOfDay = data.TimeOfDay;
            GameState.SelectedWeather = data.Weather;
        }

        var cityRecord = PlayerManager.OpenCityRecord(Level.Name);
        if(cityRecord != null)
        {
            int passedMask = cityRecord.GetPassedMask(gameMode);
            bool useDefaultVehicle = ((1 << GameState.SelectedRace) & passedMask) == 0;
            if (useDefaultVehicle && GameState.SelectedCity == "sf")
            {
                GameState.SelectedVehicle = "vpbullet";
                GameState.SelectedPaintjob = 0;
            }
            else if (useDefaultVehicle && GameState.SelectedCity == "london")
            {
                GameState.SelectedVehicle = "vpcab";
                GameState.SelectedPaintjob = 0;
            }
        }

        GameState.EnterGame();
    }

    private void LoadEventFile(string name)
    {
        string fullPath = AssetManager.CombinePath("race", Level.Name, $"{name}.csv");
        var parser = AssetManager.OpenCSV(fullPath);
        if(parser != null)
        {
            parser.PrepareHeader();
            while(!parser.EOF())
            {
                parser.PrepareLine();

                // Filename,Event,Checkpoints,TimeLimit,AmbDensity,extra,extra,extra,extra,etra,
                var @event = new StuntEvent();
                @event.Filename = parser[0];
                @event.Type = (StuntEventType)int.Parse(parser[1], CultureInfo.InvariantCulture);
                @event.Checkpoints = int.Parse(parser[2], CultureInfo.InvariantCulture);
                @event.TimeLimit = FastFloatParser.Parse(parser[3]);
                @event.AmbDensity = FastFloatParser.Parse(parser[4]);
                @event.Extra1 = FastFloatParser.Parse(parser[5]);
                @event.Extra2 = int.Parse(parser[6], CultureInfo.InvariantCulture);
                @event.Extra3 = int.Parse(parser[7], CultureInfo.InvariantCulture);
                events.Add(@event);
            }
        }
        else
        {
            Debug.LogError($"SingleStunt - data file {name} not found.");
        }
    }

    private void InitHud()
    {
        var @event = events[currentEvent];
        GameState.TimeLimit = @event.TimeLimit;
        
        // set defaults
        Player.HUD.Arrow.enabled = false;
        Player.HUD.Timer.enabled = false;
        waypointHud.enabled = false;

        // setup event ui
        if (@event.Type == StuntEventType.Jump)
        {
            Timer.Init(true, GameState.TimeLimit);
            Player.HUD.Arrow.enabled = true;
        }
        else if(@event.Type == StuntEventType.Blitz1)
        {
            Timer.Init(true, GameState.TimeLimit);
            Player.HUD.Arrow.enabled = true;
        }
        else if (@event.Type == StuntEventType.Blitz2)
        {
            Timer.Init(true, GameState.TimeLimit);
            Player.HUD.Arrow.enabled = false;
        }
        else if (@event.Type == StuntEventType.Corner)
        {
            Player.HUD.Arrow.enabled = true;
        }
        else if(@event.Type == StuntEventType.Evade)
        {
            Timer.Init(true, GameState.TimeLimit);
            Player.HUD.Arrow.enabled = true;
        }
        else if(@event.Type == StuntEventType.Stop)
        {
            Timer.Init(true, GameState.TimeLimit);
        }
        else if (@event.Type == StuntEventType.Frogger)
        {
            Timer.Init(true, GameState.TimeLimit);
            Player.HUD.Arrow.enabled = true;
        }
        else if(@event.Type == StuntEventType.Chase)
        {
            Timer.Init(false, 0.0f);
        }

        Player.HUD.Timer.enabled = (@event.Type != StuntEventType.Corner);
        waypointHud.enabled = (@event.Type != StuntEventType.Chase && @event.Type != StuntEventType.Stop);

        StopTimers();
        ResetTimers();
    }

    private void InitNewEvent()
    {
        // setup waypoints
        if(Waypoints == null)
        {
            // setup waypoints
            var wpObject = new GameObject("Waypoints");
            wpObject.transform.parent = transform;

            Waypoints = wpObject.AddComponent<MMWaypoints>();
            Waypoints.InitStatic(this, Player.Car, 32);
        }

        foreach (var wp in hudmapWaypoints)
        {
            Player.HUD.Map.RemoveItem(wp);
        }
        hudmapWaypoints.Clear();

        var data = events[currentEvent];
        if(data.Checkpoints != 0)
        {
            if (data.Type != StuntEventType.Jump)
                Waypoints.ReInit(RaceType.Stunt2, Level.Name, data.Filename);
            else
                Waypoints.ReInit(RaceType.Stunt1, Level.Name, data.Filename);
            Waypoints.enabled = true;
            Waypoints.ShowOnlyActiveOnMap = (data.Extra2 != 0);

            foreach(var wpobj in Waypoints.WaypointObjects)
            {
                var item = Player.HUD.Map.AddItem<HudmapItemWaypoint>();
                item.Init(Waypoints, wpobj);
                hudmapWaypoints.Add(item);
            }
        }
        else
        {
            Waypoints.enabled = false;
        }


        // setup corner min speed
        if(data.Type == StuntEventType.Corner)
        {
            minCornerSpeed = data.Extra1;
            if(minCornerSpeed < 1.0f)
            {
                minCornerSpeed = 50.0f;
            }
        }

        // setup hud
        InitHud();

        // setup traffic
        Level.AINetwork.SetTrafficDensity(data.AmbDensity * 0.2f);

        // and finally state
        state = StuntState.Init;
        stateTimer = 0.0f;
    }

    private void InitNextEvent()
    {
        currentEvent++;
        InitNewEvent();
    }

    private void GameOver()
    {
        Player.HUD.Arrow.enabled = false;
        Player.HUD.Timer.enabled = false;
        waypointHud.enabled = false;

        stateTimer = 5.0f;
        state = StuntState.Finishing;
        Player.EnterPostRaceMode();
        SpeechAudio.PlayResultsPoor();
        RegisterFinish(false);
    }

    private int GetOpponentIndex()
    {
        int idx = 0;
        for (int i = 0; i < currentEvent; i++)
        {
            idx += events[i].Extra3;
        }
        return idx;
    }

    private AIRouteRacer GetOpponent()
    {
        if(Level.AINetwork != null)
        {
            int index = GetOpponentIndex();
            if(index >= 0 && index < Level.AINetwork.Opponents.Count)
            {
                return Level.AINetwork.Opponents[index];
            }
        }
        return null;
    }

    private bool CheckCopPursuit()
    {
        var network = Level.AINetwork;
        if(network.PoliceCars.Count == 0)
        {
            return false;
        }

        var collideMask = LayerMask.GetMask("Default");
        foreach(var policeCar in network.PoliceCars)
        {
            if (policeCar.InPursuit)
            {
                var playerCarPos = Player.Car.transform.position + (Vector3.up * 3.5f);
                var policeCarPos = policeCar.Car.transform.position + (Vector3.up * 3.5f);

                if(!Physics.Linecast(playerCarPos, policeCarPos, collideMask))
                {
                    // nothing in the way, check proximity
                    float distance = Vector3.Distance(playerCarPos, policeCarPos);
                    if(distance < 200.0f)
                    {
                        return true;
                    }
                }
            }
        }
        return false;
    }

    private void OutOfTime()
    {
        PlaySound(GameSound.YouLose);
        Player.HUD.SetMessage(Localization.GetString(LocString.StuntTimeUp));
        GameOver();
    }

    private void DamagedOut()
    {
        PlaySound(GameSound.DamagedOut);
        Player.HUD.SetMessage(Localization.GetString(LocString.StuntGameOver));
        GameOver();
    }

    private void WonStunt()
    {
        RegisterFinish(true);

        Player.HUD.Arrow.enabled = false;
        Player.HUD.Timer.enabled = false;
        waypointHud.enabled = false;

        bool rewardAwarded = CheckReward(out var reward);

        // setup results
        var playerName = PlayerManager.CurrentPlayer.Name;
        Player.HUD.ResultsMenu.AddName(1, playerName, Timer.Value);
        Player.HUD.ResultsMenu.SetNextRaceAvailable(NextRaceAvailable());
        Player.HUD.ResultsMenu.SetRewardText(reward.RewardMessage);

        // play speech
        PlayEndOfRaceSpeech(1, 1, rewardAwarded, reward);

        stateTimer = 5.0f;
        state = StuntState.Finishing;
        Player.EnterPostRaceMode();
        PlaySound(GameSound.FinishRace);
    }

    private void RegisterFinish(bool passed)
    {
        string playerName = PlayerManager.CurrentPlayer.Name;
        if (passed)
        {
            Player.HUD.ResultsMenu.AddName(1, playerName, Localization.GetString(LocString.StuntResultsPass));
        }
        else
        {
            Player.HUD.ResultsMenu.AddLoser(1, playerName);
        }

        var city = CityList.GetCity(Level.Name);
        var cityRecord = PlayerManager.OpenCityRecord(city.RaceDir);
        if (cityRecord != null)
        {
            var record = new PlayerRecord()
            {
                Time = Timer.Value,
                Passed = passed,
                Score = GetScore(PlayerPosition, GameState.Difficulty),
                VehicleName = GameState.SelectedVehicle
            };
            cityRecord.NewRecord(record, gameMode, GameState.SelectedRace);
            PlayerManager.SaveCityRecord(city.RaceDir, cityRecord);
        }
    }

    private void EnterFinishedState()
    {
        MMAudioMixer.Mute(); // mute game audio
        ShowResults();
        state = StuntState.Finished;
    }

    private void UpdateJump()
    {
        switch (state)
        {
            case StuntState.Init:
                {
                    if(currentEventIsFirst)
                    {
                        Player.HUD.SetMessage(raceNames[GameState.SelectedRace], 1.25f, true);
                        if(stateTimer <= 1.25f)
                        {
                            stateTimer += Time.deltaTime;
                        }
                        else
                        {
                            ResetTimers();
                            StopTimers();
                            stateTimer = 5.0f;
                            PlaySound(GameSound.BeepShort);
                            state = StuntState.DescribeEvent;
                        }
                    }
                    else
                    {
                        ResetTimers();
                        StopTimers();
                        stateTimer = 0.0f;
                        EnableRacers();
                        state = StuntState.GetReady;
                    }
                    break;
                }
            case StuntState.DescribeEvent:
                {
                    stateTimer -= Time.deltaTime;
                    if(stateTimer > 1.25f)
                    {
                        Player.HUD.SetMessage(Localization.GetString(LocString.StuntBlitzReady), 1.25f, true);
                    }
                    else
                    {
                        state = StuntState.GetReady;
                        stateTimer = 1.25f;
                        PlaySound(GameSound.BeepShort);
                    }
                    break;
                }
            case StuntState.GetReady:
                {
                    stateTimer -= Time.deltaTime;
                    if (stateTimer <= 0)
                    {
                        Player.Car.SetDrivable(true);
                        ResetTimers();
                        StartTimers();
                        PlaySound(GameSound.BeepLong);
                        Player.HUD.SetMessage(Localization.GetString(LocString.StuntBlitzGo), 1.25f, true);
                        state = StuntState.Playing;
                    }
                    else
                    {
                        Player.HUD.SetMessage(Localization.GetString(LocString.StuntBlitzSet), 1.25f, true);
                    }
                    break;
                }
            case StuntState.Playing:
                {
                    if (Waypoints.Finished)
                    {
                        if (currentEventIsLast)
                        {
                            Player.HUD.SetMessage(Localization.GetString(LocString.StuntJumpFinished), 5.0f, true);
                            WonStunt();
                        }
                        else
                        {
                            InitNextEvent();
                        }
                    }
                    else if(Timer.Value <= 0.0f)
                    {
                        OutOfTime();
                    }
                    else if(Player.DamagedOut)
                    {
                        DamagedOut();
                    }
                    break;
                }
            case StuntState.Finishing:
                {
                    stateTimer -= Time.deltaTime;
                    if (stateTimer <= 0.0f)
                    {
                        EnterFinishedState();
                    }
                    break;
                }
            case StuntState.Finished:
                {
                    break;
                }
        }
    }

    private void UpdateBlitz()
    {
        switch (state)
        {
            case StuntState.Init:
                {
                    if (currentEventIsFirst)
                    {
                        Player.HUD.SetMessage(raceNames[GameState.SelectedRace], 1.25f, true);
                        if (stateTimer <= 1.25f)
                        {
                            stateTimer += Time.deltaTime;
                        }
                        else
                        {
                            ResetTimers();
                            StopTimers();
                            stateTimer = 5.0f;
                            PlaySound(GameSound.BeepShort);
                            state = StuntState.DescribeEvent;
                        }
                    }
                    else
                    {
                        ResetTimers();
                        StopTimers();
                        stateTimer = 0.0f;
                        EnableRacers();
                        state = StuntState.GetReady;
                    }
                    break;
                }
            case StuntState.DescribeEvent:
                {
                    stateTimer -= Time.deltaTime;
                    if (stateTimer > 1.25f)
                    {
                        Player.HUD.SetMessage(Localization.GetString(LocString.StuntBlitzReady), 1.25f, true);
                    }
                    else
                    {
                        state = StuntState.GetReady;
                        stateTimer = 1.25f;
                        PlaySound(GameSound.BeepShort);
                    }
                    break;
                }
            case StuntState.GetReady:
                {
                    stateTimer -= Time.deltaTime;
                    if (stateTimer <= 0)
                    {
                        Player.Car.SetDrivable(true);
                        ResetTimers();
                        StartTimers();
                        PlaySound(GameSound.BeepLong);
                        Player.HUD.SetMessage(Localization.GetString(LocString.StuntJumpGo), 1.25f, true);
                        state = StuntState.Playing;
                    }
                    else
                    {
                        Player.HUD.SetMessage(Localization.GetString(LocString.StuntBlitzSet), 1.25f, true);
                    }
                    break;
                }
            case StuntState.Playing:
                {
                    if (Waypoints.Finished)
                    {
                        if (currentEventIsLast)
                        {
                            Player.HUD.SetMessage(Localization.GetString(LocString.StuntGoodDriving), 5.0f, true);
                            WonStunt();
                        }
                        else
                        {
                            InitNextEvent();
                        }
                    }
                    else if (Timer.Value <= 0.0f)
                    {
                        OutOfTime();
                    }
                    else if (Player.DamagedOut)
                    {
                        DamagedOut();
                    }
                    break;
                }
            case StuntState.Finishing:
                {
                    stateTimer -= Time.deltaTime;
                    if (stateTimer <= 0.0f)
                    {
                        EnterFinishedState();
                    }
                    break;
                }
            case StuntState.Finished:
                {
                    break;
                }
        }
    }

    private void UpdateCorner()
    {
        switch (state)
        {
            case StuntState.Init:
                {
                    if (currentEventIsFirst)
                    {
                        Player.HUD.SetMessage(raceNames[GameState.SelectedRace], 1.25f, true);
                        if (stateTimer <= 1.25f)
                        {
                            stateTimer += Time.deltaTime;
                        }
                        else
                        {
                            ResetTimers();
                            StopTimers();
                            stateTimer = 5.0f;
                            PlaySound(GameSound.BeepShort);
                            state = StuntState.DescribeEvent;
                        }
                    }
                    else
                    {
                        ResetTimers();
                        StopTimers();
                        stateTimer = 0.0f;
                        EnableRacers();
                        state = StuntState.GetReady;
                    }
                    break;
                }
            case StuntState.DescribeEvent:
                {
                    stateTimer -= Time.deltaTime;
                    if (stateTimer > 1.25f)
                    {
                        string maintainStr = Localization.GetString(LocString.StuntCornerMaintainXSpeedThroughWaypoints);
                        maintainStr = maintainStr.Replace("%.0f", minCornerSpeed.ToString());
                        Player.HUD.SetMessage(maintainStr, 1.25f, true);
                    }
                    else
                    {
                        state = StuntState.GetReady;
                        stateTimer = 1.25f;
                        PlaySound(GameSound.BeepShort);
                    }
                    break;
                }
            case StuntState.GetReady:
                {
                    stateTimer -= Time.deltaTime;
                    if (stateTimer <= 0)
                    {
                        Player.Car.SetDrivable(true);
                        ResetTimers();
                        StartTimers();
                        PlaySound(GameSound.BeepLong);
                        Player.HUD.SetMessage(Localization.GetString(LocString.StuntCornerGo), 1.25f, true);
                        state = StuntState.Playing;
                    }
                    else
                    {
                        string maintainStr = Localization.GetString(LocString.StuntCornerMaintainXSpeedThroughWaypoints);
                        maintainStr = maintainStr.Replace("%.0f", minCornerSpeed.ToString());
                        Player.HUD.SetMessage(maintainStr, 1.25f, true);
                    }
                    break;
                }
            case StuntState.Playing:
                {
                    if(!cornerMinSpeedReached)
                    {
                        if (Waypoints.NumClearedWaypoints >= 2 )
                        {
                            // lose 
                            Player.HUD.SetMessage(Localization.GetString(LocString.StuntCornerHitCheckpointBeforeSpeed2), 5.0f, true);
                            Player.HUD.SetMessage2(Localization.GetString(LocString.StuntCornerHitCheckpointBeforeSpeed1));
                            PlaySound(GameSound.YouLose);
                            GameOver();
                        }
                        else if(Player.Car.VehCarSim.SpeedInMph >= minCornerSpeed)
                        {
                            Player.HUD.SetMessage(Localization.GetString(LocString.StuntCornerReachedMinSpeed));
                            cornerMinSpeedReached = true;
                        }
                    }
                    else
                    {
                        if(Player.Car.VehCarSim.SpeedInMph < minCornerSpeed)
                        {
                            timeUnderMinSpeed += Time.deltaTime;
                            if(timeUnderMinSpeed >= maxTimeUnderMinSpeed)
                            {
                                // lose 
                                Player.HUD.SetMessage(Localization.GetString(LocString.StuntCornerHaveNotMaintainedSpeed), 5.0f, true);
                                Player.HUD.SetMessage2(Localization.GetString(LocString.StuntCornerYouLost));
                                PlaySound(GameSound.YouLose);
                                GameOver();
                            }
                        }
                        else
                        {
                            timeUnderMinSpeed = 0.0f;
                        }
                    }
                 
                    if (Waypoints.Finished)
                    {
                        if (currentEventIsLast)
                        {
                            Player.HUD.SetMessage(Localization.GetString(LocString.StuntGoodDriving), 5.0f, true);
                            WonStunt();
                        }
                        else
                        {
                            InitNextEvent();
                        }
                    }
                    else if (Timer.Value <= 0.0f)
                    {
                        OutOfTime();
                    }
                    else if (Player.DamagedOut)
                    {
                        DamagedOut();
                    }
                    break;
                }
            case StuntState.Finishing:
                {
                    stateTimer -= Time.deltaTime;
                    if (stateTimer <= 0.0f)
                    {
                        EnterFinishedState();
                    }
                    break;
                }
            case StuntState.Finished:
                {
                    break;
                }
        }
    }

    private void UpdateEvade()
    {
        switch (state)
        {
            case StuntState.Init:
                {
                    if (currentEventIsFirst)
                    {
                        Player.HUD.SetMessage(raceNames[GameState.SelectedRace], 1.25f, true);
                        if (stateTimer <= 1.25f)
                        {
                            stateTimer += Time.deltaTime;
                        }
                        else
                        {
                            ResetTimers();
                            StopTimers();
                            stateTimer = 5.0f;
                            PlaySound(GameSound.BeepShort);
                            state = StuntState.DescribeEvent;
                        }
                    }
                    else
                    {
                        ResetTimers();
                        StopTimers();
                        stateTimer = 0.0f;
                        EnableRacers();
                        state = StuntState.GetReady;
                    }
                    break;
                }
            case StuntState.DescribeEvent:
                {
                    stateTimer -= Time.deltaTime;
                    if (stateTimer > 1.25f)
                    {
                        Player.HUD.SetMessage(Localization.GetString(LocString.StuntEvadeDescription), 1.25f, true);
                    }
                    else
                    {
                        state = StuntState.GetReady;
                        stateTimer = 1.25f;
                        PlaySound(GameSound.BeepShort);
                    }
                    break;
                }
            case StuntState.GetReady:
                {
                    stateTimer -= Time.deltaTime;
                    if (stateTimer <= 0)
                    {
                        Player.Car.SetDrivable(true);
                        ResetTimers();
                        StartTimers();
                        PlaySound(GameSound.BeepLong);
                        Player.HUD.SetMessage(Localization.GetString(LocString.StuntEvadeGetGoing), 1.25f, true);
                        state = StuntState.Playing;
                    }
                    else
                    {
                        Player.HUD.SetMessage(Localization.GetString(LocString.StuntEvadeReady), 1.25f, true);
                    }
                    break;
                }
            case StuntState.Playing:
                {
                    if (Waypoints.Finished)
                    {
                        if (CheckCopPursuit())
                        {
                            Player.HUD.SetMessage(Localization.GetString(LocString.StuntEvadeStillPursued), 5.0f, true);
                            PlaySound(GameSound.YouLose);
                            GameOver();
                        }
                        else
                        {
                            if (currentEventIsLast)
                            {
                                Player.HUD.SetMessage(Localization.GetString(LocString.StuntEvadeWin), 5.0f, true);
                                WonStunt();
                            }
                            else
                            {
                                InitNextEvent();
                            }
                        }
                    }
                    else if (Timer.Value <= 0.0f)
                    {
                        OutOfTime();
                    }
                    break;
                }
            case StuntState.Finishing:
                {
                    stateTimer -= Time.deltaTime;
                    if (stateTimer <= 0.0f)
                    {
                        EnterFinishedState();
                    }
                    break;
                }
            case StuntState.Finished:
                {
                    break;
                }
        }
    }

    private void UpdateStop()
    {
        var opponent = GetOpponent();
        switch (state)
        {
            case StuntState.Init:
                {
                    if (currentEventIsFirst)
                    {
                        Player.HUD.SetMessage(raceNames[GameState.SelectedRace], 1.25f, true);
                        if (stateTimer <= 1.25f)
                        {
                            stateTimer += Time.deltaTime;
                        }
                        else
                        {
                            ResetTimers();
                            StopTimers();
                            stateTimer = 5.0f;
                            PlaySound(GameSound.BeepShort);
                            state = StuntState.DescribeEvent;
                        }
                    }
                    else
                    {
                        ResetTimers();
                        StopTimers();
                        stateTimer = 0.0f;
                        EnableRacers();
                        state = StuntState.GetReady;
                    }
                    break;
                }
            case StuntState.DescribeEvent:
                {
                    stateTimer -= Time.deltaTime;
                    if (stateTimer > 1.25f)
                    {
                        Player.HUD.SetMessage(Localization.GetString(LocString.StuntStopReady), 1.25f, true);
                    }
                    else
                    {
                        state = StuntState.GetReady;
                        stateTimer = 1.25f;
                        PlaySound(GameSound.BeepShort);
                    }
                    break;
                }
            case StuntState.GetReady:
                {
                    stateTimer -= Time.deltaTime;
                    if (stateTimer <= 0)
                    {
                        // set opponent max damage
                        if(opponent != null)
                        {
                            opponent.Car.Damage.MedDamage = stopOpponentMaxDamage * 0.5f;
                            opponent.Car.Damage.MaxDamage = stopOpponentMaxDamage;
                        }
                        
                        // and start
                        EnableRacers();
                        ResetTimers();
                        StartTimers();
                        PlaySound(GameSound.BeepLong);
                        Player.HUD.SetMessage(Localization.GetString(LocString.StuntStopGo), 1.25f, true);
                        state = StuntState.Playing;
                    }
                    else
                    {
                        Player.HUD.SetMessage(Localization.GetString(LocString.StuntStopSet), 1.25f, true);
                    }
                    break;
                }
            case StuntState.Playing:
                {
                    if(opponent != null)
                    {
                        float damagePct = opponent.Car.Damage.DamagePercentage;
                        if (damagePct >= 1.0f)
                        {
                            if (currentEventIsLast)
                            {
                                Player.HUD.SetMessage(Localization.GetString(LocString.StuntStopYouDidIt), 5.0f, true);
                                WonStunt();
                            }
                            else
                            {
                                InitNextEvent();
                            }
                        }
                        else
                        {
                            if(opponent.Finished())
                            {
                                Player.HUD.SetMessage(Localization.GetString(LocString.StuntStopCarReachedDestination), 5.0f, true);
                                Player.HUD.SetMessage2(Localization.GetString(LocString.StuntStopYouLost));
                                PlaySound(GameSound.YouLose);
                                GameOver();
                            }
                        }
                    }
                    if (Timer.Value <= 0.0f)
                    {
                        OutOfTime();
                    }
                    break;
                }
            case StuntState.Finishing:
                {
                    stateTimer -= Time.deltaTime;
                    if (stateTimer <= 0.0f)
                    {
                        EnterFinishedState();
                    }
                    break;
                }
            case StuntState.Finished:
                {
                    break;
                }
        }
    }

    private void UpdateFrogger()
    {
        switch (state)
        {
            case StuntState.Init:
                {
                    if (currentEventIsFirst)
                    {
                        Player.HUD.SetMessage(raceNames[GameState.SelectedRace], 1.25f, true);
                        if (stateTimer <= 1.25f)
                        {
                            stateTimer += Time.deltaTime;
                        }
                        else
                        {
                            ResetTimers();
                            StopTimers();
                            stateTimer = 5.0f;
                            PlaySound(GameSound.BeepShort);
                            state = StuntState.DescribeEvent;
                        }
                    }
                    else
                    {
                        ResetTimers();
                        StopTimers();
                        stateTimer = 0.0f;
                        EnableRacers();
                        state = StuntState.GetReady;
                    }
                    break;
                }
            case StuntState.DescribeEvent:
                {
                    stateTimer -= Time.deltaTime;
                    if (stateTimer > 1.25f)
                    {
                        Player.HUD.SetMessage(Localization.GetString(LocString.StuntFroggerReady), 1.25f, true);
                    }
                    else
                    {
                        state = StuntState.GetReady;
                        stateTimer = 1.25f;
                        PlaySound(GameSound.BeepShort);
                    }
                    break;
                }
            case StuntState.GetReady:
                {
                    stateTimer -= Time.deltaTime;
                    if (stateTimer <= 0)
                    {
                        // set player max damage
                        Player.Car.Damage.MaxDamage = 10.0f;
                        Player.Car.Damage.MedDamage = 5.0f;
                        Player.Car.Damage.ImpactThreshold = 0.0f;

                        // and start
                        EnableRacers();
                        ResetTimers();
                        StartTimers();
                        PlaySound(GameSound.BeepLong);
                        Player.HUD.SetMessage(Localization.GetString(LocString.StuntFroggerGo), 1.25f, true);
                        state = StuntState.Playing;
                    }
                    else
                    {
                        Player.HUD.SetMessage(Localization.GetString(LocString.StuntFroggerSet), 1.25f, true);
                    }
                    break;
                }
            case StuntState.Playing:
                {
                    if(Timer.Value < 10.0f)
                    {
                        PlayTimerWarning(Timer.Value);
                    }
                    if (Player.Car.Damage.DamagePercentage >= 1.0f)
                    {
                        Player.HUD.SetMessage(Localization.GetString(LocString.StuntFroggerFail), 5.0f, true);
                        PlaySound(GameSound.YouLose);
                        GameOver();
                    }
                    else if (Timer.Value <= 0.0f)
                    {
                        OutOfTime();
                    }
                    else if (Waypoints.Finished)
                    {
                        if (currentEventIsLast)
                        {
                            Player.HUD.SetMessage(Localization.GetString(LocString.StuntFroggerWin), 5.0f, true);
                            WonStunt();
                        }
                        else
                        {
                            InitNextEvent();
                        }
                    }
                    break;
                }
            case StuntState.Finishing:
                {
                    stateTimer -= Time.deltaTime;
                    if (stateTimer <= 0.0f)
                    {
                        EnterFinishedState();
                    }
                    break;
                }
            case StuntState.Finished:
                {
                    break;
                }
        }
    }

    private void UpdateChase()
    {
        var opponent = GetOpponent();
        switch (state)
        {
            case StuntState.Init:
                {
                    if (currentEventIsFirst)
                    {
                        Player.HUD.SetMessage(raceNames[GameState.SelectedRace], 1.25f, true);
                        if (stateTimer <= 1.25f)
                        {
                            stateTimer += Time.deltaTime;
                        }
                        else
                        {
                            ResetTimers();
                            StopTimers();
                            stateTimer = 5.0f;
                            PlaySound(GameSound.BeepShort);
                            state = StuntState.DescribeEvent;
                        }
                    }
                    else
                    {
                        ResetTimers();
                        StopTimers();
                        stateTimer = 0.0f;
                        EnableRacers();
                        state = StuntState.GetReady;
                    }
                    break;
                }
            case StuntState.DescribeEvent:
                {
                    stateTimer -= Time.deltaTime;
                    if (stateTimer > 1.25f)
                    {
                        Player.HUD.SetMessage(Localization.GetString(LocString.StuntChaseReady), 1.25f, true);
                    }
                    else
                    {
                        state = StuntState.GetReady;
                        stateTimer = 1.25f;
                        PlaySound(GameSound.BeepShort);
                    }
                    break;
                }
            case StuntState.GetReady:
                {
                    stateTimer -= Time.deltaTime;
                    if (stateTimer <= 0)
                    {
                        EnableRacers();
                        ResetTimers();
                        StartTimers();
                        PlaySound(GameSound.BeepLong);
                        Player.HUD.SetMessage(Localization.GetString(LocString.StuntChaseGo), 1.25f, true);
                        state = StuntState.Playing;
                    }
                    else
                    {
                        Player.HUD.SetMessage(Localization.GetString(LocString.StuntChaseSet), 1.25f, true);
                    }
                    break;
                }
            case StuntState.Playing:
                {
                    if (opponent != null)
                    {
                        var oppDistance = Vector3.Distance(opponent.Car.transform.position, Player.Car.transform.position);
                        if((Waypoints.Finished || opponent.Finished()) && oppDistance < 10.0f)
                        {
                            if (currentEventIsLast)
                            {
                                Player.HUD.SetMessage(Localization.GetString(LocString.StuntChaseWin), 5.0f, true);
                                WonStunt();
                            }
                            else
                            {
                                InitNextEvent();
                            }
                        }
                        else if(oppDistance > 100.0f)
                        {
                            Player.HUD.SetMessage(Localization.GetString(LocString.StuntChaseLose), 5.0f, true);
                            PlaySound(GameSound.YouLose);
                            GameOver();
                        }
                    }
                    break;
                }
            case StuntState.Finishing:
                {
                    stateTimer -= Time.deltaTime;
                    if (stateTimer <= 0.0f)
                    {
                        EnterFinishedState();
                    }
                    break;
                }
            case StuntState.Finished:
                {
                    break;
                }
        }
    }

    protected override void UpdateGame()
    {
        if (Player.HUD.Timer.enabled)
        {
            Player.HUD.Timer.UseCountdownTimer = Timer.CountDownMode;
            Player.HUD.Timer.ElapsedSeconds = Timer.Value;
            Player.HUD.Timer.CountdownSeconds = Timer.Value;
        }
        if (waypointHud.enabled)
        {
            waypointHud.UpdateCheckpointValues(Waypoints.CheckpointsHit, Waypoints.CheckpointCount);
        }
        if(Player.HUD.Arrow.enabled)
        {
            Player.HUD.Arrow.Target = Waypoints.CurrentTargetPosition;
        }

        if (currentEvent >= 0 && currentEvent < events.Count)
        {
            var data = events[currentEvent];

            // update game specific
            switch (data.Type)
            {
                case StuntEventType.Jump:
                    UpdateJump();
                    break;
                case StuntEventType.Corner:
                    UpdateCorner();
                    break;
                case StuntEventType.Blitz1:
                case StuntEventType.Blitz2:
                    UpdateBlitz();
                    break;
                case StuntEventType.Evade:
                    UpdateEvade();
                    break;
                case StuntEventType.Stop:
                    UpdateStop();
                    break;
                case StuntEventType.Frogger:
                    UpdateFrogger();
                    break;
                case StuntEventType.Chase:
                    UpdateChase();
                    break;
                default:
                    Debug.Log($"Event type unhandled: {data.Type}");
                    break;
            }
        }
    }

    protected override void InitGameObjects()
    {
        raceNames.AddRange(CityList.GetCity(Level.Name).GetCrashCourseNames());
        InitHudmapObjects();

        int raceNum = GameState.SelectedRace;
        if (GameState.SkillLevel == MMSkillLevel.Professional)
            LoadEventFile($"crash{raceNum}data_p");
        else
            LoadEventFile($"crash{raceNum}data");

        // setup wp hud
        waypointHud = this.gameObject.AddComponent<WPHud>();
        waypointHud.Init(WPHudType.Checkpoint);
        waypointHud.enabled = false;

        // load race props
        Level.InitRaceProps($"crash{GameState.SelectedRace}");

        // setup event
        if (events.Count > 0)
        {
            InitNewEvent();

            // setup player spawn
            Player.Car.VehCarSim.SetResetPos(new Vector3(Waypoints.StartPosition.x, Waypoints.StartPosition.y, Waypoints.StartPosition.z));
            Player.Car.VehCarSim.SetResetRotation((Waypoints.StartPosition.w * Mathf.Deg2Rad) + Mathf.PI);
            Player.Car.Reset();
        }

        // fix spawns
        AdjustPlayerSpawns();

        base.InitGameObjects();
    }

    public override void PlayerHitWaterHandler()
    {
        if (state != StuntState.Finishing && state != StuntState.Finished)
        {
            PlaySound(GameSound.DamagedOut);
            GameOver();
        }
    }

    public override void Reset()
    {
        base.Reset();

        if (currentEvent > 0)
        {
            currentEvent = 0;
            InitNewEvent();
        }
        else
        {
            InitHud();
        }
        DisableRacers();
        StopTimers();
        
        cornerMinSpeedReached = false;
        timeUnderMinSpeed = 0.0f;

        state = StuntState.Init;
        stateTimer = 0.0f;
        timeSinceTimerWarning = 1.0f;

        SpeechAudio.PlayCat("PRERACE");
        StartMusic();
    }
}
