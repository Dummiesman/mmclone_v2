using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

enum CircuitState
{
    Init,
    GetReady,
    GetSet,
    Playing,
    Finishing,
    Finished
}

public class SingleCircuit : SingleGame
{
    private RaceData raceData;
    private MMGameMode gameMode => MMGameMode.Circuit;

    private CircuitState state;
    private float stateTimer = 5.0f;

    private readonly HashSet<int> oppsFinished = new HashSet<int>();
    private bool playIntenseMusic = false;
    private bool finalCheckPoint = false;
    private bool damagedOut = false;

    private CircuitHud circuitHud;

    private bool NextRaceAvailable()
    {
        var city = CityList.GetCity(Level.Name);
        if (GameState.SelectedRace < (city.CircuitNames.Length - 1))
        {
            return true;
        }
        return false;
    }

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
                if (cityRecord != null)
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
            GameState.LapCount = data.NumLaps;
            GameState.OpponentCount = data.Opponents;
        }
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

                if (state != CircuitState.Finished && state != CircuitState.Finishing)
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

        Player.HUD.Arrow.enabled = false;

        circuitHud.UpdateCheckpointValues(Waypoints.CheckpointsHit, Waypoints.CheckpointCount);
        circuitHud.UpdatePlaceValues(PlayerPosition, RacerCount);
        circuitHud.UpdateLapValues(Mathf.Min(Waypoints.CurrentLap + 1, Waypoints.NumLaps), Waypoints.NumLaps);

        // update game specific start
        switch (state)
        {
            case CircuitState.Init:
                {
                    StartMusic();
                    DisableRacers();
                    SpeechAudio.PlayPreRace();
                    stateTimer = 5.0f;
                    state = CircuitState.GetReady;
                    PlaySound(GameSound.BeepShort);
                    circuitHud.UpdateLapTime(1, 0.0f); // update first lap time
                    break;
                }
            case CircuitState.GetReady:
                {
                    stateTimer -= Time.deltaTime;
                    if (stateTimer > 1.25f)
                    {
                        Player.HUD.SetMessage(Localization.GetString(LocString.CircuitReady), 1.25f, true);
                    }
                    else
                    {
                        PlaySound(GameSound.BeepShort);
                        state = CircuitState.GetSet;
                    }
                    break;
                }
            case CircuitState.GetSet:
                {
                    stateTimer -= Time.deltaTime;
                    if (stateTimer > 0.0f)
                    {
                        Player.HUD.SetMessage(Localization.GetString(LocString.CircuitSet), 1.25f, true);
                    }
                    else
                    {
                        Player.HUD.SetMessage(Localization.GetString(LocString.CircuitGo), 1.25f, true);
                        PlaySound(GameSound.BeepLong);
                        StartTimers();
                        EnableRacers();
                        state = CircuitState.Playing;
                    }
                    break;
                }
            case CircuitState.Playing:
                {
                    UpdateRaceProgress();
                    UpdateOpponentsFinished();
                    circuitHud.UpdateLapTime(Waypoints.CurrentLap + 1, Waypoints.CurrentLapTime);

                    if (Player.DamagedOut != damagedOut)
                    {
                        if (!damagedOut)
                        {
                            Player.HUD.SetMessage(Localization.GetString(LocString.CircuitDamagePenalty), 5.0f);
                        }
                        damagedOut = Player.DamagedOut;
                    }
                    if(!playIntenseMusic)
                    {
                        bool condition = (Waypoints.NumLaps == 1 && Waypoints.HeadingForFinish) || (Waypoints.NumLaps > 1 && Waypoints.OnFinalLap);
                        if(condition)
                        {
                            Music.DisableAutoIdleSegmentChange = true;
                            Music.AlwaysUseHighIntensityMusic = true;
                            playIntenseMusic = true;
                        }
                    }
                    if(Waypoints.HeadingForFinish && !finalCheckPoint)
                    {
                        SpeechAudio.PlayFinalCheckpoint();
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
                        state = CircuitState.Finishing;
                        stateTimer = 5.0f;
                    }
                    break;
                }
            case CircuitState.Finishing:
                {
                    UpdateRaceProgress();
                    UpdateOpponentsFinished();
                    stateTimer -= Time.deltaTime;
                    if (stateTimer <= 0.0f)
                    {
                        MMAudioMixer.Mute(); // mute game audio
                        ShowResults();
                        state = CircuitState.Finished;
                    };
                    break;
                }
            case CircuitState.Finished:
                UpdateRaceProgress();
                UpdateOpponentsFinished();
                break;
        }
    }

    protected override void InitGameObjects()
    {
        base.InitGameObjects();

        // setup waypoints
        var wpObject = new GameObject("Waypoints");
        wpObject.transform.parent = transform;

        Waypoints = wpObject.AddComponent<MMWaypoints>();
        Waypoints.Init(this, Player.Car, GameState.SelectedCity, $"circuit{GameState.SelectedRace}", GameState.LapCount, false, RaceType.Circuit);

        // setup player spawn
        Player.Car.VehCarSim.SetResetPos(new Vector3(Waypoints.StartPosition.x, Waypoints.StartPosition.y, Waypoints.StartPosition.z));
        Player.Car.VehCarSim.SetResetRotation((Waypoints.StartPosition.w * Mathf.Deg2Rad) + Mathf.PI);
        Player.Car.Reset();

        // setup timer and show it on hud
        Timer.Init(false, 0.0f);
        Player.HUD.Timer.enabled = true;

        // load race props
        Level.InitRaceProps($"circuit{GameState.SelectedRace}");

        // setup wp hud
        circuitHud = this.gameObject.AddComponent<CircuitHud>();
        circuitHud.Init();

        // add objects to hudmap
        InitHudmapObjects();

        // fix spawns
        AdjustPlayerSpawns();

        // load race data
        raceData = new RaceData();
        raceData.Load(gameMode, Level.Name, "mmcircuitdata");
    }

    public override void PlayerHitWaterHandler()
    {
        Player.Car.Reset();
    }

    public override void Reset()
    {
        Music.DisableAutoIdleSegmentChange = false;
        Music.AlwaysUseHighIntensityMusic = false;
        finalCheckPoint = false;
        playIntenseMusic = false;
        damagedOut = false;
        oppsFinished.Clear();
        state = CircuitState.Init;
        base.Reset();
    }
}
