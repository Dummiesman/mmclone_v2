using UnityEngine;
using UnityEngine.Rendering;

// Non-instanced draw path for ParticleSim.
public partial class ParticleSim
{
    [Tooltip("Force the non-instanced path even where instancing works. Debug only.")]
    public bool ForceFallbackPath = false;

    // source geometry, read off particleMesh once. Mesh.vertices and friends build a
    // fresh managed array on every get, so this must never happen per frame
    private Vector3[] sourceVertices;
    private Vector2[] sourceUvs;
    private int[] sourceTriangles;
    private float sourceExtent;     // largest |vertex| in local space, for bounds padding
    private bool fallbackInitialized = false;
    private bool batchAvailable = false;

    // baked mesh state, allocated on first fallback draw so the instanced path pays nothing
    private Material batchedMaterial;
    private Mesh batchedMesh;
    private Vector3[] batchedPivots;
    private Vector3[] batchedOffsets;
    private Vector2[] batchedUvs;
    private Color32[] batchedColors;
    private int[] batchedIndices;
    private MaterialPropertyBlock fallbackBlock;

    private static readonly int ColorPropertyId = Shader.PropertyToID("_Color");
    private static readonly int UvBlockPropertyId = Shader.PropertyToID("_UvBlock");

    public string DescribeDrawPath()
    {
        if (InstancingAvailable)
            return "Instanced - 1 draw call";

        if (!fallbackInitialized)
            return "Fallback - not resolved until the first draw";

        return batchAvailable
            ? "Fallback - stamped batch, 1 draw call"
            : $"Fallback - per particle, {numEmittedParticles} draw calls";
    }

    public bool InstancingAvailable
    {
        get
        {
            if (ForceFallbackPath)
                return false;

            if (!SystemInfo.supportsInstancing)
                return false;

            // a material with the instancing box unticked never defines
            // UNITY_INSTANCING_ENABLED, so the shader silently takes its scalar branch
            return particleMaterial != null && particleMaterial.enableInstancing;
        }
    }

    private void InitFallback()
    {
        fallbackInitialized = true;

        if (fallbackBlock == null)
            fallbackBlock = new MaterialPropertyBlock();

        if (particleMesh == null || particleInstances == null)
            return;

        // an imported mesh without Read/Write Enabled has no CPU copy in a build, so
        // there is nothing to stamp - those sims take the per-particle path instead
        if (!particleMesh.isReadable)
        {
            Debug.LogWarning($"ParticleSim mesh '{particleMesh.name}' is not readable, so the non-instanced " +
                             "path falls back to one draw call per particle.", this);
            return;
        }

        sourceVertices = particleMesh.vertices;
        sourceUvs = particleMesh.uv;
        sourceTriangles = particleMesh.GetTriangles(0);

        if (sourceVertices.Length == 0 || sourceTriangles.Length == 0)
            return;

        if (sourceUvs == null || sourceUvs.Length != sourceVertices.Length)
        {
            Debug.LogWarning($"ParticleSim mesh '{particleMesh.name}' has no usable uv0 channel, " +
                             "falling back to one draw call per particle.", this);
            return;
        }

        int poolSize = particleInstances.Length;
        int vertsPer = sourceVertices.Length;
        int indicesPer = sourceTriangles.Length;

        batchedPivots = new Vector3[poolSize * vertsPer];
        batchedOffsets = new Vector3[poolSize * vertsPer];
        batchedUvs = new Vector2[poolSize * vertsPer];
        batchedColors = new Color32[poolSize * vertsPer];
        batchedIndices = new int[poolSize * indicesPer];

        // indices never change - a draw just uses the first (live * indicesPer) of them
        for (int i = 0; i < poolSize; i++)
        {
            int baseVertex = i * vertsPer;
            int baseIndex = i * indicesPer;
            for (int k = 0; k < indicesPer; k++)
                batchedIndices[baseIndex + k] = baseVertex + sourceTriangles[k];
        }

        sourceExtent = 0f;
        for (int k = 0; k < vertsPer; k++)
        {
            float magnitude = sourceVertices[k].magnitude;
            if (magnitude > sourceExtent)
                sourceExtent = magnitude;
        }

        batchedMesh = new Mesh { name = particleMesh.name + "Batch" };
        batchedMesh.MarkDynamic();

        if (poolSize * vertsPer > 65535)
            batchedMesh.indexFormat = IndexFormat.UInt32;

        // its own material instance, because the keyword is per-material and CopyFields
        // shares particleMaterial between sims that may not all be on this path
        if (particleMaterial != null)
        {
            batchedMaterial = new Material(particleMaterial) { name = particleMaterial.name + " (batched)" };
            batchedMaterial.enableInstancing = false;
            batchedMaterial.EnableKeyword("MMPARTICLE_BATCHED");
            batchAvailable = true;
        }
    }

    public void DrawFallback()
    {
        if (numEmittedParticles == 0 || particleInstances == null || particleMaterial == null)
            return;

        if (!fallbackInitialized)
            InitFallback();

        if (batchAvailable)
            DrawFallbackBatched();
        else
            DrawFallbackPerParticle();
    }

