using MM2.AI;
using System;
using System.Collections.Generic;
using UnityEngine;

public class AIVehicleModel : LevelInstance
{
    private const float BlinkPeriod = 0.8f; // seconds per on/off cycle
    public VehBreakableManager Breakables { get; private set; }

    private AITrafficCar car;
    private string basename;

    private PackageObjectInstance instance;

    private GameObject body, shadow, hlight, tlight, headlight0, headlight1;
    private GameObject tlightNight;

    private Renderer[] tlightRenderers;
    private Renderer[] tlightNightRenderers;

    private readonly GameObject[] slights = new GameObject[2];
    private readonly GameObject[] wheels = new GameObject[6];
    private readonly Quaternion[] wheelBaseRotations = new Quaternion[6];
    private readonly GameObject[] breakParts = new GameObject[4];

    private int variant;
    private bool tailLightsOn;
    private bool headlightsOn;
    private bool nightLightsOn;
    private readonly bool[] signalsOn = new bool[2];

    private Vector3 headlight0Position;
    private bool hasHeadlight0;

    private LightGlow[] lightGlows;

    private const string AdditiveShaderName = "Vehicle/Additive";
    private const string UnlitTextureTransparentColoredShaderName = "Unlit/Texture Transparent Colored";

    private static Shader additiveShader;
    private static Shader unlitTextureTransparentColoredShader;

    private static MaterialProperties noReflection;
    private static MaterialProperties whiteAdditive;
    private static MaterialProperties bodyReflection;

    private static readonly ShaderSelector VehicleShaderFor =
        (entry, mainTex) => VehicleShaderVariants.Select(mainTex);

    private static readonly string[] BreakableParts = { "break0", "break1", "break2", "break3" };

    // vehBreakableMgr settings from the constructor
    private const float BreakImpulseThreshold = 2500f;
    private const float BreakOneShotEjectVelocity = 11f;
    private const float BreakUnknown18 = 4f;

    // Shadow probe
    private const float shadowLift = 0.02f;

    public int Variant
    {
        get => variant;
        set => SetVariant(value);
    }

    public AITrafficCar Car => car;
    public bool TailLightsOn => tailLightsOn;
    public bool NightLightsOn => nightLightsOn;

    public bool HeadlightsOn
    {
        get => headlightsOn;
        set
        {
            headlightsOn = value;
            SetActiveSafe(hlight, value);
            SetActiveSafe(headlight0, value);
            SetActiveSafe(headlight1, value);
        }
    }

    public void SetSignal(int index, bool on)
    {
        if ((uint)index >= (uint)slights.Length) return;
        signalsOn[index] = on;
        SetActiveSafe(slights[index], on);
    }

    private readonly List<Renderer> renderers = new List<Renderer>();

    public void Init(AITrafficCar car)
    {
        this.car = car;
        SyncPose();
    }

    /// <summary>
    /// Port of the BeginGeom/AddGeom chain and the rest of the constructor.
    /// Call Init first so the breakable manager has a pose to start from.
    /// </summary>
    public void Load(string basename)
    {
        this.basename = basename;

        var template = GetOrBuildTemplate(basename, BuildTemplate);
        if (template == null)
        {
            Debug.LogError($"AIVehicleModel: template '{basename}' failed to build.");
            return;
        }

        // aiVehicleInstance::SetColor
        variant = PickVariant(template);

        instance = template.Instantiate(transform, variant);

        AssignNamedParts();

        // Before the renderer sweep below, so the night copy is collected with
        // everything else.
        CreateNightTaillight();

        renderers.AddRange(gameObject.GetComponentsInChildren<Renderer>(true));
        SetVariant(variant);
        // InitBreakables();
        ReadHeadlightPivot();

        lightGlows = new LightGlow[2]
        {
            new LightGlow() {SpotExponent = 3.0f},
            new LightGlow() {SpotExponent = 3.0f}
        };
    }

    public override IEnumerable<Renderer> GetRenderers() => renderers;

