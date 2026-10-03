using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public enum RaceType
{
    /// <summary>
    /// Checkpoints must be hit in sequence, then the finish line.
    /// The first point in the race file is both the spawn point and the finish line.
    /// </summary>
    Circuit,

    /// <summary>
    /// Checkpoints in any order; the finish line stays hidden and locked
    /// until they're all hit, then appears and unlocks.
    /// </summary>
    UnorderedFinish,

    /// <summary>No finish line; the lap ends when every checkpoint is hit.</summary>
    Unordered,

    /// <summary>
    /// ReInit layout. Point 0 is the spawn point, supplying the start position and
    /// heading; it's never targeted, hit or drawn. The rest are checkpoints hit in
    /// any order, and the last point is the finish line, hidden and locked until
    /// every checkpoint is hit.
    /// </summary>
    Stunt1,

    /// <summary>
    /// ReInit layout. Point 0 is the spawn point, supplying the start position and
    /// heading; it's never targeted, hit or drawn. The rest are checkpoints hit
    /// strictly in sequence. No finish line: the lap ends when the last one is hit.
    /// </summary>
    Stunt2,
}

public class MMWaypoints : MonoBehaviour
{
    public IReadOnlyList<WaypointObject> WaypointObjects => waypoints;
    public IReadOnlyList<WaypointObject> WaypointPool => pool;

    /// <summary>
    /// The waypoints in play this race. With InitStatic/ReInit this is a prefix of
    /// pool; with Init it holds everything pool does.
    /// </summary>
    private readonly List<WaypointObject> waypoints = new List<WaypointObject>();

    /// <summary>
    /// Every waypoint object this component has created, in play or spare.
    /// Owns their lifetime, so OnDestroy tears down this list rather than waypoints.
    /// </summary>
    private readonly List<WaypointObject> pool = new List<WaypointObject>();

    /// <summary>
    /// Which waypoints have been hit this lap, parallel to waypoints.
    /// This is the race's progress state. WaypointObject.Active now means only
    /// "drawn", so under ShowOnlyActive a waypoint can be live and dark at once.
    /// </summary>
    private readonly List<bool> cleared = new List<bool>();

    /// <summary>Waypoints InitStatic preallocated; a ReInit layout is truncated to fit.</summary>
    public int WaypointCapacity => pool.Count;

    /// <summary>
    /// When set, only the current target waypoint is drawn; the others stay live and
    /// hittable, they're just not shown in the world or on the hudmap. Belongs to the
    /// layout, so it's chosen by ReInit and survives Reset.
    /// </summary>
    public bool ShowOnlyActive { get; private set; }

    public Vector4 StartPosition => startPos;

    public int TargetWaypoint => targetWaypoint;
    public RaceType Type => raceType;
    public bool IsCircuit => raceType == RaceType.Circuit;
    public bool HasFinishLine => raceType != RaceType.Unordered && raceType != RaceType.Stunt2;
    public bool Finished => finished;

    public int CurrentLap => currentLap;
    public int NumLaps => numLaps;

    /// <summary>
    /// Total waypoints the player has cleared across all laps, finish line crossings included.
    /// Every lap clears exactly WaypointObjects.Count waypoints. In the ReInit layouts
    /// (Stunt1, Stunt2) the start gate is counted at the start of the lap rather than
    /// driven through, which is what the original gets from the parked car crossing its
    /// plane on frame one.
    /// Matches mmWaypoints::NumClearedWaypoints, used as the player's score.
    /// </summary>
    public int NumClearedWaypoints => numClearedWaypoints;

    /// <summary>
    /// Number of checkpoints per lap: excludes the finish line if the race has one,
    /// and the start gate in the ReInit layouts. Checkpoints run from FirstCheckpointIndex
    /// up to (but not including) CheckpointEndIndex.
    /// </summary>
    public int CheckpointCount => Mathf.Max(0, CheckpointEndIndex - FirstCheckpointIndex);

    /// <summary>
    /// Checkpoints the player has hit this lap (start gate and finish line not included).
    /// </summary>
    public int CheckpointsHit
    {
        get
        {
            int hit = 0;
            for (int i = FirstCheckpointIndex; i < CheckpointEndIndex; i++)
            {
                if (cleared[i])
                    hit++;
            }
            return hit;
        }
    }

