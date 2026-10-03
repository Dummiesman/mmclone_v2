using System.Collections.Generic;
using UnityEngine;

public class SimpleForm : MonoBehaviour
{
    public Shader shader;

    private Mesh mesh;
    private GameObject obj;
    private MeshRenderer meshRenderer;
    private ShaderSet shaders;
    private int[] materialMap;

    private readonly Dictionary<int, Material[]> materialCache = new Dictionary<int, Material[]>();

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

    public static SimpleForm Create(string basename, Shader shader, Transform parent = null, int variant = 0)
    {
        var go = new GameObject(basename);
        if (parent != null) go.transform.SetParent(parent, false);

        var form = go.AddComponent<SimpleForm>();
        form.shader = shader;
        form.variant = variant;
        form.Load(basename);
        return form;
    }

    public SimpleForm Clone(Transform parent = null)
    {
        var go = new GameObject(gameObject.name);
        go.transform.SetParent(parent != null ? parent : transform.parent, false);
        go.transform.localPosition = transform.localPosition;
        go.transform.localRotation = transform.localRotation;
        go.transform.localScale = transform.localScale;

        var clone = go.AddComponent<SimpleForm>();
        clone.shader = shader;
        clone.variant = variant;
        clone.mesh = mesh != null ? Instantiate(mesh) : null;
        clone.materialMap = materialMap != null ? (int[])materialMap.Clone() : null;
        clone.shaders = shaders;

        foreach (var entry in materialCache)
        {
            var source = entry.Value;
            var copy = new Material[source.Length];
            for (int i = 0; i < source.Length; i++)
            {
                copy[i] = source[i] != null ? new Material(source[i]) { name = source[i].name } : null;
            }
            clone.materialCache[entry.Key] = copy;
        }

        if (clone.mesh != null)
        {
            clone.mesh.name = mesh.name;
            clone.obj = new GameObject(clone.mesh.name);
            clone.obj.transform.SetParent(go.transform, false);
            clone.obj.AddComponent<MeshFilter>().sharedMesh = clone.mesh;
            clone.meshRenderer = clone.obj.AddComponent<MeshRenderer>();
            clone.SetVariant(clone.variant);
        }

        return clone;
    }

    public void Load(string basename)
    {
        using (var stream = AssetManager.Open("geometry", $"{basename}.pkg"))
        {
            var packageFile = new PackageFile(stream);
            bool haveFirst = false;

            while(packageFile.CurrentFileName != "shaders")
            {
                string currentName = packageFile.CurrentFileName;
                if (!haveFirst)
                {
                    // First object in the package
                    var file = packageFile.OpenFile(currentName);
                    var loader = new PackageModelLoader(currentName, file);
                    mesh = loader.Load(out materialMap);
                    packageFile.CloseFile();
                    haveFirst = true;
                }
                else
                {
                    // Other objects, skip with model loader in case of PKG2
                    var file = packageFile.OpenFile(currentName);
                    var loader = new PackageModelLoader(currentName, file);
                    loader.Skip();
                    packageFile.CloseFile();
                }
            }

            // Shaders
            if (packageFile.CurrentFileName == "shaders")
            {
                var reader = packageFile.OpenFile("shaders");
                shaders = new ShaderSet();
                shaders.LoadSafe(reader);
                packageFile.CloseFile();
            }
        }

        obj = new GameObject(mesh.name);
        obj.transform.SetParent(transform, false);
        obj.AddComponent<MeshFilter>().sharedMesh = mesh;
        meshRenderer = obj.AddComponent<MeshRenderer>();

        SetVariant(variant);
    }

    private Material[] BuildMaterials(int index)
    {
        var entries = shaders != null ? shaders.GetShadersForVariant(index) : null;
        if (entries == null) return new Material[0];

        var result = new Material[materialMap != null ? materialMap.Length : entries.Count];

        for (int i = 0; i < result.Length; i++)
        {
            int offset = materialMap != null ? materialMap[i] : i;
            if (offset < 0 || offset >= entries.Count) continue;

            var entry = entries[offset];
            var material = new Material(shader) { name = entry.Name };
            material.SetTexture("_MainTex", TextureCache.Get(entry.Name));
            material.SetColor("_Color", entry.Diffuse);
            material.SetFloat("_Reflection", entry.Reflectivity);
            result[i] = material;
        }

        return result;
    }

    private Material[] GetMaterials(int index)
    {
        if (!materialCache.TryGetValue(index, out var materials))
        {
            materials = BuildMaterials(index);
            materialCache[index] = materials;
        }
        return materials;
    }

    private void SetVariant(int index)
    {
        if (meshRenderer == null) return;
        meshRenderer.sharedMaterials = GetMaterials(index);
    }

    private void OnDestroy()
    {
        if (obj != null) Destroy(obj);

        foreach (var materials in materialCache.Values)
            foreach (var m in materials)
                if (m != null) Destroy(m);
        materialCache.Clear();

        if (mesh != null) Destroy(mesh);
        mesh = null;
        shaders = null;
    }
}