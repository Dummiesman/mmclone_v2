using MM2.AI;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public sealed class Lane
{
    public readonly RoadInstance Instance;
    public readonly RoadSide Side;
    public readonly RailType Type;
    public readonly AmbientTypeFlags TypeFlags;
    public readonly int RailIndex;

    private readonly List<AIEntity> ordered = new List<AIEntity>(8); // ascending Distance
    public IReadOnlyList<AIEntity> Ordered => ordered;
    public int Count => ordered.Count;

    /// Travel direction relative to increasing progress. Both sides' rail data is ordered
    /// start -> end locally, so progress always increases in the direction of travel.
    public int Direction => 1;

    public Lane(RoadInstance instance, RoadSide side, RailType type, AmbientTypeFlags typeFlags, int rail)
    {
        Instance = instance;
        Side = side;
        Type = type;
        TypeFlags = typeFlags;
        RailIndex = rail;
    }

    public void Insert(AIEntity e)
    {
        float d = e.NormalizedPathProgress;
        int i = ordered.Count;
        while (i > 0 && ordered[i - 1].NormalizedPathProgress > d) i--;
        ordered.Insert(i, e);
    }

    public bool Remove(AIEntity e) => ordered.Remove(e);

    /// Call once per frame after entities have updated. Insertion sort on a
    /// nearly-sorted list is effectively O(n) — it only does work on actual overtakes.
    public void Resort()
    {
        for (int i = 1; i < ordered.Count; i++)
        {
            var e = ordered[i];
            float d = e.NormalizedPathProgress;
            int j = i - 1;
            while (j >= 0 && ordered[j].NormalizedPathProgress > d)
            {
                ordered[j + 1] = ordered[j];
                j--;
            }
            ordered[j + 1] = e;
        }
    }

    private int LowerBound(float d)
    {
        int lo = 0, hi = ordered.Count;
        while (lo < hi)
        {
            int mid = (lo + hi) >> 1;
            if (ordered[mid].NormalizedPathProgress < d) lo = mid + 1;
            else hi = mid;
        }
        return lo;
    }

    public AIEntity NextAhead(float distance, AIEntity ignore = null)
    {
        if (Direction > 0)
        {
            for (int i = LowerBound(distance); i < ordered.Count; i++)
                if (ordered[i] != ignore) return ordered[i];
        }
        else
        {
            for (int i = LowerBound(distance) - 1; i >= 0; i--)
                if (ordered[i] != ignore) return ordered[i];
        }
        return null;
    }

    public AIEntity NextBehind(float distance, AIEntity ignore = null)
    {
        if (Direction > 0)
        {
            for (int i = LowerBound(distance) - 1; i >= 0; i--)
                if (ordered[i] != ignore) return ordered[i];
        }
        else
        {
            for (int i = LowerBound(distance); i < ordered.Count; i++)
                if (ordered[i] != ignore) return ordered[i];
        }
        return null;
    }

    /// True if nothing occupies [min, max] in arc length.
    public bool IsClear(float min, float max, AIEntity ignore = null)
    {
        for (int i = LowerBound(min); i < ordered.Count; i++)
        {
            var e = ordered[i];
            if (e.NormalizedPathProgress > max) break;
            if (e != ignore) return false;
        }
        return true;
    }

    public override string ToString() => $"{Side} {Type} #{RailIndex}";
}

public class RoadInstance
{
    public int Id => road.Id;

    public Road Road => road;
    private readonly Road road;

    private static readonly RailType[] AllRailTypes = (RailType[])Enum.GetValues(typeof(RailType));
    private const int SideCount = 2;

    /// Which lane each entity currently sits in, so removal doesn't need a lane scan.
    private readonly Dictionary<AIEntity, Lane> laneOf = new Dictionary<AIEntity, Lane>();

    /// [side, railType] -> lanes for that combination, ordered by rail index.
    /// Never null: an absent rail type just gets an empty list.
    private readonly List<Lane>[,] lanes;

    /// Flat view for per-frame iteration, so callers don't nest three loops.
    private readonly List<Lane> allLanes = new List<Lane>();
    public IReadOnlyList<Lane> AllLanes => allLanes;

    private readonly List<AIEntity> entities = new List<AIEntity>();
    public IReadOnlyList<AIEntity> Entities => entities;

    private readonly List<Obstacle> obstacles = new List<Obstacle>();
    public IReadOnlyList<Obstacle> Obstacles => obstacles;