    private static void BuildTemplate(PackageObjectLoader loader)
    {
        loader.SetDefaultShaderSelector(VehicleShaderFor);
        loader.SetFlipXZ(true);

        loader.LoadGroup("body", applyPivot: false);

        loader.SetProperties(NoReflection);
        loader.SetShader(GetUnlitTransparentTexturedShader());
        loader.LoadGroup("shadow", applyPivot: false);

        loader.SetShader(GetAdditiveShader());
        loader.SetProperties(WhiteAdditive);
        loader.LoadGroups(new[] { "hlight", "tlight", "slight0", "slight1" }, applyPivot: false);

        loader.SetShader(null);
        loader.SetProperties(NoReflection);

        loader.LoadGroups(new[] { "whl0", "whl1", "whl2", "whl3" }, applyPivot: true);

        loader.LoadGroups(BreakableParts, applyPivot: true);

        loader.SetShader(GetAdditiveShader());
        loader.SetProperties(WhiteAdditive);
        loader.LoadGroups(new[] { "headlight0", "headlight1" }, applyPivot: false);

        loader.SetShader(null);
        loader.SetProperties(NoReflection);
        loader.LoadGroups(new[] { "whl4", "whl5" }, applyPivot: true);

        ProcessShaders(loader.LoadShaders());
    }

    private void AssignNamedParts()
    {
        foreach (var part in instance.Parts)
        {
            switch (part.Name)
            {
                case "body": body = part.Object; break;
                case "shadow": shadow = part.Object; break;
                case "hlight": hlight = part.Object; break;
                case "tlight": tlight = part.Object; break;
                case "slight0": slights[0] = part.Object; break;
                case "slight1": slights[1] = part.Object; break;
                case "headlight0": headlight0 = part.Object; break;
                case "headlight1": headlight1 = part.Object; break;
                case "whl0": AssignWheel(0, part.Object); break;
                case "whl1": AssignWheel(1, part.Object); break;
                case "whl2": AssignWheel(2, part.Object); break;
                case "whl3": AssignWheel(3, part.Object); break;
                case "whl4": AssignWheel(4, part.Object); break;
                case "whl5": AssignWheel(5, part.Object); break;
                default:
                    int breakIndex = Array.IndexOf(BreakableParts, part.Name);
                    if (breakIndex >= 0)
                        breakParts[breakIndex] = part.Object;
                    else
                        part.Object.SetActive(false);
                    break;
            }
        }

        SetActiveSafe(tlight, tailLightsOn);
        HeadlightsOn = headlightsOn;
        SetSignal(0, signalsOn[0]);
        SetSignal(1, signalsOn[1]);
    }

    /// <summary>
    /// Second copy of TLIGHT, drawn on top of the original. This one is on for
    /// the whole night regardless of the car's brake state; the original keeps
    /// following AITrafficCar.TailLightsActive, so braking at night stacks two
    /// additive passes and the taillight reads brighter than its running state.
    ///
    /// The clone is not part of the PackageObjectInstance, so nothing in
    /// SetVariant reaches it - its materials are re-synced from the original
    /// by hand whenever the variant changes.
    /// </summary>
    private void CreateNightTaillight()
    {
        if (tlight == null) return;

        tlightNight = Instantiate(tlight, tlight.transform.parent, false);
        tlightNight.name = tlight.name + "_night";

        // Instantiate copies the source's local transform, but tlight is
        // loaded with applyPivot: false and sits at identity - set it
        // explicitly so the two stay welded together regardless.
        var src = tlight.transform;
        var dst = tlightNight.transform;
        dst.localPosition = src.localPosition;
        dst.localRotation = src.localRotation;
        dst.localScale = src.localScale;

        tlightRenderers = tlight.GetComponentsInChildren<Renderer>(true);
        tlightNightRenderers = tlightNight.GetComponentsInChildren<Renderer>(true);

        // The clone's own active state is the night flag, independent of
        // whatever the source was doing when it was cloned.
        tlightNight.SetActive(false);
    }

