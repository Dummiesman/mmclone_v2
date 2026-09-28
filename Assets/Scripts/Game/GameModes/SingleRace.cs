using System.Collections.Generic;
using UnityEngine;

enum RaceState
{
    Init,
    GetReady,
    GetSet,
    Playing,
    Finishing,
    Finished
}

public class SingleRace : SingleGame
{
    private RaceData raceData;
    private MMGameMode gameMode => MMGameMode.Checkpoint;

    private RaceState state;
    private float stateTimer = 5.0f;

    private readonly HashSet<int> oppsFinished = new HashSet<int>();
    private bool finalCheckPoint = false;
    private bool damagedOut = false;

    private WPHud waypointHud;

    private bool ProgressCheck(PlayerCityRecord record, int raceId, int numFinishedRacers)
    {
        var passedMask = record.GetPassedMask(gameMode);
        if ((passedMask & (1 << raceId)) != 0)
        {
            return true;
        }
        if (GameState.SkillLevel == MMSkillLevel.Professional)
        {
            return (numFinishedRacers == 0); // finished first
        }
        else
        {
            return (numFinishedRacers <= 2); // finished in top three
        }
    }

    private void RegisterFinish()
    {
        if (raceData.TryGetData(gameMode, GameState.SelectedRace, GameState.SkillLevel, out var data))
        {
            // only register with the player data if the data matches the original config
            if (GameState.TrafficDensity == data.TrafficDensity && GameState.SelectedTimeOfDay == data.TimeOfDay
                && GameState.SelectedWeather == data.Weather && GameState.CopDensity == data.NumCops)
            {
                var city = CityList.GetCity(Level.Name);
                var cityRecord = PlayerManager.OpenCityRecord(city.RaceDir);
                if(cityRecord != null)
                {
                    var record = new PlayerRecord()
                    {
                        Time = Timer.Value,
                        Passed = ProgressCheck(cityRecord, GameState.SelectedRace, oppsFinished.Count),
                        Score = GetScore(PlayerPosition, GameState.Difficulty),
                        VehicleName = GameState.SelectedVehicle
                    };
                    cityRecord.NewRecord(record, gameMode, GameState.SelectedRace);
                    PlayerManager.SaveCityRecord(city.RaceDir, cityRecord);
                }
            }
        }
    }

    private bool NextRaceAvailable()
    {
        var city = CityList.GetCity(Level.Name);
        if(GameState.SelectedRace < (city.CheckpointNames.Length - 1))
        {
            var cityRecord = PlayerManager.OpenCityRecord(city.RaceDir);
            if(cityRecord != null)
            {
                uint checkpointProgress = cityRecord.ResolveCheckpointProgress();
                return ((checkpointProgress >> (GameState.SelectedRace + 1)) & 1) != 0;
            }
            else
            {
                return true; // no city record available, likely an addon city
            }
        }
        return false;
    }

    private void GameOver()
    {
        int playerPos = PlayerPosition;
        var playerName = PlayerManager.CurrentPlayer.Name;

        PlaySound(GameSound.DamagedOut);
        Player.HUD.ResultsMenu.AddLoser(playerPos, playerName);
        Player.HUD.SetMessage(Localization.GetString(LocString.RaceGameOver), 5.0f);


        // stop music and timers
        StopTimers();
        StopMusic();

        // move to next state
        state = RaceState.Finishing;
        stateTimer = 5.0f;
    }

    public override void NextRace()
    {
        GameState.SelectedRace++;
        if (raceData.TryGetData(gameMode, GameState.SelectedRace, GameState.SkillLevel, out var data))
        {
            GameState.PedestrianDensity = data.PedestrianDensity;
            GameState.TrafficDensity = data.TrafficDensity;
            GameState.CopDensity = data.NumCops;
            GameState.SelectedTimeOfDay = data.TimeOfDay;
            GameState.SelectedWeather = data.Weather;
            GameState.TimeLimit = data.TimeLimit;
            GameState.Difficulty = data.Difficulty;
        }
        GameState.LapCount = 1;
        GameState.EnterGame();
    }

    private void UpdateOpponentsFinished()
    {
        for (int i = 0; i < RacerCount; i++)
        {
            if (!oppsFinished.Contains(i) && HasOpponentFinished(i))
            {
                int finishPos = GetOpponentFinishPosition(i);
                var oppName = Localization.GetOpponentName(i);
                var opppFinishSuffix = Localization.GetOpponentFinishSuffix(finishPos);
                float time = RunningTimer.Value;

                if (state != RaceState.Finished && state != RaceState.Finishing)
                {
                    Player.HUD.SetMessage($"{oppName} {opppFinishSuffix}");
                    PlaySound(GameSound.MessageNote);
                }

                Player.HUD.ResultsMenu.AddName(finishPos, oppName, time);
                oppsFinished.Add(i);
            }
        }
    }

