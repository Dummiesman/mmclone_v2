using System.Collections.Generic;
using UnityEngine;

public class VehBreakableManager : MonoBehaviour
{
    private const float EjectPush = 2.0f;
    private const float EjectLift = 1.0f;
    private const float EjectSpin = 6.0f;
    private const float FallbackMass = 25.0f;

    class VehBreakable
    {
        public GameObject Source;
        public GameObject Object;
        public HitBangerInstance Banger;
        public int BangerDataID;
        public Vector3 Pivot;
        public Renderer[] SourceRenderers;
        public Renderer[] CopyRenderers;
        public bool Attached = true;
    }

    public float ImpulseThreshold = 10000;
    private readonly List<VehBreakable> breakables = new List<VehBreakable>();

    private SDLCity level;
    private Rigidbody vehicleBody;

    public void Init(SDLCity level, Rigidbody vehicleBody)
    {
        this.level = level;
        this.vehicleBody = vehicleBody;
    }

    private Vector3 GetPivot(string basename, string part)
    {
        string fileName = $"{basename}_{part}.mtx";
        if (!AssetManager.Exists("geometry", fileName))
            return Vector3.zero;

        var matrixFile = new MatrixFile();
        using (var stream = AssetManager.Open("geometry", fileName))
        {
            matrixFile.Load(stream);
        }
        return new Vector3(-matrixFile.Origin.x, matrixFile.Origin.y, -matrixFile.Origin.z); // convert into vehicle space
    }

    public void Add(GameObject @object, string basename, string bangerName)
    {
        var breakable = new VehBreakable();
        breakable.Source = @object;
        breakable.Pivot = GetPivot(basename, bangerName);
        breakable.BangerDataID = BangerDataManager.Instance.AddEntry(basename, bangerName);

        breakable.Object = Instantiate(@object);
        breakable.Object.name = $"{@object.name}_broken";
        breakable.Object.transform.SetParent(null, false);
        breakable.SourceRenderers = @object.GetComponentsInChildren<Renderer>(true);
        breakable.CopyRenderers = breakable.Object.GetComponentsInChildren<Renderer>(true);
        breakable.Object.SetActive(false);

        breakable.Banger = breakable.Object.AddComponent<HitBangerInstance>();
        breakable.Banger.AllocateStandalone();

        breakables.Add(breakable);
    }

    private Vector3 WorldPivot(VehBreakable breakable)
    {
        return vehicleBody.transform.TransformPoint(breakable.Pivot);
    }

    public void Impact(float impulse, Vector3 position, int room)
    {
        if (impulse < ImpulseThreshold) return;

        VehBreakable closest = null;
        float closestSq = 100000.0f;

        foreach (var breakable in breakables)
        {
            if (!breakable.Attached)
                continue;

            float sq = (WorldPivot(breakable) - position).sqrMagnitude;
            if (sq < closestSq)
            {
                closestSq = sq;
                closest = breakable;
            }
        }

        if (closest != null)
            Eject(closest, position, room);
    }

    private void Eject(VehBreakable breakable, Vector3 impactPoint, int room)
    {
        breakable.Attached = false;

        if (breakable.Source != null)
            breakable.Source.SetActive(false);

        Vector3 spawn = WorldPivot(breakable);
        var banger = breakable.Banger;

        banger.SpawnDetached(level, breakable.BangerDataID, spawn,
                             transform.rotation, transform.lossyScale, FallbackMass);

        // carry the car's motion at the pivot, including the spin about its CG
        Vector3 velocity = Vector3.zero;
        if (vehicleBody != null)
        {
            velocity = HitBangerInstance.GetVelocity(vehicleBody);
            velocity += Vector3.Cross(vehicleBody.angularVelocity, spawn - vehicleBody.worldCenterOfMass);
        }

        Vector3 away = spawn - impactPoint;
        away.y += 0.25f;

        if (away.sqrMagnitude < 1.0e-4f)
            away = UnityEngine.Random.onUnitSphere;

        away.Normalize();

        velocity += away * EjectPush;
        velocity.y += EjectLift;

        banger.Launch(velocity, UnityEngine.Random.insideUnitSphere * EjectSpin);

        if (level != null)
            level.MoveToRoom(banger, room);
    }

    public void SetVariant(int index)
    {
        foreach (var breakable in breakables)
            RefreshMaterials(breakable);
    }

    private static void RefreshMaterials(VehBreakable breakable)
    {
        var src = breakable.SourceRenderers;
        var dst = breakable.CopyRenderers;
        if (src == null || dst == null) return;

        int count = Mathf.Min(src.Length, dst.Length);
        for (int i = 0; i < count; i++)
        {
            if (src[i] == null || dst[i] == null) continue;

            // sharedMaterials on both sides - the template owns these arrays,
            // and .material here would leak an instance per breakable per variant
            dst[i].sharedMaterials = src[i].sharedMaterials;
        }
    }

    public void Reset()
    {
        foreach (var breakable in breakables)
        {
            if (breakable.Attached)
                continue;

            breakable.Banger.Despawn();
            breakable.Attached = true;

            if (breakable.Source != null)
                breakable.Source.SetActive(true);
        }
    }

    private void OnDestroy()
    {
        // the copies are unparented, so they don't follow the car down
        foreach (var breakable in breakables)
        {
            if (breakable.Object != null)
                Destroy(breakable.Object);
        }

        breakables.Clear();
    }
}