    /// <summary>
    /// The clone shares the template's meshes but holds its own Renderer
    /// components, so the material swap SetVariant performs on the original
    /// doesn't reach it. The hierarchies are identical copies, so the renderer
    /// arrays line up index for index.
    /// </summary>
    private void SyncNightTaillightMaterials()
    {
        if (tlightRenderers == null || tlightNightRenderers == null) return;

        int count = Mathf.Min(tlightRenderers.Length, tlightNightRenderers.Length);
        for (int i = 0; i < count; i++)
        {
            if (tlightRenderers[i] == null || tlightNightRenderers[i] == null) continue;
            tlightNightRenderers[i].sharedMaterials = tlightRenderers[i].sharedMaterials;
        }
    }

    private void AssignWheel(int index, GameObject obj)
    {
        wheels[index] = obj;
        wheelBaseRotations[index] = obj.transform.localRotation;
    }

    private void InitBreakables()
    {
        Breakables = gameObject.AddComponent<VehBreakableManager>();
        Breakables.ImpulseThreshold = BreakImpulseThreshold;

        for (int i = 0; i < breakParts.Length; i++)
        {
            if (breakParts[i] == null)
                continue;

            breakParts[i].SetActive(true);
            Breakables.Add(breakParts[i], basename, BreakableParts[i]);
        }

        Breakables.SetVariant(variant);
    }

    private void ReadHeadlightPivot()
    {
        hasHeadlight0 = false;
        if (headlight0 == null)
            return;

        string fileName = $"{basename}_headlight0.mtx";
        if (!AssetManager.Exists("geometry", fileName))
            return;

        var matrixFile = new MatrixFile();
        using (var stream = AssetManager.Open("geometry", fileName))
        {
            matrixFile.Load(stream);
            matrixFile = matrixFile.FlipXZ();
        }
        headlight0Position = matrixFile.Origin;
        hasHeadlight0 = true;
    }

    private int PickVariant(PackageObjectTemplate template)
    {
        int count = Mathf.Max(1, template.Shaders.VariantCount);
        return UnityEngine.Random.Range(0, count);
    }

    public override void SetVariant(int index)
    {
        variant = index;
        instance?.SetVariant(index);
        Breakables?.SetVariant(index);

        // After instance.SetVariant, so the original is already carrying this
        // variant's materials when they're copied across.
        SyncNightTaillightMaterials();
    }

    private void LateUpdate()
    {
        if (car == null) return;

        SyncPose();
        UpdateWheels();
        UpdateLights();
        UpdateShadow();

        // Draw lights
        bool lightsOn = false;
        if (Level != null)
        {
            lightsOn = Level.Lighting.preset.Headlights;
        }
        if (hasHeadlight0 && lightsOn)
        {
            Vector3 dir = transform.TransformDirection(Vector3.forward);
            lightGlows[0].Direction = dir;
            lightGlows[1].Direction = dir;
            lightGlows[0].Position = transform.TransformPoint(headlight0Position);
            lightGlows[1].Position = transform.TransformPoint(headlight0Position.ConvertCoordinateSpace());

            if (ViewportManager.MainViewport != null && ViewportManager.MainViewport.ActiveCamera != null)
            {
                var eyePos = ViewportManager.MainViewport.ActiveCamera.transform.position;
                lightGlows[0].DrawGlow(eyePos);
                lightGlows[1].DrawGlow(eyePos);
            }
        }
    }

    private void SyncPose()
    {
        if (car == null) return;
        transform.SetPositionAndRotation(car.Position, car.Rotation);
    }

    private void UpdateWheels()
    {
        // Steering on the front pair only, roll on all four.
        var roll = Quaternion.Euler(car.WheelXAngle, 0f, 0f);
        var steer = Quaternion.Euler(0f, car.WheelYAngle, 0f);

        for (int i = 0; i < wheels.Length; i++)
        {
            if (wheels[i] == null) continue;

            var pose = i < 2 ? steer * roll : roll;
            wheels[i].transform.localRotation = wheelBaseRotations[i] * pose;
        }
    }

