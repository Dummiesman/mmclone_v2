using System.Collections.Generic;
using System.IO;
using UnityEngine;

public class VehicleForm : MonoBehaviour
{
    private string basename;

    private Mesh bodyMesh;
    private Mesh shadowMesh;
    private Mesh whl0Mesh;
    private Mesh whl1Mesh;
    private Mesh whl2Mesh;
    private Mesh whl3Mesh;
    private List<Mesh> miscMeshes = new List<Mesh>();

    private readonly Dictionary<int, Mesh> variantMeshes = new Dictionary<int, Mesh>();
    private readonly Dictionary<int, GameObject> variantObjects = new Dictionary<int, GameObject>();

    private const string bodyObjectName = "BODY_H";
    private static string[] miscMeshNames = new[] { "break0", "break1", "break2", "break3",
                                                    "break01", "break12", "break23", "break03",
                                                    "fndr0", "fndr1", "whl4", "whl5"};

    private static readonly string[] suspensionPartNames = new[] { "shock0", "shock1", "shock2", "shock3",
                                                                   "arm0", "arm1", "arm2", "arm3",
                                                                   "shaft2", "shaft3", "axle0", "axle1" };


    private ShaderSet shaders;
    private readonly Dictionary<int, Material[]> materialCache = new Dictionary<int, Material[]>();
    private readonly List<MeshRenderer> renderers = new List<MeshRenderer>();
    private readonly List<GameObject> objects = new List<GameObject>();

    // submesh index -> global shader offset, per loaded mesh
    private readonly Dictionary<Mesh, int[]> materialMaps = new Dictionary<Mesh, int[]>();

    // Parallel to renderers: the map for that renderer's mesh, and a reusable
    // array of the right length to hand to sharedMaterials.
    private readonly List<int[]> rendererMaps = new List<int[]>();
    private readonly List<Material[]> rendererMaterials = new List<Material[]>();

    private const string reflectionTextureName = "refl_showroom";

    private static Texture2D reflectionTexture; // assigned to shader _ReflTex
    private static MaterialPropertyBlock noReflectionBlock;

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

    private static Texture2D GetReflectionTexture()
    {
        if (reflectionTexture == null)
        {
            reflectionTexture = TextureLoader.Load(reflectionTextureName);
        }
        return reflectionTexture;
    }

    private static MaterialPropertyBlock GetNoReflectionBlock()
    {
        if (noReflectionBlock == null)
        {
            noReflectionBlock = new MaterialPropertyBlock();
            noReflectionBlock.SetFloat("_Reflection", 0f);
        }
        return noReflectionBlock;
    }

    private GameObject CreateObject(string name, Mesh mesh, string pivotName)
    {
        if (mesh == null) return null;

        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = go.AddComponent<MeshRenderer>();

        if (!string.Equals(name, bodyObjectName, System.StringComparison.OrdinalIgnoreCase))
        {
            renderer.SetPropertyBlock(GetNoReflectionBlock());
        }

        if (!string.IsNullOrEmpty(pivotName))
        {
            if (UsesMatrixPivot(pivotName))
            {
                if (TryGetPivot(basename, pivotName, out var matrix))
                {
                    go.transform.localPosition = matrix.GetColumn(3);
                    go.transform.localRotation = matrix.rotation;
                }
            }
            else
            {
                go.transform.localPosition = GetPivot(basename, pivotName);
            }
        }

        objects.Add(go);
        renderers.Add(renderer);

        // Keep rendererMaps/rendererMaterials index-aligned with renderers.
        materialMaps.TryGetValue(mesh, out var map);
        rendererMaps.Add(map);
        rendererMaterials.Add(map != null ? new Material[map.Length] : null);

        return go;
    }

    private GameObject CreateObject(string name, Mesh mesh)
    {
        return CreateObject(name, mesh, null);
    }

