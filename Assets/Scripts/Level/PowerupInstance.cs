using System.Collections.Generic;
using UnityEngine;

public class PowerupInstance : LevelInstance
{
    public int VariantCount = 0;

    private string basename;
    private PackageObjectInstance instance;

    private const string MainPart = "main";
    private GameObject main;

    private List<Renderer> renderers = new List<Renderer>();
    private int variant = 0;

    private static readonly ShaderSelector WorldShaderFor =
        (entry, mainTex) => WorldShaderVariants.Select(entry.Diffuse, mainTex);

    public void Init(SDLCity level, string basename, Vector3 position, Quaternion rotation, Vector3 scale)
    {
        Flags |= LevelInstanceFlags.DisableCulling;

        Init(level, basename);        
        Load(basename);

        transform.localPosition = position;
        transform.localRotation = rotation;
        transform.localScale = scale;

        renderers.AddRange(this.gameObject.GetComponentsInChildren<Renderer>(true));
    }

    private void Load(string basename)
    {
        this.basename = basename;

        var template = GetOrBuildTemplate(basename, BuildTemplate);
        if (template == null)
        {
            Debug.LogError($"PowerupInstance: template '{basename}' failed to build.");
            return;
        }

        instance = template.Instantiate(transform);
        VariantCount = template.Shaders.VariantCount;
        AssignNamedParts();
    }

    private void BuildTemplate(PackageObjectLoader loader)
    {
        BuildTemplate(loader, this.Level);
    }

    public static void BuildTemplate(PackageObjectLoader loader, SDLCity level)
    {
        loader.SetLightEnable(false);
        loader.SetDefaultShaderSelector(WorldShaderFor);
        loader.LoadMainGroup(MainPart, applyPivot: false);
        loader.LoadShaders();
    }

    private void AssignNamedParts()
    {
        foreach (var part in instance.Parts)
        {
            switch (part.Name)
            {
                case MainPart: main = part.Object; break;
            }

            if (part.Name != MainPart && part.Object != null)
                part.Object.SetActive(false);
        }
    }

    public override void Reset()
    {
        base.Reset();
    }

    public override IEnumerable<Renderer> GetRenderers()
    {
        return renderers;
    }

    public override void SetVariant(int index)
    {
        variant = index;
        instance?.SetVariant(index);
    }

    protected virtual void Update()
    {
        if (Level != null)
        {
            int curRoom = Level.FindRoomIdWithWarpsCheckMiss(this.transform.position, RoomID);
            if (curRoom != RoomID)
            {
                Level.MoveToRoom(this, curRoom);
            }
        }

        float rotationRads = Time.deltaTime * 3.0f;
        this.transform.Rotate(0, rotationRads, 0);
    }

    private void OnDestroy()
    {
        instance?.Destroy();
        instance = null;
        main = null;
    }
}
