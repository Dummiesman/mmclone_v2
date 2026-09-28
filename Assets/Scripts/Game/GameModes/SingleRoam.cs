using UnityEngine;

enum RoamState
{
    Init,
    Roaming,
    DamagedOut
}

public class SingleRoam : SingleGame
{
    private const float DamagedOutDuration = 3.0f;

    private RoamState state;
    private float stateTimer = 0.0f;

    protected override void UpdateGame()
    {
        switch (state)
        {
            case RoamState.Init:
                {
                    StartMusic();
                    SpeechAudio.PlayPreRace();
                    EnableRacers();
                    state = RoamState.Roaming;
                    break;
                }
            case RoamState.Roaming:
                {
                    if (Player.DamagedOut)
                    {
                        Player.Car.SetDrivable(false, VehUndrivableMode.HoldBrakes);
                        stateTimer = DamagedOutDuration;
                        state = RoamState.DamagedOut;
                    }
                    break;
                }
            case RoamState.DamagedOut:
                {
                    stateTimer -= Time.deltaTime;
                    if (stateTimer <= 0.0f)
                    {
                        Player.Car.SetDrivable(true);
                        state = RoamState.Roaming;
                    }
                    break;
                }
        }
    }

    protected override void InitPlayer()
    {
        base.InitPlayer();

        // setup player spawn
        if (Level != null && Level.AINetwork != null && Level.AINetwork.Intersections.Count > 0)
        {
            Vector3 playerSpawn = Level.AINetwork.GetRandomSpawn(0, true, true);
            Player.Car.VehCarSim.ResetPos = playerSpawn;
            Player.Car.Reset();
        }
        else
        {
            Vector3 playerSpawn = new Vector3(0.0f, 20.0f, 0.0f);
            Player.Car.VehCarSim.ResetPos = playerSpawn;
            Player.Car.Reset();
        }
    }

    protected override void InitGameObjects()
    {
        base.InitGameObjects();
        InitHudmapObjects();
    }

    public override void PlayerHitWaterHandler()
    {
        Player.Car.Reset();
    }

    public override void Reset()
    {
        state = RoamState.Init;
        stateTimer = 0.0f;
        base.Reset();
    }
}