    /// <summary>
    /// World position of the current target waypoint,
    /// or Vector3.zero if there is no target (race finished or no waypoints).
    /// </summary>
    public Vector3 CurrentTargetPosition =>
        (targetWaypoint >= 0 && targetWaypoint < waypoints.Count)
            ? waypoints[targetWaypoint].transform.position
            : Vector3.zero;

    /// <summary>True on the last lap (always true for single-lap races).</summary>
    public bool OnFinalLap => numLaps <= 0 || currentLap >= numLaps - 1;

    /// <summary>
    /// True when only the last waypoint of the current lap is left: the finish line
    /// (Circuit, UnorderedFinish, Stunt1) or the last remaining checkpoint (Unordered, Stunt2).
    /// </summary>
    public bool HeadingForLapEnd
    {
        get
        {
            if (finished || waypoints.Count == 0)
                return false;

            return HasFinishLine
                ? CheckpointsHit == CheckpointCount
                : CheckpointCount - CheckpointsHit == 1;
        }
    }

    /// <summary>True when the next waypoint hit ends the race.</summary>
    public bool HeadingForFinish => OnFinalLap && HeadingForLapEnd;

    /// <summary>
    /// Time elapsed on the current lap. For single-lap races (and the first lap of a
    /// multi-lap race) this matches the race timer, since lapStartTime is 0 there.
    /// </summary>
    public float CurrentLapTime => (game != null) ? game.RunningTimer.Value - lapStartTime : 0.0f;

    private int targetWaypoint = -1;
    private bool finished;
    private int numClearedWaypoints;

    private VehCar playerCar;
    private MMGame game;

    private int numLaps = -1;
    private int currentLap = 0;
    private RaceType raceType = RaceType.Circuit;

    /// <summary>
    /// Waypoints the car was already standing on when they came into play, by index.
    /// ReInit and StartLap can drop a whole layout on top of a stationary car, and the
    /// hit test is positional rather than swept, so without this every waypoint whose
    /// plane crosses the car reports a hit on the next frame. An unarmed waypoint is
    /// armed once the car is clear of it, and only then can it be hit.
    /// </summary>
    private readonly HashSet<int> unarmed = new HashSet<int>();

    /// <summary>
    /// Suppresses hit testing entirely, the way the original's DisableUpdate flag does.
    /// Use it while the player is being teleported or a cutscene is running; there's no
    /// longer a stored last position, so nothing else needs to be primed afterwards.
    /// </summary>
    public bool DisableUpdate = false;

    /// <summary>Race timer value when the current lap began.</summary>
    private float lapStartTime;

    private Vector4 startPos;
    private List<Vector4> otherPositions = new List<Vector4>();

    /// <summary>
    /// How far past the front of the car the hit segment reaches. The original uses
    /// a flat 2.0 on top of the inertia box's half length.
    /// </summary>
    private const float PlayerHitSegmentLead = 2.0f;

    private int FinishIndex => waypoints.Count - 1;

    /// <summary>
    /// True when point 0 of the layout is the spawn point rather than a checkpoint.
    /// It supplies StartPosition's position and heading, and is never targeted, hit
    /// or drawn. Both ReInit layouts work this way: SingleStunt resets the car onto
    /// StartPosition, so the car begins the race standing inside point 0.
    /// </summary>
    private bool HasStartGate => raceType == RaceType.Stunt1 || raceType == RaceType.Stunt2;

    /// <summary>
    /// First waypoint that counts as a checkpoint. The ReInit layouts reserve index 0
    /// as the start gate, so their checkpoints begin at 1; Init layouts start at 0,
    /// their spawn point having been consumed by Init rather than kept as a waypoint.
    /// </summary>
    private int FirstCheckpointIndex => HasStartGate ? 1 : 0;

    /// <summary>One past the last checkpoint: the finish line's index, or the end of the list.</summary>
    private int CheckpointEndIndex => HasFinishLine ? FinishIndex : waypoints.Count;

    /// <summary>
    /// True when the player must hit waypoints in file order, so only the current
    /// target is tested each frame (Circuit, Stunt2). The rest scan every waypoint.
    /// </summary>
    private bool IsSequential => raceType == RaceType.Circuit || raceType == RaceType.Stunt2;

