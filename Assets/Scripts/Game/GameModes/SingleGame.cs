using System.Collections;
using UnityEngine;

public class SingleGame : MMGame
{
    // Timer warning
    private float warningInterval = -1.0f;
    private float timeSinceTimerWarning = 0.0f;

    // Matches the player's InertiaBox * 0.5f half-extents
    private const float AIInertiaBoxScale = 0.5f;

    // Per-opponent state, indexed like Level.AINetwork.Opponents
    private int[] oppCurrentWaypoints = new int[0]; // total waypoints cleared, across laps
    private int[] oppFinishPositions = new int[0];  // 0 = still racing
    private int[] oppPositions = new int[0];        // current standing (1-based)
    private BitArray[] oppHitMasks = new BitArray[0]; // unordered races only
    private Vector3[] oppCarPositions = new Vector3[0];

    /// <summary>True if the opponent at this index (in AINetwork.Opponents) has finished.</summary>
    public bool HasOpponentFinished(int index) =>
        index >= 0 && index < oppFinishPositions.Length && oppFinishPositions[index] != 0;

    /// <summary>The opponent's finishing position (1-based), or 0 if still racing.</summary>
    public int GetOpponentFinishPosition(int index) =>
        (index >= 0 && index < oppFinishPositions.Length) ? oppFinishPositions[index] : 0;

    /// <summary>1-based race position of the player.</summary>
    public int PlayerPosition { get; private set; } = 1;
    public int RacerCount => oppCurrentWaypoints.Length + 1;

    public int GetOpponentPosition(int index) =>
        (index >= 0 && index < oppPositions.Length) ? oppPositions[index] : 0;

    private int WaypointsPerLap => Waypoints.WaypointObjects.Count;   // includes the finish line if any
    private int TotalLaps => Mathf.Max(1, Waypoints.NumLaps);        // numLaps <= 0 means a single lap
    private int FinishIndex => Waypoints.CheckpointCount;            // only valid if HasFinishLine

    protected void DisableRacers()
    {
        Player.Car.SetDrivable(false, VehUndrivableMode.HoldBrakes);
        Player.Car.Damage.Enabled = false;

        foreach (var opponent in Level.AINetwork.Opponents)
        {
            opponent.Car.SetDrivable(false, VehUndrivableMode.HoldBrakes);
            opponent.Car.Damage.Enabled = false;
        }
    }

    protected void EnableRacers()
    {
        Player.Car.SetDrivable(true);
        Player.Car.Damage.Enabled = true;

        foreach (var opponent in Level.AINetwork.Opponents)
        {
            opponent.Car.SetDrivable(true);
            opponent.Car.Damage.Enabled = true;
        }
    }

