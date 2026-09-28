using System.Collections.Generic;
using UnityEngine;

public class Dashboard : MonoBehaviour
{
    #region asNode Variables
    private Vector3 dashPos = Vector3.zero;
    private Vector3 roofPos = Vector3.zero;
    private Vector3 wheelPos = Vector3.zero;
    private Vector3 dmgOffset = Vector3.zero;
    private Vector3 speedOffset = Vector3.zero;
    private Vector3 tachOffset = Vector3.zero;
    private Vector3 dmgPivotOffset = Vector3.zero;
    private Vector3 speedPivotOffset = Vector3.zero;
    private Vector3 tachPivotOffset = Vector3.zero;
    private Vector3 wheelPivotOffset = Vector3.zero;
    private Vector3 gearPivotOffset = Vector3.zero;

    private float rpmRotMin = 0f;
    private float rpmRotMax = 1f;
    private float speedRotMin = 0f;
    private float speedRotMax = 1f;
    private float damageRotMin = 0f;
    private float damageRotMax = 1f;
    private float wheelFact = 1f;
    #endregion

    private const string dashShaderName = "Custom/Dashboard";
    private const float lightingAmount = 0.25f;

    // Render queues, previously applied in SetGear via SetQueue.
    private const int wheelQueue = 3150;
    private const int needleQueue = 3050;
    private const int gearQueue = 3100;
    private const int extraQueue = 3200;

    private const string dashName = "DASH";
    private const string roofName = "ROOF";
    private const string wheelName = "WHEEL";
    private const string speedNeedleName = "SPEED_NEEDLE";
    private const string tachNeedleName = "TACH_NEEDLE";
    private const string dmgNeedleName = "DAMAGE_NEEDLE";
    private const string gearIndicatorName = "GEAR_INDICATOR";
    private const string extraName = "DASH_EXTRA";

    private float maxSpeed = 160f;
    private float maxRPM = 8000.0f;

    private string basename;
    private VehCar vehicle;

    private readonly Dictionary<string, Mesh> meshes = new Dictionary<string, Mesh>();
    private readonly List<string> meshOrder = new List<string>();
    private readonly Dictionary<int, Mesh> variantMeshes = new Dictionary<int, Mesh>();
    private readonly Dictionary<int, GameObject> variantObjects = new Dictionary<int, GameObject>();

    private ShaderSet shaders;
    private readonly Dictionary<int, Material[]> materialCache = new Dictionary<int, Material[]>();

    // queue -> (source material -> copy with that renderQueue)
    private readonly Dictionary<int, Dictionary<Material, Material>> queuedMaterials
        = new Dictionary<int, Dictionary<Material, Material>>();

    private readonly List<MeshRenderer> renderers = new List<MeshRenderer>();
    private readonly List<GameObject> objects = new List<GameObject>();

    // submesh index -> global shader offset, per loaded mesh
    private readonly Dictionary<Mesh, int[]> materialMaps = new Dictionary<Mesh, int[]>();

    // Parallel to renderers.
    private readonly List<int[]> rendererMaps = new List<int[]>();
    private readonly List<Material[]> rendererMaterials = new List<Material[]>();
    private readonly List<int> rendererQueues = new List<int>();

    // Parallel to renderers: true for the parts whose materials follow the gear
    // (the VARIANT meshes and the gear indicator). Everything else stays on
    // variant 0 for the lifetime of the dashboard.
    private readonly List<bool> rendererIsGear = new List<bool>();

    // Variant 0 never changes, so it only needs assigning once.
    private bool baseMaterialsApplied;

    // Rotating parts (the holder transform, not the mesh object).
    private Transform steeringWheel;
    private Transform speedNeedle;
    private Transform tachNeedle;
    private Transform dmgNeedle;

    private static Shader dashShader;

    private int variant = 0;
    public int Variant
    {
        get => variant;
        set
        {
            variant = value;
            SetVariant(variant);
        }
    }

    private int lastGear = -2;
    private Vector3 camOffset;