    /// <summary>
    /// True when the finish line is hidden until the last checkpoint is hit, rather
    /// than sitting visible on the spawn point the whole lap the way Circuit's does.
    /// </summary>
    private bool HasHiddenFinish => raceType == RaceType.UnorderedFinish || raceType == RaceType.Stunt1;

    private static string GetLocTime(float time)
    {
        if (time <= 0.0f)
            return "  ---  ";

        time += 0.005f;

        int totalSeconds = (int)time;
        int hundredths = (int)((time - totalSeconds) * 100.0f);

        int minutes = totalSeconds / 60;
        int seconds = totalSeconds % 60;

        return $"{minutes}:{seconds:00}:{hundredths:00}";
    }

    private void ShowLapTime(bool startingFinalLap)
    {
        string text2 = Localization.GetString((startingFinalLap) ? LocString.FinalLap : LocString.LapTime);
        float time = CurrentLapTime;
        game.Player.HUD.SetMessage(GetLocTime(time), 1.0f);
        game.Player.HUD.SetMessage2(text2);
    }

    private void ShowSplitTime()
    {
        float time = CurrentLapTime;
        game.Player.HUD.SetMessage(GetLocTime(time), 1.0f);
    }

    private WaypointObject CreateWaypointObject(SDLCity level, string name, Vector3 position, float rotation, float scale)
    {
        var wpobj = new GameObject($"Waypoint:{name}");
        wpobj.transform.parent = this.transform;

        var wpcmp = wpobj.AddComponent<WaypointObject>();
        wpcmp.Init(level, name, position, rotation, scale, 7.5f);

        pool.Add(wpcmp);
        return wpcmp;
    }

    /// <summary>
    /// Legacy overload: ordered = true maps to Circuit, false maps to Unordered (no finish line).
    /// </summary>
    public void Init(MMGame game, VehCar car, string city, string raceFile, int numLaps, bool reversed, bool ordered)
    {
        Init(game, car, city, raceFile, numLaps, reversed, ordered ? RaceType.Circuit : RaceType.Unordered);
    }

    /// <summary>
    /// Preallocates waypointCount checkpoint objects at the origin, deactivated and none
    /// in play, ready to be positioned by ReInit. Use this when the layout isn't known
    /// yet or changes between rounds, so the objects are built once instead of per race.
    /// </summary>
    public void InitStatic(MMGame game, VehCar car, int waypointCount)
    {
        this.playerCar = car;
        this.game = game;
        this.numLaps = -1;

        for (int i = 0; i < waypointCount; i++)
        {
            var wp = CreateWaypointObject(game.Level, "pt_check", Vector3.zero, 0.0f, 1.0f);
            wp.Deactivate();
        }

        // Nothing is in play until ReInit loads a layout, so Update stays idle
        waypoints.Clear();
        cleared.Clear();
        unarmed.Clear();
        targetWaypoint = -1;
        finished = false;
        numClearedWaypoints = 0;
        currentLap = 0;
    }

