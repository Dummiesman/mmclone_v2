using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

public class VertexColorDamage : MonoBehaviour
{
    public MeshFilter[] BakeToMeshes;

    [Tooltip("Radius of the damaged area at ReferenceSpeed, in mesh local units.")]
    public float TexelDamageRadius = 0.5f;

    [Tooltip("Alpha removed by a hit at ReferenceSpeed. 1 = fully destroyed in one hit.")]
    public float DamagePerHit = 0.25f;

    [Tooltip("Impact speed (m/s) treated as a full-strength hit. Anything faster is clamped.")]
    public float ReferenceSpeed = 20f;

    [Tooltip("Impacts below this speed (m/s) are ignored entirely.")]
    public float MinDamageSpeed = 1.5f;

    // Normalized 0..1 strength for an impact speed.
    private float SpeedFactor(float speed)
    {
        if (ReferenceSpeed <= 0f)
            return 0f;
        return Mathf.Clamp01(speed / ReferenceSpeed);
    }

    // ------------------------------------------------------------------
    // Cached per-mesh working data.
    //
    // Mesh.vertices and Mesh.colors each allocate a fresh managed array on
    // every property access, so we pull them once and keep them. Colors are
    // written back at most once per frame, from the parent Update.
    // ------------------------------------------------------------------
    class MeshData
    {
        public Mesh Mesh;
        public Transform Transform;
        public Vector3[] Vertices;
        public Color[] Colors;
        public bool ColorsDirty;
    }

    // Value may be null for a filter whose mesh is unusable (not readable, etc.)
    private readonly Dictionary<MeshFilter, MeshData> meshData = new Dictionary<MeshFilter, MeshData>();

    private MeshData GetMeshData(MeshFilter filter)
    {
        if (filter == null)
            return null;

        if (meshData.TryGetValue(filter, out var cached))
            return cached;

        MeshData data = null;

        // .mesh instantiates a per-renderer copy the first time, then returns
        // that same instance, so this is safe to call once and hold onto.
        Mesh m = filter.mesh;

        if (m == null)
        {
            Debug.LogWarning("MeshFilter has no mesh; skipping.", filter);
        }
        else if (!m.isReadable)
        {
            Debug.LogWarning($"{m.name} is not read/write enabled; skipping.", filter);
        }
        else
        {
            var verts = m.vertices;
            var colors = m.colors;

            if (colors == null || colors.Length != verts.Length)
            {
                colors = new Color[verts.Length];
                for (int i = 0; i < colors.Length; i++)
                    colors[i] = Color.white;
                m.colors = colors;
            }

            data = new MeshData
            {
                Mesh = m,
                Transform = filter.transform,
                Vertices = verts,
                Colors = colors,
                ColorsDirty = false
            };
        }

        meshData[filter] = data;
        return data;
    }

    // ------------------------------------------------------------------
    // Snapshot of a collision.
    //
    // Unity pools Collision objects and their contact buffers, so the instance
    // handed to OnCollisionEnter is recycled as soon as the callback returns.
    // Everything we need is copied out immediately, and contact points are
    // converted to mesh-local space so they stay pinned to the impact site
    // while the vehicle keeps moving over the frames it takes to drain.
    // ------------------------------------------------------------------
    class CollisionSnapshot
    {
        public Vector3[] LocalPoints;   // contact points in BakeToMesh local space
        public float RelativeSpeed;
        public float Impulse;           // kept available; damage currently uses RelativeSpeed
    }

    class QueuedCollision
    {
        public VertexColorDamage ParentClass;
        public MeshData Mesh;
        public CollisionSnapshot Snapshot;

        public int CurrentProgress;
        public int ClosestIndex = -1;
        public float ClosestDistance = float.MaxValue;
        public int UpdatePhase = 0;
        public int ContactIndex = 0;
        public bool Completed = false;
        public readonly List<int> ComplimentaryIndices = new List<int>();

#if UNITY_ANDROID
        public const int MaxOneframeProgress = 32;
#else
        public const int MaxOneframeProgress = 128;
#endif

        private void BeginNextContact()
        {
            ContactIndex++;
            UpdatePhase = 0;
            CurrentProgress = 0;
            ClosestIndex = -1;
            ClosestDistance = float.MaxValue;
            ComplimentaryIndices.Clear();
        }

        private void UpdateFindCentriodVert()
        {
            Vector3[] verts = Mesh.Vertices;
            Vector3 point = Snapshot.LocalPoints[ContactIndex];

            int end = Mathf.Min(CurrentProgress + MaxOneframeProgress, verts.Length);
            for (; CurrentProgress < end; CurrentProgress++)
            {
                float distance = (point - verts[CurrentProgress]).sqrMagnitude;
                if (distance < ClosestDistance)
                {
                    ClosestDistance = distance;
                    ClosestIndex = CurrentProgress;
                }
            }

            // more to chew through next frame?
            if (CurrentProgress < verts.Length)
                return;

            if (ClosestIndex < 0)
            {
                BeginNextContact();
                return;
            }

            CurrentProgress = 0;
            UpdatePhase++;
        }

