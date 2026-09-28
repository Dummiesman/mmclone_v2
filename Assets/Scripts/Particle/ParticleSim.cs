using UnityEngine;

[System.Flags]
public enum ParticleBirthFlags
{
    Collision = 2,
    Animated = 4,
    KillOnCollision = 8,
    Shadowed = 16
}

public class ParticleInstance
{
    public const float MaxAlpha = 255f;

    public Vector3 Position => position;
    private Vector3 position;

    public float Rotation { get; private set; }

    public Vector3 Velocity => velocity;
    private Vector3 velocity;

    public Color Color { get; private set; }
    public float Life { get; private set; }
    public float Mass { get; private set; }
    public float Radius { get; private set; }
    public float Drag { get; private set; }
    public float Damp { get; private set; }
    public float DRadius { get; private set; }
    public float DAlpha { get; private set; }
    public float DRotation { get; private set; }
    public float Gravity { get; private set; }

    public int TexFrameStart { get; private set; }
    public int TexFrameEnd { get; private set; }
    public int CurrentTexFrame { get; private set; }
    private float texFrameTimer;

    public ParticleBirthFlags BirthFlags { get; private set; }
    public float Alpha { get; private set; } = MaxAlpha;

    public bool CollideCheap(float height)
    {
        return (position.y - Radius) < height;
    }

    public void ProcessCollideCheap(float height)
    {
        //place the particle back on top of the plane, otherwise a slow particle
        //sits under it and flips its velocity every frame
        position.y = height + Radius;

        velocity *= Damp;
        velocity.y = Mathf.Abs(velocity.y);
        DRotation *= Damp;
    }

    public bool Collide(float timeStep, int layerMask, out RaycastHit hitInfo)
    {
        hitInfo = default(RaycastHit);

        float speed = velocity.magnitude;
        if (speed < Mathf.Epsilon)
            return false;

        //probe at least a diameter ahead, but far enough to catch this frame's travel too
        float distance = Mathf.Max(Radius * 2.01f, (speed * timeStep) + Radius);
        return Physics.Raycast(position, velocity / speed, out hitInfo, distance, layerMask);
    }

    public void ProcessCollide(RaycastHit hitInfo)
    {
        float speed = velocity.magnitude;
        if (speed < Mathf.Epsilon)
            return;

        Vector3 direction = velocity / speed;
        Vector3 reflection = Vector3.Reflect(direction, hitInfo.normal);

        velocity = reflection * (speed * Damp);

        var hitRigidbody = hitInfo.rigidbody;
        if (hitRigidbody != null)
        {
            //how head-on the impact was: 1 = straight into the surface, 0 = grazing
            float impact = Mathf.Clamp01(-Vector3.Dot(direction, hitInfo.normal));
            velocity += hitRigidbody.velocity * (impact * Damp);
        }

        DRotation *= Damp;

        //push out of the surface instead of stepping the sim, so we can't re-hit it next frame
        position = hitInfo.point + (hitInfo.normal * Radius);
    }

    public void Update(float timeStep, ParticleSim parent = null)
    {
        float dt60 = timeStep * 60f;

        Vector3 additionalVelocity = (parent != null) ? parent.AdditionalVelocity : Vector3.zero;
        Vector3 totalVelocity = velocity + additionalVelocity;

        //drag. clamped at -1 so a big timestep or a very fast particle can never
        //overshoot and invert the velocity
        float dragScale = Mathf.Max(-(totalVelocity.magnitude * Drag) * Mass * timeStep, -1f);
        velocity.x += totalVelocity.x * dragScale;
        velocity.z += totalVelocity.z * dragScale;
        velocity.y += (totalVelocity.y * dragScale) + (Gravity * timeStep);

        //AdditionalVelocity advects the particle as well as feeding drag
        position += (velocity + additionalVelocity) * timeStep;

        //alpha
        if (Mathf.Abs(DAlpha) > Mathf.Epsilon)
        {
            Alpha = Mathf.Clamp(Alpha + (DAlpha * dt60), 0f, MaxAlpha);
        }

        //scale
        Radius += (dt60 * DRadius);

        //life
        Life -= timeStep;

        //rotation
        Rotation += (dt60 * DRotation);

        //animate, if specified
        if ((BirthFlags & ParticleBirthFlags.Animated) != 0 && TexFrameEnd > TexFrameStart)
        {
            float frameRate = (parent != null) ? parent.TexFrameRate : 0f;
            if (frameRate > 0f)
            {
                texFrameTimer += timeStep * frameRate;
                while (texFrameTimer >= 1f)
                {
                    texFrameTimer -= 1f;
                    CurrentTexFrame++;
                    if (CurrentTexFrame > TexFrameEnd)
                        CurrentTexFrame = TexFrameStart;
                }
            }
        }
    }