    /// <summary>
    /// Port of mmWaypoints::ReInit. Moves the preallocated waypoints onto a new layout
    /// and restarts. Unlike Init, every point in the file becomes a waypoint, point 0
    /// included: there is no separate spawn point and no reversal. What point 0 and the
    /// last point mean is the race type's business (see Stunt1, Stunt2).
    /// Pooled objects past the end of the file are parked and deactivated.
    /// </summary>
    /// <param name="showOnlyActive">
    /// Draw only the current target waypoint; see ShowOnlyActive.
    /// </param>
    public void ReInit(RaceType raceType, string city, string raceFile, bool showOnlyActive = false)
    {
        this.raceType = raceType;
        this.ShowOnlyActive = showOnlyActive;

        var positions = new MMPositions();
        string raceFilePath = AssetManager.CombinePath("race", city, $"{raceFile}.csv");
        if (AssetManager.Exists(raceFilePath))
            positions.Load(raceFilePath);

        int count = Mathf.Min(positions.Positions.Count, pool.Count);
        if (count < positions.Positions.Count)
        {
            Debug.LogWarning($"MMWaypoints: '{raceFile}' has {positions.Positions.Count} waypoints " +
                             $"but the pool only holds {pool.Count}; the race will be truncated.");
        }

        waypoints.Clear();

        Vector3 prevPosition = Vector3.zero;
        for (int i = 0; i < count; i++)
        {
            positions.Recall(i, out var pos, out var scale);
            var position = new Vector3(pos.x, pos.y, pos.z);

            var wp = pool[i];
            wp.SetPosition(position);
            wp.SetRadius(scale);
            wp.SetOrientation(pos.w);

            if (i == 0)
            {
                startPos = pos;
            }

            // A waypoint the file left at heading 0 is aimed once its successor is known,
            // so it faces along the path. Starts at i > 1, so waypoint 0 is never
            // fixed up (its heading is the race's start heading), and neither is the last.
            if (i > 1)
            {
                var prev = pool[i - 1];
                if (prev.Heading == 0.0f)
                {
                    // Heading of the step from prev to this point. Negating both the
                    // arguments and the result (as the original port did) mirrors the
                    // angle instead of flipping it, which only agrees along +/-X and
                    // leaves gates elsewhere turned across the course.
                    float heading = Mathf.Atan2(position.x - prevPosition.x,
                                                position.z - prevPosition.z) * Mathf.Rad2Deg;
                    prev.SetOrientation(heading);
                    prev.Move(); // already committed last iteration, re-commit with the new heading
                }
            }

            wp.Move();

            prevPosition = position;
            waypoints.Add(wp);
        }

        if (count > 0)
            startPos.w = pool[0].Heading;

        // Spares are parked by RefreshVisibility, which Reset reaches via StartLap
        Reset();
    }

    public void Init(MMGame game, VehCar car, string city, string raceFile, int numLaps, bool reversed, RaceType raceType)
    {
        this.raceType = raceType;
        this.ShowOnlyActive = false;

        string raceFilePath = AssetManager.CombinePath("race", city, $"{raceFile}waypoints.csv");
        if (AssetManager.Exists(raceFilePath))
        {
            var positions = new MMPositions();
            positions.Load(raceFilePath);

            bool circuit = raceType == RaceType.Circuit;

            // Circuit: point 0 is the spawn and the finish, so it stays put when
            // reversed and only the checkpoint order flips (handled in the loop below)
            if (reversed && !circuit)
                positions.Reverse();

            int count = positions.Positions.Count;
            if (count > 0)
            {
                startPos = positions.Positions[0];

                // Checkpoints (and, for UnorderedFinish, the finish as the last point)
                for (int n = 1; n < count; n++)
                {
                    int i = (circuit && reversed) ? count - n : n;
                    bool isFinish = !circuit && HasFinishLine && n == count - 1;

                    positions.Recall(i, out var pos, out var scale);
                    var heading = pos.w;

                    var wp = CreateWaypointObject(game.Level, isFinish ? "pt_finish" : "pt_check",
                        new Vector3(pos.x, pos.y, pos.z), heading, scale);
                    waypoints.Add(wp);
                }

                // Circuit: the finish line sits on the spawn point, added last so it's at FinishIndex
                if (circuit)
                {
                    positions.Recall(0, out var pos, out var scale);
                    var heading = pos.w;

                    var finish = CreateWaypointObject(game.Level, "pt_finish",
                        new Vector3(pos.x, pos.y, pos.z), heading, scale);
                    waypoints.Add(finish);
                }
            }
        }

        this.playerCar = car;
        this.numLaps = numLaps;
        this.game = game;

        Reset();
    }

