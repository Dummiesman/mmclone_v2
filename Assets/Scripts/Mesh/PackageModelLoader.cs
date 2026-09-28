using System.Collections.Generic;
using System.IO;
using System;
using UnityEngine.Rendering;
using UnityEngine;

public class PackageModelLoader
{
    private string meshName;
    private BinaryReader reader;

    /// <summary>
    /// Loads the mesh. <paramref name="materialMap"/> maps submesh index -> global shader offset.
    /// Its length always equals mesh.subMeshCount.
    /// </summary>
    public Mesh Load(out int[] materialMap)
    {
        return Load(meshName, reader, out materialMap);
    }

    private Mesh Load(string meshName, BinaryReader r, out int[] materialMap)
    {
        if (r == null) throw new ArgumentNullException(nameof(r));

        int nSections = r.ReadInt32();
        int nVerticesTot = r.ReadInt32();
        r.ReadInt32(); /* nIndicesTot */
        r.ReadInt32(); /* nSections2  */
        var fvf = (D3DFVF)r.ReadInt32();

        if (!fvf.HasPosition())
            throw new InvalidDataException($"PackageModelLoader: FVF 0x{(int)fvf:X} has no position component.");

        bool hasRhw = fvf.HasFlag(D3DFVF.XYZRHW);
        bool hasNormal = fvf.HasFlag(D3DFVF.Normal);
        bool hasDiffuse = fvf.HasFlag(D3DFVF.Diffuse);
        bool hasSpecular = fvf.HasFlag(D3DFVF.Specular);
        int texCount = fvf.TextureCount();

        int cap = Mathf.Max(nVerticesTot, 0);
        var positions = new List<Vector3>(cap);
        var normals = hasNormal ? new List<Vector3>(cap) : null;
        var colors = hasDiffuse ? new List<Color32>(cap) : null;
        var uv0 = texCount > 0 ? new List<Vector2>(cap) : null;
        var uv1 = texCount > 1 ? new List<Vector2>(cap) : null;

        // shaderOffset -> triangle list. Sorted so submesh order is stable and
        // follows ascending shader offset, which keeps draw order deterministic.
        var submeshes = new SortedDictionary<int, List<int>>();

        for (int s = 0; s < nSections; s++)
        {
            int nStrips = r.ReadUInt16();
            /* flags */
            r.ReadUInt16();
            int shaderOffset = r.ReadInt32();

            if (shaderOffset < 0)
                throw new InvalidDataException($"PackageModelLoader: negative shaderOffset {shaderOffset}.");

            if (!submeshes.TryGetValue(shaderOffset, out var triangles))
            {
                triangles = new List<int>();
                submeshes.Add(shaderOffset, triangles);
            }

            for (int p = 0; p < nStrips; p++)
            {

                r.ReadInt32(); /* primType, ignore, only ever tris */
                int nVertices = r.ReadInt32();

                int baseVertex = positions.Count;

                for (int v = 0; v < nVertices; v++)
                {
                    positions.Add(r.ReadVector3Flipped());
                    if (hasRhw) r.ReadSingle();

                    if (hasNormal)
                        normals.Add(r.ReadVector3Flipped());

                    if (hasDiffuse) colors.Add(FromD3DColor(r.ReadUInt32()));
                    if (hasSpecular) r.ReadUInt32();

                    for (int t = 0; t < texCount; t++)
                    {
                        int floats = fvf.TexCoordSize(t);
                        float u = floats > 0 ? r.ReadSingle() : 0f;
                        float vv = floats > 1 ? r.ReadSingle() : 0f;
                        for (int extra = 2; extra < floats; extra++) r.ReadSingle();

                        if (t == 0) uv0.Add(new Vector2(u, 1.0f - vv));
                        else if (t == 1) uv1.Add(new Vector2(u, 1.0f - vv));
                    }
                }

                int nIndices = r.ReadInt32();
                for (int i = 0; i < nIndices; i++)
                    triangles.Add(baseVertex + r.ReadUInt16());
            }
        }

        var mesh = new Mesh { name = meshName };
        if (positions.Count > 65535)
            mesh.indexFormat = IndexFormat.UInt32;

        mesh.SetVertices(positions);
        if (normals != null) mesh.SetNormals(normals);
        if (colors != null) mesh.SetColors(colors);
        if (uv0 != null) mesh.SetUVs(0, uv0);
        if (uv1 != null) mesh.SetUVs(1, uv1);

        if (submeshes.Count == 0)
        {
            // Degenerate model: keep one empty submesh so the invariant
            // materialMap.Length == mesh.subMeshCount always holds.
            mesh.subMeshCount = 1;
            materialMap = new int[] { 0 };
        }
        else
        {
            mesh.subMeshCount = submeshes.Count;
            materialMap = new int[submeshes.Count];

            int sub = 0;
            foreach (var kv in submeshes)
            {
                var triangles = kv.Value;
                triangles.Reverse();
                mesh.SetTriangles(triangles, sub, false);
                materialMap[sub] = kv.Key;
                sub++;
            }
        }

        if (normals == null) mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        return mesh;
    }

    private static Color32 FromD3DColor(uint argb)
    {
        byte a = (byte)((argb >> 24) & 0xFF);
        byte r = (byte)((argb >> 16) & 0xFF);
        byte g = (byte)((argb >> 8) & 0xFF);
        byte b = (byte)(argb & 0xFF);
        return new Color32(r, g, b, a);
    }

    public PackageModelLoader(string name, BinaryReader reader)
    {
        this.reader = reader;
        this.meshName = name;
    }
}