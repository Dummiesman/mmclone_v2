using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

/// <summary>
/// Port of asSparkLut: a small color lookup table indexed by (row, age).
/// Rows are random color variations; columns run over the spark's lifetime.
/// Column 0 is the end of life, the last column is a freshly spawned spark.
/// </summary>
public sealed class SparkLut
{
    public Color32[] Colors;  // Width * Height, row-major
    public int Shift;         // column = age >> Shift, so Width == 256 >> Shift
    public int Height;

    public int Width => 256 >> Shift;

    static readonly Dictionary<string, SparkLut> cache = new Dictionary<string, SparkLut>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => cache.Clear();

    /// <summary>asSparkLut::Get - cached load.</summary>
    public static SparkLut Get(string name)
    {
        string key = name ?? string.Empty;
        if (!cache.TryGetValue(key, out var lut))
        {
            lut = Load(name);
            cache[key] = lut;
        }
        return lut;
    }

    public static void ClearCache() => cache.Clear();

    /// <summary>asSparkLut::Init</summary>
    static SparkLut Load(string filename)
    {
        // Defaults, same as the original (8 wide x 4 high builtin table).
        var lut = CreateBuiltin();

        if (string.IsNullOrEmpty(filename))
            return lut;

        // Original: datAssetManager::Open("texture", filename, "tga").
        // Adjust the path/signature to match your TextureLoader.
        Texture2D tex = TextureLoader.Load(filename);
        if (tex == null)
            return lut;

        int w = tex.width, h = tex.height;

        if (!IsPowerOfTwo(w) || !IsPowerOfTwo(h))
        {
            Debug.LogError($"{filename}: size is not power of two (doesn't have to be square though)");
            return lut;
        }

        int count = w * h;
        if (count > 256)
        {
            Debug.LogError($"{filename}: can't have more than 256 pixels.");
            return lut;
        }

        if (!tex.isReadable)
        {
            Debug.LogError($"{filename}: texture is not CPU-readable, can't build spark LUT.");
            return lut;
        }

        // Width is stretched to 256 age steps: shift = log2(256 / width).
        int shift = 0;
        for (int width = w; width < 256; width <<= 1)
            shift++;

        // Original read raw TGA rows in file order (bottom-up for a standard TGA),
        // which matches GetPixels32's bottom-up row order.
        Color32[] pixels = tex.GetPixels32();

        // 24-bit TGAs got a forced alpha of 0x80 in the original.
        if (!GraphicsFormatUtility.HasAlphaChannel(tex.graphicsFormat))
        {
            for (int i = 0; i < pixels.Length; i++)
                pixels[i].a = 0x80;
        }

        lut.Colors = pixels;
        lut.Shift = shift;
        lut.Height = h;
        return lut;
    }

    static bool IsPowerOfTwo(int v) => v > 0 && (v & (v - 1)) == 0;

    /// <summary>asSparkLut::BuiltinClut - 8 wide x 4 high, D3D ARGB (0xAARRGGBB).</summary>
    static readonly uint[] BuiltinClut =
    {
        // Row 0: plain red ramp
        0xFF330000, 0xFF4C0000, 0xFF660000, 0xFF7F0000, 0xFF990000, 0xFFB20000, 0xFFCC0000, 0xFFE50000,
        // Row 1: dark red -> pink/purple
        0xFF330019, 0xFF4C0019, 0xFF660019, 0xFF7F1933, 0xFF99334C, 0xFFB24C7F, 0xFFCC66B2, 0xFFE599CC,
        // Row 2: dark red -> pink/purple (slightly different)
        0xFF330019, 0xFF4C0019, 0xFF660019, 0xFF7F3333, 0xFF994C66, 0xFFB2667F, 0xFFCC7F99, 0xFFE599B2,
        // Row 3: red -> white-hot
        0xFF4C0000, 0xFF660000, 0xFF7F0000, 0xFF990000, 0xFFB20000, 0xFFCC4C4C, 0xFFE59999, 0xFFFFFFFF,
    };

    static SparkLut CreateBuiltin()
    {
        var colors = new Color32[BuiltinClut.Length];
        for (int i = 0; i < colors.Length; i++)
            colors[i] = ArgbToColor32(BuiltinClut[i]);

        return new SparkLut { Colors = colors, Shift = 5, Height = 4 };
    }

    static Color32 ArgbToColor32(uint argb) => new Color32(
        (byte)(argb >> 16), // R
        (byte)(argb >> 8),  // G
        (byte)argb,         // B
        (byte)(argb >> 24)); // A
}

