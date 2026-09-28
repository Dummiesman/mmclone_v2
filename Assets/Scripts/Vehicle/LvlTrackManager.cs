using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Port of lvlTrackManager. Tire tracks are stored in a power-of-two ring buffer of
/// edge entries (left/right points across the tire). Consecutive entries form quads.
/// An entry with TexCoord == 0 starts a new strip.
///
/// Straight driving doesn't add vertices. The last vertex is "stretched" forward while
/// the direction stays within ~8 degrees (dot >= 0.99) and the run is at most 10 units.
/// </summary>
public class LvlTrackManager : MonoBehaviour
{
    struct TrackEntry
    {
        public Vector3 Left;
        public Vector3 Right;
        public Texture Texture;
        public float TexCoord;  // segment length in wheel widths, 0 = strip start
        public double V;        // cumulative V along the strip (added in the port for stable UVs)
        public byte Alpha;      // added in the port: intensity
    }

    [Header("Wheel")]
    [Tooltip("Its WorldPose right (X) axis spans the tire.")]
    public VehWheel wheel;
    public float wheelWidth = 0.25f;
    public Texture2D defaultTexture;

    [Header("Buffer")]
    [Tooltip("Ring buffer holds 2^N entries.")]
    [Range(6, 16)] public int bufferSizePow2 = 10;

    [Header("Rendering")]
    [Tooltip("Assign Custom/TireTracks here so it's included in builds.")]
    public Shader trackShader;
    public Color trackColor = new Color(0.15f, 0.15f, 0.15f, 0.8f);
    public float groundOffset = 0.01f;
    public int renderLayer = 0;

    // Constants from the original
    const float MinStartDistanceSqr = 0.010000001f; // 0.1 units before a strip is committed
    const float StraightDot = 0.99f;
    const float MaxStretchDistance = 10f;

    TrackEntry[] m_Tracks;
    int m_Mask;
    int m_Head;  // dword_04: next write slot
    int m_Tail;  // dword_08: oldest live entry

    float m_HalfWheelWidth;
    float m_InvWheelWidth;

    bool m_HasPendingStart;  // dword_4c
    Vector3 m_StartLeft;     // dword_34..3c
    Vector3 m_StartRight;    // dword_40..48
    byte m_StartAlpha;

    Vector3 m_LastTrackPosition;
    Vector3 m_LastDirection; // dword_28
    Texture m_LastTexture;

    // Rendering
    Mesh m_Mesh;
    bool m_Dirty;
    readonly Dictionary<Texture, Material> m_Materials = new Dictionary<Texture, Material>();
    readonly Dictionary<Texture, List<int>> m_Indices = new Dictionary<Texture, List<int>>();
    readonly List<Texture> m_SubmeshTextures = new List<Texture>();
    List<Vector3> m_Verts;
    List<Vector2> m_Uvs;
    List<Color32> m_Colors;

    // ------------------------------------------------------------------ setup

    void Awake()
    {
        int size = 1 << bufferSizePow2;
        m_Tracks = new TrackEntry[size];
        m_Mask = size - 1;

        m_Verts = new List<Vector3>(size * 4);
        m_Uvs = new List<Vector2>(size * 4);
        m_Colors = new List<Color32>(size * 4);

        m_Mesh = new Mesh { name = "TireTracks" };
        m_Mesh.MarkDynamic();
        if (size * 4 > 65535) m_Mesh.indexFormat = IndexFormat.UInt32;

        if (trackShader == null) trackShader = Shader.Find("Custom/TireTracks");
        if (trackShader == null)
        {
            Debug.LogWarning("LvlTrackManager: Custom/TireTracks not found, using Sprites/Default.");
            trackShader = Shader.Find("Sprites/Default");
        }

        SetWidth(wheelWidth);
        Clear();
    }

    public void Init(VehWheel wheel, float wheelWidth, Texture2D texture)
    {
        this.wheel = wheel;
        SetWidth(wheelWidth);
        defaultTexture = texture;
    }

    void SetWidth(float width)
    {
        wheelWidth = Mathf.Max(0.001f, width);
        m_HalfWheelWidth = wheelWidth * 0.5f;
        m_InvWheelWidth = 1f / wheelWidth;
    }

