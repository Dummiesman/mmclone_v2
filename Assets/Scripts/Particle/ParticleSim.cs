using UnityEngine;

[System.Flags]
public enum ParticleBirthFlags
{
    Collision = 2,
    Animated = 4,
    KillOnCollision = 8,
    Shadowed = 16
}

public partial class ParticleSim : MonoBehaviour
{
    // Graphics.DrawMeshInstanced hard limit
    public const int MaxInstancesPerDraw = 1023;

    public bool EmitOverTime = false;

    public bool IsLocal = true;
    public ParticleBirthRule BirthRule { get; set; }

    public bool CalculateBounds = true;
    public Bounds Bounds => bounds;
    private Bounds bounds;
    private bool boundsValid = false;

    public LayerMask CollisionLayerMask = ~0;

    [Tooltip("Y height of the flat plane used by the cheap mobile collision path.")]
    public float GroundHeight = 0f;

    public int TextureWidthTiles = 2;
    public int TextureHeightTiles = 2;

    [Tooltip("Playback speed for particles with the Animated birth flag.")]
    public float TexFrameRate = 30f;

    public Vector3 AdditionalVelocity = Vector3.zero;

    public int ParticleCount => numEmittedParticles;
    private int numEmittedParticles = 0;

    // instancing stuff
    private ParticleInstance[] particleInstances;
    private Matrix4x4[] particleMatrices;
    private Vector4[] particleUvs;
    private Vector4[] particleColors;
    private float[] particleAlphas;
    private MaterialPropertyBlock propertyBlock;

    // emit timers
    private float emitOverTimeGlobalTimer = 0f;
    private float emitOverTimeSingleTimer = 0f;

    // mesh
    private Mesh particleMesh;

    [SerializeField]
    private Material particleMaterial;
    public Material Material => particleMaterial;

    public float SimulationRate = 1f;

    // inspector convenience only - nothing reads this back
    public Texture2D ParticleTexturePREVIEW;

    public void CopyFields(ParticleSim from)
    {
        // share a material instance
        particleMaterial = from.particleMaterial;
        Init(from.particleInstances.Length, from.particleMesh); // init the rest of things
        this.BirthRule = from.BirthRule;

        EmitOverTime = from.EmitOverTime;
        IsLocal = from.IsLocal;
        TextureWidthTiles = from.TextureWidthTiles;
        TextureHeightTiles = from.TextureHeightTiles;
        TexFrameRate = from.TexFrameRate;

        CalculateBounds = from.CalculateBounds;
        CollisionLayerMask = from.CollisionLayerMask;
        GroundHeight = from.GroundHeight;
        SimulationRate = from.SimulationRate;

        AdditionalVelocity = from.AdditionalVelocity;
        ParticleTexturePREVIEW = from.ParticleTexturePREVIEW;

        SimulationRate = from.SimulationRate;
        ForceFallbackPath = from.ForceFallbackPath;

        this.ResetEmitCounter();
    }

    public void Init(int poolSize = 128, Mesh customMesh = null)
    {
        if (poolSize > MaxInstancesPerDraw)
        {
            Debug.LogWarning($"ParticleSim pool size {poolSize} exceeds the {MaxInstancesPerDraw} instance limit of " +
                             "Graphics.DrawMeshInstanced, clamping. Split into several sims or move to an indirect draw.", this);
            poolSize = MaxInstancesPerDraw;
        }

        // init instance pool
        particleInstances = new ParticleInstance[poolSize];
        particleMatrices = new Matrix4x4[poolSize];
        particleAlphas = new float[poolSize];
        particleUvs = new Vector4[poolSize];
        particleColors = new Vector4[poolSize];

        // init data
        for (int i = 0; i < poolSize; i++)
        {
            particleInstances[i] = new ParticleInstance();
        }
        ResetFallback();

        numEmittedParticles = 0;
        boundsValid = false;
        bounds = new Bounds(transform.position, Vector3.zero);

        // Init can run before Awake if the object was created inactive
        if (propertyBlock == null)
            propertyBlock = new MaterialPropertyBlock();

        // init material
        if (particleMaterial == null)
        {
            var shader = Shader.Find("Custom/MMParticle");
            if (shader == null)
            {
                Debug.LogError("ParticleSim could not find shader 'Custom/MMParticle'. " +
                               "Add it to Always Included Shaders or assign a material manually.", this);
            }
            else
            {
                particleMaterial = new Material(shader)
                {
                    enableInstancing = true
                };
            }
        }

        // init mesh
        if (customMesh == null)
        {
            particleMesh = new Mesh
            {
                name = "ParticleQuad",
                vertices = new Vector3[] { (Vector3.left + Vector3.up) * 0.5f, (Vector3.right + Vector3.up) * 0.5f,
                                           (Vector3.down + Vector3.right) * 0.5f, (Vector3.down + Vector3.left) * 0.5f},
                uv = new Vector2[] { Vector2.zero, new Vector2(1f, 0f), Vector2.one, new Vector2(0f, 1f) },
                subMeshCount = 1
            };
            particleMesh.SetTriangles(new int[] { 2, 1, 0, 3, 2, 0 }, 0);

            particleMesh.RecalculateTangents();
            particleMesh.RecalculateNormals();
            particleMesh.bounds = new Bounds(Vector3.zero, new Vector3(1f, 1f, 0f));
        }
        else
        {
            particleMesh = customMesh;
        }
    }