/// <summary>
/// Port of asLineSparks: short line-segment sparks with gravity, ground bounce
/// and a LUT-driven color over lifetime. Everything is simulated in world space.
/// </summary>
public class LineSparks : MonoBehaviour
{
    [Header("Setup")]
    public int MaxSparks = 256;
    [Tooltip("LUT texture name passed to TextureLoader.Load. Empty = builtin table.")]
    public string LutName = "";
    [Tooltip("Unlit vertex-color material. If empty, one is created from Unlit/LineSparks.")]
    public Material Material;

    [Header("Physics")]
    public float GroundHeight = 0f;
    public float Gravity = -20f;
    [Tooltip("Vertical velocity multiplier on ground bounce (original: 0.8).")]
    public float Bounce = 0.8f;

    [Header("Blast (original unknown64..76)")]
    public float MinHorizontalSpeed = 6f;
    public float MaxHorizontalSpeed = 7f;
    public float MinVerticalSpeed = 4f;
    public float MaxVerticalSpeed = 5f;
    public float XRadialRandomness = 0.1f;
    public float YRadialRandomness = 0.1f;
    public float ZRadialRandomness = 0.1f;

    [Header("Look / timing")]
    [Tooltip("Original 'SizeReductionPerFrame'. Tail end = start + velocity * dt * this.")]
    public float TrailLength = -0.036f;
    [Tooltip("Age change per second (original unknown92). Sparks spawn at age 192-255 and die below 0.")]
    public float AgeRate = -650f;
    [Tooltip("Simulation step (original UpdateTimerTarget, 30 Hz).")]
    public float UpdateInterval = 1f / 30f;

    public int SparkCount => count;

    // Per-spark data
    Vector3[] velocities;
    Vector3[] startPositions;
    Vector3[] endPositions;
    Color32[] colors;
    byte[] lutRow;   // original unknown36: row offset (row * width) into the LUT
    byte[] age;      // original unknown40: 255 = new, counts down to 0

    int count;
    float updateTimer;
    SparkLut lut;

    // Rendering
    Mesh mesh;
    Vector3[] meshVertices;
    Color32[] meshColors;
    int[] meshIndices;
    bool meshDirty;
    bool ownsMaterial;

    // Separate RNG so blasts don't disturb UnityEngine.Random
    // (stand-in for DisableGlobalSeed / EnableGlobalSeed).
    readonly System.Random rng = new System.Random();

    static int refCount;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => refCount = 0;

    void Awake()
    {
        refCount++;
        Init(MaxSparks, LutName);
    }

    void OnDestroy()
    {
        if (--refCount == 0)
            SparkLut.ClearCache();

        if (mesh != null) Destroy(mesh);
        if (ownsMaterial && Material != null) Destroy(Material);
    }

    /// <summary>asLineSparks::Init - (re)allocates buffers and loads the LUT.</summary>
    public void Init(int numSparks, string lutName)
    {
        MaxSparks = Mathf.Max(1, numSparks);
        LutName = lutName;

        velocities = new Vector3[MaxSparks];
        startPositions = new Vector3[MaxSparks];
        endPositions = new Vector3[MaxSparks];
        colors = new Color32[MaxSparks];
        lutRow = new byte[MaxSparks];
        age = new byte[MaxSparks];
        count = 0;
        updateTimer = 0f;

        lut = SparkLut.Get(lutName);

        meshVertices = new Vector3[MaxSparks * 2];
        meshColors = new Color32[MaxSparks * 2];
        meshIndices = new int[MaxSparks * 2];
        for (int i = 0; i < meshIndices.Length; i++)
            meshIndices[i] = i;

        if (mesh == null)
        {
            mesh = new Mesh { name = "LineSparks" };
            mesh.MarkDynamic();
        }
        mesh.Clear();

        if (Material == null)
        {
            Shader shader = Shader.Find("Unlit/LineSparks") ?? Shader.Find("Sprites/Default");
            Material = new Material(shader) { hideFlags = HideFlags.DontSave };
            ownsMaterial = true;
        }
    }

    public void Clear()
    {
        count = 0;
        meshDirty = true;
    }