    // ------------------------------------------------------------------ API

    /// <summary>Convenience wrapper: uses the default texture.</summary>
    public void AddPoint(Vector3 position, Vector3 normal, float intensity)
    {
        UpdateTrack(position, normal, defaultTexture, intensity);
    }

    /// <summary>Ends the current strip (same as passing a null texture).</summary>
    public void Break()
    {
        m_HasPendingStart = false;
        m_LastTexture = null;
    }

    /// <summary>Removes all tracks.</summary>
    public void Clear()
    {
        m_Head = m_Tail = 0;
        Break();
        m_Dirty = true;
    }

    public void SetColor(Color color)
    {
        trackColor = color;
        foreach (var mat in m_Materials.Values) mat.SetColor("_Color", color);
    }

    /// <summary>
    /// Port of lvlTrackManager::Update. Call once per frame per wheel while grounded.
    /// A null texture breaks the track (e.g. wheel in the air or on a surface without marks).
    /// </summary>
    public void UpdateTrack(Vector3 position, Vector3 surfaceNormal, Texture texture, float intensity = 1f)
    {
        if (texture == null)
        {
            Break();
            return;
        }

        // Original: row 0 of the wheel matrix (its X axis) times +/- half width
        Vector3 wheelRight = wheel != null ? wheel.WorldPose.right : transform.right;
        Vector3 axis = wheelRight * m_HalfWheelWidth;
        Vector3 lift = surfaceNormal * groundOffset;
        Vector3 left = position - axis + lift;
        Vector3 right = position + axis + lift;
        byte alpha = (byte)(Mathf.Clamp01(intensity) * 255f);

        // No active strip: remember a start edge, but don't commit it yet
        if (m_LastTexture == null && !m_HasPendingStart)
        {
            m_HasPendingStart = true;
            m_StartLeft = left;
            m_StartRight = right;
            m_StartAlpha = alpha;
            m_LastTrackPosition = position;
            return;
        }

        // Pending start: commit it once we've moved far enough to know the direction
        if (m_HasPendingStart)
        {
            Vector3 delta = position - m_LastTrackPosition;
            if (delta.sqrMagnitude < MinStartDistanceSqr) return;

            PushEntry(m_StartLeft, m_StartRight, texture, 0f, m_StartAlpha);
            m_HasPendingStart = false;
            m_LastDirection = delta.normalized;
            m_LastTexture = texture;
            PushEntry(left, right, texture, delta.magnitude * m_InvWheelWidth, alpha);
            // m_LastTrackPosition intentionally stays at the strip start, as in the original
            return;
        }

        // Active strip: stretch the last vertex if still going straight, otherwise add one
        Vector3 d = position - m_LastTrackPosition;
        float dist = d.magnitude;
        if (dist < 1e-4f) return; // guard: the original divides by zero here
        Vector3 dir = d / dist;
        float texCoord = dist * m_InvWheelWidth;

        if (texture == m_LastTexture &&
            Vector3.Dot(dir, m_LastDirection) >= StraightDot &&
            dist <= MaxStretchDistance)
        {
            ref TrackEntry last = ref m_Tracks[(m_Head - 1) & m_Mask];
            last.TexCoord = texCoord;
            last.Left = left;
            last.Right = right;
            last.Alpha = alpha;
            last.V = m_Tracks[(m_Head - 2) & m_Mask].V + texCoord;
            m_Dirty = true;
        }
        else
        {
            PushEntry(left, right, texture, texCoord, alpha); // AddVertex
            m_LastTexture = texture;
            m_LastDirection = dir;
            m_LastTrackPosition = position;
        }
    }

    // ------------------------------------------------------------------ ring buffer