    private void Update()
    {
        if (vehicle == null || vehicle.Basename == null)
            return;

        var sim = vehicle.VehCarSim;

        float speedFactor = Mathf.Clamp01(sim.SpeedInMph / maxSpeed);
        float rpmFactor = Mathf.Clamp01(sim.Engine.CurrentRPM / maxRPM);
        float damageFactor = Mathf.Clamp01(vehicle.Damage.DamagePercentage);
        float wheelFactor = Mathf.Clamp(wheelFact, -1f, 1f);

        int gear = sim.Transmission.CurrentGear;
        if (gear != lastGear)
        {
            lastGear = gear;
            SetGear(gear);
        }

        if (tachNeedle != null)
            tachNeedle.localEulerAngles = new Vector3(0, 0, Mathf.Lerp(rpmRotMin, rpmRotMax, rpmFactor) * Mathf.Rad2Deg);
        if (speedNeedle != null)
            speedNeedle.localEulerAngles = new Vector3(0, 0, Mathf.Lerp(speedRotMin, speedRotMax, speedFactor) * Mathf.Rad2Deg);
        if (dmgNeedle != null)
            dmgNeedle.localEulerAngles = new Vector3(0, 0, Mathf.Lerp(damageRotMin, damageRotMax, damageFactor) * Mathf.Rad2Deg);
        if (steeringWheel != null)
            steeringWheel.localEulerAngles = new Vector3(0, 0, sim.SteeringInput * wheelFactor * Mathf.Rad2Deg);
    }

    public void SetGear(int gear)
    {
        Variant = gear;
    }

    #region Materials
    private static Shader GetDashShader()
    {
        if (dashShader == null)
            dashShader = Shader.Find(dashShaderName);
        return dashShader;
    }

    private List<Material> GetMaterialsForVariant(int index)
    {
        var materials = new List<Material>();
        if (shaders == null) return materials;

        var shadersForVariant = shaders.GetShadersForVariant(index);
        if (shadersForVariant == null && index != 0)
            shadersForVariant = shaders.GetShadersForVariant(0);
        if (shadersForVariant == null) return materials;

        foreach (var entry in shadersForVariant)
        {
            var texture = TextureCache.Get(entry.Name);
            var shader = GetDashShader() ?? VehicleShaderVariants.Select(texture);
            var material = new Material(shader) { name = entry.Name };

            material.SetTexture("_MainTex", texture);
            material.SetColor("_Color", entry.Diffuse);
            material.SetFloat("_LightingAmount", lightingAmount);

            materials.Add(material);
        }

        return materials;
    }

    private Material[] GetMaterials(int index)
    {
        if (!materialCache.TryGetValue(index, out var materials))
        {
            materials = GetMaterialsForVariant(index).ToArray();
            materialCache[index] = materials;
        }
        return materials;
    }

    // Materials are shared between objects, so a part that needs its own render
    // queue gets a copy rather than stomping the queue for everything else.
    private Material GetQueued(Material material, int queue)
    {
        if (material == null || queue <= 0)
            return material;

        if (!queuedMaterials.TryGetValue(queue, out var byQueue))
        {
            byQueue = new Dictionary<Material, Material>();
            queuedMaterials[queue] = byQueue;
        }

        if (!byQueue.TryGetValue(material, out var copy))
        {
            copy = new Material(material) { name = $"{material.name}_{queue}", renderQueue = queue };
            byQueue[material] = copy;
        }
        return copy;
    }

    private void ApplyMaterials(int i, Material[] variantMaterials)
    {
        int queue = rendererQueues[i];
        var map = rendererMaps[i];

        if (map == null)
        {
            // No map recorded for this mesh; fall back to the shared array.
            if (queue <= 0)
            {
                renderers[i].sharedMaterials = variantMaterials;
            }
            else
            {
                var copies = new Material[variantMaterials.Length];
                for (int s = 0; s < variantMaterials.Length; s++)
                    copies[s] = GetQueued(variantMaterials[s], queue);
                renderers[i].sharedMaterials = copies;
            }
            return;
        }

        var slots = rendererMaterials[i];
        for (int s = 0; s < map.Length; s++)
        {
            int offset = map[s];
            var material = (offset >= 0 && offset < variantMaterials.Length) ? variantMaterials[offset] : null;
            slots[s] = GetQueued(material, queue);
        }

        // sharedMaterials copies the array, so reusing the buffer is safe.
        renderers[i].sharedMaterials = slots;
    }

