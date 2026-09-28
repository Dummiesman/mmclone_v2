using System.Collections.Generic;
using UnityEngine;

public class GizmoInstance : LevelInstance
{
    public Vector3 CenterOfGravity => centerOfGravity;
    public int VariantCount = 0;

    private string basename;
    private PackageObjectInstance instance;

    private const string MainPart = "main";
    private GameObject main;

    private int dataIndex = -1;
    private Vector3 centerOfGravity;

    private List<Renderer> renderers = new List<Renderer>();
    private int variant = 0;

    private static readonly ShaderSelector WorldShaderFor =
        (entry, mainTex) => WorldShaderVariants.Select(entry.Diffuse, mainTex);

    public void Init(SDLCity level, string basename, Vector3 position, Quaternion rotation, Vector3 scale)
    {
        Init(level, basename);

        dataIndex = level.BangerDataManager.AddEntry(basename);
        Load(basename);

        if (dataIndex >= 0)
        {
            var data = level.BangerDataManager.GetEntry(dataIndex);
            centerOfGravity = data.CG;
            position += data.CG;
        }

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
            Debug.LogError($"GizmoInstance: template '{basename}' failed to build.");
            return;
        }

        instance = template.Instantiate(transform);
        VariantCount = template.Shaders.VariantCount;
        AssignNamedParts();
    }

    private void BuildTemplate(PackageObjectLoader loader)
    {
        BuildTemplate(loader, this.Level, dataIndex);
    }

    public static void BuildTemplate(PackageObjectLoader loader, SDLCity level, int dataIndex)
    {
        if (dataIndex >= 0)
        {
            var data = level.BangerDataManager.GetEntry(dataIndex);

            if (data.BillFlags.HasFlag(BangerDataFlags.Unlit))
            {
                loader.SetLightEnable(false);
            }
        }

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

    private void Update()
    {
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
    }

    private void OnDestroy()
    {
        instance?.Destroy();
        instance = null;
        main = null;
    }
}