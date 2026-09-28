using System;
using System.Collections.Generic;
using UnityEngine;

public class LevelFixedAny : LevelInstance
{
    private const string MainPart = "main";
    private const string ShadowPart = "shadow";
    private static readonly string[] ExtraParts = { "mask", "nonrandom", "refl", "opaque" };

    private static readonly ShaderSelector WorldShaderFor =
        (entry, mainTex) => WorldShaderVariants.Select(entry.Diffuse, mainTex);
    private static readonly Action<PackageObjectLoader> BuildTemplateAction = BuildTemplate;

    private string basename;
    private PackageObjectInstance instance;

    private GameObject main;
    private GameObject shadow;
    private GameObject mask;
    private GameObject nonrandom;
    private GameObject refl;
    private GameObject opaque;

    private List<Renderer> renderers = new List<Renderer>();

    public void Init(SDLCity level, string basename, Vector3 position, Quaternion rotation, Vector3 scale)
    {
        Flags |= LevelInstanceFlags.Static;

        Init(level, basename);
        Load(basename);

        transform.localPosition = position;
        transform.localRotation = rotation;
        transform.localScale = scale;

        var template = GetTemplate(basename);
        if(template != null && template.Xrefs != null)
        {
            // instantiate bangers
            foreach(var xref in template.Xrefs.Xrefs)
            {
                var xRefPosition = xref.Origin;
                var xrefRotation = Quaternion.LookRotation(xref.zAxis, xref.yAxis);
                var xrefScale = new Vector3(xref.xAxis.magnitude, xref.yAxis.magnitude, xref.zAxis.magnitude);

                xRefPosition = this.transform.TransformPoint(xRefPosition);
                xrefRotation = this.transform.rotation * xrefRotation;
                var banger = UnhitBangerInstance.RequestBanger(level, xref.PackageFile, xRefPosition, xrefRotation);

                level.MoveToRoom(banger, level.FindRoomIdWithWarps(xRefPosition));
            }
        }
    }

    private void Load(string basename)
    {
        this.basename = basename;

        var template = GetOrBuildTemplate(basename, BuildTemplateAction);
        if (template == null)
        {
            Debug.LogError($"LevelFixedAny: template '{basename}' failed to build.");
            return;
        }

        instance = template.Instantiate(transform);
        AssignNamedParts();
        renderers.AddRange(this.gameObject.GetComponentsInChildren<Renderer>(true));
    }

    private static void BuildTemplate(PackageObjectLoader loader)
    {
        loader.SetDefaultShaderSelector(WorldShaderFor);

        loader.LoadMainGroup(MainPart, applyPivot: false);
        loader.LoadGroup(ShadowPart, applyPivot: false);
        loader.LoadGroups(ExtraParts);
        
        loader.LoadShaders();
        loader.LoadXrefs();
    }

    private void AssignNamedParts()
    {
        foreach (var part in instance.Parts)
        {
            switch (part.Name)
            {
                case MainPart: main = part.Object; break;
                case ShadowPart: shadow = part.Object; break;
                case "mask": mask = part.Object; break;
                case "nonrandom": nonrandom = part.Object; break;
                case "refl": refl = part.Object; break;
                case "opaque": opaque = part.Object; break;
            }

            if (part.Name != MainPart && part.Object != null)
                part.Object.SetActive(false);
        }
    }

    public override IEnumerable<Renderer> GetRenderers()
    {
        return renderers;
    }

    private void OnDestroy()
    {
        instance?.Destroy();
        instance = null;

        main = shadow = mask = nonrandom = refl = opaque = null;
    }
}