    private void Update()
    {
        if (finished || targetWaypoint < 0 || waypoints.Count == 0 || playerCar == null || DisableUpdate)
            return;

        Transform car = playerCar.transform;
        var halfExtents = playerCar.VehCarSim.InertiaBox * 0.5f;

        // Rebuilt from the car's body every frame, like the original, rather than
        // being the car's movement since last frame
        GetPlayerHitSegment(car, halfExtents, out var back, out var front);

        if (IsSequential)
        {
            // Circuit, Stunt2: only the current target counts. In Circuit the player
            // spawns on the finish line, but it isn't the target until every
            // checkpoint is hit.
            bool hit = waypoints[targetWaypoint].PlaneHit(car, back, front, halfExtents);

            if (unarmed.Count > 0 && unarmed.Contains(targetWaypoint))
            {
                // Still standing on it from when it came into play; arm it once clear
                if (!hit)
                    unarmed.Remove(targetWaypoint);
            }
            else if (hit)
            {
                OnSequentialHit();
            }
        }
        else
        {
            // Snapshot whether the finish is open at the start of the frame, so
            // hitting the last checkpoint can't unlock and cross it in the same frame
            bool finishOpen = HasFinishLine && IsSelectable(FinishIndex);

            // Unordered, UnorderedFinish, Stunt1: any live waypoint counts,
            // target is just a guide
            for (int i = 0; i < waypoints.Count; i++)
            {
                if (HasFinishLine && i == FinishIndex)
                {
                    if (!finishOpen)
                        continue;
                }
                else if (i < FirstCheckpointIndex || cleared[i])
                {
                    // Below FirstCheckpointIndex is the start gate: it's part of
                    // the layout, but it can't be hit or cleared
                    continue;
                }

                bool hit = waypoints[i].PlaneHit(car, back, front, halfExtents);

                if (unarmed.Count > 0 && unarmed.Contains(i))
                {
                    if (!hit)
                        unarmed.Remove(i);
                    continue;
                }

                if (!hit)
                    continue;

                // Stop if the lap/race ended, so freshly reactivated
                // waypoints aren't counted in the same frame
                if (OnUnorderedHit(i))
                    break;
            }
        }
    }

    /// <summary>
    /// Port of the segment mmWaypoints::Update builds each frame. It runs along the
    /// car's own body on the XZ plane, not along the path travelled since last frame:
    /// from halfExtents.z behind the origin to halfExtents.z + PlayerHitSegmentLead
    /// ahead of it, so the nose reaches the plane slightly before the car does.
    /// The original passes (rear, nose) in that order, which is what PlaneHit's
    /// (current, previous) parameters receive here.
    /// </summary>
    private static void GetPlayerHitSegment(Transform car, Vector3 halfExtents, out Vector2 back, out Vector2 front)
    {
        Vector2 pos = car.position.ToVec2XZ();
        Vector2 forward = car.forward.ToVec2XZ();

        back = pos - forward * halfExtents.z;
        front = pos + forward * (halfExtents.z + PlayerHitSegmentLead);
    }

    private void OnDestroy()
    {
        // Tear down the pool, not just what's in play, or InitStatic's spares leak
        foreach (var obj in pool)
        {
            Destroy(obj.gameObject);
        }
        pool.Clear();
        waypoints.Clear();
        cleared.Clear();
        unarmed.Clear();
    }

    // ------------------------------------------------------------------
    // Race control
    // ------------------------------------------------------------------

    /// <summary>
    /// Restarts the race from lap 0 with every waypoint live
    /// (except the ReInit layouts' start gate, which starts cleared).
    /// </summary>
    public void Reset()
    {
        currentLap = 0;
        numClearedWaypoints = 0;
        finished = false;
        StartLap();
    }

    private void StartLap()
    {
        // Lap times are measured from here, so splits and lap times
        // are relative to the current lap rather than the whole race
        lapStartTime = (game != null) ? game.RunningTimer.Value : 0.0f;

        cleared.Clear();
        for (int i = 0; i < waypoints.Count; i++)
            cleared.Add(false);

        // Stunt1, Stunt2: point 0 is the spawn, not a gate, so it starts cleared and
        // the first checkpoint is 1. The original leaves this to the hit test — the
        // parked car is straddling point 0's plane on frame one — but that can't be
        // told apart from a layout dropped on a stationary car, so it's done here.
        // The count guard keeps a degenerate one-point layout from starting with
        // nothing live and no way to reach CompleteLap.
        if (HasStartGate && waypoints.Count > 1)
        {
            cleared[0] = true;
            numClearedWaypoints++;

            targetWaypoint = -1;
            SetTarget(1);
        }
        else
        {
            targetWaypoint = -1;
            SetTarget(SelectNextTarget());
        }

        // The car hasn't moved, so anything already crossing it must not count
        ArmWaypoints();
    }

    /// <summary>
    /// Single funnel for the target, so visibility follows it under ShowOnlyActive.
    /// </summary>
    private void SetTarget(int index)
    {
        targetWaypoint = index;
        RefreshVisibility();
    }

