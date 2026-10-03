using System;
using System.Collections.Generic;
using UnityEngine;

public class VehicleModel : LevelInstance
{
    public VehBreakableManager Breakables { get; private set; }

    public GameObject BodyObject => body;
    public Material[] BodyMaterials => body.GetComponentInChildren<Renderer>().sharedMaterials;

    private VehCar vehicle;
    private string basename;

    private PackageObjectInstance instance;

    private GameObject body, shadow, tlight, blight, rlight;

    private readonly GameObject[] wheels = new GameObject[4];
    private readonly GameObject[] spinWheels = new GameObject[SpinWheelParts.Length];
    private readonly bool[] wheelBlurred = new bool[4];
    private readonly GameObject[] fenders = new GameObject[2];
    private readonly Vector3[] fenderPivots = new Vector3[2];
    private readonly GameObject[] variantObjects = new GameObject[VariantCount];
    private readonly GameObject[] breakParts = new GameObject[8];
    private readonly GameObject[] tsLights = new GameObject[TSLightParts.Length];

    private readonly List<VehSuspensionVisual> suspensions = new List<VehSuspensionVisual>();
    private readonly List<VehAxleVisual> axles = new List<VehAxleVisual>();

    private readonly List<NightLightClone> nightLights = new List<NightLightClone>();

    private LightGlow[] lightGlows;
    private GameObject headlight0;
    private GameObject headlight1;

    private readonly GameObject[] sirens = new GameObject[SirenParts.Length + AdditionalSirenParts.Length];
    private readonly Color[] sirenColors = new Color[SirenParts.Length + AdditionalSirenParts.Length];

    public IReadOnlyList<GameObject> Sirens => sirens;
    public IReadOnlyList<Color> SirenColors => sirenColors;

    private int variant;
    private bool brakeLightsOn;
    private bool reverseLightOn;
    private bool nightLightsOn;

    private const int VariantCount = 32;

    private const string AdditiveShaderName = "Vehicle/Additive";
    private const string UnlitTextureTransparentColoredShaderName = "Unlit/Texture Transparent Colored";

    private static Shader additiveShader;
    private static Shader unlitTextureTransparentColoredShader;

    private static MaterialProperties noReflection;
    private static MaterialProperties whiteAdditive;
    private static MaterialProperties bodyReflection;

    /// <summary>
    /// Body geometry picks its shader per ShaderSet entry: VehicleShaderVariants
    /// hands back the cheap one-pass shader for textures with no alpha channel
    /// and the two-pass one for the rest.
    /// they share one Material[] per variant.
    /// </summary>
    private static readonly ShaderSelector VehicleShaderFor =
        (entry, mainTex) => VehicleShaderVariants.Select(mainTex);

    /// <summary>Static body parts: authored pivots, default shader.</summary>
    private static readonly string[] BodyParts =
    {
        "bodydamage", "siren0", "siren1", "decal", "driver",
    };

    private static readonly string[] SuspensionParts =
    {
        "shock0", "shock1", "shock2", "shock3",
        "arm0", "arm1", "arm2", "arm3",
        "shaft2", "shaft3",
    };

    private static readonly string[] AxleParts =
{
        "axle0", "axle1",
    };


    private static readonly int[] SuspensionWheels =
    {
        0, 1, 2, 3,     // shock0-3
        0, 1, 2, 3,     // arm0-3
        2, 3,           // shaft2, shaft3
    };
    private static readonly int[] AxleWheelsLeft = { 0, 2 };
    private static readonly int[] AxleWheelsRight = { 1, 3 };

    /// <summary> Breakable parts </summary>
    private static readonly string[] BreakableParts =
     {
        "break0", "break1", "break2", "break3",
        "break01", "break12", "break23", "break03",
    };

    /// <summary>Brake discs, hubs and sirens that follow the driven wheels.</summary>
    private static readonly string[] BrakeParts =
    {
        "hub0", "hub1", "hub2", "hub3", "trailer_hitch"
    };

    private static readonly string[] SirenParts =
    {
        "srn0", "srn1", "srn2", "srn3",
    };

    private static readonly string[] AdditionalSirenParts =
{
        "srn4", "srn5", "srn6", "srn7"
    };

    /// <summary>
    /// Blurred wheel copies. 0-3 shadow the driven wheels and are swapped in
    /// once the wheel spins past WheelBlurRotationRate; 4/5 pair with whl4/whl5,
    /// which have no sim wheel behind them yet, so they stay hidden.
    /// </summary>
    private static readonly string[] SpinWheelParts =
    {
        "swhl0", "swhl1", "swhl2", "swhl3", "swhl4", "swhl5",
    };