    private void SetVariant(int index)
    {
        var baseMaterials = GetMaterials(0);
        var gearMaterials = (index == 0) ? baseMaterials : GetMaterials(index);

        for (int i = 0; i < renderers.Count; i++)
        {
            if (renderers[i] == null) continue;

            if (rendererIsGear[i])
            {
                ApplyMaterials(i, gearMaterials);
            }
            else if (!baseMaterialsApplied)
            {
                ApplyMaterials(i, baseMaterials);
            }
        }

        if (renderers.Count > 0)
            baseMaterialsApplied = true;

        foreach (var entry in variantObjects)
        {
            if (entry.Value != null) entry.Value.SetActive(entry.Key == index);
        }
    }
    #endregion

    #region Objects
    private Vector3 GetPivot(string part)
    {
        string fileName = $"{basename}_{part}.mtx";
        if (!AssetManager.Exists("geometry", fileName))
            return Vector3.zero;

        var matrixFile = new MatrixFile();
        using (var stream = AssetManager.Open("geometry", fileName))
        {
            matrixFile.Load(stream);
        }
        return matrixFile.Pivot;
    }

    /// <summary>
    /// Creates an object for a mesh. Rotating parts get a holder at the pivot
    /// with the mesh pushed back by -pivot, which is what the old ApplyPivot
    /// did by hand (move parent, pull children back).
    /// </summary>
    private GameObject CreateObject(string name, Mesh mesh, Vector3 position, Vector3 pivotOffset, bool rotates, int queue, bool isGear = false)
    {
        if (mesh == null) return null;

        var holder = new GameObject(name);
        holder.transform.SetParent(transform, false);

        Vector3 pivot = rotates ? GetPivot(name) + pivotOffset : Vector3.zero;
        holder.transform.localPosition = position + pivot;

        GameObject meshObject = holder;
        if (rotates && pivot != Vector3.zero)
        {
            meshObject = new GameObject($"{name}_MESH");
            meshObject.transform.SetParent(holder.transform, false);
            meshObject.transform.localPosition = -pivot;
        }

        meshObject.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = meshObject.AddComponent<MeshRenderer>();

        objects.Add(holder);
        renderers.Add(renderer);
        rendererQueues.Add(queue);
        rendererIsGear.Add(isGear);

        // Keep rendererMaps/rendererMaterials index-aligned with renderers.
        materialMaps.TryGetValue(mesh, out var map);
        rendererMaps.Add(map);
        rendererMaterials.Add(map != null ? new Material[map.Length] : null);

        return holder;
    }

    private Mesh GetMesh(string name)
    {
        meshes.TryGetValue(name, out var mesh);
        return mesh;
    }

    private void CreateObjects()
    {
        CreateObject(dashName, GetMesh(dashName), dashPos, Vector3.zero, false, 0);
        CreateObject(roofName, GetMesh(roofName), roofPos, Vector3.zero, false, 0);

        steeringWheel = CreateObject(wheelName, GetMesh(wheelName),
            wheelPos + dashPos, wheelPivotOffset, true, wheelQueue)?.transform;
        speedNeedle = CreateObject(speedNeedleName, GetMesh(speedNeedleName),
            speedOffset + dashPos, speedPivotOffset, true, needleQueue)?.transform;
        tachNeedle = CreateObject(tachNeedleName, GetMesh(tachNeedleName),
            tachOffset + dashPos, tachPivotOffset, true, needleQueue)?.transform;
        dmgNeedle = CreateObject(dmgNeedleName, GetMesh(dmgNeedleName),
            dmgOffset + dashPos, dmgPivotOffset, true, needleQueue)?.transform;

        // The gear indicator's material is what actually shows the gear, so it
        // follows the variant.
        CreateObject(gearIndicatorName, GetMesh(gearIndicatorName), gearPivotOffset + dashPos, Vector3.zero, false, gearQueue, true);
        CreateObject(extraName, GetMesh(extraName), dashPos, Vector3.zero, false, extraQueue);

        // Anything else the package contained, at its authored position.
        foreach (var name in meshOrder)
        {
            if (IsHandled(name)) continue;
            CreateObject(name, meshes[name], Vector3.zero, Vector3.zero, false, 0);
        }

        // Gear variants (only the one matching Variant stays active).
        foreach (var entry in variantMeshes)
        {
            var go = CreateObject(entry.Value.name, entry.Value, dashPos, Vector3.zero, false, gearQueue, true);
            if (go != null) variantObjects[entry.Key] = go;
        }
    }