    // ------------------------------------------------------------------
    // Visibility (WaypointObject.Active means "drawn", nothing more)
    // ------------------------------------------------------------------

    /// <summary>
    /// Whether a waypoint in play should be drawn. Hit waypoints go dark, a hidden
    /// finish line stays dark until the last checkpoint, and under ShowOnlyActive
    /// everything but the current target is dark as well.
    /// </summary>
    private bool IsVisible(int index)
    {
        if (index < 0 || index >= waypoints.Count)
            return false;

        // Also covers the ReInit layouts' start gate, which starts cleared
        if (cleared[index])
            return false;

        // UnorderedFinish, Stunt1: the finish line is hidden until every checkpoint is hit
        if (HasHiddenFinish && index == FinishIndex && AnyCheckpointsRemaining())
            return false;

        return !ShowOnlyActive || index == targetWaypoint;
    }

    private void RefreshVisibility()
    {
        for (int i = 0; i < waypoints.Count; i++)
        {
            if (IsVisible(i))
                waypoints[i].Activate();
            else
                waypoints[i].Deactivate();
        }

        // ReInit's spares aren't in play, so they're never drawn
        for (int i = waypoints.Count; i < pool.Count; i++)
            pool[i].Deactivate();
    }

    // ------------------------------------------------------------------
    // Arming (waypoints that came into play underneath the car)
    // ------------------------------------------------------------------

    /// <summary>
    /// Marks every live waypoint the car is currently crossing as unarmed, so a layout
    /// dropped on a stationary car (ReInit between stunt events, a lap rollover)
    /// doesn't register the whole set as hits on the next frame.
    /// </summary>
    private void ArmWaypoints()
    {
        unarmed.Clear();

        if (playerCar == null || waypoints.Count == 0)
            return;

        Transform car = playerCar.transform;
        var halfExtents = playerCar.VehCarSim.InertiaBox * 0.5f;
        GetPlayerHitSegment(car, halfExtents, out var back, out var front);

        for (int i = FirstCheckpointIndex; i < waypoints.Count; i++)
        {
            if (cleared[i])
                continue;

            if (waypoints[i].PlaneHit(car, back, front, halfExtents))
                unarmed.Add(i);
        }
    }

    /// <summary>
    /// Marks one waypoint unarmed if the car is already crossing it. Used when the
    /// finish line unlocks under a car that happens to be sitting on its plane.
    /// </summary>
    private void TryArm(int index)
    {
        if (playerCar == null || index < 0 || index >= waypoints.Count)
            return;

        Transform car = playerCar.transform;
        var halfExtents = playerCar.VehCarSim.InertiaBox * 0.5f;
        GetPlayerHitSegment(car, halfExtents, out var back, out var front);

        if (waypoints[index].PlaneHit(car, back, front, halfExtents))
            unarmed.Add(index);
    }

    // ------------------------------------------------------------------
    // Selection (pure queries, don't change state)
    // ------------------------------------------------------------------

    /// <summary>
    /// Returns the index of the next selectable waypoint after the current target,
    /// wrapping around. Returns -1 if nothing is selectable.
    /// For sequential races this is simply the next waypoint in file order.
    /// </summary>
    public int SelectNextWaypoint()
    {
        int count = waypoints.Count;
        if (count == 0)
            return -1;

        int start = targetWaypoint < 0 ? 0 : targetWaypoint + 1;
        for (int i = 0; i < count; i++)
        {
            int idx = (start + i) % count;
            if (idx != targetWaypoint && IsSelectable(idx))
                return idx;
        }

        // Only the current target is left
        if (targetWaypoint >= 0 && IsSelectable(targetWaypoint))
            return targetWaypoint;

        return -1;
    }

    /// <summary>
    /// Returns the index of the selectable waypoint closest to the player car.
    /// Returns -1 if nothing is selectable, or if the race is sequential: there the
    /// target is fixed by file order, so proximity has no say in it.
    /// </summary>
    public int SelectNextClosestWaypoint()
    {
        if (IsSequential || waypoints.Count == 0)
            return -1;
        if (playerCar == null)
            return 0;

        Vector2 carPos = playerCar.transform.position.ToVec2XZ();

        int best = -1;
        float bestDistSq = float.MaxValue;
        for (int i = 0; i < waypoints.Count; i++)
        {
            if (!IsSelectable(i))
                continue;

            float distSq = (waypoints[i].transform.position.ToVec2XZ() - carPos).sqrMagnitude;
            if (distSq < bestDistSq)
            {
                bestDistSq = distSq;
                best = i;
            }
        }

        return best;
    }

