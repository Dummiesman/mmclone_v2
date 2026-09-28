using System.Collections.Generic;
using UnityEngine;
using MM2.AI;

/// <summary>
/// Spawns one AI car and drives it with AiVehiclePhysics. Start in Follow mode: it needs
/// nothing but the two cars, so it exercises steering, throttle, brake and VehCarSim.Speed
/// without any of the route machinery. Move up to Route once that looks right.
///
///   1 = off      2 = follow      3 = mirror      4 = route      R = reset to spawn
/// </summary>
public class VehicleAITest : MonoBehaviour
{
    [Header("Spawn")]
    public string Basename = "vppanoz";
    public int Variant = 0;

    /// <summary>Offset from the target car, in its own frame: 8m behind, 3m left.</summary>
    public Vector3 SpawnOffset = new Vector3(-3f, 0f, -8f);

    [Header("Route tuning")]
    public float MaxThrottle = 1f;
    public float CornerSpeedMultiplier = 1f;
    public float CornerBrakingThreshold = 0.1f;
    public float RouteDistancePad = 60f;

    public VehCar Car { get; private set; }
    public AiVehiclePhysics Ai { get; private set; }

    private SDLCity level;
    private VehCar target;
    private Vector3 spawnPos;
    private Quaternion spawnRot;

    public void Init(SDLCity level, VehCar followMe)
    {
        this.level = level;
        this.target = followMe;

        // ---- spawn the car beside and behind the one we're following ----
        spawnRot = followMe.transform.rotation;
        spawnPos = followMe.transform.position + spawnRot * SpawnOffset;

        var go = new GameObject("test car");
        go.transform.SetPositionAndRotation(spawnPos, spawnRot);

        Car = go.AddComponent<VehCar>();
        Car.Init(level, Basename, Variant, vehCarType.Opponent);
        Car.SetDrivable(true);

        // ---- the AI ----
        Ai = go.AddComponent<AiVehiclePhysics>();
        Ai.Init(Car, level.AINetwork);                       // also takes the bumper and side distances off the bound
        Ai.Id = 1;

        Ai.MaxThrottle = MaxThrottle;
        Ai.CornerSpeedMultiplier = CornerSpeedMultiplier;
        Ai.CornerBrakingThreshold = CornerBrakingThreshold;
        Ai.LookAheadDistance = RouteDistancePad;

        Ai.FollowTarget = followMe;
        Ai.Mode = AiVehiclePhysics.AiDriveMode.Follow;
    }

    private void Update()
    {
        if (Ai == null) return;

        if (Input.GetKeyDown(KeyCode.Alpha1)) Ai.Mode = AiVehiclePhysics.AiDriveMode.Off;
        if (Input.GetKeyDown(KeyCode.Alpha2)) Ai.Mode = AiVehiclePhysics.AiDriveMode.Follow;
        if (Input.GetKeyDown(KeyCode.Alpha3)) Ai.Mode = AiVehiclePhysics.AiDriveMode.Mirror;
        if (Input.GetKeyDown(KeyCode.Alpha4)) StartRoute();
        if (Input.GetKeyDown(KeyCode.R)) ResetCar();
    }

    private void ResetCar()
    {
        Ai.Mode = AiVehiclePhysics.AiDriveMode.Off;
        Car.transform.SetPositionAndRotation(spawnPos, spawnRot);
        Car.Body.velocity = Vector3.zero;
        Car.Body.angularVelocity = Vector3.zero;
        Car.Reset();
    }

    // =====================================================================
    //  Route mode
    // =====================================================================

    /// <summary>
    /// Registers a lap around the nearest few intersections and switches to Route mode.
    /// InitRoadTurns has to have run on every road first - see PrepareNetwork.
    /// </summary>
    public void StartRoute()
    {
        var net = level != null ? level.AINetwork : null;
        if (net == null || net.Data == null)
        {
            Debug.LogError("VehicleAITest: no AINetwork to route on.");
            return;
        }

        PrepareNetwork(net);
        Ai.Net = net;

        var waypoints = BuildWaypoints(net, Car.transform.position, 6);
        if (waypoints.Length < 2)
        {
            Debug.LogError("VehicleAITest: couldn't build a route.");
            return;
        }

        var endInter = net.Data.Intersections[waypoints[waypoints.Length - 1]];

        Ai.RegisterRoute(
            waypoints,
            (short)waypoints.Length,
            endInter.Center,
            Vector3.forward,        // end orientation
            numLaps: 1,
            targetSpeed: 0f,
            finishRadius: 0f,
            unkFlag: false,
            avoidTraffic: true,
            avoidProps: true,
            avoidPlayers: true,
            avoidOpponents: true,
            reckless: false,
            maxThrottle: MaxThrottle,
            cornerSpeedMultiplier: CornerSpeedMultiplier,
            cornerBrakingThreshold: CornerBrakingThreshold,
            lookAheadDistance: RouteDistancePad);

        Ai.Mode = AiVehiclePhysics.AiDriveMode.Route;
    }

    /// <summary>One-time: every road needs its turn list built before the AI drives it.</summary>
    private static bool networkPrepared;
    private static void PrepareNetwork(AINetwork net)
    {
        if (networkPrepared) return;
        foreach (var road in net.Data.Roads)
            road.InitRoadTurns();
        networkPrepared = true;
    }

    /// <summary>Walks from the nearest intersection to a neighbour each step, for a rough loop.</summary>
    private static int[] BuildWaypoints(AINetwork net, Vector3 from, int count)
    {
        var ids = new List<int>();
        var start = net.GetNearestIntersection(from);
        if (start == null) return ids.ToArray();

        var current = start;
        int previousId = -1;

        for (int i = 0; i < count; i++)
        {
            ids.Add((short)current.Id);

            Intersection next = null;
            foreach (var road in current.Roads)
            {
                int otherId = road.LeftEndData.IntersectionID == current.Id
                    ? road.RightEndData.IntersectionID
                    : road.LeftEndData.IntersectionID;

                if (otherId < 0 || otherId == previousId || otherId >= net.Data.Intersections.Count) continue;
                next = net.Data.Intersections[otherId];
                break;
            }
            if (next == null) break;

            previousId = current.Id;
            current = next;
        }
        return ids.ToArray();
    }

    // =====================================================================

    private void OnDrawGizmos()
    {
        if (Ai == null || Car == null) return;

        // where the AI is aiming
        Gizmos.color = Color.magenta;
        Gizmos.DrawSphere(Ai.TargetPt, 0.4f);
        Gizmos.DrawLine(Car.transform.position, Ai.TargetPt);

        // steering demand, drawn off the nose
        Vector3 nose = Car.transform.position + Car.transform.forward * Ai.FrontBumperDistance;
        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(nose, nose + Car.transform.right * Ai.Steering * 3f);

#if UNITY_EDITOR
        UnityEditor.Handles.Label(Car.transform.position + Vector3.up * 3f,
            $"{Ai.Mode}\nsteer {Ai.Steering:F2}  thr {Ai.Throttle:F2}  brk {Ai.Brake:F2}\n" +
            $"{Car.VehCarSim.Speed:F1} m/s");
#endif
    }
}
