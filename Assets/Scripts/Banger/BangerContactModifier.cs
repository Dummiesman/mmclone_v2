using Unity.Collections;
using UnityEngine;

public static class BangerContactModifier
{
    // colliderInstanceID -> impulse limit (N·s)
    private static NativeParallelHashMap<int, float> Limits;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Init()
    {
        if (Limits.IsCreated) Limits.Dispose();
        Limits = new NativeParallelHashMap<int, float>(512, Allocator.Persistent);

        Physics.ContactModifyEvent -= OnModify;
        Physics.ContactModifyEvent += OnModify;

        // Swept CCD contacts (Continuous / ContinuousDynamic) are raised here instead
        Physics.ContactModifyEventCCD -= OnModify;
        Physics.ContactModifyEventCCD += OnModify;

        Application.quitting -= Shutdown;
        Application.quitting += Shutdown;
    }

    private static void Shutdown()
    {
        Physics.ContactModifyEvent -= OnModify;
        Physics.ContactModifyEventCCD -= OnModify;
        if (Limits.IsCreated) Limits.Dispose();
    }

    public static void Register(int instanceId, float limit)
    {
        if (!Limits.IsCreated) return;
        Limits[instanceId] = limit;          // indexer upserts
    }

    public static void Unregister(int instanceId)
    {
        if (!Limits.IsCreated) return;
        Limits.Remove(instanceId);
    }

    // Shared by both the regular and CCD contact modification events
    private static void OnModify(PhysicsScene scene, NativeArray<ModifiableContactPair> pairs)
    {
        var limits = Limits;                 // local copy, avoids static field access per iteration
        if (!limits.IsCreated) return;

        for (int p = 0; p < pairs.Length; p++)
        {
            var pair = pairs[p];

            if (!limits.TryGetValue(pair.colliderInstanceID, out float limit) &&
                !limits.TryGetValue(pair.otherColliderInstanceID, out limit))
                continue;

            int count = pair.contactCount;
            if (count == 0) continue;

            float perPoint = limit / count;
            for (int i = 0; i < count; i++)
                pair.SetMaxImpulse(i, perPoint);
        }
    }
}