    protected bool CheckReward(out RewardData reward)
    {
        reward = default;

        var city = CityList.GetCity(Level.Name);
        var cityRecord = PlayerManager.OpenCityRecord(city.RaceDir);
        if (cityRecord != null)
        {
            // check rewards
            var rewardsList = new RewardsList();
            if (rewardsList.Init(city.RaceDir))
            {
                if (rewardsList.CheckReward(GameState.SelectedGameMode, cityRecord, out reward))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Snap player and AI spawns to just above the ground in case they are ex. too high/low
    /// </summary>
    protected void AdjustPlayerSpawns()
    {
        var castMask = ~LayerMask.GetMask("PlayerVehicleBody", "VehicleBody", "Banger");
        if (Player != null && Player.Car != null)
        {
            var resetPos = Player.Car.VehCarSim.ResetPos;
            var castOrigin = resetPos + (Vector3.up * 2.0f);
            var castEnd = resetPos - (Vector3.up * 10.0f);

            if(Physics.Linecast(castOrigin, castEnd, out var hitInfo, castMask))
            {
                Player.Car.VehCarSim.SetResetPos(hitInfo.point + (Vector3.up * 0.9f));
                Player.Car.Reset();
            }
        }
        if(Level != null && Level.AINetwork != null)
        {
            foreach(var opponent in Level.AINetwork.Opponents)
            {
                var resetPos = opponent.Car.VehCarSim.ResetPos;
                var castOrigin = resetPos + (Vector3.up * 2.0f);
                var castEnd = resetPos - (Vector3.up * 10.0f);

                if (Physics.Linecast(castOrigin, castEnd, out var hitInfo, castMask))
                {
                    opponent.Car.VehCarSim.SetResetPos(hitInfo.point + (Vector3.up * 0.9f));
                    opponent.Car.Reset();
                }
            }
        }
    }

    protected int GetScore(int finishPos, float difficulty)
    {
        var vehInfo = VehicleList.GetVehicle(GameState.SelectedVehicle);
        int[] scoreMultipliers = new int[4];

        scoreMultipliers[0] = 0;
        scoreMultipliers[1] = 50;
        scoreMultipliers[2] = 25;
        scoreMultipliers[3] = 10;
        if (finishPos > 3) finishPos = 0;

        return (int)(difficulty * scoreMultipliers[finishPos] * vehInfo.ScoringBias);
    }

    protected void PlayEndOfRaceSpeech(int playerPos, int racerCount, bool rewardAwarded, RewardData reward)
    {
        if (rewardAwarded)
        {
            var vehicleInfo = VehicleList.GetVehicle(reward.VehicleBasename);
            if (reward.IsTextureUnlock)
            {
                SpeechAudio.LoadTextureUnlock(vehicleInfo.BaseName);
                SpeechAudio.PlayTextureUnlock();
            }
            else
            {
                SpeechAudio.LoadVehicleUnlock(vehicleInfo.BaseName);
                SpeechAudio.PlayVehicleUnlock();
            }
        }
        else
        {
            SpeechAudio.PlayResults(playerPos, racerCount);
        }
    }

    // Events
    public override void Reset()
    {
        timeSinceTimerWarning = 1.0f;
        warningInterval = -1.0f;

        ResetTimers();
        StopTimers();
        ResetRaceProgress();
        base.Reset();
    }

    // Race progress
    /// <summary>
    /// Call after the waypoints and opponents are set up, and alongside Waypoints.Reset().
    /// </summary>
    protected void ResetRaceProgress()
    {
        int count = Level.AINetwork.Opponents.Count;
        oppCurrentWaypoints = new int[count];
        oppFinishPositions = new int[count];
        oppPositions = new int[count];
        oppHitMasks = new BitArray[count];
        oppCarPositions = new Vector3[count];

        for (int i = 0; i < count; i++)
        {
            oppPositions[i] = 1;
            ResetOpponentMask(i);
        }

        PlayerPosition = 1;
    }

    /// <summary>
    /// Call once per frame while the race is running.
    /// </summary>
    protected void UpdateRaceProgress()
    {
        if (Waypoints == null || WaypointsPerLap == 0)
            return;

        UpdateOpponentWaypoints();
        UpdateScore();
    }

    private void UpdateOpponentWaypoints()
    {
        var opponents = Level.AINetwork.Opponents;
        int perLap = WaypointsPerLap;

        for (int i = 0; i < oppCurrentWaypoints.Length && i < opponents.Count; i++)
        {
            var car = opponents[i].Car;
            if (car == null || oppFinishPositions[i] != 0)
                continue;

            Transform carTransform = car.transform;
            Vector3 inertiaBox = car.VehCarSim.InertiaBox;

            bool hit;
            if (Waypoints.IsCircuit)
            {
                // Ordered: only the next waypoint in sequence counts
                int next = oppCurrentWaypoints[i] % perLap;
                hit = Waypoints.AIWaypointHit(next, carTransform, inertiaBox, AIInertiaBoxScale);
            }
            else
            {
                // Unordered: anything not in the mask counts (a locked finish line is in the mask)
                hit = Waypoints.AnyAIWaypointHit(oppHitMasks[i], carTransform, inertiaBox, AIInertiaBoxScale) >= 0;
            }

            if (!hit)
                continue;

            oppCurrentWaypoints[i]++;
            int lapProgress = oppCurrentWaypoints[i] % perLap;

            if (lapProgress == 0)
            {
                // Lap complete
                if (oppCurrentWaypoints[i] >= perLap * TotalLaps)
                    oppFinishPositions[i] = NextFinishPosition();
                else
                    ResetOpponentMask(i);
            }
            else if (!Waypoints.IsCircuit && Waypoints.HasFinishLine && lapProgress == perLap - 1)
            {
                // All checkpoints hit, unlock the finish line
                oppHitMasks[i][FinishIndex] = false;
            }
        }
    }

    private void ResetOpponentMask(int index)
    {
        if (Waypoints.IsCircuit)
        {
            oppHitMasks[index] = null; // ordered races use oppCurrentWaypoints % perLap instead
            return;
        }

        var mask = Waypoints.CreateAIHitMask();

        // Lock the finish line by marking it as already hit,
        // so AnyAIWaypointHit skips it until every checkpoint is done
        if (Waypoints.HasFinishLine && Waypoints.CheckpointCount > 0)
            mask[FinishIndex] = true;

        oppHitMasks[index] = mask;
    }

    private int NextFinishPosition()
    {
        int position = Waypoints.Finished ? 2 : 1;
        foreach (int finishPos in oppFinishPositions)
        {
            if (finishPos != 0)
                position++;
        }
        return position;
    }

    // ------------------------------------------------------------------
    // Standings (port of mmSingleCircuit::UpdateScore)
    // ------------------------------------------------------------------

    private void UpdateScore()
    {
        var opponents = Level.AINetwork.Opponents;
        int count = Mathf.Min(oppCurrentWaypoints.Length, opponents.Count);
        for (int i = 0; i < count; i++)
            oppCarPositions[i] = opponents[i].Car != null ? opponents[i].Car.transform.position : Vector3.zero;

        int playerScore = Waypoints.NumClearedWaypoints;
        Vector3 playerPos = Player.Car.transform.position;

        // Player: 1 + opponents who are further along, finished,
        // or level but closer to the player's goal waypoint.
        // Locked once the player finishes, so opponents finishing later don't push them down.
        if (!Waypoints.Finished)
        {
            // Closest selectable waypoint in unordered races, so cycling
            // the target with the hotkey doesn't change the player's position
            int goalIndex = Waypoints.IsCircuit ? Waypoints.TargetWaypoint : Waypoints.SelectNextClosestWaypoint();
            Vector3 goal = WaypointPosition(goalIndex);
            float playerDistSq = (playerPos - goal).sqrMagnitude;

            int position = 1;
            for (int i = 0; i < count; i++)
            {
                if (playerScore < oppCurrentWaypoints[i]
                    || oppFinishPositions[i] != 0
                    || (playerScore == oppCurrentWaypoints[i]
                        && (oppCarPositions[i] - goal).sqrMagnitude < playerDistSq))
                {
                    position++;
                }
            }
            PlayerPosition = position;
        }

        // Opponents: same rule, measured against each opponent's own goal waypoint
        for (int i = 0; i < count; i++)
        {
            if (oppFinishPositions[i] != 0)
            {
                oppPositions[i] = oppFinishPositions[i];
                continue;
            }

            int score = oppCurrentWaypoints[i];
            Vector3 goal = WaypointPosition(OpponentGoal(i));
            float distSq = (oppCarPositions[i] - goal).sqrMagnitude;

            int position = 1;
            for (int j = 0; j < count; j++)
            {
                if (j == i)
                    continue;

                if (score < oppCurrentWaypoints[j]
                    || oppFinishPositions[j] != 0
                    || (score == oppCurrentWaypoints[j]
                        && (oppCarPositions[j] - goal).sqrMagnitude < distSq))
                {
                    position++;
                }
            }

            if (playerScore > score
                || (playerScore == score && (playerPos - goal).sqrMagnitude < distSq))
            {
                position++;
            }

            oppPositions[i] = position;
        }
    }

    private int OpponentGoal(int index)
    {
        if (Waypoints.IsCircuit)
            return oppCurrentWaypoints[index] % WaypointsPerLap;

        // Closest waypoint not in the mask
        var mask = oppHitMasks[index];
        var wps = Waypoints.WaypointObjects;
        int best = -1;
        float bestDistSq = float.MaxValue;
        for (int i = 0; i < wps.Count && i < mask.Length; i++)
        {
            if (mask[i])
                continue;

            float distSq = (wps[i].transform.position - oppCarPositions[index]).sqrMagnitude;
            if (distSq < bestDistSq)
            {
                bestDistSq = distSq;
                best = i;
            }
        }
        return best;
    }

    private Vector3 WaypointPosition(int index)
    {
        var wps = Waypoints.WaypointObjects;
        return (index >= 0 && index < wps.Count) ? wps[index].transform.position : Vector3.zero;
    }

    protected void PlayTimerWarning(float timeLeft)
    {
        float wait = (timeLeft <= 3.0f) ? 0.245f : 1.0f;

        // interval changed: don't carry credit from the slower cadence
        if (wait != warningInterval)
        {
            warningInterval = wait;
            timeSinceTimerWarning = Mathf.Min(timeSinceTimerWarning, wait);
        }

        timeSinceTimerWarning += Time.deltaTime;

        if (timeSinceTimerWarning >= wait)
        {
            PlaySound(GameSound.TimeWarning);
            timeSinceTimerWarning = 0.0f;
        }
    }
}