    /// <summary>
    /// Combined turn-signal / brake lights. Signals aren't implemented yet, so
    /// for now these are driven exactly like TLIGHT: on with the brakes, plus a
    /// night copy that stays lit as a running light.
    /// </summary>
    private static readonly string[] TSLightParts =
    {
        "tslight0", "tslight1",
    };

    /// <summary>The original relied on draw order to keep the shadow off the road; Unity needs a real gap.</summary>
    private const float shadowLift = 0.02f;

    /// <summary>
    /// Wheel spin speed, in rad/s
    /// </summary>
    private const float WheelBlurRotationRate = 26.0f;

    public int Variant
    {
        get => variant;
        set => SetVariant(value);
    }

    public bool BrakeLightsOn => brakeLightsOn;
    public bool ReverseLightOn => reverseLightOn;
    public bool NightLightsOn => nightLightsOn;
    public VehCar Vehicle => vehicle;

    private List<Renderer> renderers = new List<Renderer>();

    /// <summary>
    /// A light that also burns as a running light after dark: the authored part
    /// keeps its brake-driven behaviour and this second copy, drawn on top of
    /// it, is simply on for the whole night. Braking at night stacks two
    /// additive passes, so the lamp reads brighter than its running state.
    ///
    /// The copy is not part of the PackageObjectInstance, so nothing in
    /// SetVariant reaches it - its materials are re-synced by hand from the
    /// source whenever the variant changes.
    /// </summary>
    private sealed class NightLightClone
    {
        public GameObject Clone;
        public Renderer[] SourceRenderers;
        public Renderer[] CloneRenderers;
    }

    private Color GetSurfaceColor(GameObject obj)
    {
        Color color = Color.white;
        var renderer = obj.GetComponentInChildren<Renderer>();
        if (renderer != null)
        {
            var mat = renderer.sharedMaterial;
            if (mat != null)
            {
                color = mat.color;
            }
        }

        return color;
    }

    private void InitBreakables()
    {
        Breakables = gameObject.AddComponent<VehBreakableManager>();
        Breakables.Init(Level, vehicle.Body);

        for (int i = 0; i < breakParts.Length; i++)
        {
            if (breakParts[i] == null)
                continue;

            // attached parts render with the car; the manager owns the copy it
            // ejects, and turns this one off when that happens
            breakParts[i].SetActive(true);

            Breakables.Add(breakParts[i], basename, BreakableParts[i]);
        }
    }

    public void Init(VehCar vehicle)
    {
        this.vehicle = vehicle;

        transform.SetParent(vehicle.transform, false);
        transform.localPosition = Vector3.zero;
        transform.localRotation = Quaternion.identity;
        transform.localScale = Vector3.one;
    }

    /// <summary>
    /// Port of the vehicle's BeginGeom/AddGeom chain. First vehicle of this
    /// basename builds and caches the template; every later instance just
    /// instantiates the cached result, sharing its meshes and materials.
    /// </summary>
    public void Load(string basename)
    {
        this.basename = basename;

        var template = GetOrBuildTemplate(basename, BuildTemplate);
        if (template == null)
        {
            Debug.LogError($"VehicleModel: template '{basename}' failed to build.");
            return;
        }

        instance = template.Instantiate(transform, variant);

        AssignNamedParts();

        // Before the renderer sweep below, so the night copies are collected
        // with everything else.
        CreateNightLights();

        renderers.AddRange(this.gameObject.GetComponentsInChildren<Renderer>(true));
        SetVariant(variant);
        InitBreakables();

        lightGlows = new LightGlow[2]
        {
            new LightGlow() {SpotExponent = 3.0f},
            new LightGlow() {SpotExponent = 3.0f}
        };
    }

    public override IEnumerable<Renderer> GetRenderers()
    {
        return renderers;
    }