    void PushEntry(Vector3 left, Vector3 right, Texture texture, float texCoord, byte alpha)
    {
        int next = (m_Head + 1) & m_Mask;
        if (next == m_Tail)
        {
            // Full: drop the oldest entry. If that leaves a lone vertex before
            // a new strip start, drop that too.
            m_Tail = (m_Tail + 1) & m_Mask;
            int after = (m_Tail + 1) & m_Mask;
            if (m_Tracks[after].TexCoord == 0f) m_Tail = after;
        }

        double v = texCoord == 0f ? 0.0 : m_Tracks[(m_Head - 1) & m_Mask].V + texCoord;

        m_Tracks[m_Head] = new TrackEntry
        {
            Left = left,
            Right = right,
            Texture = texture,
            TexCoord = texCoord,
            V = v,
            Alpha = alpha
        };
        m_Head = next;
        m_Dirty = true;
    }

    // ------------------------------------------------------------------ rendering

    void LateUpdate()
    {
        if (m_Mesh == null) return;
        if (m_Dirty) Rebuild();

        for (int s = 0; s < m_SubmeshTextures.Count; s++)
        {
            Graphics.DrawMesh(m_Mesh, Matrix4x4.identity, GetMaterial(m_SubmeshTextures[s]),
                renderLayer, null, s, null, ShadowCastingMode.Off, false);
        }
    }

    void Rebuild()
    {
        m_Dirty = false;
        m_Verts.Clear();
        m_Uvs.Clear();
        m_Colors.Clear();
        m_SubmeshTextures.Clear();
        foreach (var list in m_Indices.Values) list.Clear();

        int i = m_Tail;
        while (i != m_Head)
        {
            int n = (i + 1) & m_Mask;
            if (n == m_Head) break;
            if (m_Tracks[n].TexCoord > 0f) AddQuad(ref m_Tracks[i], ref m_Tracks[n]);
            i = n;
        }

        m_Mesh.Clear();
        m_Mesh.SetVertices(m_Verts);
        m_Mesh.SetUVs(0, m_Uvs);
        m_Mesh.SetColors(m_Colors);
        m_Mesh.subMeshCount = m_SubmeshTextures.Count;
        for (int s = 0; s < m_SubmeshTextures.Count; s++)
            m_Mesh.SetTriangles(m_Indices[m_SubmeshTextures[s]], s, false);
        m_Mesh.RecalculateBounds();
    }

    void AddQuad(ref TrackEntry e0, ref TrackEntry e1)
    {
        int b = m_Verts.Count;
        m_Verts.Add(e0.Left);
        m_Verts.Add(e0.Right);
        m_Verts.Add(e1.Left);
        m_Verts.Add(e1.Right);

        // Wrap V per quad to keep float precision; seamless with Repeat wrap mode
        float v0 = (float)(e0.V - Math.Floor(e0.V));
        float v1 = v0 + e1.TexCoord;
        m_Uvs.Add(new Vector2(0f, v0));
        m_Uvs.Add(new Vector2(1f, v0));
        m_Uvs.Add(new Vector2(0f, v1));
        m_Uvs.Add(new Vector2(1f, v1));

        var c0 = new Color32(255, 255, 255, e0.Alpha);
        var c1 = new Color32(255, 255, 255, e1.Alpha);
        m_Colors.Add(c0);
        m_Colors.Add(c0);
        m_Colors.Add(c1);
        m_Colors.Add(c1);

        // The segment uses the texture of its end vertex
        if (!m_Indices.TryGetValue(e1.Texture, out var idx))
        {
            idx = new List<int>();
            m_Indices[e1.Texture] = idx;
        }
        if (idx.Count == 0) m_SubmeshTextures.Add(e1.Texture);

        idx.Add(b); idx.Add(b + 2); idx.Add(b + 1);
        idx.Add(b + 1); idx.Add(b + 2); idx.Add(b + 3);
    }

    Material GetMaterial(Texture texture)
    {
        if (!m_Materials.TryGetValue(texture, out var mat))
        {
            mat = new Material(trackShader) { name = "TireTracks_" + texture.name };
            mat.mainTexture = texture;
            mat.SetColor("_Color", trackColor);
            m_Materials[texture] = mat;
        }
        return mat;
    }

    void OnDestroy()
    {
        if (m_Mesh != null) Destroy(m_Mesh);
        foreach (var mat in m_Materials.Values) Destroy(mat);
        m_Materials.Clear();
    }
}