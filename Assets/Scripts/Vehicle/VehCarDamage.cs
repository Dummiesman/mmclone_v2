using UnityEngine;

public class VehCarDamage : VehSubsystem
{
    public bool Enabled = true;
    public bool EnableRepair = true;
    public bool EnableRegeneration = false;

    public float MedMaxDamagePercentage
    {
        get
        {
            if (currentDamage <= MedDamage) return 0.0f;
            return Mathf.Clamp01((currentDamage - MedDamage) / (MaxDamage - MedDamage));
        }
    }

    public float DamagePercentage
    {
        get
        {
            return currentDamage / MaxDamage;
        }
        set
        {
            SetDamage(Mathf.Clamp01(value) * MaxDamage);
        }
    }

    public float CurrentDamage => currentDamage;
    private float currentDamage = 0f;

    // settings
    public float MaxDamage = 10000f;
    public float MedDamage = 5000f;
    public float ImpactThreshold = 1500f;
    public float RegenerateRate = 1f; 
    public bool DoublePivot = false;
    public Vector3 SmokeOffset = Vector3.zero;
    public Vector3 SmokeOffset2 = Vector3.zero;

    // particle systems
    private bool smokeSystemsCreated = false;
    private ParticleSim smokeSystem;
    private ParticleSim smokeSystem2;

    private bool exhaustSystemsCreated = false;
    private ParticleSim exhaustSystem;
    private ParticleSim exhaustSystem2;
    private float exhaustAccumulator = 0.0f;

    private ParticleBirthRule smokeRule;

    private LineSparks sparks;
    private float sparkMultiplier = 16.0f;

    private ShardManager shards;

    // visual damage
    private VertexColorDamage vertexDamage;

    // particle constants
    private const string ptxTextureName = "fxpt8";
    private const int ptxWidthTiles = 2;
    private const int ptxHeightTiles = 2;

    private bool isDamagedOut = false;
    private float damageResetCounter = 0f;

    private ParticleSim CreateParticleSystem(string name, ParticleBirthRule rule, Vector3 localPosition, int maxParticles = 32)
    {
        var texture = TextureCache.Get(ptxTextureName);
        if (texture == null)
            return null;

        var sim = new GameObject(name).AddComponent<ParticleSim>();
        sim.Init(maxParticles);

        sim.TextureWidthTiles = ptxWidthTiles;
        sim.TextureHeightTiles = ptxHeightTiles;
        sim.SetTextureSheet(texture);
        sim.BirthRule = rule;

        sim.transform.parent = this.transform;
        sim.transform.localPosition = localPosition;
        sim.EmitOverTime = false;

        return sim;
    }

    private bool GetPivot(string basename, string part, out Vector3 pivot)
    {
        string fileName = $"{basename}_{part}.mtx";
        pivot = Vector3.zero;
        if (!AssetManager.Exists("geometry", fileName))
            return false;

        var matrixFile = new MatrixFile();
        using (var stream = AssetManager.Open("geometry", fileName))
        {
            matrixFile.Load(stream);
            matrixFile = matrixFile.FlipXZ();
        }
        pivot = matrixFile.Origin;
        return true;
    }

    private void DamagedOut()
    {
        Car.SetDrivable(false, VehUndrivableMode.HoldBrakes);
        isDamagedOut = true;
        damageResetCounter = 5f;
    }

    private void UnDamagedOut()
    {
        Car.Model?.Reset(); // resets breakables
        Car.SetDrivable(true);
        isDamagedOut = false;
    }

    public void ReadSettings(TokenFileParser parser)
    {
        MaxDamage = parser.Read("MaxDamage", MaxDamage);
        MedDamage = parser.Read("MedDamage", MedDamage);
        ImpactThreshold = parser.Read("ImpactThreshold", ImpactThreshold);
        RegenerateRate = parser.Read("RegenerateRate", RegenerateRate);

        smokeRule = new ParticleBirthRule(parser)
        {
            SpewTimeLimit = 0f,
            SpewRate = 60.0f
        };

        SmokeOffset = parser.Read("SmokeOffset", Vector3.zero).ConvertCoordinateSpace();
        SmokeOffset.z *= -1;

        SmokeOffset2 = parser.Read("SmokeOffset2", Vector3.zero).ConvertCoordinateSpace();
        SmokeOffset2.z *= -1;

        DoublePivot = parser.Read("DoublePivot", 0) != 0;
    }

    public override void Init(VehCar car)
    {
        base.Init(car);

        // load settings
        var node = AssetManager.OpenNode("tune", "vehicle", $"{car.Basename}.vehCarDamage");
        if (node != null)
        {
            ReadSettings(node);
        }

        // create spark system
        sparks = this.gameObject.AddComponent<LineSparks>();
        sparks.Init(64, "spark");

        // create shard system
        shards = this.gameObject.AddComponent<ShardManager>();
        shards.Init(16, car.Model.BodyMaterials);
        shards.maxShardsPerEmit = 4;    
        shards.impulseEmitRatio = 150f; 

        // create smoke system
        var ptxTexture = TextureCache.Get(ptxTextureName);
        if (ptxTexture != null)
        {
            // create smoke systems
            smokeSystem = CreateParticleSystem("EngineSmoke", smokeRule, SmokeOffset);
            smokeSystem2 = CreateParticleSystem("EngineSmoke2", smokeRule, SmokeOffset2);
            smokeSystemsCreated = (smokeSystem2 != null);

            // create exhaust systems — each pivot is independent
            if (GetPivot(car.Basename, "exhaust0", out var exhaustPivot0))
                exhaustSystem = CreateParticleSystem("Exhaust", smokeRule, exhaustPivot0, 16);

            if (GetPivot(car.Basename, "exhaust1", out var exhaustPivot1))
                exhaustSystem2 = CreateParticleSystem("Exhaust2", smokeRule, exhaustPivot1, 16);

            exhaustSystemsCreated = (exhaustSystem != null || exhaustSystem2 != null);
        }

        // init visual
        vertexDamage = this.gameObject.AddComponent<VertexColorDamage>();
        vertexDamage.Init(Car.Model);
    }

