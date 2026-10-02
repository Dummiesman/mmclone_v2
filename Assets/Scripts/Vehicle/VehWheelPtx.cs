using UnityEngine;

public class VehWheelPtx : VehSubsystem
{
    private const string TEX_SHEET_NAME = "ptx_wheel";
    private static string[] PtxName = new[] { "dirt", "dust", "grass", "leaf", "smoke", "snow", "splash", "rock" };
    private static ParticleBirthRule[] birthRules;
    private ParticleSim particleSim;
    private float[] ptxTimers = new float[2] { 0.0f, 0.0f };
    private static int instanceCount = 0;
    private bool counted = false;

    public static void SetRainyWeatherMode()
    {
        // swap smoke with splash
        birthRules[4] = birthRules[6];
    }

    private static void LoadBirthRules()
    {
        birthRules = new ParticleBirthRule[PtxName.Length];
        for(int i=0; i < PtxName.Length; i++)
        {
            birthRules[i] = new ParticleBirthRule();
            var node = AssetManager.OpenNode("tune", "effects", $"{PtxName[i]}.asBirthRule");
            if(node != null)
            {
                birthRules[i].ReadSettings(node);
            }
        }
    }

    private void Awake()
    {
        var texture = TextureCache.Get(TEX_SHEET_NAME);
        if (texture != null)
        {
            particleSim = this.gameObject.AddComponent<ParticleSim>();
            particleSim.Init();
            particleSim.SetTextureSheet(texture);
            particleSim.TextureHeightTiles = 8;
            particleSim.TextureWidthTiles = 8;
            particleSim.IsLocal = false;

            if (instanceCount++ == 0)
            {
                LoadBirthRules();
            }
            counted = true;
        }
        else
        {
            Debug.LogWarning($"vehWheelPtx::Init() - Texture {TEX_SHEET_NAME} not found, deactivating...");
            this.enabled = false;
        }
    }

    private void OnDestroy()
    {
        if (counted)
        {
            counted = false;
            if (--instanceCount == 0)
            {
                birthRules = null;
            }
        }

        Destroy(particleSim);
    }

    public override void Init(VehCar car)
    {
        base.Init(car);
    }

    private void Blast(VehWheel wheel, float threshold, int birthRuleIndex, int timerIndex)
    {
        if (wheel.LastSlippage <= threshold) return;

        var rule = birthRules[birthRuleIndex];

        // Save originals so the rule can be restored afterwards
        Vector3 savedVelocity = rule.Velocity.Value;
        float savedRadius = rule.Radius.Value;

        // Load factor 0..1 based on how the suspension is loaded
        float loadScale;
        if (wheel.CurrentSuspensionForce < wheel.NormalLoad)
        {
            loadScale = (wheel.CurrentSuspensionForce / wheel.NormalLoad + 1.0f) * 0.25f;
        }
        else
        {
            loadScale = ((wheel.CurrentSuspensionForce - wheel.NormalLoad)
                         / (wheel.SuspensionMaxForce * wheel.SuspensionLimit) + 1.0f) * 0.5f;
            if (loadScale > 1.0f) loadScale = 1.0f;
        }

        float radius = loadScale * wheel.Width * savedRadius * 0.5f;

        // Contact point pushed fore/aft depending on spin direction
        float spinSign = Mathf.Sign(-wheel.RotationRate);
        if (wheel.RotationRate == 0.0f) spinSign = 0.0f;
        float offset = spinSign * wheel.Radius;

        Vector3 position = wheel.LastHitPosition + wheel.rearwardsDirection * offset;
        position.y += radius;

        // float surfaceSpeed = Mathf.Abs(wheel.RotationRate) * wheel.Radius;
        float surfaceSpeed = 1.0f;

        float sideways = wheel.wheelSpaceVelocity.x * savedVelocity.x;
        float upwards = surfaceSpeed * savedVelocity.y;
        float rearwards = -wheel.motorAdjustedForwardVelocity * savedVelocity.z;

        Vector3 velocity = wheel.rearwardsDirection * rearwards
                         + wheel.LastSurfaceNormal * upwards
                         + wheel.sidewaysDirection * sideways;

        rule.Position.Value = position;
        rule.Radius.Value = radius;
        rule.Velocity.Value = velocity;

        float amount = ptxTimers[timerIndex] + loadScale * (rule.InitialBlast * Time.deltaTime);
        int count = (int)amount;
        ptxTimers[timerIndex] = amount - count;

        if (count >= 1)
        {
            particleSim.BirthRule = rule;
            particleSim.Emit(count);
        }

        rule.Velocity.Value = savedVelocity;
        rule.Radius.Value = savedRadius;
    }

    private void UpdateWheel(VehWheel wheel)
    {
        var physMaterial = wheel.CurrentMaterial;
        if(physMaterial != null)
        {
            int ptxIndex0 = physMaterial.PtxIndex[0];
            if(ptxIndex0 >= 0 && ptxIndex0 < birthRules.Length)
            {
                Blast(wheel, physMaterial.PtxThreshold[0], ptxIndex0, 0);
            }

            int ptxIndex1 = physMaterial.PtxIndex[1];
            if(ptxIndex1 >= 0 && ptxIndex1 < birthRules.Length)
            {
                Blast(wheel, physMaterial.PtxThreshold[1], ptxIndex1, 1);
            }
        }
    }

    public override void Update()
    {
        for(int i=0; i < 4; i++)
        {
            UpdateWheel(Car.VehCarSim.Wheels[i]);
        }
    }
}
