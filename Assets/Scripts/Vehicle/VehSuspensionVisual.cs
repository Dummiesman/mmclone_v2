using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

public class VehSuspensionVisual : MonoBehaviour
{
    private Transform carBody;
    private VehWheel wheel;

    private Matrix4x4 pivot;
    private float invReach;
    private bool mode;

    private new MeshRenderer renderer;
    private Mesh mesh;
    private MaterialPropertyBlock props;

    void Awake()
    {
        // Nothing draws until Init succeeds.
        enabled = false;
    }

    public bool Init(Transform carBody, string basename, string name, VehWheel wheel, LODGroup visual)
    {
        enabled = false;

        this.carBody = carBody;
        this.name = name;
        this.wheel = wheel;

        if (this.carBody == null || this.wheel == null || visual == null)
        {
            Debug.LogWarning($"VehSuspension '{name}': missing car body, wheel or LODGroup.", this);
            return false;
        }

        // if (GetPivot(&SuspensionPivot, a3, a4)) { ... } else node.flags &= 0xFE;
        if (!TryGetPivot(basename, this.name, out pivot))
            return false;

        Vector3 axis = pivot.GetColumn(2);   // AS row 2 (m20,m21,m22)
        Vector3 origin = pivot.GetColumn(3); // AS row 3 (m30,m31,m32)

        // 1.0 / dot(WheelCenter - PivotOrigin, Axis)
        float reach = Vector3.Dot(this.wheel.Center - origin, axis);
        if (Mathf.Abs(reach) < 1e-6f)
        {
            Debug.LogWarning($"VehSuspension '{this.name}': degenerate reach — the wheel centre lies on the " +
                             "pivot plane.", this);
            return false;
        }

        invReach = 1f / reach;
        mode = Mathf.Abs(pivot.m12) >= 0.5f;   // Mode = fabs(m21) >= 0.5

        if (!CacheRenderer(visual))
            return false;

        enabled = true;
        return true;
    }

    private static Vector3 FlipZ(Vector3 v) => new Vector3(v.x, v.y, -v.z);
    private static bool TryGetPivot(string basename, string name, out Matrix4x4 pivot)
    {
        pivot = Matrix4x4.identity;

        using (var stream = AssetManager.Open("geometry", $"{basename}_{name}.mtx"))
        {
            if (stream == null) return false;

            using (var reader = new BinaryReader(stream, System.Text.Encoding.ASCII, true))
            {
                Vector3 xaxis = FlipZ(reader.ReadVector3());
                Vector3 yaxis = FlipZ(reader.ReadVector3());
                Vector3 zaxis = -FlipZ(reader.ReadVector3());
                Vector3 offset = FlipZ(reader.ReadVector3());

                pivot.SetColumn(0, new Vector4(xaxis.x, xaxis.y, xaxis.z, 0f));
                pivot.SetColumn(1, new Vector4(yaxis.x, yaxis.y, yaxis.z, 0f));
                pivot.SetColumn(2, new Vector4(zaxis.x, zaxis.y, zaxis.z, 0f));
                pivot.SetColumn(3, new Vector4(offset.x, offset.y, offset.z, 1f));
            }
        }

        return true;
    }

    private bool CacheRenderer(LODGroup visual)
    {
        LOD[] lods = visual.GetLODs();
        if (lods.Length == 0)
        {
            Debug.LogWarning($"VehSuspension '{name}': {visual.name} has no LOD levels.", this);
            return false;
        }

        // Highest detail level.
        foreach (Renderer r in lods[0].renderers)
        {
            var mr = r as MeshRenderer;
            if (mr == null) continue;

            var filter = mr.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null) continue;

            renderer = mr;
            mesh = filter.sharedMesh;
            break;
        }

        if (renderer == null)
        {
            Debug.LogWarning($"VehSuspension '{name}': no MeshRenderer with a mesh on LOD 0 of " +
                             $"{visual.name}.", this);
            return false;
        }

        props = new MaterialPropertyBlock();

        // We draw LOD 0 ourselves and the lower levels are never used.
        foreach (LOD lod in lods)
            foreach (Renderer r in lod.renderers)
                if (r != null) r.enabled = false;

        // Stop the LODGroup from re-enabling them behind our back.
        visual.enabled = false;
        return true;
    }

    // ----------------------------------------------------------------- update

    void LateUpdate()
    {
        UpdateMatrix();
        Draw();
    }

    public void UpdateMatrix()
    {
        float travel = wheel.TargetSuspensionTravel - wheel.GetVisualDispVert();
        float delta = travel * invReach;
        pivot.m12 = mode ? delta - 1f : delta;
    }

    void Draw()
    {
        if (renderer.forceRenderingOff) return;

        // Mesh vertices are expected in pivot-local space, as in the original.
        Matrix4x4 world = carBody.localToWorldMatrix * pivot;

        int layer = renderer.gameObject.layer;
        ShadowCastingMode shadows = renderer.shadowCastingMode;
        bool receive = renderer.receiveShadows;

        LightProbeUsage probes = renderer.lightProbeUsage;
        if (probes == LightProbeUsage.UseProxyVolume) probes = LightProbeUsage.BlendProbes;

        renderer.GetPropertyBlock(props);

        Material[] mats = renderer.sharedMaterials;
        int count = Mathf.Min(mats.Length, mesh.subMeshCount);

        for (int s = 0; s < count; s++)
        {
            if (mats[s] == null) continue;
            Graphics.DrawMesh(mesh, world, mats[s], layer, null, s, props, shadows, receive, null, probes, null);
        }
    }
}