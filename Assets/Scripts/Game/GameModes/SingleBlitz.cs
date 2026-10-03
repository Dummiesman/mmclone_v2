using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;

enum BlitzState
{
    Init,
    GetReady,
    GetSet,
    Playing,
    Finishing,
    Finished
}

public class SingleBlitz : SingleGame
{
    private RaceData raceData;
    private MMGameMode gameMode => MMGameMode.Blitz;

    private bool outOfTime = false;
    private bool damagedOut = false;
   
    private BlitzState state;
    private float stateTimer = 5.0f;

    private WPHud waypointHud;

    private bool ProgressCheck(PlayerCityRecord record, int raceId)
    {
        return raceId < record.GetNumRaces(gameMode);
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
                        Passed = ProgressCheck(cityRecord, GameState.SelectedRace),
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
        if (GameState.SelectedRace < (city.CheckpointNames.Length - 1))
        {
            return true;
        }
        return false;
    }

    private void GameOver()
    {
        int playerPos = PlayerPosition;
        var playerName = PlayerManager.CurrentPlayer.Name;

        PlaySound(GameSound.DamagedOut);
        Player.HUD.ResultsMenu.AddLoser(playerPos, playerName);
        Player.HUD.SetMessage(Localization.GetString(LocString.BlitzGameOver), 5.0f);

        // stop music and timers
        StopTimers();
        StopMusic();

        // move to next state
        state = BlitzState.Finishing;
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

    protected override void UpdateGame()
    {
        // update hud
        Player.HUD.Timer.enabled = true;
        Player.HUD.Timer.UseCountdownTimer = true;
        Player.HUD.Timer.CountdownSeconds = Timer.Value;

        Player.HUD.Arrow.enabled = (state == BlitzState.Playing);
        Player.HUD.Arrow.Target = Waypoints.CurrentTargetPosition;

        waypointHud.UpdateCheckpointValues(Waypoints.CheckpointsHit, Waypoints.CheckpointCount);

        // update game specific start
        switch (state)
        {
            case BlitzState.Init:
                {
                    StartMusic();
                    DisableRacers();
                    SpeechAudio.PlayPreRace();
                    stateTimer = 5.0f;
                    state = BlitzState.GetReady;
                    PlaySound(GameSound.BeepShort);
                    break;
                }
            case BlitzState.GetReady:
                {
                    stateTimer -= Time.deltaTime;
                    if (stateTimer > 1.25f)
                    {
                        Player.HUD.SetMessage(Localization.GetString(LocString.BlitzReady), 1.25f, true);
                    }
                    else
                    {
                        PlaySound(GameSound.BeepShort);
                        state = BlitzState.GetSet;
                    }
                    break;
                }
            case BlitzState.GetSet:
                {
                    stateTimer -= Time.deltaTime;
                    if(stateTimer > 0.0f)
                    {
                        Player.HUD.SetMessage(Localization.GetString(LocString.BlitzSet), 1.25f, true);
                    }
                    else
                    {
                        Player.HUD.SetMessage(Localization.GetString(LocString.BlitzGo), 1.25f, true);
                        PlaySound(GameSound.BeepLong);
                        StartTimers();
                        EnableRacers();
                        state = BlitzState.Playing;
                    }
                    break;
                }
            case BlitzState.Playing:
                {
                    if(!outOfTime && Timer.Value <= 10.0f)
                    {
                        PlayTimerWarning(Timer.Value);
                    }
                    if(Timer.Value <= 0.0f && !outOfTime)
                    {
                        PlaySound(GameSound.YouLose);
                        outOfTime = true;
                        Player.HUD.SetMessage(Localization.GetString(LocString.BlitzOutOfTime), 5.0f);
                    }
                    if(Waypoints.Finished)
                    {
                        if(outOfTime)
                        {
                            PlaySound(GameSound.YouLose);
                            Player.HUD.SetMessage(Localization.GetString(LocString.BlitzOutOfTime), 5.0f);

                            // setup results
                            var playerName = PlayerManager.CurrentPlayer.Name;
                            Player.HUD.ResultsMenu.AddLoser(1, playerName);
                            Player.HUD.ResultsMenu.SetNextRaceAvailable(NextRaceAvailable());
                            Player.HUD.ResultsMenu.SetRewardText(string.Empty);

                            // play speech
                            SpeechAudio.PlayResultsPoor();
                        }
                        else
                        {
                            Player.HUD.SetMessage(Localization.GetString(LocString.BlitzYouWon), 5.0f, true);
                            PlaySound(GameSound.FinishRace);

                            // register finish
                            RegisterFinish();

                            // check rewards
                            bool rewardAwarded = CheckReward(out var reward);

                            // setup results
                            var playerName = PlayerManager.CurrentPlayer.Name;
                            Player.HUD.ResultsMenu.AddName(1, playerName, Timer.Value);
                            Player.HUD.ResultsMenu.SetNextRaceAvailable(NextRaceAvailable());
                            Player.HUD.ResultsMenu.SetRewardText(reward.RewardMessage);

                            // play speech
                            PlayEndOfRaceSpeech(1, 1, rewardAwarded, reward);
                        }
                        StopTimers();
                        StopMusic();
                        Player.EnterPostRaceMode();
                        state = BlitzState.Finishing;
                        stateTimer = 5.0f;
                    }
                    if (Player.DamagedOut && !damagedOut)
                    {
                        SpeechAudio.PlayDamagePenalty();
                        damagedOut = true;
                        GameOver();
                    }

                    break;
                }
            case BlitzState.Finishing:
                {
                    stateTimer -= Time.deltaTime;
                    if(stateTimer <= 0.0f)
                    {
                        MMAudioMixer.Mute(); // mute game audio
                        ShowResults();
                        state = BlitzState.Finished;
                    };
                    break;
                }
            case BlitzState.Finished:
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
        Waypoints.Init(this, Player.Car, GameState.SelectedCity, $"blitz{GameState.SelectedRace}", 0, false, RaceType.Unordered); ;

        // setup player spawn
        Player.Car.VehCarSim.SetResetPos(new Vector3(Waypoints.StartPosition.x, Waypoints.StartPosition.y, Waypoints.StartPosition.z));
        Player.Car.VehCarSim.SetResetRotation((Waypoints.StartPosition.w * Mathf.Deg2Rad) + Mathf.PI);
        Player.Car.Reset();

        // setup timer and show it on hud
        Timer.Init(true, GameState.TimeLimit);
        Player.HUD.Timer.enabled = true;

        // setup wp hud
        waypointHud = this.gameObject.AddComponent<WPHud>();
        waypointHud.Init(WPHudType.Checkpoint);

        // load race props
        Level.InitRaceProps($"blitz{GameState.SelectedRace}");

        // add objects to hudmap
        InitHudmapObjects();

        // fix spawns
        AdjustPlayerSpawns();

        // load race data
        raceData = new RaceData();
        raceData.Load(gameMode, Level.Name, "mmblitzdata");
    }

    public override void PlayerHitWaterHandler()
    {
        if (state != BlitzState.Finishing && state != BlitzState.Finished)
        {
            GameOver();
        }
    }

    public override void Reset()
    {
        outOfTime = false;
        damagedOut = false;
        state = BlitzState.Init;
        base.Reset();
    }
}