    // one draw call. the shader still does the billboarding, so what gets stamped is the
    // pivot (particle position, repeated per vertex) plus the spun and scaled local vertex
    // that MMParticle adds in view space - NOT a finished world-space position.
    // tile and tint fold into the uv and vertex colour channels.
    private void DrawFallbackBatched()
    {
        // SetTextureSheet only touches particleMaterial
        if (batchedMaterial.mainTexture != particleMaterial.mainTexture)
            batchedMaterial.mainTexture = particleMaterial.mainTexture;

        int vertsPer = sourceVertices.Length;
        int indicesPer = sourceTriangles.Length;

        Vector3 min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        Vector3 max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
        float maxRadius = 0f;

        int vertexCount = 0;
        for (int i = 0; i < numEmittedParticles; i++)
        {
            var particle = particleInstances[i];

            // matches the instanced scale of Vector3.one * (Radius * 2f)
            float size = particle.Radius * 2f;

            // SetMatrix spins around Vector3.forward, so this is a 2D rotation in xy
            float sin = 0f, cos = 1f;
            if (Mathf.Abs(particle.Rotation) > Mathf.Epsilon)
            {
                float radians = particle.Rotation * Mathf.Deg2Rad;
                sin = Mathf.Sin(radians);
                cos = Mathf.Cos(radians);
            }

            Vector4 uvBlock = GetUvBlockForFrame(particle.CurrentTexFrame);

            Color color = particle.Color;
            color.a *= particle.Alpha / ParticleInstance.MaxAlpha;
            Color32 packedColor = color;

            Vector3 pivot = particle.Position;

            for (int k = 0; k < vertsPer; k++)
            {
                Vector3 local = sourceVertices[k] * size;

                batchedPivots[vertexCount] = pivot;
                batchedOffsets[vertexCount] = new Vector3((local.x * cos) - (local.y * sin),
                                                          (local.x * sin) + (local.y * cos),
                                                          local.z);
                batchedUvs[vertexCount] = new Vector2(uvBlock.x + (sourceUvs[k].x * uvBlock.z),
                                                      uvBlock.y + (sourceUvs[k].y * uvBlock.w));
                batchedColors[vertexCount] = packedColor;
                vertexCount++;
            }

            min = Vector3.Min(min, pivot);
            max = Vector3.Max(max, pivot);
            if (particle.Radius > maxRadius)
                maxRadius = particle.Radius;
        }

        // Clear first, otherwise a shrinking particle count leaves indices pointing past
        // the new vertex array for one assignment
        batchedMesh.Clear(false);
        batchedMesh.SetVertices(batchedPivots, 0, vertexCount);
        batchedMesh.SetUVs(0, batchedUvs, 0, vertexCount);
        batchedMesh.SetUVs(1, batchedOffsets, 0, vertexCount);
        batchedMesh.SetColors(batchedColors, 0, vertexCount);
        batchedMesh.SetIndices(batchedIndices, 0, (vertexCount / vertsPer) * indicesPer,
                               MeshTopology.Triangles, 0, false);

        // every vertex sits at its pivot, so the geometry expands outside these bounds at
        // draw time - pad by the biggest local reach or particles pop at screen edges.
        // the mesh is already in world space under an identity transform, so it has to
        // carry its own bounds rather than inheriting the transform's.
        Vector3 padding = Vector3.one * (maxRadius * 2f * sourceExtent);
        batchedMesh.bounds = new Bounds((min + max) * 0.5f, (max - min) + (padding * 2f));

        Graphics.DrawMesh(batchedMesh, Matrix4x4.identity, batchedMaterial, this.gameObject.layer,
                          null, 0, null, ShadowCastingMode.Off, false);
    }

    // last resort for meshes with no CPU copy to stamp. runs through the shader's scalar
    // branch. Graphics.DrawMesh copies the property block at submit, so a single reused
    // block is safe. this is N draw calls - keep the pool small here.
    private void DrawFallbackPerParticle()
    {
        int layer = this.gameObject.layer;

        for (int i = 0; i < numEmittedParticles; i++)
        {
            var particle = particleInstances[i];
            particle.SetMatrix(ref particleMatrices[i]);

            Color color = particle.Color;
            color.a *= particle.Alpha / ParticleInstance.MaxAlpha;

            fallbackBlock.SetColor(ColorPropertyId, color);
            fallbackBlock.SetVector(UvBlockPropertyId, GetUvBlockForFrame(particle.CurrentTexFrame));

            Graphics.DrawMesh(particleMesh, particleMatrices[i], particleMaterial, layer,
                              null, 0, fallbackBlock, ShadowCastingMode.Off, false);
        }
    }

    private void ResetFallback()
    {
        fallbackInitialized = false;
        batchAvailable = false;

        if (batchedMesh != null)
            Destroy(batchedMesh);
        if (batchedMaterial != null)
            Destroy(batchedMaterial);

        batchedMesh = null;
        batchedMaterial = null;
        sourceVertices = null;
        sourceUvs = null;
        sourceTriangles = null;
    }

    private void OnDestroy()
    {
        ResetFallback();
    }
}