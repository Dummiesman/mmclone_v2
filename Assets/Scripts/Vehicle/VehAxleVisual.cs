using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

public class VehAxleVisual : MonoBehaviour
{
    private Transform carBody;
    private VehWheel leftWheel;
    private VehWheel rightWheel;

    private Matrix4x4 pivot;
    private float invReachZ;
    private float invReachX;

    private new MeshRenderer renderer;
    private Mesh mesh;
    private MaterialPropertyBlock props;

    void Awake()
    {
        // Nothing draws until Init succeeds.
        enabled = false;
    }

    public bool Init(Transform carBody, string basename, string name,
                     VehWheel left, VehWheel right, LODGroup visual)
    {
        enabled = false;

        this.carBody = carBody;
        this.name = name;
        leftWheel = left;
        rightWheel = right;

        if (this.carBody == null || leftWheel == null || rightWheel == null || visual == null)
        {
            Debug.LogWarning($"VehAxleVisual '{name}': missing car body, wheels or LODGroup.", this);
            return false;
        }

        if (!TryGetPivot(basename, this.name, out pivot))
            return false;

        Vector3 axisX = pivot.GetColumn(0);   // AS row 0 (m00,m01,m02)
        Vector3 axisZ = pivot.GetColumn(2);   // AS row 2 (m20,m21,m22)
        Vector3 origin = pivot.GetColumn(3);  // AS row 3 (m30,m31,m32)

        // Both reaches are measured to the LEFT wheel only, as in the original -
        // the axle is assumed symmetric about its pivot.
        Vector3 arm = leftWheel.Center - origin;

        float reachZ = Vector3.Dot(arm, axisZ);
        float reachX = Vector3.Dot(arm, axisX);

        if (Mathf.Abs(reachZ) < 1e-6f || Mathf.Abs(reachX) < 1e-6f)
        {
            Debug.LogWarning($"VehAxleVisual '{this.name}': degenerate reach (z={reachZ}, x={reachX}) — the " +
                             "left wheel centre lies on one of the pivot planes.", this);
            return false;
        }

        invReachZ = 1f / reachZ;
        invReachX = 1f / reachX;

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
            Debug.LogWarning($"VehAxleVisual '{name}': {visual.name} has no LOD levels.", this);
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
            Debug.LogWarning($"VehAxleVisual '{name}': no MeshRenderer with a mesh on LOD 0 of " +
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
        float travelL = leftWheel.TargetSuspensionTravel - leftWheel.GetVisualDispVert();
        float travelR = rightWheel.TargetSuspensionTravel - rightWheel.GetVisualDispVert();

        // Both writes land without a sign fix, unlike vehSuspension's mode-1
        // baseline: the z conversion negates m12 and the z reach together so
        // they cancel, and the x column isn't negated at all.
        pivot.m12 = (travelL + travelR) * invReachZ * 0.5f;
        pivot.m10 = (travelL - travelR) * invReachX * 0.5f;
    }

    void Draw()
    {
        if (renderer.forceRenderingOff) return;

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