    /// <summary>
    /// Declares the vehicle's parts in package order, along with which shader
    /// and extra material properties each run wants. Runs once per basename;
    /// everything it produces is shared by every vehicle of that model.
    /// </summary>
    private static void BuildTemplate(PackageObjectLoader loader)
    {
        // Everything except the additive light shells is vehicle geometry, so
        // the per-texture selector is the default and SetShader(null) returns
        // to it. The original's gfxForceLVERTEX toggle is gone: the one-pass /
        // two-pass choice is now made per material from the texture itself,
        // not per part by the caller.
        loader.SetDefaultShaderSelector(VehicleShaderFor);
        loader.SetFlipXZ(true);

        // The body is the one part that keeps its ShaderSet reflectivity.
        loader.LoadGroup("body", applyPivot: false);

        loader.SetProperties(NoReflection);
        loader.SetShader(GetUnlitTransparentTexturedShader());
        loader.LoadGroup("shadow", applyPivot: false);

        // Additive glow shells - _Color must be forced white or the shell
        // draws black, which an additive blend shows as fully invisible.
        loader.SetShader(GetAdditiveShader());
        loader.SetProperties(WhiteAdditive);
        loader.LoadGroups(
            new[] { "hlight", "tlight", "rlight", "slight0", "slight1", "blight" },
            applyPivot: false);

        loader.SetShader(null);
        loader.SetProperties(NoReflection);
        loader.LoadGroups(BodyParts);

        // No pivot: VehSuspension reads the same .mtx itself and folds it into
        // the sheared matrix it draws with, so applying it to the Transform too
        // would double it.
        loader.LoadGroups(SuspensionParts, applyPivot: false);

        // Axles DO take their pivot: VehAxle reads the beam's chassis-space
        // matrix off the Transform rather than from a .mtx of its own.
        loader.LoadGroups(AxleParts, applyPivot: true);
        loader.LoadGroup("engine");

        // Driven wheels: no pivot - VehWheel owns the wheel center and
        // produces the complete visual pose.
        loader.LoadGroups(new[] { "whl0", "whl1", "whl2", "whl3" }, applyPivot: false);

        loader.LoadGroups(BreakableParts);
        loader.LoadGroups(BrakeParts);
        loader.LoadGroups(SirenParts);
        loader.LoadGroups(new[] { "headlight0", "headlight1" }, applyPivot: true);

        // Fenders keep their raw pivot (read back in AssignNamedParts and
        // combined with the CarSim wheel center); whl4/whl5 are extra
        // non-driven wheels, so unlike whl0-3 they do take an authored pivot.
        loader.LoadGroups(new[] { "fndr0", "fndr1", "whl4", "whl5" });

        // Additions from mm2hook
        loader.LoadGroup("plighton");
        loader.LoadGroup("plightoff");

        // Spinning wheels: shown instead of whl versions when wheels are
        // spinning fast enough. No pivot, for the same reason whl0-3 take
        // none - they're posed straight from the sim wheel.
        loader.LoadGroups(SpinWheelParts, applyPivot: false);

        // Additional hubs and spinning versions
        loader.LoadGroups(new[] { "hub4", "hub5" }, applyPivot: false);
        loader.LoadGroups(new[] { "shub0", "shub1", "shub2", "shub3", "shub4", "shub5" }, applyPivot: false);

        // Additional lights
        loader.LoadGroups(new[] { "headlight2", "headlight3", "headlight4", "headlight5", "headlight6", "headlight7" }, applyPivot: true);

        // Additional sirens
        loader.LoadGroups(AdditionalSirenParts);

        // Lightbar breakables
        loader.LoadGroups(new[] { "lightbar0", "lightbar1" });

        // combined signal and brake lights
        loader.SetShader(GetAdditiveShader());
        loader.SetProperties(WhiteAdditive);
        loader.LoadGroups(TSLightParts, applyPivot: false);
        loader.SetShader(null);
        loader.SetProperties(NoReflection);

        for (int i = 0; i < VariantCount; i++)
            loader.LoadGroup($"variant{i}");

        ProcessShaders(loader.LoadShaders());
    }


    private static bool TryGetBreakIndex(string name, out int index)
    {
        index = Array.IndexOf(BreakableParts, name);
        return index >= 0;
    }

    /// <summary>
    /// srn0-3 and srn4-7 are one flat run as far as callers are concerned, so
    /// the extra set is folded onto the end of the base set's indices.
    /// </summary>
    private static bool TryGetSirenIndex(string name, out int index)
    {
        index = Array.IndexOf(SirenParts, name);
        if (index >= 0)
            return true;

        int extra = Array.IndexOf(AdditionalSirenParts, name);
        if (extra >= 0)
        {
            index = SirenParts.Length + extra;
            return true;
        }

        index = -1;
        return false;
    }

    private static bool TryGetSuspensionIndex(string name, out int index)
    {
        index = Array.IndexOf(SuspensionParts, name);
        return index >= 0;
    }

    private static bool TryGetAxleIndex(string name, out int index)
    {
        index = Array.IndexOf(AxleParts, name);
        return index >= 0;
    }

    private static bool TryGetSpinWheelIndex(string name, out int index)
    {
        index = Array.IndexOf(SpinWheelParts, name);
        return index >= 0;
    }