    private static bool IsHandled(string name)
    {
        return name == dashName || name == roofName || name == wheelName
            || name == speedNeedleName || name == tachNeedleName || name == dmgNeedleName
            || name == gearIndicatorName || name == extraName;
    }
    #endregion

    #region Loading
    private Mesh LoadMesh(PackageFile model, string name)
    {
        model.SkipTo(name);
        var file = model.OpenFile(name);
        var loader = new PackageModelLoader(name, file);
        var loaded = loader.Load(out var materialMap);
        model.CloseFile();

        if (loaded != null) materialMaps[loaded] = materialMap;
        return loaded;
    }

    // Returns null for LODs we don't want, otherwise the name without its suffix.
    private static string StripLod(string fileName)
    {
        if (fileName.EndsWith("_H", System.StringComparison.OrdinalIgnoreCase))
            return fileName.Substring(0, fileName.Length - 2).ToUpperInvariant();
        if (fileName.EndsWith("_M", System.StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith("_L", System.StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith("_VL", System.StringComparison.OrdinalIgnoreCase))
            return null;
        return fileName.ToUpperInvariant();
    }

    private void LoadPackage()
    {
        using (var stream = AssetManager.Open("geometry", $"{basename}.pkg"))
        {
            var packageFile = new PackageFile(stream);

            while (!string.IsNullOrEmpty(packageFile.CurrentFileName)
                   && packageFile.CurrentFileName != "shaders")
            {
                string fileName = packageFile.CurrentFileName;
                string name = StripLod(fileName);

                if (name == null)
                {
                    packageFile.Skip();
                    continue;
                }

                if (name.StartsWith("VARIANT", System.StringComparison.OrdinalIgnoreCase))
                {
                    if (int.TryParse(name.Substring(7), out var variantNumber))
                    {
                        var mesh = LoadMesh(packageFile, fileName);
                        if (mesh != null) variantMeshes[variantNumber] = mesh;
                    }
                    else
                    {
                        packageFile.Skip();
                    }
                    continue;
                }

                if (meshes.ContainsKey(name))
                {
                    packageFile.Skip();
                    continue;
                }

                var loaded = LoadMesh(packageFile, fileName);
                if (loaded != null)
                {
                    meshes[name] = loaded;
                    meshOrder.Add(name);
                }
            }

            if (packageFile.CurrentFileName == "shaders")
            {
                packageFile.SkipTo("shaders");
                var reader = packageFile.OpenFile("shaders");
                shaders = new ShaderSet();
                shaders.LoadSafe(reader);
                packageFile.CloseFile();
            }
            else
            {
                Debug.LogWarning($"No shaders in {basename}.pkg");
            }
        }
    }

    private void LoadSettings(string tuneName)
    {
        var dashReader = AssetManager.OpenNode("tune", $"{tuneName}.asNode");

        dashPos = dashReader.Read("DashPos", Vector3.zero).ConvertCoordinateSpace();
        roofPos = dashReader.Read("RoofPos", Vector3.zero).ConvertCoordinateSpace();
        wheelPos = dashReader.Read("WheelPos", Vector3.zero).ConvertCoordinateSpace();
        dmgOffset = dashReader.Read("DmgOffset", Vector3.zero).ConvertCoordinateSpace();
        speedOffset = dashReader.Read("SpeedOffset", Vector3.zero).ConvertCoordinateSpace();
        tachOffset = dashReader.Read("TachOffset", Vector3.zero).ConvertCoordinateSpace();
        dmgPivotOffset = dashReader.Read("DmgPivotOffset", Vector3.zero).ConvertCoordinateSpace();
        speedPivotOffset = dashReader.Read("SpeedPivotOffset", Vector3.zero).ConvertCoordinateSpace();
        tachPivotOffset = dashReader.Read("TachPivotOffset", Vector3.zero).ConvertCoordinateSpace();
        wheelPivotOffset = dashReader.Read("WheelPivotOffset", Vector3.zero).ConvertCoordinateSpace();

        wheelFact = dashReader.Read("WheelFact", wheelFact);
        rpmRotMin = dashReader.Read("RPMRotMin", rpmRotMin);
        rpmRotMax = dashReader.Read("RPMRotMax", rpmRotMax);
        speedRotMin = dashReader.Read("SpeedRotMin", speedRotMin);
        speedRotMax = dashReader.Read("SpeedRotMax", speedRotMax);
        damageRotMin = dashReader.Read("DamageRotMin", damageRotMin);
        damageRotMax = dashReader.Read("DamageRotMax", damageRotMax);

        gearPivotOffset = dashReader.Read("GearPivotOffset", gearPivotOffset).ConvertCoordinateSpace();

        // added by mm2hook
        maxSpeed = dashReader.Read("MaxSpeed", maxSpeed);
        maxRPM = dashReader.Read("MaxRPM", maxRPM);
    }

    private void InitPositionFromCamData(string vehicleBasename)
    {
        string camFile = $"{vehicleBasename}_dash.camPovCS";

        if (!AssetManager.Exists("tune", "camera", camFile))
            return;

        var camReader = AssetManager.OpenNode("tune", $"camera/{camFile}");
        camOffset = camReader.Read("Offset", Vector3.zero).ConvertCoordinateSpace();
        camOffset.z = -camOffset.z;
    }

    public bool Load(string vehicleBasename)
    {
        basename = $"{vehicleBasename}_dash";

        if (!AssetManager.Exists("tune", $"{basename}.asNode"))
        {
            Debug.LogWarning($"Could not load dashboard for {vehicleBasename}");
            return false;
        }

        if (!AssetManager.Exists("geometry", $"{basename}.pkg"))
        {
            Debug.LogWarning($"Could not load dashboard model for {vehicleBasename}");
            return false;
        }

        LoadSettings(basename);
        LoadPackage();
        CreateObjects();
        SetVariant(variant);

        InitPositionFromCamData(vehicleBasename);
        return true;
    }

    public void Init(VehCar vehicle)
    {
        this.vehicle = vehicle;

        if (!Load(vehicle.Basename))
        {
            enabled = false;
            return;
        }

        SetGear(0);
        Deactivate();
    }
    #endregion

    #region Camera
    public void Activate()
    {
        foreach (var renderer in renderers)
            if (renderer != null) renderer.enabled = true;
    }

    public void Deactivate()
    {
        foreach (var renderer in renderers)
            if (renderer != null) renderer.enabled = false;
    }
    #endregion

    private void OnDestroy()
    {
        foreach (var go in objects) Destroy(go);
        objects.Clear();
        renderers.Clear();
        rendererMaps.Clear();
        rendererMaterials.Clear();
        rendererQueues.Clear();
        rendererIsGear.Clear();
        baseMaterialsApplied = false;

        foreach (var byQueue in queuedMaterials.Values)
        {
            foreach (var material in byQueue.Values) Destroy(material);
        }
        queuedMaterials.Clear();

        foreach (var materials in materialCache.Values)
        {
            foreach (var material in materials) Destroy(material);
        }
        materialCache.Clear();

        foreach (var mesh in meshes.Values) Destroy(mesh);
        meshes.Clear();
        meshOrder.Clear();

        foreach (var mesh in variantMeshes.Values) Destroy(mesh);
        variantMeshes.Clear();
        variantObjects.Clear();
        materialMaps.Clear();

        steeringWheel = speedNeedle = tachNeedle = dmgNeedle = null;
        shaders = null;
    }

    private void LateUpdate()
    {
        // follow player car
        transform.SetPositionAndRotation(vehicle.Model.transform.TransformPoint(camOffset), vehicle.Model.transform.rotation * Quaternion.Euler(0, 180, 0));
    }
}