        private void UpdateCalcDmgVerts()
        {
            Vector3[] verts = Mesh.Vertices;
            Color[] colors = Mesh.Colors;

            // already fully damaged here - skip to the next contact
            if (colors[ClosestIndex].a <= 0f)
            {
                BeginNextContact();
                return;
            }

            // Sqrt response: light contacts stay small, and the curve flattens as
            // speed rises instead of running away linearly. Squared once up front
            // so it can be compared against sqrMagnitude.
            float t = ParentClass.SpeedFactor(Snapshot.RelativeSpeed);
            float radius = ParentClass.TexelDamageRadius * Mathf.Sqrt(t);
            float radiusSq = radius * radius;

            Vector3 centroid = verts[ClosestIndex];

            int end = Mathf.Min(CurrentProgress + MaxOneframeProgress, verts.Length);
            for (; CurrentProgress < end; CurrentProgress++)
            {
                if (CurrentProgress == ClosestIndex)
                    continue;

                // seam duplicates first, then anything inside the radius,
                // so a vertex can't be added twice
                if (verts[CurrentProgress] == centroid ||
                    (verts[CurrentProgress] - centroid).sqrMagnitude < radiusSq)
                {
                    ComplimentaryIndices.Add(CurrentProgress);
                }
            }

            if (CurrentProgress < verts.Length)
                return;

            CurrentProgress = 0;
            UpdatePhase++;
        }

        private void UpdateApplyDmg()
        {
            Color[] colors = Mesh.Colors;

            float t = ParentClass.SpeedFactor(Snapshot.RelativeSpeed);
            float newAlpha = Mathf.Max(0f, colors[ClosestIndex].a - ParentClass.DamagePerHit * t);

            Color newColor = new Color(1f, 1f, 1f, newAlpha);

            colors[ClosestIndex] = newColor;
            for (int i = 0; i < ComplimentaryIndices.Count; i++)
                colors[ComplimentaryIndices[i]] = newColor;

            // upload is deferred to the parent so several queued collisions
            // sharing a mesh only cost one Mesh.colors assignment per frame
            Mesh.ColorsDirty = true;

            BeginNextContact();
        }

        public void Update()
        {
            if (Completed)
                return;

            if (Mesh == null || Snapshot == null || ContactIndex >= Snapshot.LocalPoints.Length)
            {
                Completed = true;
                return;
            }

            switch (UpdatePhase)
            {
                case 0:
                    UpdateFindCentriodVert();
                    break;
                case 1:
                    UpdateCalcDmgVerts();
                    break;
                case 2:
                    UpdateApplyDmg();
                    break;
            }
        }
    }

    private readonly List<QueuedCollision> collisionQueue = new List<QueuedCollision>();

    // Reused buffer so Collision.GetContacts doesn't allocate.
    private static readonly List<ContactPoint> s_Contacts = new List<ContactPoint>(32);

    void Update()
    {
        // advance, dropping finished entries as we go
        for (int i = collisionQueue.Count - 1; i >= 0; i--)
        {
            var queued = collisionQueue[i];
            queued.Update();

            if (queued.Completed)
                collisionQueue.RemoveAt(i);
        }

        // one upload per mesh per frame
        foreach (var kvp in meshData)
        {
            var data = kvp.Value;
            if (data == null || !data.ColorsDirty)
                continue;

            data.Mesh.colors = data.Colors;
            data.ColorsDirty = false;
        }
    }

    public void Reset()
    {
        collisionQueue.Clear();

        foreach (var filter in BakeToMeshes)
        {
            var data = GetMeshData(filter);
            if (data == null)
                continue;

            var colors = data.Colors;
            for (int i = 0; i < colors.Length; i++)
                colors[i].a = 1f;

            data.Mesh.colors = colors;
            data.ColorsDirty = false;
        }
    }

    public void Collision(Collision collision)
    {
        if (collision == null || BakeToMeshes == null)
            return;

        // Everything below is read while the Collision is still valid.
        int contactCount = collision.GetContacts(s_Contacts);
        if (contactCount == 0)
            return;

        float relativeSpeed = collision.relativeVelocity.magnitude;
        if (relativeSpeed < MinDamageSpeed)
            return;

        float impulse = collision.impulse.magnitude;

        for (int i = 0; i < BakeToMeshes.Length; i++)
        {
            var filter = BakeToMeshes[i];
            var data = GetMeshData(filter);
            if (data == null)
                continue;

            var toLocal = data.Transform.worldToLocalMatrix;
            var points = new Vector3[contactCount];
            for (int c = 0; c < contactCount; c++)
                points[c] = toLocal.MultiplyPoint3x4(s_Contacts[c].point);

            collisionQueue.Add(new QueuedCollision
            {
                ParentClass = this,
                Mesh = data,
                Snapshot = new CollisionSnapshot
                {
                    LocalPoints = points,
                    RelativeSpeed = relativeSpeed,
                    Impulse = impulse
                }
            });
        }
    }

    private static void EnsureWhiteVertexColors(MeshFilter[] filters)
    {
        var done = new HashSet<Mesh>();

        foreach (var mf in filters)
        {
            var mesh = mf.sharedMesh;
            if (mesh == null || !done.Add(mesh)) continue;
            if (mesh.HasVertexAttribute(VertexAttribute.Color)) continue;

            if (!mesh.isReadable)
            {
                Debug.LogWarning($"{mesh.name} is not read/write enabled; skipping.", mf);
                continue;
            }

            var white = new Color32(255, 255, 255, 255);
            var colors = new Color32[mesh.vertexCount];
            for (int i = 0; i < colors.Length; i++) colors[i] = white;
            mesh.colors32 = colors;
        }
    }

    public void Init(VehicleModel vehModel)
    {
        // get meshes
        BakeToMeshes = vehModel.BodyObject.GetComponentsInChildren<MeshFilter>(true).Take(1).ToArray();

        // ensure they have a color channel
        EnsureWhiteVertexColors(BakeToMeshes);

        // drop any cache from a previous body
        collisionQueue.Clear();
        meshData.Clear();
    }
}