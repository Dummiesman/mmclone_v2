using System;
using UnityEngine;

public class DecalDrawer
{
    private static string decalShaderName = "Custom/Decal";
    private static Shader decalShader;

    static int[] StripToIndex(int start, int length, bool flip)
    {
        // ignore those that can't form a triangle
        if (length < 3) return new int[0];

        // calculate number of index needed for given length
        int total = (length - 2) * 3;
        int[] index = new int[total];

        // loop all triangle to create index list
        int count = 0;
        int max = 0;
        //bool flip = false;
        for (int i = 2; i < length; i++)
        {
            // to ensure all triangle are clockwise, 
            // triangle strip alternate between the points.
            // we emulate this opengl behavior with the following codes.
            if (flip)
            {
                index[count] = start + i - 1; count++;
                index[count] = start + i - 2; count++;
            }
            else
            {
                index[count] = start + i - 2; count++;
                index[count] = start + i - 1; count++;
            }
            flip = !flip;
            // last point is always latest point.
            max = index[count] = start + i; count++;
        }
        return index;
    }


    static Material CreateDecalMaterial(string name)
    {
        if (decalShader == null) decalShader = Shader.Find(decalShaderName);

        var mat = new Material(decalShader)
        {
            name = name
        };

        if (!string.IsNullOrEmpty(name))
        {
            var tex = TextureCache.Get(name);
            mat.mainTexture = tex;

            if (tex != null)
            {
                if ((tex.Flags & AGETexFlags.Transparent) != 0)
                    mat.EnableKeyword("ALPHA");
                if ((tex.Flags & AGETexFlags.AnyCloudShadows) != 0)
                    mat.EnableKeyword("SHADOWMAP");
            }
        }

        return mat;
    }

    static Vector2[] BuildStripUvs(Vector3[] verts)
    {
        int n = verts.Length;
        var uvs = new Vector2[n];
        if (n < 2) return uvs;

        // width sampled once from the first pair, as in the ctor
        float width = Vector3.Distance(verts[0], verts[1]);
        if (width < 0.0001f) return uvs;

        int rows = (n + 1) / 2;
        var v = new float[rows];

        for (int r = 0; r < rows; r++)
        {
            // absolute distance from the origin vertex, quantized to whole tiles
            float d = Vector3.Distance(verts[0], verts[r * 2]);
            v[r] = Mathf.Floor(d / width + 0.5f);
        }

        for (int i = 0; i < n; i++)
            uvs[i] = new Vector2((i & 1) == 0 ? 0f : 1f, v[i >> 1]);

        return uvs;
    }

    public static void RedrawDecal(PathSet.Path path, GameObject target)
    {
        if (path.Points.Count <= 2)
            return;

        //get vertex data
        Vector3[] stripVerts = path.Points.ToArray();

        //prepare the rest of the data
        Vector2[] stripUvs = BuildStripUvs(stripVerts);
        Material material = CreateDecalMaterial(path.Name);

        //assign to mesh
        var indices = StripToIndex(0, stripVerts.Length, false);
        Array.Reverse(indices);
        Mesh msh = new Mesh()
        {
            vertices = stripVerts,
            uv = stripUvs,
            triangles = indices
        };

        //finalize mesh
        msh.RecalculateBounds();
        msh.RecalculateNormals();

        //assign to object
        var renderer = target.GetComponent<MeshRenderer>();
        var filter = target.GetComponent<MeshFilter>();

        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.sharedMaterial = material;
        filter.sharedMesh = msh;
    }

    public static GameObject DrawDecal(PathSet.Path path)
    {
        //create object
        GameObject decalInstance = new GameObject(path.Name, typeof(MeshRenderer), typeof(MeshFilter));
        RedrawDecal(path, decalInstance);

        return decalInstance;
    }
}