    // ------------------------------------------------------------------
    // Cycling (hotkey actions, unordered races only)
    // ------------------------------------------------------------------

    /// <summary>
    /// Moves the target to the next selectable waypoint. Unordered races only.
    /// </summary>
    public void CycleWaypoint()
    {
        if (IsSequential || targetWaypoint < 0)
            return;

        int next = SelectNextWaypoint();
        if (next >= 0)
            SetTarget(next);
    }

    /// <summary>
    /// Moves the target back to the closest selectable waypoint. Unordered races only.
    /// </summary>
    public void CycleWaypointClosest()
    {
        if (IsSequential || targetWaypoint < 0)
            return;

        int closest = SelectNextClosestWaypoint();
        if (closest >= 0)
            SetTarget(closest);
    }

    // ------------------------------------------------------------------
    // AI (opponents track their own progress, not the player's race state)
    // ------------------------------------------------------------------

    // AnyAIWPHit ignores waypoints further than 50 units away (3D)
    private const float AIHitRadiusSq = 50f * 50f;

    /// <summary>
    /// Creates a hit mask for one AI opponent, sized to this race's waypoints.
    /// Replaces the original's int bitmask so there's no 32-waypoint limit.
    /// </summary>
    public BitArray CreateAIHitMask() => new BitArray(waypoints.Count);

    /// <summary>
    /// Port of mmWaypoints::AIWPHit. Tests whether the AI car is crossing a specific waypoint.
    /// Instead of the car's movement since last frame, the hit segment runs
    /// along the car's body, from its back end to its front end.
    /// </summary>
    public bool AIWaypointHit(int index, Transform car, Vector3 inertiaBox, float inertiaBoxScale)
    {
        if (index < 0 || index >= waypoints.Count || car == null)
            return false;

        GetAIHitSegment(car, inertiaBox, out var front, out var back);
        return waypoints[index].PlaneHit(car, front, back, inertiaBox * inertiaBoxScale);
    }

    /// <summary>
    /// Port of mmWaypoints::AnyAIWPHit. Checks every waypoint not yet set in hitMask,
    /// within range of the car. On the first hit, sets its bit and returns its index.
    /// Returns -1 if nothing was hit.
    /// </summary>
    public int AnyAIWaypointHit(BitArray hitMask, Transform car, Vector3 inertiaBox, float inertiaBoxScale)
    {
        if (hitMask == null || car == null)
            return -1;

        GetAIHitSegment(car, inertiaBox, out var front, out var back);
        Vector3 carPos = car.position;
        Vector3 extents = inertiaBox * inertiaBoxScale;

        int count = Mathf.Min(waypoints.Count, hitMask.Length);
        for (int i = 0; i < count; i++)
        {
            if (hitMask[i])
                continue;

            var wp = waypoints[i];
            if ((carPos - wp.transform.position).sqrMagnitude > AIHitRadiusSq)
                continue;

            if (!wp.PlaneHit(car, front, back, extents))
                continue;

            hitMask[i] = true;
            return i;
        }

        return -1;
    }

    /// <summary>
    /// The original offsets the car position by +/- its Z axis * inertiaBox.z (unscaled)
    /// on the XZ plane, and passes (+Z end, -Z end) as (current, previous).
    /// </summary>
    private static void GetAIHitSegment(Transform car, Vector3 inertiaBox, out Vector2 front, out Vector2 back)
    {
        Vector2 pos = car.position.ToVec2XZ();
        Vector2 offset = car.forward.ToVec2XZ() * inertiaBox.z;
        front = pos + offset;
        back = pos - offset;
    }

    // ------------------------------------------------------------------
    // Internal
    // ------------------------------------------------------------------