    public void SetDamage(float amount)
    {
        float lastDamage = currentDamage;
        amount = Mathf.Clamp(amount, 0f, MaxDamage);

        if (amount >= MaxDamage && !isDamagedOut)
        {
            DamagedOut();
        }
        
        currentDamage = amount;
    }

    public void AddDamage(float amount)
    {
        SetDamage(Mathf.Clamp(currentDamage + amount, 0f, MaxDamage));
    }

    public void RemoveDamage(float amount)
    {
        SetDamage(Mathf.Clamp(currentDamage - amount, 0f, MaxDamage));
    }

    public void Impact(Collision collision)
    {
        if (Enabled)
        {
            float impulse = collision.impulse.magnitude;
            if (impulse >= ImpactThreshold && Car.VehCarSim.SpeedInMph >= 10.0f)
            {
                vertexDamage.Collision(collision);

                if (Car.VehCarSim.SpeedInMph >= 15.0f)
                {
                    sparks.RadialBlast(Mathf.RoundToInt(sparkMultiplier * impulse * 0.016f), collision.contacts[0].point, collision.contacts[0].normal);
                }
                shards.EmitShards(collision.contacts[0].point, impulse, Car.VehCarSim.Speed, Car.transform.rotation);

                AddDamage(impulse);
            }
        }
    }

    private void UpdateRegeneration()
    {
        if (!EnableRegeneration)
            return;
        if (Car.VehCarSim.Speed >= 5.0f) //5 m/s or ~11mph
        {
            RemoveDamage(50000f * Time.deltaTime);
        }
    }

    private void UpdateExhaust()
    {
        if (!exhaustSystemsCreated)
            return;

        float smokeAmount = Mathf.Clamp01(
            (Car.VehCarSim.Engine.CurrentRPM - 2000.0f) /
            (Car.VehCarSim.Engine.MaxRPM - 2000.0f)
        );
        exhaustAccumulator += (smokeAmount * 60.0f * Time.deltaTime); // original game was unscaled, assume 60fps was baseline

        int numToEmit = Mathf.FloorToInt(exhaustAccumulator);
        if(numToEmit > 0)
        {
            if (exhaustSystem != null) exhaustSystem.Emit(numToEmit);
            if (exhaustSystem2 != null) exhaustSystem2.Emit(numToEmit);
            exhaustAccumulator -= numToEmit;
        }
    }

    private void UpdateSmoke()
    {
        if (!smokeSystemsCreated)
            return;

        bool secondSmokeSystemValid = (SmokeOffset2.sqrMagnitude > 0f);
        bool useSecondSmokeSystem = secondSmokeSystemValid && DoublePivot;

        bool isEmitting = currentDamage > MedDamage;
        if (isEmitting != smokeSystem.EmitOverTime)
        {
            smokeSystem.EmitOverTime = isEmitting;
            smokeSystem2.EmitOverTime = isEmitting && useSecondSmokeSystem;
        }

        if (isEmitting)
        {
            //handle double pviot
            if (!DoublePivot && secondSmokeSystemValid)
            {
                //translate first smoke system between offsets
                float lerpAmount = UnityEngine.Random.value;
                smokeSystem.transform.localPosition = Vector3.LerpUnclamped(SmokeOffset, SmokeOffset2, lerpAmount);
            }
            else
            {
                //move first smoke system to its original offset
                smokeSystem.transform.localPosition = SmokeOffset;
            }
        }
    }

    private void UpdateWheelWobble()
    {
        var sim = Car.VehCarSim;

        float spin = Mathf.Abs(sim.Wheels[0].RotationRate);
        float fade = Mathf.Clamp01(2f * spin * Time.deltaTime / Mathf.PI);

        float wobble = MedMaxDamagePercentage;
        wobble *= 1f - fade; 

        float a = wobble * -0.15f;
        float b = wobble * 0.35f;

        sim.Wheels[0].WobbleAmount = a;
        sim.Wheels[1].WobbleAmount = b;
        sim.Wheels[2].WobbleAmount = b;
        sim.Wheels[3].WobbleAmount = a;
    }

    public override void Update()
    {
        shards.Materials = Car.Model.BodyMaterials;

        // update particle frame and velocity
        if(smokeRule != null)
        {
            smokeRule.Velocity.Value = Car.Body.velocity;

            //get current particle frame for this damage percentage
            int[] texFrameMap = new int[] { 1, 0, 3, 2 };

            float damagePercentage = MedMaxDamagePercentage;
            int texFrameIndex = Mathf.Min(Mathf.RoundToInt(damagePercentage * 3f), 3);
            texFrameIndex = texFrameMap[texFrameIndex];

            smokeRule.TexFrameEnd = texFrameIndex;
            smokeRule.TexFrameStart = texFrameIndex;
        }

        UpdateRegeneration();
        UpdateSmoke();
        UpdateExhaust();
        UpdateWheelWobble();

        //damaged out?
        if (isDamagedOut && EnableRepair)
        {
            if (damageResetCounter <= 0f)
            {
                Reset();
            }
            damageResetCounter -= Time.deltaTime;
        }
    }

    public void Reset()
    {
        //if we were damaged out, reverse that
        if (isDamagedOut)
        {
            UnDamagedOut();
        }
        SetDamage(0f);
        vertexDamage.Reset();
    }
}