    private static bool TryGetTSLightIndex(string name, out int index)
    {
        index = Array.IndexOf(TSLightParts, name);
        return index >= 0;
    }

    /// <summary>
    /// LoadGroup silently omits parts the vehicle doesn't have, so position in
    /// the list isn't safe to index by - look each part up by name instead.
    /// </summary>
    private void AssignNamedParts()
    {
        foreach (var part in instance.Parts)
        {
            switch (part.Name)
            {
                case "body": body = part.Object; break;
                case "shadow": shadow = part.Object; break;
                case "tlight": tlight = part.Object; break;
                case "blight": blight = part.Object; break;
                case "rlight": rlight = part.Object; break;
                case "whl0": wheels[0] = part.Object; break;
                case "whl1": wheels[1] = part.Object; break;
                case "whl2": wheels[2] = part.Object; break;
                case "whl3": wheels[3] = part.Object; break;

                case "fndr0":
                    fenders[0] = part.Object;
                    fenderPivots[0] = part.Object.transform.localPosition - vehicle.VehCarSim.Wheels[0].Center;
                    break;
                case "fndr1":
                    fenders[1] = part.Object;
                    fenderPivots[1] = part.Object.transform.localPosition - vehicle.VehCarSim.Wheels[1].Center;
                    break;

                case "headlight0":
                    headlight0 = part.Object;
                    headlight0.SetActive(false);
                    break;
                case "headlight1":
                    headlight1 = part.Object;
                    headlight1.SetActive(false);
                    break;
                default:
                    if (TryGetBreakIndex(part.Name, out int breakNumber))
                    {
                        breakParts[breakNumber] = part.Object;
                    }
                    else if (TryGetSirenIndex(part.Name, out int sirenNumber))
                    {
                        sirens[sirenNumber] = part.Object;
                        sirenColors[sirenNumber] = GetSurfaceColor(part.Object);
                        part.Object.SetActive(false);
                    }
                    else if (TryGetSpinWheelIndex(part.Name, out int spinNumber))
                    {
                        // The solid wheel is what loads visible; the blurred
                        // copy only comes up once UpdateWheelBlur swaps them.
                        spinWheels[spinNumber] = part.Object;
                        part.Object.SetActive(false);
                    }
                    else if (TryGetTSLightIndex(part.Name, out int tsNumber))
                    {
                        tsLights[tsNumber] = part.Object;
                    }
                    else if (TryGetVariantIndex(part.Name, out int variantNumber))
                    {
                        variantObjects[variantNumber] = part.Object;
                    }
                    else if (TryGetSuspensionIndex(part.Name, out int suspensionNumber))
                    {
                        InitSuspension(part.Object, part.Name, suspensionNumber);
                    }
                    else if (TryGetAxleIndex(part.Name, out int axleNumber))
                    {
                        InitAxle(part.Object, part.Name, axleNumber);
                    }
                    else
                    {
                        // Parts this VehicleModel has no runtime driver for
                        // (hlight, slight0/1, bodydamage, siren0/1, decal,
                        // driver, engine, break*, hub0-5, shub0-5,
                        // trailer_hitch, whl4/5) - hidden by default rather
                        // than left inert and visible. A future feature
                        // (driver model, damage states, extra wheels) can
                        // SetActive(true) it once it's wired up.
                        part.Object.SetActive(false);
                    }
                    break;
            }
        }

        if (tlight != null) tlight.SetActive(brakeLightsOn);
        if (blight != null) blight.SetActive(brakeLightsOn);
        if (rlight != null) rlight.SetActive(reverseLightOn);

        // Signals aren't wired up yet, so these follow the brakes exactly.
        for (int i = 0; i < tsLights.Length; i++)
        {
            if (tsLights[i] != null) tsLights[i].SetActive(brakeLightsOn);
        }

        // Blurred copies start stowed; nothing has spun up yet.
        for (int i = 0; i < wheelBlurred.Length; i++)
            wheelBlurred[i] = false;
    }

