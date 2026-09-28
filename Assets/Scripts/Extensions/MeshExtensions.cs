using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public static class MeshExtensions
{
    private static List<Vector2> twoBuffer = new List<Vector2>(2048);
    private static List<Vector3> threeBuffer = new List<Vector3>(2048);
    private static List<Vector3> threeBuffer2 = new List<Vector3>(2048);
    private static List<int> indexBuffer = new List<int>(4096);
    private static List<int> indexBuffer2 = new List<int>(4096);

    public static void GetUVs(this Mesh msh, int channel, Vector2[] output, int startIndex)
    {
        msh.GetUVs(channel, twoBuffer);
        for(int i=0; i < msh.vertexCount; i++)
        {
            output[i + startIndex] = twoBuffer[i];
        }
    }

    public static void GetVertices(this Mesh msh, Vector3[] output, int startIndex)
    {
        msh.GetVertices(threeBuffer);
        for(int i=0; i < msh.vertexCount; i++)
        {
            output[i + startIndex] = threeBuffer[i];
        }
    }

    public static void GetNormals(this Mesh msh, Vector3[] output, int startIndex)
    {
        msh.GetNormals(threeBuffer);
        for (int i = 0; i < msh.vertexCount; i++)
        {
            output[i + startIndex] = threeBuffer[i];
        }
    }

    public static int GetTriangleCount(this Mesh msh)
    {
        int triCount = 0;
        for(int i=0; i < msh.subMeshCount; i++)
        {
            msh.GetTriangles(indexBuffer, i);
            if((indexBuffer.Count % 3) != 0)
                Debug.LogError("GetTriangleCount is not multiple of 3. The returned number will be incorrect");
            triCount += (indexBuffer.Count / 3);
        }

        return triCount;
    }

    public static void GetTriangles(this Mesh msh, int submesh, int[] output, int startIndex)
    {
        int[] triangles = msh.GetTriangles(submesh);
        for(int i=0; i < triangles.Length; i++)
        {
            output[i + startIndex] = triangles[i];
        }
    }

    public static void GetTrianglesReversed(this Mesh msh, int submesh, int[] output, int startIndex)
    {
        int[] triangles = msh.GetTriangles(submesh);
        int triangleCount = triangles.Length;
        for (int i = 0; i < triangles.Length; i++)
        {
            output[i + startIndex] = triangles[triangleCount - 1 - i];
        }
    }

    public static int[] GetTrianglesReversed(this Mesh msh, int submesh)
    {
        int[] triangles = msh.GetTriangles(submesh);
        int[] trianglesSrc = new int[triangles.Length];
        triangles.CopyTo(trianglesSrc, 0);

        int triangleCount = triangles.Length;

        for (int i = 0; i < triangleCount; i++)
        {
            triangles[i] = trianglesSrc[triangleCount - 1 - i];
        }

        return triangles;
    }

    public static float CalculateSurfaceArea(this Mesh mesh)
    {
        var triangles = mesh.triangles;
        var vertices = mesh.vertices;

        double sum = 0.0;

        for (int i = 0; i < triangles.Length; i += 3)
        {
            Vector3 corner = vertices[triangles[i]];
            Vector3 a = vertices[triangles[i + 1]] - corner;
            Vector3 b = vertices[triangles[i + 2]] - corner;

            sum += Vector3.Cross(a, b).magnitude;
        }

        return (float)(sum / 2.0);
    }

    public static float CalculateVolume(this Mesh msh)
    {
        float volume = 0f;
        Vector3[] verts = msh.vertices;
        int[] tris = msh.triangles;
        for(int i=0; i < tris.Length; i += 3)
        {
            Vector3 p1 = verts[tris[i + 0]];
            Vector3 p2 = verts[tris[i + 1]];
            Vector3 p3 = verts[tris[i + 2]];

            float v321 = p3.x * p2.y * p1.z;
            float v231 = p2.x * p3.y * p1.z;
            float v312 = p3.x * p1.y * p2.z;
            float v132 = p1.x * p3.y * p2.z;
            float v213 = p2.x * p1.y * p3.z;
            float v123 = p1.x * p2.y * p3.z;
            volume += (1.0f / 6.0f) * (-v321 + v231 + v312 - v132 - v213 + v123);
        }
        return volume;
    }

    public static void FlipX(this Mesh msh, bool recalculate = true)
    {
        msh.GetVertices(threeBuffer);  //vertices go to threeBuffer
        msh.GetNormals(threeBuffer2);  //normals go to threeBuffer2
        bool hasNormals = threeBuffer2.Count > 0;

        //flip x
        if (hasNormals)
        {
            for (int i = 0; i < msh.vertexCount; i++)
            {
                threeBuffer[i] = new Vector3(-threeBuffer[i].x, threeBuffer[i].y, threeBuffer[i].z);
                threeBuffer2[i] = new Vector3(-threeBuffer2[i].x, threeBuffer2[i].y, threeBuffer2[i].z);
            }
        }
        else
        {
            for (int i = 0; i < msh.vertexCount; i++)
            {
                threeBuffer[i] = new Vector3(-threeBuffer[i].x, threeBuffer[i].y, threeBuffer[i].z);
            }
        }

        //flip indices
        for(int i=0; i < msh.subMeshCount; i++)
        {
            msh.SetTriangles(msh.GetTrianglesReversed(i), i);
        }

        //set new verts
        msh.SetVertices(threeBuffer);
        if(hasNormals)
            msh.SetNormals(threeBuffer2);

        if (recalculate)
        {
            msh.RecalculateBounds();
            msh.RecalculateTangents();
        }
    }

    public static void FlipXZ(this Mesh mesh)
    {
        Vector3[] vertices = mesh.vertices;

        for (int i = 0; i < vertices.Length; i++)
        {
            vertices[i].x = -vertices[i].x;
            vertices[i].z = -vertices[i].z;
        }

        mesh.vertices = vertices;

        Vector3[] normals = mesh.normals;

        if (normals.Length == vertices.Length)
        {
            for (int i = 0; i < normals.Length; i++)
            {
                normals[i].x = -normals[i].x;
                normals[i].z = -normals[i].z;
            }

            mesh.normals = normals;
        }

        Bounds bounds = mesh.bounds;
        Vector3 min = bounds.min;
        Vector3 max = bounds.max;

        bounds.SetMinMax(
            new Vector3(-max.x, min.y, -max.z),
            new Vector3(-min.x, max.y, -min.z)
        );

        mesh.bounds = bounds;

        if (mesh.tangents.Length == vertices.Length)
            mesh.RecalculateTangents();
    }

    public static void LightTransformed(this Mesh msh, Transform transform, IEnumerable<Light> lights, bool includeAmbient)
    {
        if(msh == null)
        {
            Debug.LogError("Mesh.LightTransform failed. Null mesh??");
            return;
        }

        var lightList = lights.ToList();
        if (lightList.Count == 0)
        {
            lightList.AddRange(GameObject.FindObjectsOfType<Light>());
        }

        if (lightList.Count == 0)
        {
            Debug.LogError("Mesh.Light failed. Cannot find any lights!");
            return;
        }

        msh.GetNormals(threeBuffer);
        msh.GetVertices(threeBuffer2);
        if (threeBuffer.Count == 0)
        {
            Debug.LogError("Mesh.Light failed. Mesh has no normals!");
            return;
        }

        List<Color> colorList = new List<Color>();
        for (int i = 0; i < threeBuffer.Count; i++)
        {
            var vertex = (transform == null) ? threeBuffer2[i] : transform.TransformPoint(threeBuffer2[i]);
            var normal = (transform == null) ? threeBuffer[i] : transform.TransformDirection(threeBuffer[i]);
            float rTotal = 0f;
            float gTotal = 0f;
            float bTotal = 0f;
            foreach (var light in lightList)
            {
                switch (light.type)
                {
                    case LightType.Directional:
                        var lightNormal = -light.transform.forward;
                        var lightDot = Vector3.Dot(lightNormal, normal);
                        if (lightDot > 0f)
                        {
                            float lightIntensity = light.bounceIntensity == 0.101f ? -light.intensity : light.intensity;
                            rTotal += light.color.r * lightDot * lightIntensity;
                            gTotal += light.color.g * lightDot * lightIntensity;
                            bTotal += light.color.b * lightDot * lightIntensity;
                        }
                        break;
                    default:
                        Debug.LogError($"Unsupported Mesh.Light type: {light.type}");
                        break;
                }

            }

            if (includeAmbient)
            {
                rTotal += RenderSettings.ambientLight.r;
                gTotal += RenderSettings.ambientLight.g;
                bTotal += RenderSettings.ambientLight.b;
            }

            rTotal = (rTotal > 1.0f) ? (rTotal - (rTotal - 1.0f)) : rTotal;
            gTotal = (gTotal > 1.0f) ? (gTotal - (gTotal - 1.0f)) : gTotal;
            bTotal = (bTotal > 1.0f) ? (bTotal - (bTotal - 1.0f)) : bTotal;
            rTotal = Mathf.Max(0f, rTotal);
            gTotal = Mathf.Max(0f, gTotal);
            bTotal = Mathf.Max(0f, bTotal);
            colorList.Add(new Color(rTotal, gTotal, bTotal));
        }
        msh.SetColors(colorList);
    }

    public static Mesh ExtractSubmesh(this Mesh msh, int submesh = 0)
    {
        msh.GetVertices(threeBuffer);  //vertices go to threeBuffer
        msh.GetNormals(threeBuffer2);  //normals go to threeBuffer2
        msh.GetUVs(0, twoBuffer); //uvs go to twoBuffer

        List<Vector3> vertices = new List<Vector3>();
        List<Vector3> normals = new List<Vector3>();
        List<Vector2> uvs = new List<Vector2>();

        Dictionary<int, int> indexRemap = new Dictionary<int, int>();
        int[] submeshIndices = msh.GetIndices(submesh);
        int[] newIndices = new int[submeshIndices.Length];

        for(int i=0; i < submeshIndices.Length; i++)
        {
            int index = submeshIndices[i];
            int remappedIndex;

            if (!indexRemap.TryGetValue(index, out remappedIndex))
            {
                remappedIndex = vertices.Count;
                indexRemap[index] = remappedIndex;

                vertices.Add(threeBuffer[index]);

                if(threeBuffer2.Count > 0)
                    normals.Add(threeBuffer2[index]);

                if(twoBuffer.Count > 0)
                    uvs.Add(twoBuffer[index]);
            }

            newIndices[i] = remappedIndex;
        }

        //generate return mesh
        var newMsh = new Mesh
        {
            name = msh.name,
            subMeshCount = 1
        };

        
        newMsh.SetVertices(vertices);
        if(normals.Count > 0)
            newMsh.SetNormals(normals);
        if(uvs.Count > 0)
            newMsh.SetUVs(0, uvs);

        newMsh.RecalculateTangents();
        newMsh.RecalculateBounds();
            
        newMsh.SetIndices(newIndices, MeshTopology.Triangles, 0);

        return newMsh;
    }
}
