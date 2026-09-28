using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

[StructLayout(LayoutKind.Sequential)]
public struct LightGlowInstance
{
    public Vector3 position;
    public float size;      // half-extent of the quad (same as a2 in tglDrawParticle)
    public Vector4 color;

    public const int Stride = sizeof(float) * 8;
}


[DefaultExecutionOrder(10000)] // run after other scripts' LateUpdate so their glows get included
public class LightGlowRenderer : MonoBehaviour
{
    static readonly int GlowsId = Shader.PropertyToID("_Glows");
    static readonly int MainTexId = Shader.PropertyToID("_MainTex");

    static LightGlowRenderer s_Instance;

    const string ShaderName = "Lights/LightGlow";
    [SerializeField] Shader shader;

    Material material;

    void Awake()
    {
        if (shader == null)
            shader = Shader.Find(ShaderName);

        if (shader == null)
        {
            Debug.LogError($"LightGlowRenderer: shader '{ShaderName}' not found.", this);
            return;
        }

        material = new Material(shader)
        {
            name = "LightGlow (Runtime)",
            hideFlags = HideFlags.HideAndDontSave
        };
    }

    void OnDestroy()
    {
        if (material != null)
        {
            Destroy(material);
            material = null;
        }
    }

    /// <summary>All glows sharing one texture.</summary>
    class LightGlowBatch
    {
        public readonly Texture texture;
        public readonly List<LightGlowInstance> glows = new List<LightGlowInstance>(64);
        public readonly MaterialPropertyBlock props = new MaterialPropertyBlock();
        public GraphicsBuffer buffer;
        public Bounds bounds;
        public int unusedFrames;

        public LightGlowBatch(Texture texture)
        {
            this.texture = texture;
            props.SetTexture(MainTexId, texture);
        }

        public void Add(in LightGlowInstance glow)
        {
            var b = new Bounds(glow.position, Vector3.one * (glow.size * 2f));
            if (glows.Count == 0) bounds = b;
            else bounds.Encapsulate(b);
            glows.Add(glow);
        }

        public void EnsureCapacity(int count)
        {
            if (buffer != null && buffer.count >= count)
                return;

            buffer?.Release();
            buffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                Mathf.NextPowerOfTwo(Mathf.Max(count, 16)),
                LightGlowInstance.Stride);
            props.SetBuffer(GlowsId, buffer);
        }

        public void Release()
        {
            buffer?.Release();
            buffer = null;
        }
    }

    // Batches that haven't been used for this many frames free their GPU buffer.
    const int ReleaseAfterUnusedFrames = 120;

    readonly Dictionary<Texture, LightGlowBatch> _batches = new Dictionary<Texture, LightGlowBatch>();
    readonly List<Texture> _toRemove = new List<Texture>();

    /// <summary>
    /// Equivalent of tglDrawParticle(position, size, color) with the glow texture bound.
    /// A null texture draws a plain white quad.
    /// </summary>
    public static void Draw(Vector3 position, float size, Color color, Texture texture)
    {
        if (s_Instance == null)
            return;
        s_Instance.Queue(position, size, color, texture != null ? texture : Texture2D.whiteTexture);
    }

    void OnEnable()
    {
        s_Instance = this;
    }

    void OnDisable()
    {
        if (s_Instance == this)
            s_Instance = null;

        foreach (var batch in _batches.Values)
            batch.Release();
        _batches.Clear();
    }

    void Queue(Vector3 position, float size, Color color, Texture texture)
    {
        // The original packed each channel into a byte, so it could never exceed 1.
        // Remove the clamp if you want HDR glows (e.g. for bloom).
        color.r = Mathf.Clamp01(color.r);
        color.g = Mathf.Clamp01(color.g);
        color.b = Mathf.Clamp01(color.b);
        color.a = Mathf.Clamp01(color.a);

        // The original colors were gamma-space vertex colors.
        if (QualitySettings.activeColorSpace == ColorSpace.Linear)
            color = color.linear;

        if (!_batches.TryGetValue(texture, out var batch))
        {
            batch = new LightGlowBatch(texture);
            _batches.Add(texture, batch);
        }

        batch.Add(new LightGlowInstance
        {
            position = position,
            size = size,
            color = color
        });
    }

    void LateUpdate()
    {
        if (material == null)
        {
            foreach (var batch in _batches.Values)
                batch.glows.Clear();
            return;
        }

        foreach (var pair in _batches)
        {
            var batch = pair.Value;
            int count = batch.glows.Count;

            if (count == 0)
            {
                // Destroyed textures compare equal to null; drop those batches right away.
                if (pair.Key == null || ++batch.unusedFrames > ReleaseAfterUnusedFrames)
                    _toRemove.Add(pair.Key);
                continue;
            }

            batch.unusedFrames = 0;
            batch.EnsureCapacity(count);
            batch.buffer.SetData(batch.glows, 0, 0, count);

            var rp = new RenderParams(material)
            {
                worldBounds = batch.bounds,
                matProps = batch.props,
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = false
            };

            // 6 vertices per instance (the original 4-vertex fan split into two triangles)
            Graphics.RenderPrimitives(rp, MeshTopology.Triangles, 6, count);

            batch.glows.Clear();
        }

        foreach (var key in _toRemove)
        {
            _batches[key].Release();
            _batches.Remove(key);
        }
        _toRemove.Clear();
    }
}