    /// <summary>
    /// Attaches a VehSuspension to a strut/arm/shaft/axle. It reads its own
    /// pivot from geometry/{basename}_{name}.mtx, shears it against the wheel's
    /// travel and draws LOD 0 by hand. The part stays active - its GameObject
    /// has to tick for that draw to happen - and anything that fails to
    /// initialise is hidden the same way an undriven part would be.
    /// </summary>
    private void InitSuspension(GameObject obj, string name, int index)
    {
        var sim = vehicle.VehCarSim;
        int wheelIndex = SuspensionWheels[index];

        if (sim == null || sim.Wheels == null || wheelIndex >= sim.Wheels.Length)
        {
            obj.SetActive(false);
            return;
        }

        var group = obj.GetComponent<LODGroup>();
        if (group == null)
        {
            Debug.LogWarning($"VehicleModel '{basename}': suspension part '{name}' has no LODGroup.", this);
            obj.SetActive(false);
            return;
        }

        var suspension = obj.AddComponent<VehSuspensionVisual>();

        // Pivot and wheel centre are both car-body space, so the car root is
        // the parent frame - not this VehicleModel, even though it sits at
        // identity under the car.
        if (!suspension.Init(vehicle.transform, basename, name, sim.Wheels[wheelIndex], group))
        {
            Destroy(suspension);
            obj.SetActive(false);
            return;
        }

        obj.SetActive(true);
        suspensions.Add(suspension);
    }

    /// <summary>
    /// Hands a solid axle's geometry to the sim. Unlike the suspension parts
    /// there's no component to add: VehAxle already exists on VehCarSim, owns
    /// the wheel pairing and the anti-roll bar, and just needs the LODGroup so
    /// it can draw the beam with its shear applied.
    /// </summary>
    /// <summary>
    /// Attaches a VehAxleVisual to a solid axle. Same deal as InitSuspension -
    /// own pivot from the .mtx, LOD 0 drawn by hand - except a beam spans a
    /// pair, so it takes the left and right wheel rather than a single one.
    /// </summary>
    private void InitAxle(GameObject obj, string name, int index)
    {
        var sim = vehicle.VehCarSim;
        int left = AxleWheelsLeft[index];
        int right = AxleWheelsRight[index];

        if (sim == null || sim.Wheels == null || left >= sim.Wheels.Length || right >= sim.Wheels.Length)
        {
            obj.SetActive(false);
            return;
        }

        var group = obj.GetComponent<LODGroup>();
        if (group == null)
        {
            Debug.LogWarning($"VehicleModel '{basename}': axle part '{name}' has no LODGroup.", this);
            obj.SetActive(false);
            return;
        }

        var axle = obj.AddComponent<VehAxleVisual>();

        if (!axle.Init(vehicle.transform, basename, name, sim.Wheels[left], sim.Wheels[right], group))
        {
            Destroy(axle);
            obj.SetActive(false);
            return;
        }

        obj.SetActive(true);
        axles.Add(axle);
    }

    /// <summary>
    /// Builds the night running-light copies. TLIGHT has always had one;
    /// tslight0/1 get the same treatment while they're standing in as plain
    /// taillights.
    /// </summary>
    private void CreateNightLights()
    {
        CreateNightClone(tlight);

        for (int i = 0; i < tsLights.Length; i++)
            CreateNightClone(tsLights[i]);
    }

    /// <summary>
    /// Second copy of a lamp, drawn on top of the original. This one is on for
    /// the whole night regardless of brake input; the original keeps its
    /// brake-driven behaviour, so braking at night stacks two additive passes
    /// and the lamp reads brighter than its running state.
    /// </summary>
    private void CreateNightClone(GameObject source)
    {
        if (source == null) return;

        var clone = Instantiate(source, source.transform.parent, false);
        clone.name = source.name + "_night";

        // Instantiate copies the source's local transform, but these lamps are
        // loaded with applyPivot: false and sit at identity - set it
        // explicitly so the two stay welded together regardless.
        var src = source.transform;
        var dst = clone.transform;
        dst.localPosition = src.localPosition;
        dst.localRotation = src.localRotation;
        dst.localScale = src.localScale;

        // The clone's own active state is the night flag; the source may have
        // been cloned while it was off if the car isn't braking, but don't
        // rely on that.
        clone.SetActive(false);

        nightLights.Add(new NightLightClone
        {
            Clone = clone,
            SourceRenderers = source.GetComponentsInChildren<Renderer>(true),
            CloneRenderers = clone.GetComponentsInChildren<Renderer>(true),
        });
    }

    /// <summary>
    /// The clones share the template's meshes but hold their own Renderer
    /// components, so the material swap SetVariant performs on the originals
    /// doesn't reach them. The hierarchies are identical copies, so the
    /// renderer arrays line up index for index.
    /// </summary>
    private void SyncNightLightMaterials()
    {
        for (int n = 0; n < nightLights.Count; n++)
        {
            var entry = nightLights[n];
            if (entry.SourceRenderers == null || entry.CloneRenderers == null) continue;

            int count = Mathf.Min(entry.SourceRenderers.Length, entry.CloneRenderers.Length);
            for (int i = 0; i < count; i++)
            {
                if (entry.SourceRenderers[i] == null || entry.CloneRenderers[i] == null) continue;
                entry.CloneRenderers[i].sharedMaterials = entry.SourceRenderers[i].sharedMaterials;
            }
        }
    }