    public void SetMatrix(ref Matrix4x4 matrix)
    {
        matrix.SetTRS(Position, Quaternion.AngleAxis(Rotation, Vector3.forward), Vector3.one * (Radius * 2f));
    }

    public void InitFromSim(ParticleSim sim)
    {
        if (sim.BirthRule == null)
        {
            Debug.LogError("An attempt was made to initialize a ParticleInstance with a NULL BirthRule, aborting!", sim);
            return;
        }

        //
        var rule = sim.BirthRule;
        float particleRandom()
        {
            return Random.Range(-0.5f, 0.5f);
        }

        //position
        Vector3 positionVariance;
        positionVariance.x = rule.Position.Variation.x * particleRandom();
        positionVariance.y = rule.Position.Variation.y * particleRandom();
        positionVariance.z = rule.Position.Variation.z * particleRandom();

        if (sim.IsLocal)
        {
            position = sim.transform.position + positionVariance;
        }
        else
        {
            position = rule.Position.Value + positionVariance;
        }

        //velocity
        velocity.x = rule.Velocity.Value.x + (rule.Velocity.Variation.x * particleRandom());
        velocity.y = rule.Velocity.Value.y + (rule.Velocity.Variation.y * particleRandom());
        velocity.z = rule.Velocity.Value.z + (rule.Velocity.Variation.z * particleRandom());

        //life
        Life = rule.Life.Value + (rule.Life.Variation * particleRandom());

        //mass
        Mass = rule.Mass.Value + (rule.Mass.Variation * particleRandom());

        //radius
        Radius = rule.Radius.Value + (rule.Radius.Variation * particleRandom());

        //drag
        Drag = rule.Drag.Value + (rule.Drag.Variation * particleRandom());

        //damp
        Damp = rule.Damp.Value + (rule.Damp.Variation * particleRandom());

        //dradius dalpha drotation
        DRadius = rule.DRadius.Value + (rule.DRadius.Variation * particleRandom());
        DAlpha = rule.DAlpha.Value + (rule.DAlpha.Variation * particleRandom());
        DRotation = rule.DRotation.Value + (rule.DRotation.Variation * particleRandom());

        //flags
        BirthFlags = rule.BirthFlags;

        //gravity
        Gravity = rule.Gravity;

        //alpha + rotation reset (instances are pooled and reused)
        Alpha = MaxAlpha;
        Rotation = 0f;

        //color
        Color = rule.Color;

        //tile range + starting frame
        TexFrameStart = rule.TexFrameStart;
        TexFrameEnd = rule.TexFrameEnd;
        texFrameTimer = 0f;

        if ((rule.BirthFlags & ParticleBirthFlags.Animated) != 0)
        {
            CurrentTexFrame = TexFrameStart;
        }
        else
        {
            CurrentTexFrame = Random.Range(TexFrameStart, TexFrameEnd + 1);
        }
    }
}

public partial class ParticleSim : MonoBehaviour
{
    //Graphics.DrawMeshInstanced hard limit
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

    //instancing stuff
    private ParticleInstance[] particleInstances;
    private Matrix4x4[] particleMatrices;
    private Vector4[] particleUvs;
    private Vector4[] particleColors;
    private float[] particleAlphas;
    private MaterialPropertyBlock propertyBlock;

    //emit timers
    private float emitOverTimeGlobalTimer = 0f;
    private float emitOverTimeSingleTimer = 0f;

    //mesh
    private Mesh particleMesh;

    [SerializeField]
    private Material particleMaterial;
    public Material Material => particleMaterial;

    public float SimulationRate = 1f;

    //inspector convenience only - nothing reads this back
    public Texture2D ParticleTexturePREVIEW;