    // TEXTURE
    private Vector4 GetUvBlockForFrame(int texFrame)
    {
        int widthTiles = Mathf.Max(1, TextureWidthTiles);
        int heightTiles = Mathf.Max(1, TextureHeightTiles);

        // rows are counted in height tiles, not width tiles
        int texRow = heightTiles - 1 - (texFrame / widthTiles);
        int texColumn = texFrame % widthTiles;

        float uvSizeX = 1f / (float)widthTiles;
        float uvSizeY = 1f / (float)heightTiles;
        return new Vector4(texColumn * uvSizeX, texRow * uvSizeY, uvSizeX, uvSizeY);
    }

    public void SetTextureSheet(Texture2D texture)
    {
        ParticleTexturePREVIEW = texture;
        if (particleMaterial != null)
            particleMaterial.mainTexture = texture;
    }

    public void SetTextureSheet(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            ParticleTexturePREVIEW = null;
            if (particleMaterial != null)
                particleMaterial.mainTexture = null;
        }
        else
        {
            SetTextureSheet(TextureCache.Get(name));
        }
    }

    // RULE
    public void ResetEmitCounter()
    {
        emitOverTimeGlobalTimer = 0f;
        emitOverTimeSingleTimer = 0f;
    }

    // BOUNDS
    private void EncapsulateParticle(ParticleInstance instance)
    {
        Vector3 pos = instance.Position;
        Vector3 extent = Vector3.one * instance.Radius;

        if (!boundsValid)
        {
            // seed from the first particle. starting from Vector3.zero made every
            // bounding box stretch back to the world origin
            bounds = new Bounds(pos, extent * 2f);
            boundsValid = true;
            return;
        }

        bounds.Encapsulate(pos - extent);
        bounds.Encapsulate(pos + extent);
    }

    // EMISSION FUNCTIONS
    public void Emit(ParticleInstance instance)
    {
        if (instance == null)
            return;

        if (numEmittedParticles < particleInstances.Length)
        {
            particleInstances[numEmittedParticles] = instance;
            if (CalculateBounds)
                EncapsulateParticle(instance);
            numEmittedParticles++;
        }
    }

    public void Emit(int count)
    {
        if (BirthRule == null)
        {
            Debug.LogError("ParticleSim::Emit - cannot be called without a BirthRule!", this);
            return;
        }
        if(particleInstances == null)
        {
            Debug.LogError("ParticleSim::Emit - not initialized!", this);
            return;
        }

        // init instances
        int emitCount = 0;
        for (int i = numEmittedParticles; i < numEmittedParticles + count && i < particleInstances.Length; i++)
        {
            particleInstances[i].InitFromSim(this);
            if (CalculateBounds)
                EncapsulateParticle(particleInstances[i]);
            emitCount++;
        }

        // add to emission count
        numEmittedParticles += emitCount;
    }

    public void Blast()
    {
        if (BirthRule == null)
        {
            Debug.LogError("ParticleSim::Blast cannot be called without a BirthRule!", this);
            return;
        }
        Emit(BirthRule.InitialBlast);
    }

    public void ClearParticles()
    {
        numEmittedParticles = 0;
        boundsValid = false;
        bounds = new Bounds(transform.position, Vector3.zero);
    }

    private void Awake()
    {
        if (propertyBlock == null)
            propertyBlock = new MaterialPropertyBlock();
    }

    private void Update()
    {
        float timeStep = Time.deltaTime * SimulationRate;

        // emit if emit over time is set
        if (EmitOverTime && BirthRule != null)
        {
            emitOverTimeGlobalTimer += timeStep;
            if (BirthRule.SpewTimeLimit == 0f || emitOverTimeGlobalTimer < BirthRule.SpewTimeLimit)
            {
                emitOverTimeSingleTimer += timeStep * BirthRule.SpewRate;

                // keep the fractional remainder so the spew rate is exact, and allow
                // more than one particle per frame when the rate is above the framerate
                int emitCount = Mathf.FloorToInt(emitOverTimeSingleTimer);
                if (emitCount > 0)
                {
                    emitOverTimeSingleTimer -= emitCount;
                    Emit(emitCount);
                }
            }
        }

        // bounds are rebuilt from scratch each frame so they can shrink as well as grow
        if (CalculateBounds)
            boundsValid = false;

        // update emitted particles
        for (int i = 0; i < numEmittedParticles; i++)
        {
            var particle = particleInstances[i];
            particle.Update(timeStep, this);
            bool instanceIsAlive = particle.Life > 0f;

            // do collision
            bool doCollide = (particle.BirthFlags & (ParticleBirthFlags.Collision | ParticleBirthFlags.KillOnCollision)) != 0;
            if (instanceIsAlive && doCollide && timeStep > 0f)
            {
                bool killOnCollision = (particle.BirthFlags & ParticleBirthFlags.KillOnCollision) != 0;

                if (Application.isMobilePlatform)
                {
                    if (particle.CollideCheap(GroundHeight))
                    {
                        if (killOnCollision)
                            instanceIsAlive = false;
                        else
                            particle.ProcessCollideCheap(GroundHeight);
                    }
                }
                else
                {
                    RaycastHit hitInfo;
                    if (particle.Collide(timeStep, CollisionLayerMask, out hitInfo))
                    {
                        if (killOnCollision)
                            instanceIsAlive = false;
                        else
                            particle.ProcessCollide(hitInfo);
                    }
                }
            }

            // post-update
            if (!instanceIsAlive)
            {
                // swap-back removal. O(1) instead of shifting the whole pool, and the
                // dead instance is parked at the end for reuse instead of being dropped
                numEmittedParticles--;
                particleInstances[i] = particleInstances[numEmittedParticles];
                particleInstances[numEmittedParticles] = particle;
                i--; // we've filled a hole, go back to it
                continue;
            }

            if (CalculateBounds)
                EncapsulateParticle(particle);
        }

        if (CalculateBounds && !boundsValid)
            bounds = new Bounds(transform.position, Vector3.zero);

        // draw
        Draw();
    }

    private void DrawInstanced()
    {
        if (propertyBlock == null)
            propertyBlock = new MaterialPropertyBlock();

        // setup arrays
        for (int i = 0; i < numEmittedParticles; i++)
        {
            particleInstances[i].SetMatrix(ref particleMatrices[i]);
            particleAlphas[i] = particleInstances[i].Alpha / ParticleInstance.MaxAlpha;
            particleUvs[i] = GetUvBlockForFrame(particleInstances[i].CurrentTexFrame);

            var color = particleInstances[i].Color;
            particleColors[i].x = color.r;
            particleColors[i].y = color.g;
            particleColors[i].z = color.b;
            particleColors[i].w = color.a;
        }

        // set block
        propertyBlock.SetFloatArray("_Alphas", particleAlphas);
        propertyBlock.SetVectorArray("_UVs", particleUvs);
        propertyBlock.SetVectorArray("_Colors", particleColors);

        // draw!
        int layer = this.gameObject.layer;
        Graphics.DrawMeshInstanced(this.particleMesh, 0, this.particleMaterial, particleMatrices, numEmittedParticles, propertyBlock,
                                   UnityEngine.Rendering.ShadowCastingMode.Off, false, layer);
    }

    public void Draw()
    {
        // if we have no active particles, or nothing to draw them with, don't draw
        if (numEmittedParticles == 0 || particleMaterial == null || particleMesh == null)
            return;

        // no instancing on this device (or forced off) - stamp or submit per particle instead
        if (!InstancingAvailable)
        {
            DrawFallback();
        }
        else
        {
            DrawInstanced();
        }
    }

    // MISC
    public void Dump()
    {
        Debug.Log($"=== Particle System Dump {this.GetInstanceID()} ===", this.gameObject);
        Debug.Log("Number of particles: " + numEmittedParticles);
        Debug.Log("Bounds: " + bounds.ToString());

        if (BirthRule == null)
        {
            Debug.Log(">> NO BIRTHRULE <<");
        }
        else
        {
            Debug.Log(">> START BIRTHRULE DUMP <<");
            BirthRule.Dump();
            Debug.Log(">> END BIRTHRULE DUMP <<");
        }

        for (int i = 0; i < numEmittedParticles; i++)
        {
            Debug.Log($"Particle {i} {{");
            Debug.Log($"\tPosition: {particleInstances[i].Position}");
            Debug.Log($"\tRotation: {particleInstances[i].Rotation}");
            Debug.Log($"\tRadius: {particleInstances[i].Radius}");
            Debug.Log($"\tAlpha: {particleInstances[i].Alpha}");
            Debug.Log("}");
        }
    }
}