    private static bool TryGetVariantIndex(string name, out int index)
    {
        index = -1;

        if (name == null || !name.StartsWith("variant", StringComparison.OrdinalIgnoreCase))
            return false;

        return int.TryParse(name.Substring("variant".Length), out index)
            && index >= 0
            && index < VariantCount;
    }

    public override void SetVariant(int index)
    {
        index %= instance.Template.Shaders.VariantCount;
        variant = index;
        instance?.SetVariant(index);

        // setup damage textures (todo: handle this in a better way, this is the bare minimum 'hack' way)
        foreach (var renderer in body.GetComponentsInChildren<Renderer>(true))
        {
            foreach (var material in renderer.sharedMaterials)
            {
                if (material.mainTexture != null && material.HasProperty("_DamageTex") && material.GetTexture("_DamageTex") == null)
                {
                    Texture2D mainTex = (Texture2D)material.mainTexture;
                    string dmgTexName = $"{mainTex.name}_dmg";

                    Texture2D loadedDmgTex = TextureCache.Get(dmgTexName);
                    if (loadedDmgTex == null) loadedDmgTex = mainTex;

                    material.SetTexture("_DamageTex", loadedDmgTex);
                }
            }
        }


        for (int i = 0; i < variantObjects.Length; i++)
        {
            if (variantObjects[i] != null)
                variantObjects[i].SetActive(i == index);
        }
        for (int i = 0; i < sirens.Length; i++)
        {
            if (sirens[i] != null) sirenColors[i] = GetSurfaceColor(sirens[i]);
        }

        Breakables?.SetVariant(index);

        // Suspension parts need no pass of their own: VehSuspension reads
        // sharedMaterials off its renderer at draw time, so whatever
        // instance.SetVariant just assigned is what gets submitted. Same goes
        // for the blurred wheels - they're ordinary instance parts.

        // After instance.SetVariant, so the originals are already carrying this
        // variant's materials when they're copied across.
        SyncNightLightMaterials();
    }

    private void LateUpdate()
    {
        if (vehicle == null) return;
        UpdateModel();

        // Draw lights
        bool lightsOn = false;
        if(Level != null)
        {
            lightsOn = Level.Lighting.preset.Headlights;
        }
        if (lightsOn)
        {
            Vector3 dir = transform.TransformDirection(Vector3.forward);
            lightGlows[0].Direction = dir;
            lightGlows[1].Direction = dir;
            lightGlows[0].Position = headlight0.transform.position;
            lightGlows[1].Position = headlight1.transform.position;

            if (ViewportManager.MainViewport != null && ViewportManager.MainViewport.ActiveCamera != null)
            {
                var eyePos = ViewportManager.MainViewport.ActiveCamera.transform.position;
                lightGlows[0].DrawGlow(eyePos);
                lightGlows[1].DrawGlow(eyePos);
            }
        }
    }

    private void UpdateBody()
    {
        const bool enableBodyShake = false;

        var sim = vehicle.VehCarSim;
        Quaternion bodyShake = Quaternion.identity;

        if (!enableBodyShake || sim == null || sim.Wheels == null || sim.OnGround() == 0)
        {
            bodyShake = Quaternion.identity;
            transform.localRotation = bodyShake;
            return;
        }

        const float ShakeRandomScale = 0.06f;
        const float ShakeSpinScale = 0.03f;   // 0.01 on the other branch

        var wheel = sim.Wheels[0];
        float spin = Mathf.Abs(wheel.RotationRate);

        // 0 below the aliasing threshold, 1 one full pi above it.
        float alias = Mathf.Clamp01((spin * Time.deltaTime - Mathf.PI * 0.5f) / Mathf.PI);

        float amp = vehicle.Damage.MedMaxDamagePercentage;

        float jitter = (UnityEngine.Random.value - 0.5f) * amp * alias * ShakeRandomScale;
        float coherent = Mathf.Sin(wheel.AccumulatedRotation) * amp * (1f - alias) * ShakeSpinScale;

        // Diagonal axis in the parent's (car root) frame.
        Vector3 axis = (Vector3.right + Vector3.forward).normalized;

        transform.localRotation =
            Quaternion.AngleAxis((jitter + coherent) * Mathf.Rad2Deg, axis);
    }