    protected override void UpdateGame()
    {
        // update hud
        Player.HUD.Timer.enabled = true;
        Player.HUD.Timer.UseCountdownTimer = false;
        Player.HUD.Timer.ElapsedSeconds = Timer.Value;

        Player.HUD.Arrow.enabled = (state == RaceState.Playing);
        Player.HUD.Arrow.Target = Waypoints.CurrentTargetPosition;

        waypointHud.UpdateCheckpointValues(Waypoints.CheckpointsHit, Waypoints.CheckpointCount);
        waypointHud.UpdatePlaceValues(PlayerPosition, RacerCount); 

        // update game specific start
        switch (state)
        {
            case RaceState.Init:
                {
                    StartMusic();
                    DisableRacers();
                    SpeechAudio.PlayPreRace();
                    stateTimer = 5.0f;
                    state = RaceState.GetReady;
                    PlaySound(GameSound.BeepShort);
                    UpdateRaceProgress(); // call once to update player position
                    break;
                }
            case RaceState.GetReady:
                {
                    stateTimer -= Time.deltaTime;
                    if (stateTimer > 1.25f)
                    {
                        Player.HUD.SetMessage(Localization.GetString(LocString.RaceReady), 1.25f, true);
                    }
                    else
                    {
                        PlaySound(GameSound.BeepShort);
                        state = RaceState.GetSet;
                    }
                    break;
                }
            case RaceState.GetSet:
                {
                    stateTimer -= Time.deltaTime;
                    if (stateTimer > 0.0f)
                    {
                        Player.HUD.SetMessage(Localization.GetString(LocString.RaceSet), 1.25f, true);
                    }
                    else
                    {
                        Player.HUD.SetMessage(Localization.GetString(LocString.RaceGo), 1.25f, true);
                        PlaySound(GameSound.BeepLong);
                        StartTimers();
                        EnableRacers();
                        state = RaceState.Playing;
                    }
                    break;
                }
            case RaceState.Playing:
                {
                    UpdateRaceProgress();
                    UpdateOpponentsFinished();

                    if(Waypoints.HeadingForFinish && !finalCheckPoint)
                    {
                        SpeechAudio.PlayFinalCheckpoint();
                        Music.DisableAutoIdleSegmentChange = true;
                        Music.AlwaysUseHighIntensityMusic = true;
                        finalCheckPoint = true;
                    }
                    if (Waypoints.Finished)
                    {
                        int playerPos = PlayerPosition;
                        var playerName = PlayerManager.CurrentPlayer.Name;
                        int finishMessageIndex = Mathf.Min(playerPos - 1, 8);

                        // register finish
                        RegisterFinish();

                        // check rewards
                        bool rewardAwarded = CheckReward(out var reward);

                        // setup results
                        Player.HUD.ResultsMenu.AddName(playerPos, playerName, Timer.Value);
                        Player.HUD.ResultsMenu.SetNextRaceAvailable(NextRaceAvailable());
                        Player.HUD.ResultsMenu.SetRewardText(reward.RewardMessage);

                        // set hud text and play audio
                        Player.HUD.SetMessage(Localization.GetString(LocString.RaceFinished1st + finishMessageIndex), 5.0f, true);
                        PlayEndOfRaceSpeech(playerPos, RacerCount, rewardAwarded, reward);
                        PlaySound((playerPos == 1) ? GameSound.FinishRace : GameSound.YouLose);

                        // stop music and timers
                        StopTimers();
                        StopMusic();
                        
                        // stop the player
                        Player.EnterPostRaceMode();

                        // move to next state
                        state = RaceState.Finishing;
                        stateTimer = 5.0f;
                    }
                    if(Player.DamagedOut && !damagedOut)
                    {
                        damagedOut = true;
                        GameOver();
                    }
                    break;
                }
            case RaceState.Finishing:
                {
                    UpdateRaceProgress();
                    UpdateOpponentsFinished();

                    stateTimer -= Time.deltaTime;
                    if (stateTimer <= 0.0f)
                    {
                        MMAudioMixer.Mute(); // mute game audio
                        ShowResults();
                        state = RaceState.Finished;
                    };
                    break;
                }
            case RaceState.Finished:
                {
                    UpdateRaceProgress();
                    UpdateOpponentsFinished();
                }
                break;
        }
    }

    protected override void InitPlayer()
    {
        base.InitPlayer();
        Player.Car.Damage.EnableRepair = false;
    }

    protected override void InitGameObjects()
    {
        base.InitGameObjects();

        // setup waypoints
        var wpObject = new GameObject("Waypoints");
        wpObject.transform.parent = transform;

        Waypoints = wpObject.AddComponent<MMWaypoints>();
        Waypoints.Init(this, Player.Car, GameState.SelectedCity, $"race{GameState.SelectedRace}", 0, false, RaceType.UnorderedFinish); ;

        // setup player spawn
        Player.Car.VehCarSim.SetResetPos(new Vector3(Waypoints.StartPosition.x, Waypoints.StartPosition.y, Waypoints.StartPosition.z));
        Player.Car.VehCarSim.SetResetRotation((Waypoints.StartPosition.w * Mathf.Deg2Rad) + Mathf.PI);
        Player.Car.Reset();

        // setup timer and show it on hud
        Timer.Init(false, 0.0f);
        Player.HUD.Timer.enabled = true;

        // setup wp hud
        waypointHud = this.gameObject.AddComponent<WPHud>();
        waypointHud.Init(WPHudType.PositionAndCheckpoint);

        // load race props
        Level.InitRaceProps($"race{GameState.SelectedRace}");

        // add objects to hudmap
        InitHudmapObjects();

        // fix spawns
        AdjustPlayerSpawns();

        // load race data
        raceData = new RaceData();
        raceData.Load(gameMode, Level.Name, "mmracedata");
    }

    public override void PlayerHitWaterHandler()
    {
        if (state != RaceState.Finishing && state != RaceState.Finished)
        {
            GameOver();
        }
    }

    public override void Reset()
    {
        Music.DisableAutoIdleSegmentChange = false;
        Music.AlwaysUseHighIntensityMusic = false;
        finalCheckPoint = false;
        damagedOut = false;
        oppsFinished.Clear();
        state = RaceState.Init;
        base.Reset();
    }
}
