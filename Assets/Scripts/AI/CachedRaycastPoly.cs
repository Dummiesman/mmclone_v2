using System.Collections.Generic;
using UnityEngine;

public class CachedRaycastPoly
{
    public bool IsValid { get; private set; } = false;

    private Transform hitObject;
    private int triangleIndex;
    private Vector3[] trianglePoints = new Vector3[3];

    private static readonly List<Vector3> vertexBuf = new List<Vector3>(4096);
    private static readonly List<int> indexBuf = new List<int>(4096 * 3);

    public float CalculateHeightForPoint(Vector3 point)
    {
        var point2d = point.ToVec2XZ();
        float det = (trianglePoints[1].z - trianglePoints[2].z) * (trianglePoints[0].x - trianglePoints[2].x) + (trianglePoints[2].x - trianglePoints[1].x) * (trianglePoints[0].z - trianglePoints[2].z);
        float l1 = ((trianglePoints[1].z - trianglePoints[2].z) * (point2d.x - trianglePoints[2].x) + (trianglePoints[2].x - trianglePoints[1].x) * (point2d.y - trianglePoints[2].z)) / det;
        float l2 = ((trianglePoints[2].z - trianglePoints[0].z) * (point2d.x - trianglePoints[2].x) + (trianglePoints[0].x - trianglePoints[2].x) * (point2d.y - trianglePoints[2].z)) / det;
        float l3 = 1.0f - l1 - l2;
        return l1 * trianglePoints[0].y + l2 * trianglePoints[1].y + l3 * trianglePoints[2].y;
    }

    public bool IsValidForPoint(Vector3 point)
    {
        if (!IsValid)
            return false;
        if (trianglePoints == null || trianglePoints.Length == 0)
            return false;

        float as_x = point.x - trianglePoints[0].x;
        float as_y = point.z - trianglePoints[0].z;

        bool s_ab = (trianglePoints[1].x - trianglePoints[0].x) * as_y - (trianglePoints[1].z - trianglePoints[0].z) * as_x > 0;

        if ((trianglePoints[2].x - trianglePoints[0].x) * as_y - (trianglePoints[2].z - trianglePoints[0].z) * as_x > 0 == s_ab)
         return false;

        if ((trianglePoints[2].x - trianglePoints[1].x) * (point.z - trianglePoints[1].z) - (trianglePoints[2].z - trianglePoints[1].z) * (point.x - trianglePoints[1].x) > 0 != s_ab)
         return false;

        return true;
    }

    public override bool Equals(object obj)
    {
        if(obj is RaycastHit rh)
        {
            return rh.triangleIndex == triangleIndex && rh.transform == hitObject;
        }else if(obj is CachedRaycastPoly cr)
        {
            return cr.hitObject == hitObject && cr.triangleIndex == triangleIndex;
        }
        return false;
    }

    public void Invalidate()
    {
        IsValid = false;
    }

    public void Init(RaycastHit hit)
    {
        hitObject = hit.transform;
        triangleIndex = hit.triangleIndex;

        if (!hit.transform.gameObject.TryGetComponent<MeshCollider>(out var mc))
        {
            IsValid = false;
            Debug.LogError($"CachedRaycastPoly cannot be initialized from {hitObject.name} because it's missing a mesh collider.");
            return;
        }

        var mesh = mc.sharedMesh;
        mesh.GetVertices(vertexBuf);

        //find submesh index
        int localIndex = triangleIndex * 3;

        int submesh;
        for (submesh = 0; submesh < mesh.subMeshCount; submesh++)
        {
            int indexCount = (int)mesh.GetIndexCount(submesh);
            if (indexCount > localIndex)
                break;
            localIndex -= indexCount;
        }

        //get triangle points
        mesh.GetTriangles(indexBuf, submesh);
        trianglePoints[0] = hitObject.TransformPoint(vertexBuf[indexBuf[(localIndex) + 0]]);
        trianglePoints[1] = hitObject.TransformPoint(vertexBuf[indexBuf[(localIndex) + 1]]);
        trianglePoints[2] = hitObject.TransformPoint(vertexBuf[indexBuf[(localIndex) + 2]]);

        //
        IsValid = true;
    }

    public CachedRaycastPoly()
    {
    }

    public CachedRaycastPoly(RaycastHit hit)
    {
        Init(hit);
    }
}