    private void UpdateModel()
    {
        UpdateBody();
        UpdateWheels();
        UpdateFenders();
        UpdateLights();
        UpdateShadow();
    }

    private void UpdateLights()
    {
        var sim = vehicle.VehCarSim;
        bool braking = sim != null && sim.BrakeInput > 0f;

        if (braking != brakeLightsOn)
        {
            brakeLightsOn = braking;
            if (tlight != null) tlight.SetActive(braking);
            if (blight != null) blight.SetActive(braking);

            // Turn signals aren't implemented yet, so the combined lamps are
            // brake lights and nothing else for now.
            for (int i = 0; i < tsLights.Length; i++)
            {
                if (tsLights[i] != null) tsLights[i].SetActive(braking);
            }
        }

        // Gear 0 is reverse - forward ratios start above it.
        bool reversing = sim != null && sim.Transmission.CurrentGear == 0;

        if (reversing != reverseLightOn)
        {
            reverseLightOn = reversing;
            if (rlight != null) rlight.SetActive(reversing);
        }

        // Running taillights
        bool lightsOn = false;
        if (Level != null)
        {
            lightsOn = Level.Lighting.preset.Headlights;
        }
        if (lightsOn != nightLightsOn)
        {
            nightLightsOn = lightsOn;
            for (int i = 0; i < nightLights.Count; i++)
            {
                if (nightLights[i].Clone != null) nightLights[i].Clone.SetActive(lightsOn);
            }
        }
    }

    private void UpdateWheels()
    {
        var sim = vehicle.VehCarSim;
        if (sim == null || sim.Wheels == null) return;

        int count = Mathf.Min(4, sim.Wheels.Length);

        for (int i = 0; i < count; i++)
        {
            var simWheel = sim.Wheels[i];

            // Decide which copy is on first, so whichever one ends up visible
            // is posed below before it draws.
            UpdateWheelBlur(i, simWheel.RotationRate, simWheel.LocalPosition, simWheel.LocalRotation);

            if (wheels[i] == null) continue;

            // Chassis-space pose from VehWheel.UpdateVisualTransform during
            // the fixed step - already includes pivot, steer, suspension
            // travel, sag, camber, spin and wobble. Straight assignment.
            var t = wheels[i].transform;
            t.localPosition = simWheel.LocalPosition;
            t.localRotation = simWheel.LocalRotation;
        }
    }

    /// <summary>
    /// Swaps a wheel between its solid mesh and its blurred swhl copy once the
    /// spin passes WheelBlurRotationRate. The blurred copy takes the hub
    /// rotation rather than the full wheel rotation: the blur is baked into the
    /// texture, so spinning it as well just makes the smear rotate.
    /// Cars with no swhl for this wheel keep the solid mesh at all speeds.
    /// </summary>
    private void UpdateWheelBlur(int index, float rotationRate, Vector3 localPosition, Quaternion localRotation)
    {
        var spin = spinWheels[index];

        if (spin == null)
        {
            if (wheelBlurred[index])
            {
                wheelBlurred[index] = false;
                if (wheels[index] != null) wheels[index].SetActive(true);
            }
            return;
        }

        float rate = Mathf.Abs(rotationRate);
        bool blurred = rate > WheelBlurRotationRate;

        if (blurred != wheelBlurred[index])
        {
            wheelBlurred[index] = blurred;
            if (wheels[index] != null) wheels[index].SetActive(!blurred);
            spin.SetActive(blurred);
        }

        if (!blurred) return;

        var t = spin.transform;
        t.localPosition = localPosition;
        t.localRotation = localRotation;
    }

    private void UpdateFenders()
    {
        var sim = vehicle.VehCarSim;
        if (sim == null || sim.Wheels == null) return;

        for (int i = 0; i < 2; i++)
        {
            if (fenders[i] == null) continue;

            var simWheel = sim.Wheels[i];
            var t = fenders[i].transform;

            t.localPosition = simWheel.LocalPosition + fenderPivots[i];
            t.localRotation = simWheel.LocalHubRotation;
        }
    }