    /// <summary>
    /// Stunt1, Stunt2: index 0 is the start gate and is never selectable.
    /// Races with a finish line (Circuit, UnorderedFinish, Stunt1): checkpoints are
    /// selectable until they're hit, and the finish line only becomes selectable once
    /// every checkpoint is hit.
    /// Unordered, Stunt2: no finish line, every waypoint is selectable until it's hit.
    /// Whether a waypoint is drawn has no say here, so ShowOnlyActive doesn't
    /// change what can be targeted or hit.
    /// </summary>
    private bool IsSelectable(int index)
    {
        if (index < FirstCheckpointIndex)
            return false;

        if (HasFinishLine && index == FinishIndex)
            return !AnyCheckpointsRemaining();

        return !cleared[index];
    }

    /// <summary>
    /// Only meaningful for races with a finish line (excludes the last waypoint,
    /// and the start gate).
    /// </summary>
    private bool AnyCheckpointsRemaining()
    {
        for (int i = FirstCheckpointIndex; i < FinishIndex; i++)
        {
            if (!cleared[i])
                return true;
        }
        return false;
    }

    private bool AnyWaypointsRemaining()
    {
        for (int i = FirstCheckpointIndex; i < waypoints.Count; i++)
        {
            if (!cleared[i])
                return true;
        }
        return false;
    }

    private int SelectNextTarget()
    {
        return IsSequential ? SelectNextWaypoint() : SelectNextClosestWaypoint();
    }

    /// <summary>
    /// Handles hitting the current target in a sequential race (Circuit, Stunt2).
    /// </summary>
    private void OnSequentialHit()
    {
        numClearedWaypoints++;

        // Circuit: crossed the finish line back at the spawn point
        // (never marked cleared, so it stays drawn all lap)
        if (HasFinishLine && targetWaypoint == FinishIndex)
        {
            CompleteLap();
            return;
        }

        cleared[targetWaypoint] = true;

        // Stunt2: no finish line, the lap ends once the last waypoint in the order is hit
        if (!HasFinishLine && !AnyWaypointsRemaining())
        {
            CompleteLap();
            return;
        }

        game.PlaySound(GameSound.HitWaypoint);
        if (game.SpeechAudio != null) game.SpeechAudio.PlayCheckpoint(targetWaypoint);
        ShowSplitTime();

        // One refresh covers both the waypoint just cleared and the new target
        SetTarget(SelectNextWaypoint());
    }

    /// <summary>
    /// Handles hitting a hittable waypoint in an unordered race (with or without a finish line).
    /// Returns true if this hit completed the lap (or the race).
    /// </summary>
    private bool OnUnorderedHit(int index)
    {
        numClearedWaypoints++;

        // Crossed the (unlocked) finish line
        if (HasFinishLine && index == FinishIndex)
        {
            CompleteLap();
            return true;
        }

        cleared[index] = true;

        // Last checkpoint hit: the finish line is now live. If the car is already
        // standing on it, hold it until the car is clear so it has to be crossed.
        if (HasFinishLine && !AnyCheckpointsRemaining())
            TryArm(FinishIndex);

        // With a finish line, the lap only ends at the finish.
        // Without one, it ends when every checkpoint is hit.
        bool lapDone = !HasFinishLine && !AnyWaypointsRemaining();
        if (!lapDone)
        {
            game.PlaySound(GameSound.HitWaypoint);
            if (game.SpeechAudio != null) game.SpeechAudio.PlayCheckpoint(targetWaypoint);
            ShowSplitTime();

            // Always retarget to the closest selectable waypoint, even if the
            // previous target is still live. The player has moved, so the
            // old target may no longer be the nearest one. Once all checkpoints
            // are hit in an UnorderedFinish or Stunt1 race, this picks the finish line.
            SetTarget(SelectNextClosestWaypoint());

            return false;
        }

        // Every waypoint hit
        CompleteLap();
        return true;
    }

    private void CompleteLap()
    {
        if (numLaps > 0)
        {
            currentLap++;
            if (currentLap < numLaps)
            {
                game.PlaySound(GameSound.HitLastWaypoint);

                // Must come before StartLap, which resets lapStartTime
                ShowLapTime(currentLap == numLaps - 1);

                StartLap();
                return;
            }
        }

        // Final crossing: no split or lap message, the results screen takes over
        finished = true;
        SetTarget(-1); // race over, stop checking
    }
}