    private void CreateObjects()
    {
        CreateObject("BODY_H", bodyMesh);
        CreateObject("SHADOW_H", shadowMesh);
        CreateObject("WHL0_H", whl0Mesh, "whl0");
        CreateObject("WHL1_H", whl1Mesh, "whl1");
        CreateObject("WHL2_H", whl2Mesh, "whl2");
        CreateObject("WHL3_H", whl3Mesh, "whl3");

        foreach (var mesh in miscMeshes)
        {
            string withoutLodName = mesh.name;
            if (withoutLodName.EndsWith("_H", System.StringComparison.OrdinalIgnoreCase))
            {
                withoutLodName = withoutLodName.Substring(0, withoutLodName.Length - 2);
            }
            CreateObject(mesh.name, mesh, withoutLodName);
        }
        foreach (var entry in variantMeshes)
        {
            string name = entry.Value.name;
            string withoutLodName = name;
            if (withoutLodName.EndsWith("_H", System.StringComparison.OrdinalIgnoreCase))
            {
                withoutLodName = withoutLodName.Substring(0, withoutLodName.Length - 2);
            }
            var go = CreateObject(name, entry.Value, withoutLodName);
            if (go != null) variantObjects[entry.Key] = go;
        }
    }

    private List<Material> GetMaterialsForVariant(int index)
    {
        var materials = new List<Material>();
        if (shaders == null) return materials;

        var shadersForVariant = shaders.GetShadersForVariant(index);
        if (shadersForVariant == null) return materials;

        foreach (var entry in shadersForVariant)
        {
            var texture = TextureCache.Get(entry.Name);
            var shader = VehicleShaderVariants.Select(texture);
            var material = new Material(shader) { name = entry.Name };

            material.SetTexture("_MainTex", texture);
            material.SetTexture("_ReflTex", GetReflectionTexture());
            material.SetColor("_Color", entry.Diffuse);
            material.SetFloat("_Reflection", entry.Reflectivity);

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

    private void SetVariant(int index)
    {
        var materials = GetMaterials(index);
        for (int i = 0; i < renderers.Count; i++)
        {
            if (renderers[i] == null) continue;

            var map = rendererMaps[i];
            if (map == null)
            {
                // No map recorded for this mesh; fall back to the shared array.
                renderers[i].sharedMaterials = materials;
                continue;
            }

            var slots = rendererMaterials[i];
            for (int s = 0; s < map.Length; s++)
            {
                int offset = map[s];
                slots[s] = (offset >= 0 && offset < materials.Length) ? materials[offset] : null;
            }

            // sharedMaterials copies the array, so reusing the buffer is safe.
            renderers[i].sharedMaterials = slots;
        }
        foreach (var entry in variantObjects)
        {
            if (entry.Value != null) entry.Value.SetActive(entry.Key == index);
        }
    }

    private Vector3 GetPivot(string basename, string part)
    {
        string fileName = $"{basename}_{part}.mtx";
        var matrixFile = new MatrixFile();
        using (var stream = AssetManager.Open("geometry", fileName))
        {
            matrixFile.Load(stream);
        }
        return matrixFile.Origin;
    }

    private static bool UsesMatrixPivot(string part)
    {
        foreach (var name in suspensionPartNames)
        {
            if (name.Equals(part, System.StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static bool TryGetPivot(string basename, string name, out Matrix4x4 pivot)
    {
        pivot = Matrix4x4.identity;

        using (var stream = AssetManager.Open("geometry", $"{basename}_{name}.mtx"))
        {
            if (stream == null) return false;

            using (var reader = new BinaryReader(stream, System.Text.Encoding.ASCII, true))
            {
                Vector3 xaxis = -(reader.ReadVector3()).ConvertCoordinateSpace();
                Vector3 yaxis = (reader.ReadVector3()).ConvertCoordinateSpace();
                Vector3 zaxis = (reader.ReadVector3()).ConvertCoordinateSpace();
                Vector3 offset = (reader.ReadVector3()).ConvertCoordinateSpace();

                pivot.SetColumn(0, new Vector4(xaxis.x, xaxis.y, xaxis.z, 0f));
                pivot.SetColumn(1, new Vector4(yaxis.x, yaxis.y, yaxis.z, 0f));
                pivot.SetColumn(2, new Vector4(zaxis.x, zaxis.y, zaxis.z, 0f));
                pivot.SetColumn(3, new Vector4(offset.x, offset.y, offset.z, 1f));
            }
        }

        return true;
    }

    private void PostprocessShaders()
    {
        foreach (var shader in shaders.Shaders)
        {
            shader.Specular = Color.black;
            shader.Ambient = Color.black;
            if (shader.Name.EndsWith("_dmg", System.StringComparison.OrdinalIgnoreCase))
            {
                shader.Name = shader.Name.Substring(0, shader.Name.Length - 4);
            }
        }
    }

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

    public void Load(string basename)
    {
        this.basename = basename;
        using (var stream = AssetManager.Open("geometry", $"{basename}.pkg"))
        {
            if(stream == null)
            {
                Debug.LogError($"VehicleForm: No geometry for {basename}");
                return;
            }

            var packageFile = new PackageFile(stream);
            bodyMesh = LoadMesh(packageFile, "BODY_H");
            shadowMesh = LoadMesh(packageFile, "SHADOW_H");


            while (!packageFile.CurrentFileName.Equals("WHL0_H", System.StringComparison.OrdinalIgnoreCase) && !packageFile.EOF)
            {
                string fileName = packageFile.CurrentFileName;
                if (fileName.EndsWith("_H", System.StringComparison.OrdinalIgnoreCase))
                {
                    string withoutLodName = fileName.Substring(0, fileName.Length - 2);
                    bool shouldLoad = false;
                    foreach (var name in suspensionPartNames)
                    {
                        if (name.Equals(withoutLodName, System.StringComparison.OrdinalIgnoreCase))
                        {
                            shouldLoad = true;
                            break;
                        }
                    }

                    if (shouldLoad)
                    {
                        miscMeshes.Add(LoadMesh(packageFile, packageFile.CurrentFileName));
                    }
                    else
                    {
                        packageFile.Skip();
                    }
                }
                else
                {
                    packageFile.Skip();
                }
            }

            whl0Mesh = LoadMesh(packageFile, "WHL0_H");
            whl1Mesh = LoadMesh(packageFile, "WHL1_H");
            whl2Mesh = LoadMesh(packageFile, "WHL2_H");
            whl3Mesh = LoadMesh(packageFile, "WHL3_H");

            while (packageFile.CurrentFileName != "shaders" && !packageFile.EOF)
            {
                string fileName = packageFile.CurrentFileName;
                if (fileName.EndsWith("_H", System.StringComparison.OrdinalIgnoreCase))
                {
                    string withoutLodName = fileName.Substring(0, fileName.Length - 2);
                    if (fileName.StartsWith("VARIANT", System.StringComparison.OrdinalIgnoreCase))
                    {
                        string variantNumberStr = withoutLodName.Substring(7);
                        if (int.TryParse(variantNumberStr, out var variantNumber))
                        {
                            variantMeshes[variantNumber] = LoadMesh(packageFile, fileName);
                        }
                        else
                        {
                            packageFile.Skip();
                        }
                    }
                    else
                    {

                        bool shouldLoad = false;
                        foreach (var name in miscMeshNames)
                        {
                            if (name.Equals(withoutLodName, System.StringComparison.OrdinalIgnoreCase))
                            {
                                shouldLoad = true;
                                break;
                            }
                        }

                        if (shouldLoad)
                        {
                            miscMeshes.Add(LoadMesh(packageFile, packageFile.CurrentFileName));
                        }
                        else
                        {
                            packageFile.Skip();
                        }
                    }
                }
                else
                {
                    packageFile.Skip();
                }
            }

            packageFile.SkipTo("shaders");
            var reader = packageFile.OpenFile("shaders");
            shaders = new ShaderSet();
            shaders.LoadSafe(reader);
            packageFile.CloseFile();
        }

        PostprocessShaders();
        CreateObjects();
        SetVariant(variant);
        Shader.SetGlobalFloat("_ReflectionIntensity", 1.0f);
    }

    private void OnDestroy()
    {
        foreach (var go in objects) Destroy(go);
        objects.Clear();
        renderers.Clear();
        rendererMaps.Clear();
        rendererMaterials.Clear();

        foreach (var materials in materialCache.Values)
        {
            foreach (var material in materials) Destroy(material);
        }
        materialCache.Clear();

        Destroy(bodyMesh);
        Destroy(shadowMesh);
        Destroy(whl0Mesh);
        Destroy(whl1Mesh);
        Destroy(whl2Mesh);
        Destroy(whl3Mesh);
        bodyMesh = shadowMesh = whl0Mesh = whl1Mesh = whl2Mesh = whl3Mesh = null;

        foreach (var mesh in variantMeshes.Values) Destroy(mesh);
        variantMeshes.Clear();
        variantObjects.Clear();
        materialMaps.Clear();

        foreach (var mesh in miscMeshes) Destroy(mesh);
        miscMeshes.Clear();

        shaders = null;
    }
}