    /// <summary>
    /// Port of lvlInstance::ComputeShadowMatrix. The shadow does not ride the
    /// body: it is planted on whatever surface is under the vehicle origin,
    /// rolled to that surface's normal, and hidden when there is nothing flat
    /// enough beneath it.
    /// </summary>
    private void UpdateShadow()
    {
        if (shadow == null) return;

        var root = vehicle.transform;
        Vector3 origin = root.position + Vector3.up;

        if (!ProbeGround(origin, out var point, out var normal))
        {
            if (shadow.activeSelf) shadow.SetActive(false);
            return;
        }

        Vector3 forward = root.forward;

        // Nose straight up/down - fall back to the roof axis. Also covers the
        // rolled-car case, keeping the shadow facing the sky rather than the ground.
        if (Mathf.Abs(Vector3.Dot(forward, normal)) > 0.99f)
            forward = root.up;

        forward = Vector3.ProjectOnPlane(forward, normal);
        if (forward.sqrMagnitude < 1e-6f) return;

        if (!shadow.activeSelf) shadow.SetActive(true);

        // Deliberately not carried by the parent - the body pitches and rolls
        // above a shadow welded to the road.
        shadow.transform.SetPositionAndRotation(
            point + normal * shadowLift,
            Quaternion.LookRotation(forward.normalized, normal));
    }

    private static void ProcessShaders(ShaderSet shaders)
    {
        foreach (var shader in shaders.Shaders)
        {
            shader.Specular = Color.black;
            shader.Ambient = Color.black;
            if (shader.Name.EndsWith("_dmg", StringComparison.OrdinalIgnoreCase))
            {
                shader.Name = shader.Name.Substring(0, shader.Name.Length - 4);
            }
        }
    }

    private static Shader GetAdditiveShader()
    {
        if (additiveShader == null)
        {
            additiveShader = Shader.Find(AdditiveShaderName);
        }
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

    private void Update()
    {
        if (Level != null)
        {
            int curRoom = Level.FindRoomIdWithWarpsCheckMiss(this.transform.position, RoomID);
            if (curRoom != RoomID)
            {
                Level.MoveToRoom(this, curRoom);
            }
        }
    }

    private void OnDestroy()
    {
        // Meshes, the ShaderSet and materials all belong to the shared
        // template - only the GameObjects created for this instance are ours
        // to destroy.
        instance?.Destroy();
        instance = null;

        // The night copies are ours, not the template's - they were cloned
        // here, so they have to go here.
        for (int i = 0; i < nightLights.Count; i++)
        {
            if (nightLights[i].Clone != null) Destroy(nightLights[i].Clone);
        }
        nightLights.Clear();

        // The components went down with the part GameObjects; just drop the
        // references so nothing keeps drawing off a stale list.
        suspensions.Clear();
        axles.Clear();

        body = shadow = tlight = blight = rlight = null;
        for (int i = 0; i < wheels.Length; i++) wheels[i] = null;
        for (int i = 0; i < spinWheels.Length; i++) spinWheels[i] = null;
        for (int i = 0; i < wheelBlurred.Length; i++) wheelBlurred[i] = false;
        for (int i = 0; i < fenders.Length; i++) fenders[i] = null;
        for (int i = 0; i < variantObjects.Length; i++) variantObjects[i] = null;
        for (int i = 0; i < breakParts.Length; i++) breakParts[i] = null;
        for (int i = 0; i < sirens.Length; i++) sirens[i] = null;
        for (int i = 0; i < tsLights.Length; i++) tsLights[i] = null;

        vehicle = null;
        brakeLightsOn = false;
        reverseLightOn = false;
        nightLightsOn = false;
    }

    public override void Reset()
    {
        base.Reset();

        // the manager despawns its copies and flips Attached back
        Breakables?.Reset();

        // and the authored parts go back to however they load
        for (int i = 0; i < breakParts.Length; i++)
        {
            if (breakParts[i] != null)
                breakParts[i].SetActive(true);
        }

        // lights are driven from sim state, but the sim resets too - clear the
        // cached flags so UpdateLights sees a real change and re-applies
        brakeLightsOn = false;
        reverseLightOn = false;
        nightLightsOn = false;

        if (tlight != null) tlight.SetActive(false);
        if (blight != null) blight.SetActive(false);
        if (rlight != null) rlight.SetActive(false);

        for (int i = 0; i < tsLights.Length; i++)
        {
            if (tsLights[i] != null) tsLights[i].SetActive(false);
        }

        for (int i = 0; i < nightLights.Count; i++)
        {
            if (nightLights[i].Clone != null) nightLights[i].Clone.SetActive(false);
        }

        // Wheels come back stopped, so the solid meshes are what should show.
        for (int i = 0; i < wheelBlurred.Length; i++)
        {
            wheelBlurred[i] = false;
            if (wheels[i] != null) wheels[i].SetActive(true);
        }

        for (int i = 0; i < spinWheels.Length; i++)
        {
            if (spinWheels[i] != null) spinWheels[i].SetActive(false);
        }
    }
}