    public void CopyFields(ParticleSim from)
    {
        //share a material instance
        particleMaterial = from.particleMaterial;
        Init(from.particleInstances.Length, from.particleMesh); //init the rest of things
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

        //init instance pool
        particleInstances = new ParticleInstance[poolSize];
        particleMatrices = new Matrix4x4[poolSize];
        particleAlphas = new float[poolSize];
        particleUvs = new Vector4[poolSize];
        particleColors = new Vector4[poolSize];

        //init data
        for (int i = 0; i < poolSize; i++)
        {
            particleInstances[i] = new ParticleInstance();
        }

        numEmittedParticles = 0;
        boundsValid = false;
        bounds = new Bounds(transform.position, Vector3.zero);

        //Init can run before Awake if the object was created inactive
        if (propertyBlock == null)
            propertyBlock = new MaterialPropertyBlock();

        //init material
        if (particleMaterial == null)
        {
            var shader = Shader.Find("Custom/MMParticle");
            if (shader == null)
            {
                //Shader.Find returns null in player builds unless the shader is in Resources
                //or in the Always Included Shaders list
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

        //init mesh
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

    //TEXTURE
    private Vector4 GetUvBlockForFrame(int texFrame)
    {
        int widthTiles = Mathf.Max(1, TextureWidthTiles);
        int heightTiles = Mathf.Max(1, TextureHeightTiles);

        //rows are counted in height tiles, not width tiles
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

    //RULE
    public void ResetEmitCounter()
    {
        emitOverTimeGlobalTimer = 0f;
        emitOverTimeSingleTimer = 0f;
    }

    //BOUNDS
    private void EncapsulateParticle(ParticleInstance instance)
    {
        Vector3 pos = instance.Position;
        Vector3 extent = Vector3.one * instance.Radius;

        if (!boundsValid)
        {
            //seed from the first particle. starting from Vector3.zero made every
            //bounding box stretch back to the world origin
            bounds = new Bounds(pos, extent * 2f);
            boundsValid = true;
            return;
        }

        bounds.Encapsulate(pos - extent);
        bounds.Encapsulate(pos + extent);
    }

    //EMISSION FUNCTIONS
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

        //init instances
        int emitCount = 0;
        for (int i = numEmittedParticles; i < numEmittedParticles + count && i < particleInstances.Length; i++)
        {
            particleInstances[i].InitFromSim(this);
            if (CalculateBounds)
                EncapsulateParticle(particleInstances[i]);
            emitCount++;
        }

        //add to emission count
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

        //emit if emit over time is set
        if (EmitOverTime && BirthRule != null)
        {
            emitOverTimeGlobalTimer += timeStep;
            if (BirthRule.SpewTimeLimit == 0f || emitOverTimeGlobalTimer < BirthRule.SpewTimeLimit)
            {
                emitOverTimeSingleTimer += timeStep * BirthRule.SpewRate;

                //keep the fractional remainder so the spew rate is exact, and allow
                //more than one particle per frame when the rate is above the framerate
                int emitCount = Mathf.FloorToInt(emitOverTimeSingleTimer);
                if (emitCount > 0)
                {
                    emitOverTimeSingleTimer -= emitCount;
                    Emit(emitCount);
                }
            }
        }

        //bounds are rebuilt from scratch each frame so they can shrink as well as grow
        if (CalculateBounds)
            boundsValid = false;

        //update emitted particles
        for (int i = 0; i < numEmittedParticles; i++)
        {
            var particle = particleInstances[i];
            particle.Update(timeStep, this);
            bool instanceIsAlive = particle.Life > 0f;

            //do collision
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

            //post-update
            if (!instanceIsAlive)
            {
                //swap-back removal. O(1) instead of shifting the whole pool, and the
                //dead instance is parked at the end for reuse instead of being dropped
                numEmittedParticles--;
                particleInstances[i] = particleInstances[numEmittedParticles];
                particleInstances[numEmittedParticles] = particle;
                i--; //we've filled a hole, go back to it
                continue;
            }

            if (CalculateBounds)
                EncapsulateParticle(particle);
        }

        if (CalculateBounds && !boundsValid)
            bounds = new Bounds(transform.position, Vector3.zero);

        //draw
        Draw();
    }

    public void Draw()
    {
        //if we have no active particles, or nothing to draw them with, don't draw
        if (numEmittedParticles == 0 || particleMaterial == null || particleMesh == null)
            return;

        if (propertyBlock == null)
            propertyBlock = new MaterialPropertyBlock();

        //setup arrays
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

        //set block
        propertyBlock.SetFloatArray("_Alphas", particleAlphas);
        propertyBlock.SetVectorArray("_UVs", particleUvs);
        propertyBlock.SetVectorArray("_Colors", particleColors);

        //draw!
        int layer = this.gameObject.layer;
        Graphics.DrawMeshInstanced(this.particleMesh, 0, this.particleMaterial, particleMatrices, numEmittedParticles, propertyBlock,
                                   UnityEngine.Rendering.ShadowCastingMode.Off, false, layer);
    }

    //MISC
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