using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Fixed-size pool of dynamic banger pieces. Allocates its instances up front and never
/// grows - if every banger is in use, the oldest one on the ground gets recycled.
/// </summary>
public class HitBangerPool : MonoBehaviour
{
    public const int DefaultCapacity = 32;

    public static HitBangerPool Current { get; private set; }

    [SerializeField] private float lifetime = 20.0f;

    private SDLCity level;

    private List<HitBangerInstance> all = new List<HitBangerInstance>();
    private Stack<HitBangerInstance> free = new Stack<HitBangerInstance>();
    private List<HitBangerInstance> live = new List<HitBangerInstance>();

    public float Lifetime => lifetime;
    public int Capacity => all.Count;
    public int FreeCount => free.Count;
    public int LiveCount => live.Count;


    public static HitBangerPool Create(SDLCity level, int capacity = DefaultCapacity)
    {
        var go = new GameObject("HitBangerPool");
        go.transform.SetParent(level.transform, false);

        var pool = go.AddComponent<HitBangerPool>();
        pool.Allocate(level, capacity);

        Current = pool;
        return pool;
    }

    private void Allocate(SDLCity level, int capacity)
    {
        this.level = level;

        for (int i = 0; i < capacity; i++)
        {
            var go = new GameObject($"HitBanger{i:000}");
            go.transform.SetParent(transform, false);
            go.SetActive(false);

            var banger = go.AddComponent<HitBangerInstance>();
            banger.Allocate(this);

            all.Add(banger);
            free.Push(banger);
        }
    }

    /// <summary>
    /// Hand out a banger already positioned and initialized. The caller is expected to
    /// follow up with Launch/AddForce. Returns null if the pool is empty or the request
    /// names a package or part that doesn't exist.
    /// </summary>
    public HitBangerInstance Request(in HitBangerInstance.SpawnRequest request)
    {
        if (string.IsNullOrEmpty(request.Package) || string.IsNullOrEmpty(request.Part))
            return null;

        var banger = Take();
        if (banger == null)
            return null;

        if (!banger.Spawn(level, request))
        {
            banger.Despawn();
            free.Push(banger);
            return null;
        }

        live.Add(banger);
        return banger;
    }

    private HitBangerInstance Take()
    {
        if (free.Count > 0)
            return free.Pop();

        if (live.Count == 0)
            return null;

        // live is kept in spawn order, so index 0 is the oldest debris in the world
        var oldest = live[0];
        live.RemoveAt(0);
        oldest.Despawn();

        return oldest;
    }

    public void Release(HitBangerInstance banger)
    {
        if (banger == null)
            return;

        int index = live.IndexOf(banger);
        if (index >= 0)
            live.RemoveAt(index);

        banger.Despawn();
        free.Push(banger);
    }

    public void ReleaseAll()
    {
        for (int i = live.Count - 1; i >= 0; i--)
        {
            live[i].Despawn();
            free.Push(live[i]);
        }

        live.Clear();
    }

    private void Update()
    {
        float now = Time.time;

        for (int i = live.Count - 1; i >= 0; i--)
        {
            var banger = live[i];

            if (banger == null)
            {
                live.RemoveAt(i);
                continue;
            }

            if (!banger.IsSpawned || now >= banger.ExpireTime)
            {
                live.RemoveAt(i);
                banger.Despawn();
                free.Push(banger);
            }
        }
    }

    private void OnDestroy()
    {
        if (Current == this)
            Current = null;
    }
}