    /// <summary>asLineSparks::RadialBlast - spawns sparks spraying out along 'normal'.</summary>
    public void RadialBlast(int numSparks, Vector3 position, Vector3 normal)
    {
        // Build a basis around the normal. The original didn't normalize these,
        // which only matters for tilted normals (it shrank the sideways spread).
        Vector3 tangent = Mathf.Abs(Vector3.Dot(Vector3.up, normal)) >= 0.95f
            ? Vector3.Cross(normal, Vector3.right)
            : Vector3.Cross(normal, Vector3.up);
        tangent.Normalize();
        Vector3 bitangent = Vector3.Cross(tangent, normal).normalized;

        int rowMask = (lut.Height - 1) << (8 - lut.Shift);

        for (int k = 0; k < numSparks && count < MaxSparks; k++)
        {
            int i = count;

            // Mostly "up" (along the normal) with a small random lean.
            Vector3 v = new Vector3(
                (Random.value - 0.5f) * XRadialRandomness,
                Random.value * YRadialRandomness,
                (Random.value - 0.5f) * ZRadialRandomness);

            endPositions[i] = position + v;
            startPositions[i] = endPositions[i];

            v = v.normalized;
            float horizontal = Random.value * (MaxHorizontalSpeed - MinHorizontalSpeed) + MinHorizontalSpeed;
            v.x *= horizontal;
            v.z *= horizontal;
            v.y *= Random.value * (MaxVerticalSpeed - MinVerticalSpeed) + MinVerticalSpeed;

            // Local Y maps onto the normal.
            velocities[i] = tangent * v.x + normal * v.y + bitangent * v.z;

            lutRow[i] = (byte)(rowMask & Irand());
            age[i] = (byte)(192 + (Irand() & 0x3F)); // == (irand() & 0x3F) - 0x40 as a byte
            colors[i] = lut.Colors[lutRow[i] + (age[i] >> lut.Shift)];

            count++;
        }

        meshDirty = true;
    }

    void Update()
    {
        // asLineSparks::Update() - fixed-ish 30 Hz stepping with the accumulated time.
        updateTimer += Time.deltaTime;
        if (updateTimer >= UpdateInterval)
        {
            Simulate(updateTimer);
            updateTimer = 0f;
        }
    }

    /// <summary>asLineSparks::Update(float)</summary>
    public void Simulate(float dt)
    {
        float gravityStep = dt * Gravity;
        float trail = dt * TrailLength;
        int ageStep = (int)(dt * AgeRate); // C-style truncation, same as the original

        for (int i = 0; i < count;)
        {
            int newAge = age[i] + ageStep;
            if (newAge < 0)
            {
                RemoveAt(i);
                continue; // re-process the spark swapped into this slot
            }
            age[i] = (byte)Mathf.Min(newAge, 255);

            colors[i] = lut.Colors[lutRow[i] + (age[i] >> lut.Shift)];

            Vector3 v = velocities[i];
            Vector3 s = startPositions[i];

            // Tail is placed slightly behind the current head (TrailLength is negative).
            endPositions[i] = s + v * trail;

            v.y += gravityStep;
            s += v * dt;

            if (s.y < GroundHeight && v.y < 0f)
                v.y *= -Bounce;

            velocities[i] = v;
            startPositions[i] = s;
            i++;
        }

        meshDirty = true;
    }

    void RemoveAt(int i)
    {
        int last = count - 1;
        velocities[i] = velocities[last];
        startPositions[i] = startPositions[last];
        endPositions[i] = endPositions[last];
        colors[i] = colors[last];
        lutRow[i] = lutRow[last];
        age[i] = age[last];
        count--;
    }

    /// <summary>asLineSparks::Draw - world-space line list with per-spark color.</summary>
    void LateUpdate()
    {
        if (count == 0)
            return;

        if (meshDirty)
            RebuildMesh();

        Graphics.DrawMesh(mesh, Matrix4x4.identity, Material, gameObject.layer);
    }

    void RebuildMesh()
    {
        meshDirty = false;
        int n = count * 2;

        Vector3 min = startPositions[0], max = startPositions[0];
        for (int i = 0; i < count; i++)
        {
            Vector3 a = startPositions[i], b = endPositions[i];
            meshVertices[i * 2] = a;
            meshVertices[i * 2 + 1] = b;
            meshColors[i * 2] = colors[i];
            meshColors[i * 2 + 1] = colors[i];
            min = Vector3.Min(min, Vector3.Min(a, b));
            max = Vector3.Max(max, Vector3.Max(a, b));
        }

        mesh.Clear();
        mesh.SetVertices(meshVertices, 0, n);
        mesh.SetColors(meshColors, 0, n);
        mesh.SetIndices(meshIndices, 0, n, MeshTopology.Lines, 0, false);
        mesh.bounds = new Bounds((min + max) * 0.5f, max - min);
    }

    static int Irand() => Random.Range(0, 0x8000); // 0..32767, int max is exclusive
}