using MM2.AI;
using UnityEngine;

public class CableCarInstance : UnhitBangerInstance
{
    private const float ShadowLift = 0.02f;
    private static readonly Quaternion ModelFlip = Quaternion.Euler(0f, 180f, 0f);

    private AICableCar car;
    private GameObject shadow, hlight;

    public AICableCar Car => car;

    private static readonly ShaderSelector WorldShaderFor =
        (entry, mainTex) => WorldShaderVariants.Select(entry.Diffuse, mainTex);

    public static CableCarInstance Create(SDLCity level, AICableCar car, string basename)
    {
        var inst = new GameObject(basename).AddComponent<CableCarInstance>();
        inst.car = car;
        inst.Init(level, basename);
        return inst;
    }

    public override void Init(SDLCity level, string basename)
    {
        base.Init(level, basename, Vector3.zero, Quaternion.identity, Vector3.one); // init the banger part
        SetVariant(Random.Range(0, Mathf.Max(1, VariantCount)));

        // moves between rooms on its own
        Flags &= ~(LevelInstanceFlags.Static | LevelInstanceFlags.DisableWhenRoomHidden);

        SyncPose();
    }

    protected override bool ShouldInstantiatePart(string partName)
    {
        return true;
    }

    protected override void BuildTemplate(PackageObjectLoader loader)
    {
        if (DataIndex >= 0 &&
            Level.BangerDataManager.GetEntry(DataIndex).BillFlags.HasFlag(BangerDataFlags.Unlit))
        {
            loader.SetLightEnable(false);
        }

        loader.SetDefaultShaderSelector(WorldShaderFor);

        loader.LoadGroup("body", applyPivot: false);
        loader.LoadGroup("shadow", applyPivot: false);
        loader.LoadGroup("hlight", applyPivot: false);

        loader.LoadShaders();
    }

    protected override void AssignNamedParts(PackageObjectInstance instance)
    {
        foreach (var part in instance.Parts)
        {
            switch (part.Name)
            {
                case "body": part.Object.SetActive(true); break; // base only keeps "main" visible
                case "shadow": shadow = part.Object; break;
                case "hlight": hlight = part.Object; break;
            }
        }
    }

    protected override void Update()
    {
        base.Update();

        if (Level != null && car != null && car.RoomID != RoomID)
            Level.MoveToRoom(this, car.Active ? car.RoomID : 0);

        SetActiveSafe(hlight, GameState.SelectedTimeOfDay == MMTimeOfDay.Night);
    }

    private void LateUpdate()
    {
        SyncPose();
        UpdateShadow();
    }

    public override void Reset()
    {
        base.Reset();
        SyncPose();
    }

    private void SyncPose()
    {
        if (car == null) return;

        var rot = car.Rotation * ModelFlip;
        var pos = car.Position;
        transform.SetPositionAndRotation(pos, rot);
    }

    private void UpdateShadow()
    {
        if (shadow == null) return;

        if (!ProbeGround(transform.position + Vector3.up, out var point, out var normal))
        {
            SetActiveSafe(shadow, false);
            return;
        }

        Vector3 forward = Vector3.ProjectOnPlane(transform.forward, normal);
        if (forward.sqrMagnitude < 1e-6f) return;

        SetActiveSafe(shadow, true);
        shadow.transform.SetPositionAndRotation(
            point + normal * ShadowLift,
            Quaternion.LookRotation(forward.normalized, normal));
    }

    private static void SetActiveSafe(GameObject obj, bool active)
    {
        if (obj != null && obj.activeSelf != active)
            obj.SetActive(active);
    }
}