    private void UpdateLights()
    {
        bool on = car.TailLightsActive;
        if (on != tailLightsOn)
        {
            tailLightsOn = on;
            SetActiveSafe(tlight, on);
        }

        // Running taillight
        bool lightsOn = false;
        if (Level != null)
        {
            lightsOn = Level.Lighting.preset.Headlights;
        }
        if (lightsOn != nightLightsOn)
        {
            nightLightsOn = lightsOn;
            SetActiveSafe(tlightNight, lightsOn);
        }

        bool blink = Mathf.Repeat(Time.time, BlinkPeriod) < BlinkPeriod * 0.5f;
        SetSignal(0, blink && car.LeftIndicatorOn);
        SetSignal(1, blink && car.RightIndicatorOn);
    }

    private void UpdateShadow()
    {
        if (shadow == null) return;

        Vector3 origin = transform.position + Vector3.up;
        if (!ProbeGround(origin, out var point, out var normal))
        {
            if (shadow.activeSelf) shadow.SetActive(false);
            return;
        }

        Vector3 forward = transform.forward;
        if (Mathf.Abs(Vector3.Dot(forward, normal)) > 0.99f)
            forward = transform.up;

        forward = Vector3.ProjectOnPlane(forward, normal);
        if (forward.sqrMagnitude < 1e-6f) return;

        if (!shadow.activeSelf) shadow.SetActive(true);

        shadow.transform.SetPositionAndRotation(
            point + normal * shadowLift,
            Quaternion.LookRotation(forward.normalized, normal));
    }

    private void Update()
    {
        // The rail entity already tracks its room, so just follow it.
        if (Level != null && car != null && car.RoomID != RoomID)
        {
            int target = car.Active ? car.RoomID : 0;
            Level.MoveToRoom(this, target);
        }
    }

    private static void SetActiveSafe(GameObject obj, bool active)
    {
        if (obj != null && obj.activeSelf != active)
            obj.SetActive(active);
    }

    private static void ProcessShaders(ShaderSet shaders)
    {
        foreach (var shader in shaders.Shaders)
        {
            shader.Specular = Color.black;
            if (shader.Name.EndsWith("_dmg", StringComparison.Ordinal))
                shader.Name = shader.Name.Substring(0, shader.Name.Length - 4);
        }
    }

    private static Shader GetAdditiveShader()
    {
        if (additiveShader == null)
            additiveShader = Shader.Find(AdditiveShaderName);
        return additiveShader;
    }

    private static Shader GetUnlitTransparentTexturedShader()
    {
        if (unlitTextureTransparentColoredShader == null)
        {
            unlitTextureTransparentColoredShader = Shader.Find(UnlitTextureTransparentColoredShaderName);
        }
        return unlitTextureTransparentColoredShader;
    }

    private static MaterialProperties NoReflection =>
        noReflection ?? (noReflection = new MaterialProperties().SetFloat("_Reflection", 0f));

    private static MaterialProperties WhiteAdditive =>
        whiteAdditive ?? (whiteAdditive = new MaterialProperties().SetColor("_Color", Color.white));

    private void OnDestroy()
    {
        instance?.Destroy();
        instance = null;

        // The night taillight is ours, not the template's - it was cloned
        // here, so it has to go here.
        if (tlightNight != null) Destroy(tlightNight);
        tlightNight = null;
        tlightRenderers = null;
        tlightNightRenderers = null;

        body = shadow = hlight = tlight = headlight0 = headlight1 = null;
        for (int i = 0; i < slights.Length; i++) slights[i] = null;
        for (int i = 0; i < wheels.Length; i++) wheels[i] = null;
        for (int i = 0; i < breakParts.Length; i++) breakParts[i] = null;

        car = null;
        tailLightsOn = false;
        nightLightsOn = false;
    }

    public override void Reset()
    {
        base.Reset();

        Breakables?.Reset();
        for (int i = 0; i < breakParts.Length; i++)
        {
            if (breakParts[i] != null)
                breakParts[i].SetActive(true);
        }

        tailLightsOn = false;
        nightLightsOn = false;
        SetActiveSafe(tlight, false);
        SetActiveSafe(tlightNight, false);
        SetSignal(0, false);
        SetSignal(1, false);
    }
}