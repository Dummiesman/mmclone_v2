using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Port of fxShardManager / fxShard.
/// A fixed pool of CPU-simulated triangle shards (glass, debris) that spin, fall
/// under gravity and expire after a fixed lifetime. All live shards are batched
/// into one dynamic world-space mesh, with one submesh per material.
/// </summary>
public class ShardManager : MonoBehaviour
{
    // fxShardTimeToLive
    public const float ShardTimeToLive = 1.8f;
    // Hardcoded in fxShard::Update (roughly 2x Earth gravity; reads better for small debris)
    const float Gravity = 20f;
    // The original only ever draws with the first 4 shaders.
    const int MaxMaterials = 4;

    [Header("Pool")]
    [Tooltip("Rounded up to a power of two, because the ring buffer wraps with a bitmask.")]
    [SerializeField] int shardCount = 32;
    [Tooltip("Only the first 4 are used, chosen per shard as in the original Draw. Any material works; shards are built double-sided.")]
    [SerializeField] Material[] materials;

    /// <summary>
    /// Materials used for drawing (fxShardManager::SetShader). Safe to set every frame:
    /// it only swaps the reference and takes effect at the next LateUpdate.
    /// </summary>
    public Material[] Materials
    {
        get => materials;
        set => materials = value;
    }

    [Header("Emission thresholds")]
    public float impulseThreshold = 500f;
    public float speedThreshold = 5f;
    public float impulseEmitRatio = 300f;
    public int maxShardsPerEmit = 2;

    [Header("Launch velocity (fraction of impact speed, per local axis)")]
    public float xDamp = 0.3f;
    public float yDamp = 0.3f;
    public float zDamp = 0.2f;

    [Header("Spin")]
    [Tooltip("Radians per second. Each shard spins at a random rate in [base, 5 * base].")]
    public float rotationSpeedBase = 10.8f;

    [Header("Appearance")]
    [Tooltip("Length of the triangle's two short edges, in world units.")]
    public float shardSize = 0.1f;
    [Tooltip("How much of the texture one shard's UVs span. Each shard gets a random UV offset, so set the texture's wrap mode to Repeat.")]
    public float uvScale = 0.3f;

    struct Shard
    {
        public Vector3 position;
        public Quaternion rotation;
        public Vector3 velocity;
        public Vector3 rotationAxis;
        public float rotationSpeed; // rad/s
        public float aliveTime;
        public Vector2 uvOffset;

        public bool Alive => aliveTime < ShardTimeToLive;
    }

    Shard[] shards;
    int lastShardIndex;

    // Private RNG stands in for DisableGlobalSeed()/EnableGlobalSeed():
    // shard randomness never disturbs the game's seeded/deterministic random stream.
    System.Random rng;

    Mesh mesh;
    Vector3[] vertices;
    Vector3[] normals;
    Vector4[] tangents;
    Vector2[] uvs;
    List<int>[] submeshTriangles;

    // draw_textured_tri: a right triangle lying flat in the local XZ plane, with the
    // right-angle corner at the shard's origin, so the shard spins around that corner.
    static readonly Vector3[] TriLocal =
    {
        new Vector3(0f, 0f, 1f),
        new Vector3(0f, 0f, 0f),
        new Vector3(1f, 0f, 0f),
    };

    static readonly Vector2[] TriUV =
    {
        new Vector2(0f, 1f),
        new Vector2(0f, 0f),
        new Vector2(1f, 0f),
    };

    // Uses the inspector values. When set up from code, call Init afterwards;
    // it replaces everything Awake allocated.
    void Awake() => Init(shardCount, materials);

    void OnDestroy()
    {
        if (mesh != null) Destroy(mesh);
    }

    /// <summary>
    /// fxShardManager::Init — allocates the pool (all shards start dead) and sets the
    /// starting materials. Only the first 4 materials are used.
    /// </summary>
    public void Init(int count, Material[] startMaterials)
    {
        materials = startMaterials;
        shardCount = Mathf.NextPowerOfTwo(Mathf.Max(1, count));
        shards = new Shard[shardCount];
        for (int i = 0; i < shardCount; i++)
        {
            shards[i].aliveTime = ShardTimeToLive;
            shards[i].rotation = Quaternion.identity;
        }
        lastShardIndex = 0;

        if (rng == null) rng = new System.Random();

        // 6 vertices per shard: a front face and a back face, each with its own normals.
        // This stands in for the original forcing culling off, and gives lit materials
        // (like car body materials) the normals and tangents they need.
        vertices = new Vector3[shardCount * 6];
        normals = new Vector3[shardCount * 6];
        tangents = new Vector4[shardCount * 6];
        uvs = new Vector2[shardCount * 6];

        // Always allocate for the maximum, so the material count can change at runtime.
        submeshTriangles = new List<int>[MaxMaterials];
        for (int i = 0; i < MaxMaterials; i++)
            submeshTriangles[i] = new List<int>(shardCount * 3);

        if (mesh == null)
        {
            mesh = new Mesh { name = "Shards" };
            mesh.MarkDynamic();
        }
        mesh.Clear();
    }

    float Rand() => (float)rng.NextDouble(); // frand(): [0, 1)

    /// <summary>
    /// fxShardManager::EmitShards — call on impact. Emits 1..maxShardsPerEmit shards
    /// depending on impulse, but only if both impulse and speed pass their thresholds.
    /// </summary>
    public void EmitShards(Vector3 position, float impulse, float speed, Quaternion orientation)
    {
        if (impulse <= impulseThreshold || speed <= speedThreshold) return;

        int count = Mathf.Min((int)(impulse / impulseEmitRatio), maxShardsPerEmit);
        for (int i = 0; i < count; i++)
            EmitShard(position, speed, orientation);
    }