    public RoadInstance(Road road)
    {
        this.road = road;
        lanes = new List<Lane>[SideCount, AllRailTypes.Length];

        // Passing the method group avoids naming the side-data type here.
        BuildSide(RoadSide.Left, road.LeftData.GetRailCount, road.LeftData.AiTypeFlags);
        BuildSide(RoadSide.Right, road.RightData.GetRailCount, road.RightData.AiTypeFlags);
    }

    private void BuildSide(RoadSide side, Func<RailType, int> getRailCount, AmbientTypeFlags typeFlags)
    {
        for (int t = 0; t < AllRailTypes.Length; t++)
        {
            RailType type = AllRailTypes[t];
            int count = getRailCount(type);
            var list = new List<Lane>(count);

            for (int i = 0; i < count; i++)
            {
                var lane = new Lane(this, side, type, typeFlags, i);
                list.Add(lane);
                allLanes.Add(lane);
            }

            lanes[(int)side, (int)type] = list;
        }
    }

    // Lane access

    public int GetLaneCount(RoadSide side, RailType type)
    {
        return lanes[(int)side, (int)type].Count;
    }

    public IReadOnlyList<Lane> GetLanes(RoadSide side, RailType type)
    {
        return lanes[(int)side, (int)type];
    }

    public IEnumerable<Lane> GetLanes(RoadSide side, RailType type, AmbientTypeFlags typeFlags)
    {
        var baseLanes = GetLanes(side, type);
        foreach(var lane in baseLanes)
        {
            if ((lane.TypeFlags & typeFlags) == typeFlags)
                yield return lane;
        }
    }

    public Lane GetLane(RoadSide side, RailType type, int railIndex)
    {
        var list = lanes[(int)side, (int)type];
        return (uint)railIndex < (uint)list.Count ? list[railIndex] : null;
    }

    public bool TryGetLane(RoadSide side, RailType type, int railIndex, out Lane lane)
    {
        lane = GetLane(side, type, railIndex);
        return lane != null;
    }

    public Lane GetLaneOf(AIEntity entity) =>
        laneOf.TryGetValue(entity, out var lane) ? lane : null;

    public Lane GetNeighbourLane(Lane lane, int offset) =>
        GetLane(lane.Side, lane.Type, lane.RailIndex + offset);

    public bool AddEntity(AIEntity entity)
    {
        var lane = GetLane(entity.RoadInfo.SideOfRoad, entity.RailType, entity.RoadInfo.RailIndex);
        if (lane == null)
        {
            Debug.LogError($"Road {road.Id} has no {entity.RoadInfo.SideOfRoad} {entity.RailType} lane " +
                           $"at index {entity.RoadInfo.RailIndex} for entity {entity.ID}.");
            return false;
        }

        if (laneOf.ContainsKey(entity))
        {
            Debug.LogError($"Entity {entity.ID} is trying to re-add itself to road {road.Id}??");
            return false;
        }

        obstacles.Add(new EntityObstacle(entity));
        entities.Add(entity);
        laneOf[entity] = lane;
        lane.Insert(entity);
        return true;
    }

    public bool RemoveEntity(AIEntity entity)
    {
        if (!laneOf.TryGetValue(entity, out var lane))
        {
            Debug.LogError($"Entity {entity.ID} tried to remove itself from road {road.Id}, but it's not here.");
            return false;
        }

        lane.Remove(entity);
        laneOf.Remove(entity);
        entities.Remove(entity);
        obstacles.RemoveAll(obs => obs is EntityObstacle entityObstacle && entityObstacle.Entity == entity);
        return true;
    }

    /// Lane change within this road. Keeps the flat entity list untouched.
    public bool MoveToLane(AIEntity entity, Lane target)
    {
        if (target == null || target.Instance != this) return false;
        if (!laneOf.TryGetValue(entity, out var current)) return false;
        if (current == target) return true;

        current.Remove(entity);
        target.Insert(entity);
        laneOf[entity] = target;

        return true;
    }

    public void AddObstacle(Obstacle obstacle)
    {
        obstacles.Add(obstacle);
    }

    /// Call once per frame, after entities have advanced their path progress.
    public void ResortLanes()
    {
        for (int i = 0; i < allLanes.Count; i++) allLanes[i].Resort();
    }

    public void Update()
    {
        ResortLanes();
    }

    public void Reset()
    {
        for(int i=entities.Count - 1; i >= 0; i--)
        {
            var entity = entities[i];
            RemoveEntity(entity);
        }
    }
}