    /// <summary>fxShardManager::EmitAllShards — fire the whole pool at once.</summary>
    public void EmitAllShards(Vector3 position, float speed, Quaternion orientation)
    {
        for (int i = 0; i < shards.Length; i++)
            EmitShard(position, speed, orientation);
    }

    /// <summary>fxShardManager::EmitShard — recycles the oldest slot in the ring buffer.</summary>
    public void EmitShard(Vector3 position, float speed, Quaternion orientation)
    {
        // Local-space launch velocity: sideways spread, always upward, always along +Z.
        Vector3 localVel = new Vector3(
            (Rand() * 2f - 1f) * xDamp * speed,
            (Rand() + 1f) * 0.5f * yDamp * speed,
            (Rand() * 0.9f + 0.1f) * zDamp * speed);

        // Original multiplies by the rows of the 3x3 part of a Matrix34 (row-vector
        // convention), which is the same as rotating from local to world space.
        Vector3 velocity = orientation * localVel;

        // Random spin axis, biased toward +Y (the Y component is never negative).
        float a = Rand() - 0.5f;
        float b = Rand();
        float c = Rand() - 0.5f;
        Vector3 axis = new Vector3(c, b, a);
        axis = axis.sqrMagnitude > 0f ? axis.normalized : Vector3.up;

        float spin = Mathf.Lerp(rotationSpeedBase, rotationSpeedBase * 5f, Rand());

        ref Shard s = ref shards[lastShardIndex];
        lastShardIndex = (lastShardIndex + 1) & (shards.Length - 1);

        // fxShard::AddShard. Only the translation is set: the orientation carries over
        // from the slot's previous shard, which adds free variety to the starting pose.
        s.position = position;
        s.velocity = velocity;
        s.rotationAxis = axis;
        s.rotationSpeed = spin;
        s.aliveTime = 0f;
        s.uvOffset = new Vector2(Rand(), Rand()); // TexUCoord / TexVCoord
    }

    /// <summary>fxShardManager::Update / fxShard::Update</summary>
    void Update()
    {
        float dt = Time.deltaTime;

        for (int i = 0; i < shards.Length; i++)
        {
            ref Shard s = ref shards[i];
            if (!s.Alive) continue;

            // Semi-implicit Euler, same order as the original: velocity, then position.
            s.velocity.y -= Gravity * dt;
            s.rotation = Quaternion.AngleAxis(s.rotationSpeed * dt * Mathf.Rad2Deg, s.rotationAxis) * s.rotation;
            s.position += s.velocity * dt;
            s.aliveTime += dt;
        }
    }

    /// <summary>
    /// fxShardManager::Draw / fxShard::Draw. Instead of one draw_textured_tri per shard,
    /// all live shards go into one mesh. Each shard has a front and back face, which
    /// replaces the original's CullMode save/set/restore.
    /// </summary>
    void LateUpdate()
    {
        if (materials == null || materials.Length == 0) return;

        // Only the first MaxMaterials entries are used; any extras are ignored.
        int subCount = Mathf.Min(materials.Length, MaxMaterials);
        for (int m = 0; m < subCount; m++) submeshTriangles[m].Clear();

        bool anyAlive = false;

        // Same selection as fxShardManager::Draw: the index counts up once per shard
        // (live or dead) and wraps at ShardCount / ShaderCount, not at ShaderCount.
        // Where that runs past the end of the array, it is capped to the last material.
        int wrapAt = shards.Length / subCount;
        int materialCounter = 0;

        for (int i = 0; i < shards.Length; i++)
        {
            int materialIndex = Mathf.Min(materialCounter, subCount - 1);
            materialCounter++;
            if (materialCounter == wrapAt) materialCounter = 0;

            ref Shard s = ref shards[i];
            if (!s.Alive) continue;
            anyAlive = true;

            // Front face uses the original winding; the triangle lies in local XZ,
            // so with Unity's clockwise-front convention it faces local -Y.
            Vector3 frontNormal = s.rotation * Vector3.down;
            Vector3 uAxis = s.rotation * Vector3.right; // U runs along local +X

            int v = i * 6;
            for (int k = 0; k < 3; k++)
            {
                Vector3 pos = s.position + s.rotation * (TriLocal[k] * shardSize);
                Vector2 uv = s.uvOffset + TriUV[k] * uvScale;

                vertices[v + k] = pos;
                normals[v + k] = frontNormal;
                tangents[v + k] = new Vector4(uAxis.x, uAxis.y, uAxis.z, 1f);
                uvs[v + k] = uv;

                vertices[v + 3 + k] = pos;
                normals[v + 3 + k] = -frontNormal;
                tangents[v + 3 + k] = new Vector4(uAxis.x, uAxis.y, uAxis.z, -1f);
                uvs[v + 3 + k] = uv;
            }

            List<int> tris = submeshTriangles[materialIndex];
            tris.Add(v); tris.Add(v + 1); tris.Add(v + 2); // front
            tris.Add(v + 3); tris.Add(v + 5); tris.Add(v + 4); // back (reversed winding)
        }

        if (!anyAlive) return;

        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetTangents(tangents);
        mesh.SetUVs(0, uvs);
        mesh.subMeshCount = subCount;
        for (int m = 0; m < subCount; m++)
            mesh.SetTriangles(submeshTriangles[m], m, false);
        mesh.RecalculateBounds();

        // Vertices are already in world space, so draw with identity
        // (the original also resets the world matrix to identity after drawing).
        for (int m = 0; m < subCount; m++)
            if (materials[m] != null)
                Graphics.DrawMesh(mesh, Matrix4x4.identity, materials[m], gameObject.layer, null, m);
    }
}