using PSDL;
using PSDL.Elements;
using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Rendering;
using static PSDL.Elements.TunnelElement;

public class SDLBuilder : IDisposable
{
    //private const string shaderName = "Custom/MMVertexLitShadowMapped";
    private const string shaderName = "Custom/MMVertexColorLitShadowMapped";

    // mesh things
    private const float SidewalkOffsetHeight = 0.15f;
    private static readonly MeshUpdateFlags UpdateFlags = MeshUpdateFlags.DontNotifyMeshUsers | MeshUpdateFlags.DontResetBoneBounds | MeshUpdateFlags.DontValidateIndices;

    private static readonly VertexAttributeDescriptor[] SDLVertexLayout = new[]
    {
            new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.Float32, 4),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2),
        };

    private static readonly VertexAttributeDescriptor[] CollisionVertexLayout = new[]
    {
            new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3)
        };

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct SDLVertex
    {
        public Vector3 pos;
        public Vector3 normal;
        public Vector4 color;
        public Vector2 uv;
    }

    // 
    private Shader shader;
    private Material blankMaterial;
    private Material[] materialCache;
    private PSDLFile psdl;
    private SDLCity city;
    private Color cachedUpColor;

    // directional lights, read once - Light/Transform properties are native calls and far too
    // slow to touch per vertex. Lighting doesn't change while a city is being built.
    private Vector3[] lightDirs = Array.Empty<Vector3>();   // towards the light
    private Vector3[] lightColors = Array.Empty<Vector3>(); // color * intensity
    private Vector3 ambientLight;

    // ---- async collider cooking ------------------------------------------------
    private struct BakeMeshJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<int> MeshIds;
        public void Execute(int i) => Physics.BakeMesh(MeshIds[i], false);
    }

    private readonly List<MeshCollider> pendingColliders = new List<MeshCollider>();
    private readonly List<Mesh> pendingColliderMeshes = new List<Mesh>();
    private readonly List<bool> pendingNeedsBound = new List<bool>();

    private NativeArray<int> colliderBakeIds;
    private JobHandle colliderBakeHandle;
    private bool colliderBakeScheduled;

    /// <summary>True once the scheduled bake has finished and CompleteColliderBake won't block.</summary>
    public bool ColliderBakeDone => !colliderBakeScheduled || colliderBakeHandle.IsCompleted;

    // Helpers
    private const int StripMapBufferCount = 8;
    private int stripMapBufferIndex = 0;
    private Vector2[][] stripMapBuffers = CreateStripMapBuffers();

    private static Vector2[][] CreateStripMapBuffers()
    {
        var buffers = new Vector2[StripMapBufferCount][];
        for (int i = 0; i < StripMapBufferCount; i++)
            buffers[i] = new Vector2[64];
        return buffers;
    }

    private Vector2[] RentStripBuffer(int uvCount)
    {
        int bufferIndex = stripMapBufferIndex;
        if (stripMapBuffers[bufferIndex].Length < uvCount)
            stripMapBuffers[bufferIndex] = new Vector2[Mathf.NextPowerOfTwo(uvCount)];
        stripMapBufferIndex = (stripMapBufferIndex + 1) % StripMapBufferCount;
        return stripMapBuffers[bufferIndex];
    }

    private Vector2[] StripMap(System.Collections.Generic.IList<Vertex> verts, int stride,
                           int leftIndexOffset, int rightIndexOffset,
                           float lengthScale = 1f, float widthScale = 1f)
    {
        int vertexCount = verts.Count;
        int limit = vertexCount - stride;
        int uvCount = (limit / stride + 1) * 2;

        var buf = RentStripBuffer((limit / stride + 1) * 2);
        float lastV = 0f;
        int uvCounter = 0;

        for (int i = 0; i < limit; i += stride)
        {
            var leftVertex = verts[i + leftIndexOffset];
            var rightVertex = verts[i + rightIndexOffset];
            var leftVertexNext = verts[i + leftIndexOffset + stride];

            float len = leftVertex.Distance(leftVertexNext);
            float width = leftVertex.FlatDistance(rightVertex);

            buf[uvCounter] = new Vector2(lastV, 0f);
            buf[uvCounter + 1] = new Vector2(lastV, widthScale);
            uvCounter += 2;

            lastV += width > 1e-6f ? (len / width) * lengthScale : 0f;
        }

        buf[uvCounter] = new Vector2(lastV, 0f);
        buf[uvCounter + 1] = new Vector2(lastV, widthScale);

        return buf;
    }

    private Vector2[] VerticalStripMap(System.Collections.Generic.IList<Vertex> verts, int stride,
                                   int indexOffset, float height)
    {
        int vertexCount = verts.Count;
        int limit = vertexCount - stride;
        int uvCount = (limit / stride + 1) * 2;

        var buf = RentStripBuffer((limit / stride + 1) * 2);
        float lastU = 0f;
        int uvCounter = 0;

        for (int i = 0; i < limit; i += stride)
        {
            var current = verts[i + indexOffset];
            var next = verts[i + indexOffset + stride];

            buf[uvCounter] = new Vector2(lastU, 1f);
            buf[uvCounter + 1] = new Vector2(lastU, 0f);
            uvCounter += 2;

            lastU += height > 1e-6f ? current.Distance(next) / height : 0f;
        }

        buf[uvCounter] = new Vector2(lastU, 1f);
        buf[uvCounter + 1] = new Vector2(lastU, 0f);

        return buf;
    }

    private void CacheLights()
    {
        var dirs = new List<Vector3>();
        var colors = new List<Vector3>();

        foreach (var light in city.Lighting.ActiveLights)
        {
            if (light == null || !light.enabled || light.type != LightType.Directional)
                continue;

            dirs.Add(-light.transform.forward);
            Color c = light.color * light.intensity;
            colors.Add(new Vector3(c.r, c.g, c.b));
        }

        lightDirs = dirs.ToArray();
        lightColors = colors.ToArray();

        Color ambientSky = city.Lighting.AmbientColor;
        ambientLight = new Vector3(ambientSky.r, ambientSky.g, ambientSky.b);
    }

    /// <summary>Expects a unit-length normal.</summary>
    private Color CalculateLightingForNormal(Vector3 normal)
    {
        Vector3 lighting = ambientLight;

        for (int i = 0; i < lightDirs.Length; i++)
        {
            float diffuse = Vector3.Dot(lightDirs[i], normal);
            if (diffuse > 0f)
                lighting += lightColors[i] * diffuse;
        }

        return new Color(
            Mathf.Clamp01(lighting.x),
            Mathf.Clamp01(lighting.y),
            Mathf.Clamp01(lighting.z),
            1f);
    }

    /// <summary>
    /// Per-vertex lighting from each vertex's normal. Every caller runs CalculateNormals first,
    /// which leaves the normals unit length, so they aren't renormalized here.
    /// </summary>
    private void BakeLighting(NativeArray<SDLVertex> verts)
    {
        for (int i = 0; i < verts.Length; i++)
        {
            var v = verts[i];
            v.color = CalculateLightingForNormal(v.normal);
            verts[i] = v;
        }
    }

    private void BakeFlatLighting(NativeArray<SDLVertex> verts)
    {
        var color = cachedUpColor;
        for (int i = 0; i < verts.Length; i++)
        {
            var v = verts[i];
            v.color = color;
            verts[i] = v;
        }
    }

    private void BakeUniformLighting(NativeArray<SDLVertex> verts, Vector3 normal)
    {
        var color = CalculateLightingForNormal(normal.normalized);
        for (int i = 0; i < verts.Length; i++)
        {
            var v = verts[i];
            v.color = color;
            verts[i] = v;
        }
    }

    private static void CalculateNormals(NativeArray<SDLVertex> verts, NativeArray<ushort> indices)
    {
        for (int i = 0; i < verts.Length; i++)
        {
            var v = verts[i];
            v.normal = Vector3.zero;
            verts[i] = v;
        }

        for (int i = 0; i < indices.Length; i += 3)
        {
            int a = indices[i], b = indices[i + 1], c = indices[i + 2];
            var faceNormal = Vector3.Cross(verts[b].pos - verts[a].pos, verts[c].pos - verts[a].pos);

            var va = verts[a]; va.normal += faceNormal; verts[a] = va;
            var vb = verts[b]; vb.normal += faceNormal; verts[b] = vb;
            var vc = verts[c]; vc.normal += faceNormal; verts[c] = vc;
        }

        for (int i = 0; i < verts.Length; i++)
        {
            var v = verts[i];
            v.normal = v.normal.sqrMagnitude > 1e-12f ? v.normal.normalized : Vector3.up;
            verts[i] = v;
        }
    }

    public static bool ElementTexturesAreValid(ISDLElement element)
    {
        if (element.Textures == null || element.Textures.Length == 0 || element.Textures[0] < 0)
        {
            return false;
        }
        return true;
    }

    private static Vector2 GenerateWorldspaceUV(Vector3 vec, float deltaS, float deltaT, float scale = 0.125f)
    {
        return new Vector2((vec.x * scale) - deltaS, (vec.z * scale) - deltaT);
    }

    private Material GetOrCreateMaterial(int index)
    {
        if (index < 0)
        {
            if (blankMaterial == null)
            {
                if (shader == null) shader = Shader.Find(shaderName);
                blankMaterial = new Material(shader)
                {
                    name = "null"
                };
            }
            return blankMaterial;
        }
        else
        {
            if (materialCache[index] != null)
            {
                return materialCache[index];
            }
            else
            {

                if (shader == null) shader = Shader.Find(shaderName);

                string name = psdl.GetTextureFromCache(index);
                int dashIndex = name.IndexOf('-');
                if (dashIndex >= 0)
                {
                    name = name.Substring(0, dashIndex); // remove animated texture suffix
                }

                var material = new Material(shader)
                {
                    name = name
                };

                var texture = TextureCache.Get(name);
                material.mainTexture = texture;

                if (texture != null)
                {
                    bool shouldShadowMap = (texture.Flags.HasFlag(AGETexFlags.CloudShadowsHigh) || texture.Flags.HasFlag(AGETexFlags.CloudShadowsLow));
                    if (shouldShadowMap)
                        material.EnableKeyword("SHADOWMAP");
                    else
                        material.DisableKeyword("SHADOWMAP");
                }

                materialCache[index] = material;
                return material;
            }
        }
    }

    private static void WriteTri(NativeArray<ushort> indices, int at, int a, int b, int c)
    {
        indices[at] = (ushort)a;
        indices[at + 1] = (ushort)b;
        indices[at + 2] = (ushort)c;
    }

    private static void WriteStripQuad(NativeArray<ushort> indices, int at, int b, int n)
    {
        WriteTri(indices, at, b, n, n + 1);
        WriteTri(indices, at + 3, b, n + 1, b + 1);
    }

    private static void WriteCapQuad(NativeArray<ushort> indices, int at, int c)
    {
        WriteTri(indices, at, c, c + 1, c + 2);
        WriteTri(indices, at + 3, c, c + 2, c + 3);
    }

    private static void SetTriSubMesh(Mesh.MeshData data, int subMesh, int indexStart, int indexCount, int vertexCount)
    {
        data.SetSubMesh(subMesh, new SubMeshDescriptor()
        {
            baseVertex = 0,
            firstVertex = 0,
            indexStart = indexStart,
            indexCount = indexCount,
            topology = MeshTopology.Triangles,
            vertexCount = vertexCount
        }, UpdateFlags);
    }

    private static IRoad FindTunnelRoadElement(Room room, int tunnelIndex)
    {
        int limit = Mathf.Min(room.Elements.Count, tunnelIndex + 3);
        for (int i = tunnelIndex + 1; i < limit; i++)
        {
            var candidate = room.Elements[i] as IRoad;
            if (candidate != null && candidate.RowBreadth > 0)
                return candidate;
        }
        return null;
    }

    private static Vector3 FlipVertex(Vertex v) => new Vector3(-v.x, v.y, v.z);

    private static float FlatDistance(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x;
        float dz = a.z - b.z;
        return Mathf.Sqrt((dx * dx) + (dz * dz));
    }

    private static Vector3 FlatDirectionBetween(Vector3 from, Vector3 to)
    {
        var dir = new Vector3(to.x - from.x, 0f, to.z - from.z);
        return dir.sqrMagnitude > 1e-12f ? dir.normalized : Vector3.zero;
    }

    private static Vertex GetPerimeterVertex(Room room, int index) => room.Perimeter[index].Vertex;

    private static bool TriangleFanValid(IList<Vertex> verts)
    {
        if (verts.Count < 3)
            return false;

        var a = verts[0];

        for (int i = 1; i < verts.Count - 1; i++)
        {
            var b = verts[i];
            var c = verts[i + 1];

            var ab = new Vector3(
                -(b.x - a.x),
                 b.y - a.y,
                 b.z - a.z);

            var ac = new Vector3(
                -(c.x - a.x),
                 c.y - a.y,
                 c.z - a.z);

            if (Vector3.Cross(ab, ac).sqrMagnitude > 1e-10f)
                return true;
        }

        return false;
    }

    // Generators
    private void DrawFanCore(ref Mesh.MeshDataArray dataArray, int dataIndex,
                         System.Collections.Generic.IList<Vertex> verts, bool smoothNormals)
    {
        int vertexCount = verts.Count;
        int indexCount = (vertexCount - 2) * 3;

        var data = dataArray[dataIndex];
        data.SetVertexBufferParams(vertexCount, SDLVertexLayout);
        data.SetIndexBufferParams(indexCount, IndexFormat.UInt16);

        var sdlVertices = data.GetVertexData<SDLVertex>();
        var sdlIndices = data.GetIndexData<ushort>();

        float deltaS = Mathf.Floor(-verts[0].x * 0.125f);
        float deltaT = Mathf.Floor(verts[0].z * 0.125f);

        for (int i = 0; i < vertexCount; i++)
        {
            var p = new Vector3(-verts[i].x, verts[i].y, verts[i].z);
            sdlVertices[i] = new SDLVertex()
            {
                pos = p,
                uv = GenerateWorldspaceUV(p, deltaS, deltaT),
                normal = Vector3.up
            };
        }

        int vs1 = vertexCount - 1;
        for (int i = 1; i < vs1; i++)
        {
            int indexBase = (i - 1) * 3;
            sdlIndices[indexBase] = 0;
            sdlIndices[indexBase + 1] = (ushort)(i + 1);
            sdlIndices[indexBase + 2] = (ushort)i;
        }

        if (smoothNormals)
        {
            CalculateNormals(sdlVertices, sdlIndices);
            BakeLighting(sdlVertices);
        }
        else
        {
            BakeFlatLighting(sdlVertices);
        }

        data.subMeshCount = 1;
        data.SetSubMesh(0, new SubMeshDescriptor() { baseVertex = 0, firstVertex = 0, indexCount = sdlIndices.Length, indexStart = 0, topology = MeshTopology.Triangles, vertexCount = sdlVertices.Length }, UpdateFlags);
    }

    private void DrawTriangleFan(ref Mesh.MeshDataArray dataArray, int dataIndex, TriangleFanElement fan)
        => DrawFanCore(ref dataArray, dataIndex, fan.Vertices, true);

    private void DrawCulledTriangleFan(ref Mesh.MeshDataArray dataArray, int dataIndex, CulledTriangleFanElement fan)
        => DrawFanCore(ref dataArray, dataIndex, fan.Vertices, false);

    private void DrawRoofTriangleFan(ref Mesh.MeshDataArray dataArray, int dataIndex, RoofTriangleFanElement fan)
    {
        //generate data
        int vertexCount = fan.Vertices.Count;
        int indexCount = (fan.Vertices.Count - 2) * 3;

        var data = dataArray[dataIndex];

        data.SetVertexBufferParams(vertexCount, SDLVertexLayout);
        data.SetIndexBufferParams(indexCount, IndexFormat.UInt16);

        var sdlVertices = data.GetVertexData<SDLVertex>();
        var sdlIndices = data.GetIndexData<ushort>();

        float deltaS = Mathf.Floor(-fan.Vertices[0].x * 0.125f);
        float deltaT = Mathf.Floor(fan.Vertices[0].z * 0.125f);
        int vs1 = fan.Vertices.Count - 1; // < micro optimization
        for (int i = 0; i < fan.Vertices.Count; i++)
        {
            var vertexPosition = new Vector3(-fan.Vertices[i].x, fan.Height, fan.Vertices[i].z);
            sdlVertices[i] = new SDLVertex()
            {
                pos = vertexPosition,
                uv = GenerateWorldspaceUV(vertexPosition, deltaS, deltaT),
                normal = Vector3.up
            };

            if (i > 0 && i != vs1)
            {
                int indexBase = (i - 1) * 3;
                sdlIndices[indexBase] = 0;
                sdlIndices[indexBase + 1] = (ushort)(i + 1);
                sdlIndices[indexBase + 2] = (ushort)i;
            }
        }

        BakeFlatLighting(sdlVertices);

        data.subMeshCount = 1;
        data.SetSubMesh(0, new SubMeshDescriptor() { baseVertex = 0, firstVertex = 0, indexCount = sdlIndices.Length, indexStart = 0, topology = MeshTopology.Triangles, vertexCount = sdlVertices.Length }, UpdateFlags);
    }

    private void DrawFacade(ref Mesh.MeshDataArray dataArray, int dataIndex, FacadeElement facade)
    {
        var data = dataArray[dataIndex];

        data.SetVertexBufferParams(4, SDLVertexLayout);
        data.SetIndexBufferParams(6, IndexFormat.UInt16);

        var sdlVertices = data.GetVertexData<SDLVertex>();
        var sdlIndices = data.GetIndexData<ushort>();

        float topHeight = facade.TopHeight;
        float bottomHeight = facade.BottomHeight;
        float uTiling = facade.UTiling;
        float vTiling = facade.VTiling;

        var leftVert = facade.Vertices[0];
        var rightVert = facade.Vertices[1];

        var bottomRight = new Vector3(-rightVert.x, bottomHeight, rightVert.z);
        var bottomLeft = new Vector3(-leftVert.x, bottomHeight, leftVert.z);
        var topLeft = new Vector3(-leftVert.x, topHeight, leftVert.z);
        var topRight = new Vector3(-rightVert.x, topHeight, rightVert.z);

        var normal = Vector3.Cross(bottomLeft - bottomRight, topLeft - bottomRight);
        normal = normal.sqrMagnitude > 1e-12f ? normal.normalized : Vector3.up;

        sdlVertices[0] = new SDLVertex() { pos = bottomRight, uv = new Vector2(uTiling, vTiling), normal = normal };
        sdlVertices[1] = new SDLVertex() { pos = bottomLeft, uv = new Vector2(0f, vTiling), normal = normal };
        sdlVertices[2] = new SDLVertex() { pos = topLeft, uv = new Vector2(0f, 0f), normal = normal };
        sdlVertices[3] = new SDLVertex() { pos = topRight, uv = new Vector2(uTiling, 0f), normal = normal };

        sdlIndices[0] = 0; sdlIndices[1] = 1; sdlIndices[2] = 2;
        sdlIndices[3] = 0; sdlIndices[4] = 2; sdlIndices[5] = 3;

        BakeUniformLighting(sdlVertices, normal);

        data.subMeshCount = 1;
        data.SetSubMesh(0, new SubMeshDescriptor() { baseVertex = 0, firstVertex = 0, indexCount = sdlIndices.Length, indexStart = 0, topology = MeshTopology.Triangles, vertexCount = sdlVertices.Length }, UpdateFlags);
    }

    private void DrawWalkway(ref Mesh.MeshDataArray dataArray, int dataIndex, WalkwayElement walkway)
    {
        var verts = walkway.Vertices;
        int rowCount = verts.Count / 2;
        int vertexCount = rowCount * 2;
        int indexCount = (rowCount - 1) * 6;

        var data = dataArray[dataIndex];
        data.SetVertexBufferParams(vertexCount, SDLVertexLayout);
        data.SetIndexBufferParams(indexCount, IndexFormat.UInt16);

        var sdlVertices = data.GetVertexData<SDLVertex>();
        var sdlIndices = data.GetIndexData<ushort>();

        var surfaceUvs = StripMap(verts, 2, 0, 1);

        for (int i = 0; i < rowCount; i++)
        {
            int indexBase = i * 2;

            var left = verts[indexBase];
            var right = verts[indexBase + 1];

            sdlVertices[indexBase] = new SDLVertex()
            {
                pos = new Vector3(-left.x, left.y, left.z),
                uv = surfaceUvs[indexBase],
                normal = Vector3.up
            };
            sdlVertices[indexBase + 1] = new SDLVertex()
            {
                pos = new Vector3(-right.x, right.y, right.z),
                uv = surfaceUvs[indexBase + 1],
                normal = Vector3.up
            };
        }

        int indexCounter = 0;
        for (int i = 0; i < rowCount - 1; i++)
        {
            int indexBase = i * 2;
            int indexBaseNext = indexBase + 2;

            WriteStripQuad(sdlIndices, indexCounter, indexBase, indexBaseNext);

            indexCounter += 6;
        }

        CalculateNormals(sdlVertices, sdlIndices);
        BakeLighting(sdlVertices);

        data.subMeshCount = 1;
        data.SetSubMesh(0, new SubMeshDescriptor() { baseVertex = 0, firstVertex = 0, indexCount = sdlIndices.Length, indexStart = 0, topology = MeshTopology.Triangles, vertexCount = sdlVertices.Length }, UpdateFlags);
    }

    private void DrawRoad(ref Mesh.MeshDataArray dataArray, int dataIndex, RoadElement road)
    {
        var verts = road.Vertices;
        int rowCount = verts.Count / 4;

        int vertexCount = rowCount * 11;
        int sidewalkIndexCount = (rowCount - 1) * 24;
        int surfaceIndexCount = (rowCount - 1) * 12;
        int indexCount = sidewalkIndexCount + surfaceIndexCount;

        var data = dataArray[dataIndex];
        data.SetVertexBufferParams(vertexCount, SDLVertexLayout);
        data.SetIndexBufferParams(indexCount, IndexFormat.UInt16);

        var sdlVertices = data.GetVertexData<SDLVertex>();
        var sdlIndices = data.GetIndexData<ushort>();

        var surfaceUvs = StripMap(verts, 4, 1, 2);
        var sidewalkLeftUvs = StripMap(verts, 4, 0, 1);
        var sidewalkRightUvs = StripMap(verts, 4, 2, 3);

        for (int i = 0; i < rowCount; i++)
        {
            int indexBase = i * 4;
            int stripIndexBase = i * 2;
            int v = i * 11;

            var swLeftLeftSrc = verts[indexBase];
            var swLeftRightSrc = verts[indexBase + 1];
            var swRightLeftSrc = verts[indexBase + 2];
            var swRightRightSrc = verts[indexBase + 3];

            var swLeftLeft = new Vector3(-swLeftLeftSrc.x, swLeftLeftSrc.y, swLeftLeftSrc.z);
            var swLeftRight = new Vector3(-swLeftRightSrc.x, swLeftRightSrc.y, swLeftRightSrc.z);
            if (!swLeftLeftSrc.Equals(swLeftRightSrc))
                swLeftRight.y += SidewalkOffsetHeight;

            var roadLeft = new Vector3(-swLeftRightSrc.x, swLeftRightSrc.y, swLeftRightSrc.z);
            var roadRight = new Vector3(-swRightLeftSrc.x, swRightLeftSrc.y, swRightLeftSrc.z);
            var roadCenter = Vector3.Lerp(roadLeft, roadRight, 0.5f);

            var swRightLeft = new Vector3(-swRightLeftSrc.x, swRightLeftSrc.y, swRightLeftSrc.z);
            var swRightRight = new Vector3(-swRightRightSrc.x, swRightRightSrc.y, swRightRightSrc.z);
            if (!swRightLeftSrc.Equals(swRightRightSrc))
                swRightLeft.y += SidewalkOffsetHeight;

            // LEFT SIDEWALK
            sdlVertices[v] = new SDLVertex() { pos = swLeftLeft, uv = sidewalkLeftUvs[stripIndexBase] };
            sdlVertices[v + 1] = new SDLVertex() { pos = swLeftRight, uv = sidewalkLeftUvs[stripIndexBase + 1] };
            sdlVertices[v + 2] = new SDLVertex() { pos = swLeftRight, uv = sidewalkLeftUvs[stripIndexBase + 1] };
            sdlVertices[v + 3] = new SDLVertex() { pos = roadLeft, uv = sidewalkLeftUvs[stripIndexBase + 1] };

            // SURFACE
            sdlVertices[v + 4] = new SDLVertex() { pos = roadLeft, uv = surfaceUvs[stripIndexBase] };
            sdlVertices[v + 5] = new SDLVertex() { pos = roadCenter, uv = surfaceUvs[stripIndexBase + 1] };
            sdlVertices[v + 6] = new SDLVertex() { pos = roadRight, uv = surfaceUvs[stripIndexBase] };

            // RIGHT SIDEWALK
            sdlVertices[v + 7] = new SDLVertex() { pos = roadRight, uv = sidewalkRightUvs[stripIndexBase + 1] };
            sdlVertices[v + 8] = new SDLVertex() { pos = swRightLeft, uv = sidewalkRightUvs[stripIndexBase + 1] };
            sdlVertices[v + 9] = new SDLVertex() { pos = swRightLeft, uv = sidewalkRightUvs[stripIndexBase + 1] };
            sdlVertices[v + 10] = new SDLVertex() { pos = swRightRight, uv = sidewalkRightUvs[stripIndexBase] };
        }

        int sidewalkCounter = 0;
        int surfaceCounter = sidewalkIndexCount;

        for (int i = 0; i < rowCount - 1; i++)
        {
            int b = i * 11;
            int n = b + 11;

            // Sidewalk 1
            WriteStripQuad(sdlIndices, sidewalkCounter, b, n);

            // Sidewalk 2
            WriteStripQuad(sdlIndices, sidewalkCounter + 6, b + 2, n + 2);

            // Sidewalk 3
            WriteStripQuad(sdlIndices, sidewalkCounter + 12, b + 7, n + 7);

            // Sidewalk 4
            WriteStripQuad(sdlIndices, sidewalkCounter + 18, b + 9, n + 9);

            // Surface
            WriteStripQuad(sdlIndices, surfaceCounter, b + 4, n + 4);
            WriteStripQuad(sdlIndices, surfaceCounter + 6, b + 5, n + 5);

            sidewalkCounter += 24;
            surfaceCounter += 12;
        }

        CalculateNormals(sdlVertices, sdlIndices);
        BakeLighting(sdlVertices);

        data.subMeshCount = 2;
        data.SetSubMesh(1, new SubMeshDescriptor() { baseVertex = 0, firstVertex = 0, indexStart = 0, indexCount = sidewalkIndexCount, topology = MeshTopology.Triangles, vertexCount = vertexCount }, UpdateFlags);
        data.SetSubMesh(0, new SubMeshDescriptor() { baseVertex = 0, firstVertex = 0, indexStart = sidewalkIndexCount, indexCount = surfaceIndexCount, topology = MeshTopology.Triangles, vertexCount = vertexCount }, UpdateFlags);
    }

    private void DrawSidewalkStrip(ref Mesh.MeshDataArray dataArray, int dataIndex, SidewalkStripElement strip)
    {
        var verts = strip.Vertices;
        var data = dataArray[dataIndex];

        float deltaS = Mathf.Floor(-verts[0].x * 0.125f);
        float deltaT = Mathf.Floor(verts[0].z * 0.125f);

        // special case: start/end cap - a single triangle closing off the curb
        if (strip.IsStartCap || strip.IsEndCap)
        {
            data.SetVertexBufferParams(3, SDLVertexLayout);
            data.SetIndexBufferParams(3, IndexFormat.UInt16);

            var capVertices = data.GetVertexData<SDLVertex>();
            var capIndices = data.GetIndexData<ushort>();

            var c0 = new Vector3(-verts[0].x, verts[0].y, verts[0].z);
            var c1 = new Vector3(-verts[1].x, verts[1].y, verts[1].z);
            var c2 = new Vector3(c0.x, c0.y + SidewalkOffsetHeight, c0.z);

            capVertices[0] = new SDLVertex() { pos = c0, uv = GenerateWorldspaceUV(c0, deltaS, deltaT), normal = Vector3.up };
            capVertices[1] = new SDLVertex() { pos = c1, uv = GenerateWorldspaceUV(c1, deltaS, deltaT), normal = Vector3.up };
            capVertices[2] = new SDLVertex() { pos = c2, uv = GenerateWorldspaceUV(c2, deltaS, deltaT), normal = Vector3.up };

            if (strip.IsEndCap)
                WriteTri(capIndices, 0, 2, 1, 0);
            else
                WriteTri(capIndices, 0, 0, 1, 2);

            CalculateNormals(capVertices, capIndices);
            BakeLighting(capVertices);

            data.subMeshCount = 1;
            data.SetSubMesh(0, new SubMeshDescriptor() { baseVertex = 0, firstVertex = 0, indexCount = capIndices.Length, indexStart = 0, topology = MeshTopology.Triangles, vertexCount = capVertices.Length }, UpdateFlags);
            return;
        }

        int rowCount = verts.Count / 2;          // pairs along the strip
        int surfaceVertexCount = rowCount * 2;   // truncate any stray odd vertex
        int curbVertexCount = rowCount * 2;      // bottom + top per raised-side vertex
        int vertexCount = surfaceVertexCount + curbVertexCount;

        int surfaceIndexCount = (surfaceVertexCount - 2) * 3;
        int curbIndexCount = (rowCount - 1) * 6;
        int indexCount = surfaceIndexCount + curbIndexCount;

        data.SetVertexBufferParams(vertexCount, SDLVertexLayout);
        data.SetIndexBufferParams(indexCount, IndexFormat.UInt16);

        var sdlVertices = data.GetVertexData<SDLVertex>();
        var sdlIndices = data.GetIndexData<ushort>();

        // surface: every other vertex sits on top of the curb
        for (int i = 0; i < surfaceVertexCount; i++)
        {
            var src = verts[i];
            var pos = new Vector3(-src.x, src.y, src.z);
            if ((i & 1) == 0)
                pos.y += SidewalkOffsetHeight;

            sdlVertices[i] = new SDLVertex()
            {
                pos = pos,
                uv = GenerateWorldspaceUV(pos, deltaS, deltaT),
                normal = Vector3.up
            };
        }

        // curb: vertical strip along the raised edge
        int curbVertexStart = surfaceVertexCount;
        int w = curbVertexStart;
        for (int i = 0; i < surfaceVertexCount; i += 2)
        {
            var src = verts[i];
            var bottom = new Vector3(-src.x, src.y, src.z);
            var top = new Vector3(bottom.x, bottom.y + SidewalkOffsetHeight, bottom.z);
            var uv = GenerateWorldspaceUV(bottom, deltaS, deltaT);

            sdlVertices[w] = new SDLVertex() { pos = bottom, uv = uv, normal = Vector3.up };
            sdlVertices[w + 1] = new SDLVertex() { pos = top, uv = uv, normal = Vector3.up };
            w += 2;
        }

        // surface indices (triangle strip, alternating winding)
        int indexCounter = 0;
        bool flip = true;
        for (int i = 2; i < surfaceVertexCount; i++)
        {
            if (flip)
                WriteTri(sdlIndices, indexCounter, i, i - 1, i - 2);
            else
                WriteTri(sdlIndices, indexCounter, i - 2, i - 1, i);

            indexCounter += 3;
            flip = !flip;
        }

        // curb indices
        for (int i = 0; i < rowCount - 1; i++)
        {
            int b = curbVertexStart + (i * 2);
            int n = b + 2;

            WriteTri(sdlIndices, indexCounter, b, n + 1, b + 1);
            WriteTri(sdlIndices, indexCounter + 3, n, n + 1, b);

            indexCounter += 6;
        }

        CalculateNormals(sdlVertices, sdlIndices);
        BakeLighting(sdlVertices);

        data.subMeshCount = 1;
        data.SetSubMesh(0, new SubMeshDescriptor() { baseVertex = 0, firstVertex = 0, indexCount = sdlIndices.Length, indexStart = 0, topology = MeshTopology.Triangles, vertexCount = sdlVertices.Length }, UpdateFlags);
    }

    private void DrawCrosswalk(ref Mesh.MeshDataArray dataArray, int dataIndex, CrosswalkElement crosswalk)
    {
        var data = dataArray[dataIndex];

        data.SetVertexBufferParams(4, SDLVertexLayout);
        data.SetIndexBufferParams(6, IndexFormat.UInt16);

        var sdlVertices = data.GetVertexData<SDLVertex>();
        var sdlIndices = data.GetIndexData<ushort>();

        var vert0 = crosswalk.Vertices[0];
        var vert1 = crosswalk.Vertices[1];
        var vert2 = crosswalk.Vertices[2];
        var vert3 = crosswalk.Vertices[3];

        float dx = vert0.x - vert1.x;
        float dz = vert0.z - vert1.z;
        float width = Mathf.Sqrt((dx * dx) + (dz * dz));
        float length = vert0.Distance(vert2) + vert1.Distance(vert3);
        float uvV = width > 1e-6f ? length / width * 0.5f : 0f;

        // note the 0,1,3,2 ordering - the quad is wound around its perimeter
        var p0 = new Vector3(-vert0.x, vert0.y, vert0.z);
        var p1 = new Vector3(-vert1.x, vert1.y, vert1.z);
        var p2 = new Vector3(-vert3.x, vert3.y, vert3.z);
        var p3 = new Vector3(-vert2.x, vert2.y, vert2.z);

        var normal = Vector3.Cross(p1 - p0, p2 - p0);
        normal = normal.sqrMagnitude > 1e-12f ? normal.normalized : Vector3.up;

        sdlVertices[0] = new SDLVertex() { pos = p0, uv = new Vector2(0f, 0f), normal = normal };
        sdlVertices[1] = new SDLVertex() { pos = p1, uv = new Vector2(1f, 0f), normal = normal };
        sdlVertices[2] = new SDLVertex() { pos = p2, uv = new Vector2(1f, uvV), normal = normal };
        sdlVertices[3] = new SDLVertex() { pos = p3, uv = new Vector2(0f, uvV), normal = normal };

        sdlIndices[0] = 0; sdlIndices[1] = 1; sdlIndices[2] = 2;
        sdlIndices[3] = 0; sdlIndices[4] = 2; sdlIndices[5] = 3;

        BakeFlatLighting(sdlVertices);

        data.subMeshCount = 1;
        data.SetSubMesh(0, new SubMeshDescriptor() { baseVertex = 0, firstVertex = 0, indexCount = sdlIndices.Length, indexStart = 0, topology = MeshTopology.Triangles, vertexCount = sdlVertices.Length }, UpdateFlags);
    }

    private void DrawSliver(ref Mesh.MeshDataArray dataArray, int dataIndex, SliverElement sliver)
    {
        var leftSrc = sliver.Vertices[0];
        var rightSrc = sliver.Vertices[1];

        float height = sliver.Height;
        float textureScale = sliver.TextureScale;

        var left = new Vector3(-leftSrc.x, leftSrc.y, leftSrc.z);
        var right = new Vector3(-rightSrc.x, rightSrc.y, rightSrc.z);
        var topLeft = new Vector3(left.x, height, left.z);
        var topRight = new Vector3(right.x, height, right.z);

        bool leftClipped = left.y >= height;
        bool rightClipped = right.y >= height;

        int vertexCount = (leftClipped || rightClipped) ? 3 : 4;
        int indexCount = (vertexCount - 2) * 3;

        var data = dataArray[dataIndex];
        data.SetVertexBufferParams(vertexCount, SDLVertexLayout);
        data.SetIndexBufferParams(indexCount, IndexFormat.UInt16);

        var sdlVertices = data.GetVertexData<SDLVertex>();
        var sdlIndices = data.GetIndexData<ushort>();

        float dx = left.x - right.x;
        float dz = left.z - right.z;
        float width = Mathf.Sqrt((dx * dx) + (dz * dz));

        float u = Mathf.Floor((width * textureScale) + 0.5f);
        float v1 = (topLeft.y - left.y) * textureScale;
        float v2 = (topRight.y - right.y) * textureScale;

        // all three windings lie in the same vertical plane and face the same way
        var normal = Vector3.Cross(Vector3.up, right - left);
        normal = normal.sqrMagnitude > 1e-12f ? normal.normalized : Vector3.up;

        if (rightClipped)
        {
            sdlVertices[0] = new SDLVertex() { pos = left, uv = new Vector2(0f, v1), normal = normal };
            sdlVertices[1] = new SDLVertex() { pos = topLeft, uv = new Vector2(0f, 0f), normal = normal };
            sdlVertices[2] = new SDLVertex() { pos = topRight, uv = new Vector2(u, 0f), normal = normal };
        }
        else if (leftClipped)
        {
            sdlVertices[0] = new SDLVertex() { pos = topLeft, uv = new Vector2(0f, 0f), normal = normal };
            sdlVertices[1] = new SDLVertex() { pos = topRight, uv = new Vector2(u, 0f), normal = normal };
            sdlVertices[2] = new SDLVertex() { pos = right, uv = new Vector2(u, v2), normal = normal };
        }
        else
        {
            sdlVertices[0] = new SDLVertex() { pos = right, uv = new Vector2(u, v2), normal = normal };
            sdlVertices[1] = new SDLVertex() { pos = left, uv = new Vector2(0f, v1), normal = normal };
            sdlVertices[2] = new SDLVertex() { pos = topLeft, uv = new Vector2(0f, 0f), normal = normal };
            sdlVertices[3] = new SDLVertex() { pos = topRight, uv = new Vector2(u, 0f), normal = normal };
        }

        sdlIndices[0] = 0; sdlIndices[1] = 1; sdlIndices[2] = 2;
        if (vertexCount == 4)
        {
            sdlIndices[3] = 0; sdlIndices[4] = 2; sdlIndices[5] = 3;
        }

        BakeUniformLighting(sdlVertices, normal);

        data.subMeshCount = 1;
        data.SetSubMesh(0, new SubMeshDescriptor() { baseVertex = 0, firstVertex = 0, indexCount = sdlIndices.Length, indexStart = 0, topology = MeshTopology.Triangles, vertexCount = sdlVertices.Length }, UpdateFlags);
    }

    private void DrawDividedRoad(ref Mesh.MeshDataArray dataArray, int dataIndex, DividedRoadElement road)
    {
        var verts = road.Vertices;

        var dividerType = road.DividerType;
        int dividerValue = road.Value;
        bool closedStart = road.DividerFlags.HasFlag(DividerFlags.ClosedStart);
        bool closedEnd = road.DividerFlags.HasFlag(DividerFlags.ClosedEnd);

        int rowCount = verts.Count / 6;
        int spanCount = rowCount - 1;

        bool capsSupported = dividerType == DividerType.Wedged || dividerType == DividerType.Elevated;
        bool haveStartCap = capsSupported && closedStart;
        bool haveEndCap = capsSupported && closedEnd;
        bool haveCaps = haveStartCap || haveEndCap;
        int capCount = (haveStartCap ? 1 : 0) + (haveEndCap ? 1 : 0);

        int dividerVertsPerRow;
        switch (dividerType)
        {
            case DividerType.Flat: dividerVertsPerRow = 2; break;
            case DividerType.Wedged: dividerVertsPerRow = 6; break;
            case DividerType.Elevated: dividerVertsPerRow = 10; break;
            default: dividerVertsPerRow = 0; break;
        }

        // vertex block offsets
        int dividerVertexOffset = rowCount * 12;
        int capVertexOffset = dividerVertexOffset + (rowCount * dividerVertsPerRow);
        int vertexCount = capVertexOffset + (capCount * 4);

        // index block offsets
        int surfaceIndexCount = spanCount * 12;
        int sidewalkIndexCount = spanCount * 24;
        int sidesIndexCount = (dividerType == DividerType.Wedged || dividerType == DividerType.Elevated) ? spanCount * 12 : 0;
        int stripsIndexCount = (dividerType == DividerType.Elevated) ? spanCount * 12 : 0;
        int centerIndexCount = dividerVertsPerRow > 0 ? spanCount * 6 : 0;
        int capIndexCount = capCount * 6;

        int surfaceStart = 0;
        int sidewalkStart = surfaceStart + surfaceIndexCount;
        int sidesStart = sidewalkStart + sidewalkIndexCount;
        int stripsStart = sidesStart + sidesIndexCount;
        int centerStart = stripsStart + stripsIndexCount;
        int capStart = centerStart + centerIndexCount;
        int indexCount = capStart + capIndexCount;

        var data = dataArray[dataIndex];
        data.SetVertexBufferParams(vertexCount, SDLVertexLayout);
        data.SetIndexBufferParams(indexCount, IndexFormat.UInt16);

        var sdlVertices = data.GetVertexData<SDLVertex>();
        var sdlIndices = data.GetIndexData<ushort>();

        var surfaceUvs = StripMap(verts, 6, 1, 4);
        var sidewalkLeftUvs = StripMap(verts, 6, 0, 1);
        var sidewalkRightUvs = StripMap(verts, 6, 4, 5);

        // ---- road vertices ----------------------------------------------------
        for (int i = 0; i < rowCount; i++)
        {
            int s = i * 6;   // source vertex base
            int u = i * 2;   // strip uv base
            int v = i * 12;  // output vertex base

            var swLeftLeftSrc = verts[s];
            var swLeftRightSrc = verts[s + 1];
            var roadLeftRightSrc = verts[s + 2];
            var roadRightLeftSrc = verts[s + 3];
            var roadRightRightSrc = verts[s + 4];
            var swRightRightSrc = verts[s + 5];

            var swLeftLeft = new Vector3(-swLeftLeftSrc.x, swLeftLeftSrc.y, swLeftLeftSrc.z);
            var swLeftRight = new Vector3(-swLeftRightSrc.x, swLeftRightSrc.y, swLeftRightSrc.z);
            if (!swLeftLeftSrc.Equals(swLeftRightSrc))
                swLeftRight.y += SidewalkOffsetHeight;

            var roadLeftLeft = new Vector3(-swLeftRightSrc.x, swLeftRightSrc.y, swLeftRightSrc.z);
            var roadLeftRight = new Vector3(-roadLeftRightSrc.x, roadLeftRightSrc.y, roadLeftRightSrc.z);
            var roadRightLeft = new Vector3(-roadRightLeftSrc.x, roadRightLeftSrc.y, roadRightLeftSrc.z);
            var roadRightRight = new Vector3(-roadRightRightSrc.x, roadRightRightSrc.y, roadRightRightSrc.z);

            var swRightLeft = new Vector3(-roadRightRightSrc.x, roadRightRightSrc.y, roadRightRightSrc.z);
            var swRightRight = new Vector3(-swRightRightSrc.x, swRightRightSrc.y, swRightRightSrc.z);
            if (!roadRightRightSrc.Equals(swRightRightSrc))
                swRightLeft.y += SidewalkOffsetHeight;

            // LEFT SIDEWALK
            sdlVertices[v] = new SDLVertex() { pos = swLeftLeft, uv = sidewalkLeftUvs[u] };
            sdlVertices[v + 1] = new SDLVertex() { pos = swLeftRight, uv = sidewalkLeftUvs[u + 1] };
            sdlVertices[v + 2] = new SDLVertex() { pos = swLeftRight, uv = sidewalkLeftUvs[u + 1] };
            sdlVertices[v + 3] = new SDLVertex() { pos = roadLeftLeft, uv = sidewalkLeftUvs[u + 1] };

            // SURFACE
            sdlVertices[v + 4] = new SDLVertex() { pos = roadLeftLeft, uv = surfaceUvs[u] };
            sdlVertices[v + 5] = new SDLVertex() { pos = roadLeftRight, uv = surfaceUvs[u + 1] };
            sdlVertices[v + 6] = new SDLVertex() { pos = roadRightLeft, uv = surfaceUvs[u + 1] };
            sdlVertices[v + 7] = new SDLVertex() { pos = roadRightRight, uv = surfaceUvs[u] };

            // RIGHT SIDEWALK
            sdlVertices[v + 8] = new SDLVertex() { pos = roadRightRight, uv = sidewalkRightUvs[u + 1] };
            sdlVertices[v + 9] = new SDLVertex() { pos = swRightLeft, uv = sidewalkRightUvs[u + 1] };
            sdlVertices[v + 10] = new SDLVertex() { pos = swRightLeft, uv = sidewalkRightUvs[u + 1] };
            sdlVertices[v + 11] = new SDLVertex() { pos = swRightRight, uv = sidewalkRightUvs[u] };
        }

        // ---- divider vertices -------------------------------------------------
        float centerHeight = dividerValue / 256f;

        if (dividerType == DividerType.Flat)
        {
            // the V tiling is stored in the opposite endianness
            float uvV = ((dividerValue & 0xFF) << 8) | ((dividerValue >> 8) & 0xFF);
            var dividerUvs = StripMap(verts, 6, 2, 3, 1f, uvV);

            for (int i = 0; i < rowCount; i++)
            {
                int s = i * 6;
                int u = i * 2;
                int v = dividerVertexOffset + (i * 2);

                var leftSrc = verts[s + 2];
                var rightSrc = verts[s + 3];

                sdlVertices[v] = new SDLVertex()
                {
                    pos = new Vector3(-leftSrc.x, leftSrc.y, leftSrc.z),
                    uv = dividerUvs[u]
                };
                sdlVertices[v + 1] = new SDLVertex()
                {
                    pos = new Vector3(-rightSrc.x, rightSrc.y, rightSrc.z),
                    uv = dividerUvs[u + 1]
                };
            }
        }
        else if (dividerType == DividerType.Wedged)
        {
            var dividerUvs = StripMap(verts, 6, 2, 3);

            for (int i = 0; i < rowCount; i++)
            {
                int s = i * 6;
                int u = i * 2;
                int v = dividerVertexOffset + (i * 6);

                var leftSrc = verts[s + 2];
                var rightSrc = verts[s + 3];

                var left = new Vector3(-leftSrc.x, leftSrc.y, leftSrc.z);
                var right = new Vector3(-rightSrc.x, rightSrc.y, rightSrc.z);

                var innerLeft = Vector3.Lerp(left, right, 0.4f);
                var innerRight = Vector3.Lerp(right, left, 0.4f);
                innerLeft.y += centerHeight;
                innerRight.y += centerHeight;

                sdlVertices[v] = new SDLVertex() { pos = left, uv = dividerUvs[u + 1] };
                sdlVertices[v + 1] = new SDLVertex() { pos = innerLeft, uv = dividerUvs[u] };

                sdlVertices[v + 2] = new SDLVertex() { pos = innerLeft, uv = dividerUvs[u + 1] };
                sdlVertices[v + 3] = new SDLVertex() { pos = innerRight, uv = dividerUvs[u] };

                sdlVertices[v + 4] = new SDLVertex() { pos = innerRight, uv = dividerUvs[u] };
                sdlVertices[v + 5] = new SDLVertex() { pos = right, uv = dividerUvs[u + 1] };
            }
        }
        else if (dividerType == DividerType.Elevated)
        {
            var dividerUvs = StripMap(verts, 6, 2, 3);
            var centerHeightVector = Vector3.up * centerHeight;

            for (int i = 0; i < rowCount; i++)
            {
                int s = i * 6;
                int u = i * 2;
                int v = dividerVertexOffset + (i * 10);

                var leftSrc = verts[s + 2];
                var rightSrc = verts[s + 3];

                var left = new Vector3(-leftSrc.x, leftSrc.y, leftSrc.z);
                var right = new Vector3(-rightSrc.x, rightSrc.y, rightSrc.z);

                var medianLeft = left + centerHeightVector;
                var medianRight = right + centerHeightVector;
                var medianInnerLeft = Vector3.Lerp(medianLeft, medianRight, 0.05f);
                var medianInnerRight = Vector3.Lerp(medianRight, medianLeft, 0.05f);

                sdlVertices[v] = new SDLVertex() { pos = left, uv = dividerUvs[u + 1] };
                sdlVertices[v + 1] = new SDLVertex() { pos = medianLeft, uv = dividerUvs[u] };

                sdlVertices[v + 2] = new SDLVertex() { pos = medianLeft, uv = dividerUvs[u + 1] };
                sdlVertices[v + 3] = new SDLVertex() { pos = medianInnerLeft, uv = dividerUvs[u] };

                sdlVertices[v + 4] = new SDLVertex() { pos = medianInnerLeft, uv = dividerUvs[u + 1] };
                sdlVertices[v + 5] = new SDLVertex() { pos = medianInnerRight, uv = dividerUvs[u] };

                sdlVertices[v + 6] = new SDLVertex() { pos = medianInnerRight, uv = dividerUvs[u + 1] };
                sdlVertices[v + 7] = new SDLVertex() { pos = medianRight, uv = dividerUvs[u] };

                sdlVertices[v + 8] = new SDLVertex() { pos = medianRight, uv = dividerUvs[u] };
                sdlVertices[v + 9] = new SDLVertex() { pos = right, uv = dividerUvs[u + 1] };
            }
        }

        // ---- divider cap vertices ---------------------------------------------
        if (haveCaps)
        {
            var centerHeightVector = Vector3.up * centerHeight;
            int w = capVertexOffset;

            if (haveStartCap)
            {
                WriteDividerCap(sdlVertices, w, verts[2], verts[3], dividerType, centerHeightVector);
                w += 4;
            }

            if (haveEndCap)
            {
                // last row, and the two edges swapped so the cap faces the other way
                int endRow = (rowCount - 1) * 6;
                WriteDividerCap(sdlVertices, w, verts[endRow + 3], verts[endRow + 2], dividerType, centerHeightVector);
                w += 4;
            }
        }

        // ---- road indices -----------------------------------------------------
        int surfaceCounter = surfaceStart;
        int sidewalkCounter = sidewalkStart;

        for (int i = 0; i < spanCount; i++)
        {
            int b = i * 12;
            int n = b + 12;

            // Sidewalk 1 (left surface) / 2 (left curb)
            WriteStripQuad(sdlIndices, sidewalkCounter, b, n);
            WriteStripQuad(sdlIndices, sidewalkCounter + 6, b + 2, n + 2);

            // Sidewalk 3 (right curb) / 4 (right surface)
            WriteStripQuad(sdlIndices, sidewalkCounter + 12, b + 8, n + 8);
            WriteStripQuad(sdlIndices, sidewalkCounter + 18, b + 10, n + 10);

            // Surface, both carriageways
            WriteStripQuad(sdlIndices, surfaceCounter, b + 4, n + 4);
            WriteStripQuad(sdlIndices, surfaceCounter + 6, b + 6, n + 6);

            sidewalkCounter += 24;
            surfaceCounter += 12;
        }

        // ---- divider indices --------------------------------------------------
        if (dividerType == DividerType.Flat)
        {
            int centerCounter = centerStart;
            for (int i = 0; i < spanCount; i++)
            {
                int b = dividerVertexOffset + (i * 2);
                WriteStripQuad(sdlIndices, centerCounter, b, b + 2);
                centerCounter += 6;
            }
        }
        else if (dividerType == DividerType.Wedged)
        {
            int sidesCounter = sidesStart;
            int centerCounter = centerStart;

            for (int i = 0; i < spanCount; i++)
            {
                int b = dividerVertexOffset + (i * 6);
                int n = b + 6;

                WriteStripQuad(sdlIndices, sidesCounter, b, n);              // outer face, left
                WriteStripQuad(sdlIndices, sidesCounter + 6, b + 4, n + 4);  // outer face, right
                WriteStripQuad(sdlIndices, centerCounter, b + 2, n + 2);     // top

                sidesCounter += 12;
                centerCounter += 6;
            }
        }
        else if (dividerType == DividerType.Elevated)
        {
            int sidesCounter = sidesStart;
            int stripsCounter = stripsStart;
            int centerCounter = centerStart;

            for (int i = 0; i < spanCount; i++)
            {
                int b = dividerVertexOffset + (i * 10);
                int n = b + 10;

                WriteStripQuad(sdlIndices, sidesCounter, b, n);
                WriteStripQuad(sdlIndices, sidesCounter + 6, b + 8, n + 8);

                WriteStripQuad(sdlIndices, stripsCounter, b + 2, n + 2);
                WriteStripQuad(sdlIndices, stripsCounter + 6, b + 6, n + 6);

                WriteStripQuad(sdlIndices, centerCounter, b + 4, n + 4);

                sidesCounter += 12;
                stripsCounter += 12;
                centerCounter += 6;
            }
        }

        // ---- divider cap indices ----------------------------------------------
        if (haveCaps)
        {
            int capCounter = capStart;
            for (int i = 0; i < capCount; i++)
            {
                int c = capVertexOffset + (i * 4);
                WriteTri(sdlIndices, capCounter, c, c + 2, c + 1);
                WriteTri(sdlIndices, capCounter + 3, c, c + 3, c + 2);
                capCounter += 6;
            }
        }

        CalculateNormals(sdlVertices, sdlIndices);
        BakeLighting(sdlVertices);

        // ---- submeshes --------------------------------------------------------
        int subMesh = 0;
        data.subMeshCount = GetDividedRoadSubMeshCount(dividerType, haveCaps);

        SetTriSubMesh(data, subMesh++, surfaceStart, surfaceIndexCount, vertexCount);
        SetTriSubMesh(data, subMesh++, sidewalkStart, sidewalkIndexCount, vertexCount);
        if (sidesIndexCount > 0) SetTriSubMesh(data, subMesh++, sidesStart, sidesIndexCount, vertexCount);
        if (stripsIndexCount > 0) SetTriSubMesh(data, subMesh++, stripsStart, stripsIndexCount, vertexCount);
        if (centerIndexCount > 0) SetTriSubMesh(data, subMesh++, centerStart, centerIndexCount, vertexCount);
        if (capIndexCount > 0) SetTriSubMesh(data, subMesh++, capStart, capIndexCount, vertexCount);
    }

    private static void WriteDividerCap(NativeArray<SDLVertex> sdlVertices, int at,
                                    Vertex leftSrc, Vertex rightSrc,
                                    DividerType dividerType, Vector3 centerHeightVector)
    {
        var left = new Vector3(-leftSrc.x, leftSrc.y, leftSrc.z);
        var right = new Vector3(-rightSrc.x, rightSrc.y, rightSrc.z);

        Vector3 medianLeft, medianRight;
        if (dividerType == DividerType.Wedged)
        {
            medianLeft = Vector3.Lerp(left, right, 0.4f) + centerHeightVector;
            medianRight = Vector3.Lerp(right, left, 0.4f) + centerHeightVector;
        }
        else
        {
            medianLeft = left + centerHeightVector;
            medianRight = right + centerHeightVector;
        }

        sdlVertices[at] = new SDLVertex() { pos = left, uv = new Vector2(1f, 1f) };
        sdlVertices[at + 1] = new SDLVertex() { pos = right, uv = new Vector2(0f, 1f) };
        sdlVertices[at + 2] = new SDLVertex() { pos = medianRight, uv = new Vector2(0f, 0f) };
        sdlVertices[at + 3] = new SDLVertex() { pos = medianLeft, uv = new Vector2(1f, 0f) };
    }

    private static int GetDividedRoadSubMeshCount(DividerType dividerType, bool haveCaps)
    {
        int count = 2; // surface + sidewalk
        switch (dividerType)
        {
            case DividerType.Flat: count += 1; break;
            case DividerType.Wedged: count += 2; break;
            case DividerType.Elevated: count += 3; break;
        }
        if (haveCaps) count += 1;
        return count;
    }

    private Material[] GetDividedRoadMaterials(DividedRoadElement road, int subMeshCount)
    {
        var textures = road.Textures;
        var dividerTextures = road.DividerTextures;

        int surfaceTexture = textures[0];
        int sidewalkTexture = textures.Length > 1 ? textures[1] : surfaceTexture;

        var materials = new Material[subMeshCount];
        int subMesh = 0;

        materials[subMesh++] = GetOrCreateMaterial(surfaceTexture);
        materials[subMesh++] = GetOrCreateMaterial(sidewalkTexture);

        // the last divider slot in use is also what the caps get textured with
        int capSlot = 0;
        switch (road.DividerType)
        {
            case DividerType.Flat:
                materials[subMesh++] = GetOrCreateMaterial(road.GetDividerTexture(DividerTextureType.Top));
                capSlot = 0;
                break;
            case DividerType.Wedged:
                materials[subMesh++] = GetOrCreateMaterial(road.GetDividerTexture(DividerTextureType.Side)); // sides
                materials[subMesh++] = GetOrCreateMaterial(road.GetDividerTexture(DividerTextureType.Top)); // center
                capSlot = 1;
                break;
            case DividerType.Elevated:
                materials[subMesh++] = GetOrCreateMaterial(road.GetDividerTexture(DividerTextureType.Side)); // sides
                materials[subMesh++] = GetOrCreateMaterial(road.GetDividerTexture(DividerTextureType.SideStrips)); // strips
                materials[subMesh++] = GetOrCreateMaterial(road.GetDividerTexture(DividerTextureType.Top)); // center
                capSlot = 2;
                break;
        }

        if (subMesh < subMeshCount)
            materials[subMesh++] = GetOrCreateMaterial(road.GetDividerTexture(DividerTextureType.Cap));

        return materials;
    }

    private const int WallBitShift = 1;

    private static bool HasWallBit(uint bits, int edgeIndex)
        => (bits & (1u << (edgeIndex + WallBitShift))) != 0;

    private void DrawJunctionTunnel(ref Mesh.MeshDataArray dataArray, int dataIndex, Room room, TunnelElement tunnel)
    {
        int perimeterCount = room.Perimeter.Count;

        var flags = tunnel.Flags;
        float tunnelHeight = tunnel.Height;
        uint wallBits = unchecked((uint)tunnel.WallBits);
        uint invertFirst = unchecked((uint)tunnel.WallInvertFirstEdge);
        uint invertSecond = unchecked((uint)tunnel.WallInvertSecondEdge);
        int ceilingOrigin = tunnel.CeilingOriginVertex;

        bool isWall = (flags & TunnelFlags.IsWall) != 0;
        bool flatCeiling = (flags & TunnelFlags.FlatCeiling) != 0;
        bool doubleSidedWalls = (flags & TunnelFlags.Culled) != 0 && !isWall;

        float wallThickness = tunnelHeight / 3f;
        var wallHeightVec = Vector3.up * tunnelHeight;
        var wallThirdHeightVec = Vector3.up * wallThickness;

        int wallCount = 0;
        for (int i = 0; i < perimeterCount; i++)
        {
            if (HasWallBit(wallBits, i))
                wallCount++;
        }

        // ---- buffer sizes -----------------------------------------------------
        // The Lua's totalVertexCount/submeshCount used truthiness tests on the flag
        // masks, so every branch always ran. These are the counts the index and
        // material code below actually implies.
        int innerVertsPerWall = doubleSidedWalls ? 8 : 4;
        int innerVertexOffset = 0;
        int innerVertexCount = wallCount * innerVertsPerWall;

        int outerVertexOffset = innerVertexOffset + innerVertexCount;
        int outerVertexCount = isWall ? wallCount * 8 : 0;

        int undersideVertexOffset = outerVertexOffset + outerVertexCount;
        int undersideVertexCount = isWall ? perimeterCount : 0;

        int ceilingVertexOffset = undersideVertexOffset + undersideVertexCount;
        int ceilingVertexCount = flatCeiling ? perimeterCount : 0;

        int vertexCount = ceilingVertexOffset + ceilingVertexCount;

        int innerIndexCount = wallCount * (doubleSidedWalls ? 12 : 6);
        int outerIndexCount = isWall ? wallCount * 12 : 0;
        int undersideIndexCount = isWall ? (perimeterCount - 2) * 3 : 0;
        int ceilingIndexCount = flatCeiling ? (perimeterCount - 2) * 3 : 0;

        int innerStart = 0;
        int outerStart = innerStart + innerIndexCount;
        int undersideStart = outerStart + outerIndexCount;
        int ceilingStart = undersideStart + undersideIndexCount;
        int indexCount = ceilingStart + ceilingIndexCount;

        var data = dataArray[dataIndex];
        data.SetVertexBufferParams(vertexCount, SDLVertexLayout);
        data.SetIndexBufferParams(indexCount, IndexFormat.UInt16);

        var sdlVertices = data.GetVertexData<SDLVertex>();
        var sdlIndices = data.GetIndexData<ushort>();

        // ---- max perimeter height ---------------------------------------------
        float maxPerimeterY = GetPerimeterVertex(room, 0).y;
        for (int i = 1; i < perimeterCount; i++)
            maxPerimeterY = Mathf.Max(maxPerimeterY, GetPerimeterVertex(room, i).y);

        // ---- inner walls ------------------------------------------------------
        int vertexCursor = innerVertexOffset;
        int indexCursor = innerStart;

        for (int i = 0; i < perimeterCount; i++)
        {
            if (!HasWallBit(wallBits, i))
                continue;

            var vertLeft = FlipVertex(GetPerimeterVertex(room, i));
            var vertRight = FlipVertex(GetPerimeterVertex(room, (i + 1) % perimeterCount));

            var vertLeftTop = vertLeft + wallHeightVec;
            var vertRightTop = vertRight + wallHeightVec;

            float uvEnd = Mathf.Max(1f, FlatDistance(vertLeft, vertRight) / tunnelHeight);

            sdlVertices[vertexCursor] = new SDLVertex() { pos = vertLeft, uv = new Vector2(0f, 1f) };
            sdlVertices[vertexCursor + 1] = new SDLVertex() { pos = vertRight, uv = new Vector2(uvEnd, 1f) };
            sdlVertices[vertexCursor + 2] = new SDLVertex() { pos = vertRightTop, uv = new Vector2(uvEnd, 0f) };
            sdlVertices[vertexCursor + 3] = new SDLVertex() { pos = vertLeftTop, uv = new Vector2(0f, 0f) };

            WriteTri(sdlIndices, indexCursor, vertexCursor, vertexCursor + 1, vertexCursor + 2);
            WriteTri(sdlIndices, indexCursor + 3, vertexCursor, vertexCursor + 2, vertexCursor + 3);

            vertexCursor += 4;
            indexCursor += 6;

            if (doubleSidedWalls)
            {
                sdlVertices[vertexCursor] = new SDLVertex() { pos = vertLeft, uv = new Vector2(0f, 1f) };
                sdlVertices[vertexCursor + 1] = new SDLVertex() { pos = vertRight, uv = new Vector2(uvEnd, 1f) };
                sdlVertices[vertexCursor + 2] = new SDLVertex() { pos = vertRightTop, uv = new Vector2(uvEnd, 0f) };
                sdlVertices[vertexCursor + 3] = new SDLVertex() { pos = vertLeftTop, uv = new Vector2(0f, 0f) };

                WriteTri(sdlIndices, indexCursor, vertexCursor + 2, vertexCursor + 1, vertexCursor);
                WriteTri(sdlIndices, indexCursor + 3, vertexCursor + 3, vertexCursor + 2, vertexCursor);

                vertexCursor += 4;
                indexCursor += 6;
            }
        }

        if (isWall)
        {
            // ---- outer walls --------------------------------------------------
            int outerCursor = outerVertexOffset;
            int outerIndexCursor = outerStart;

            for (int i = 0; i < perimeterCount; i++)
            {
                if (!HasWallBit(wallBits, i))
                    continue;

                var vertLeft = FlipVertex(GetPerimeterVertex(room, i));
                var vertRight = FlipVertex(GetPerimeterVertex(room, (i + 1) % perimeterCount));

                var outerDirection = Vector3.Cross(vertLeft - vertRight, Vector3.up);
                outerDirection = outerDirection.sqrMagnitude > 1e-12f ? outerDirection.normalized : Vector3.zero;

                var outerDirectionA = HasWallBit(invertFirst, i) ? -outerDirection : outerDirection;
                var outerDirectionB = HasWallBit(invertSecond, i) ? -outerDirection : outerDirection;

                var vertOuterLeft = vertLeft + (outerDirectionA * wallThickness) - wallThirdHeightVec;
                var vertOuterRight = vertRight + (outerDirectionB * wallThickness) - wallThirdHeightVec;

                var vertOuterTopLeft = vertLeft + (outerDirectionA * wallThickness) + wallHeightVec;
                var vertOuterTopRight = vertRight + (outerDirectionB * wallThickness) + wallHeightVec;

                var vertTopLeft = vertLeft + wallHeightVec;
                var vertTopRight = vertRight + wallHeightVec;

                float uvEnd = Mathf.Max(1f, FlatDistance(vertLeft, vertRight) / tunnelHeight);

                // outward face
                sdlVertices[outerCursor] = new SDLVertex() { pos = vertOuterRight, uv = new Vector2(uvEnd, 1f) };
                sdlVertices[outerCursor + 1] = new SDLVertex() { pos = vertOuterLeft, uv = new Vector2(0f, 1f) };
                sdlVertices[outerCursor + 2] = new SDLVertex() { pos = vertOuterTopLeft, uv = new Vector2(0f, 0f) };
                sdlVertices[outerCursor + 3] = new SDLVertex() { pos = vertOuterTopRight, uv = new Vector2(uvEnd, 0f) };

                // top strip, outer edge back to the inner wall
                sdlVertices[outerCursor + 4] = new SDLVertex() { pos = vertOuterTopRight, uv = new Vector2(uvEnd, 0f) };
                sdlVertices[outerCursor + 5] = new SDLVertex() { pos = vertOuterTopLeft, uv = new Vector2(0f, 0f) };
                sdlVertices[outerCursor + 6] = new SDLVertex() { pos = vertTopLeft, uv = new Vector2(0f, 1f) };
                sdlVertices[outerCursor + 7] = new SDLVertex() { pos = vertTopRight, uv = new Vector2(uvEnd, 1f) };

                WriteTri(sdlIndices, outerIndexCursor, outerCursor, outerCursor + 1, outerCursor + 2);
                WriteTri(sdlIndices, outerIndexCursor + 3, outerCursor, outerCursor + 2, outerCursor + 3);
                WriteTri(sdlIndices, outerIndexCursor + 6, outerCursor + 4, outerCursor + 5, outerCursor + 6);
                WriteTri(sdlIndices, outerIndexCursor + 9, outerCursor + 4, outerCursor + 6, outerCursor + 7);

                outerCursor += 8;
                outerIndexCursor += 12;
            }

            // ---- underside ----------------------------------------------------
            // Same worldspace mapping the fans use, anchored exactly on the origin
            // vertex rather than floored to the texture grid.
            var undersideOrigin = FlipVertex(GetPerimeterVertex(room, 0));
            float deltaS = undersideOrigin.x * 0.125f;
            float deltaT = undersideOrigin.z * 0.125f;

            int undersideIndexCursor = undersideStart;

            for (int i = 0; i < perimeterCount; i++)
            {
                int p = (i + ceilingOrigin) % perimeterCount;
                int pNext = (i + ceilingOrigin + 1) % perimeterCount;

                var perimeterVertex = FlipVertex(GetPerimeterVertex(room, p));
                perimeterVertex.y = maxPerimeterY;

                var nextPerimeterVertex = FlipVertex(GetPerimeterVertex(room, pNext));
                nextPerimeterVertex.y = maxPerimeterY;

                var uv = GenerateWorldspaceUV(perimeterVertex, deltaS, deltaT);

                // where either adjoining edge carries a wall, push the underside out
                // to meet the outer face
                Vector3 pos;
                if (HasWallBit(wallBits, p) || HasWallBit(wallBits, pNext))
                {
                    var outsideDir = Vector3.Cross(perimeterVertex - nextPerimeterVertex, Vector3.up);
                    outsideDir = outsideDir.sqrMagnitude > 1e-12f ? outsideDir.normalized : Vector3.zero;
                    pos = perimeterVertex + (outsideDir * wallThickness) - wallThirdHeightVec;
                }
                else
                {
                    pos = perimeterVertex - wallThirdHeightVec;
                }

                sdlVertices[undersideVertexOffset + i] = new SDLVertex() { pos = pos, uv = uv };

                if (i >= 2)
                {
                    WriteTri(sdlIndices, undersideIndexCursor,
                             undersideVertexOffset,
                             undersideVertexOffset + (i - 1),
                             undersideVertexOffset + i);
                    undersideIndexCursor += 3;
                }
            }
        }

        if (flatCeiling)
        {
            var ceilingOriginVertex = FlipVertex(GetPerimeterVertex(room, 0));
            float deltaS = ceilingOriginVertex.x * 0.125f;
            float deltaT = ceilingOriginVertex.z * 0.125f;

            int ceilingIndexCursor = ceilingStart;

            for (int i = 0; i < perimeterCount; i++)
            {
                int p = (i + ceilingOrigin) % perimeterCount;
                var perimeterVertex = FlipVertex(GetPerimeterVertex(room, p));

                var pos = new Vector3(perimeterVertex.x, maxPerimeterY + tunnelHeight, perimeterVertex.z);

                sdlVertices[ceilingVertexOffset + i] = new SDLVertex()
                {
                    pos = pos,
                    uv = GenerateWorldspaceUV(perimeterVertex, deltaS, deltaT)
                };

                if (i >= 2)
                {
                    WriteTri(sdlIndices, ceilingIndexCursor,
                             ceilingVertexOffset + (i - 1),
                             ceilingVertexOffset + i,
                             ceilingVertexOffset);
                    ceilingIndexCursor += 3;
                }
            }
        }

        CalculateNormals(sdlVertices, sdlIndices);
        BakeLighting(sdlVertices);

        int subMesh = 0;
        data.subMeshCount = 1 + (isWall ? 2 : 0) + (flatCeiling ? 1 : 0);

        SetTriSubMesh(data, subMesh++, innerStart, innerIndexCount, vertexCount);
        if (isWall)
        {
            SetTriSubMesh(data, subMesh++, outerStart, outerIndexCount, vertexCount);
            SetTriSubMesh(data, subMesh++, undersideStart, undersideIndexCount, vertexCount);
        }
        if (flatCeiling)
            SetTriSubMesh(data, subMesh++, ceilingStart, ceilingIndexCount, vertexCount);
    }

    private static Vector3 MitreOffset(Vector3 sideDir, float wallThickness, bool isStart, bool isLeft)
    {
        const float MitreScale = 1.414f; // sqrt(2) approx.
        float sign = (isStart ? 1f : -1f) * (isLeft ? 1f : -1f);
        return Vector3.Cross(Vector3.up, sideDir) * (wallThickness * MitreScale * sign);
    }

    private void DrawRoadTunnel(ref Mesh.MeshDataArray dataArray, int dataIndex, TunnelElement tunnel, IRoad roadElement)
    {
        var roadGeometry = (IGeometricSDLElement)roadElement;

        var roadVerts = roadGeometry.GetVertices();
        int rowBreadth = roadElement.RowBreadth;
        int rowCount = roadElement.RowCount;
        int spanCount = rowCount - 1;

        var flags = tunnel.Flags;
        float tunnelHeight = tunnel.Height;

        bool leftSide = (flags & TunnelFlags.LeftSide) != 0;
        bool rightSide = (flags & TunnelFlags.RightSide) != 0;
        bool isWall = (flags & TunnelFlags.IsWall) != 0;
        bool flatCeiling = (flags & TunnelFlags.FlatCeiling) != 0;
        bool curvedCeiling = (flags & TunnelFlags.CurvedCeiling) != 0;
        bool curvedSides = (flags & TunnelFlags.CurvedSides) != 0;
        bool haveCeiling = flatCeiling || curvedCeiling;
        bool doubleSidedWalls = (flags & TunnelFlags.Culled) != 0 && !isWall;

        // A road can carry the 3D wall flag while only having a wall on one side.
        // The outer face, its top strip and its caps only exist where there is a
        // wall to thicken; the underside always spans the full width.
        bool leftOuter = isWall && leftSide;
        bool rightOuter = isWall && rightSide;

        bool closedStartLeft = (flags & TunnelFlags.ClosedStartLeft) != 0;
        bool closedEndLeft = (flags & TunnelFlags.ClosedEndLeft) != 0;
        bool closedStartRight = (flags & TunnelFlags.ClosedStartRight) != 0;
        bool closedEndRight = (flags & TunnelFlags.ClosedEndRight) != 0;

        float wallThickness = tunnel.WallWidth;
        var wallHeightVec = Vector3.up * tunnelHeight;
        var wallThirdHeightVec = Vector3.up * wallThickness;

        int lrWallVertexCount = curvedSides ? 4 : 2;
        int ceilingVertexCount = curvedCeiling ? 5 : 2;

        // ---- per-row vertex layout --------------------------------------------
        int vertexCountPerRow = 0;
        int leftWallBase = -1, rightWallBase = -1;
        int leftOuterWallBase = -1, rightOuterWallBase = -1, undersideBase = -1, ceilingBase = -1;

        if (leftSide)
        {
            leftWallBase = vertexCountPerRow;
            vertexCountPerRow += lrWallVertexCount;
            if (doubleSidedWalls) vertexCountPerRow += lrWallVertexCount;
        }
        if (rightSide)
        {
            rightWallBase = vertexCountPerRow;
            vertexCountPerRow += lrWallVertexCount;
            if (doubleSidedWalls) vertexCountPerRow += lrWallVertexCount;
        }
        if (isWall)
        {
            if (leftOuter)
            {
                leftOuterWallBase = vertexCountPerRow;
                vertexCountPerRow += 4;
            }
            if (rightOuter)
            {
                rightOuterWallBase = vertexCountPerRow;
                vertexCountPerRow += 4;
            }
            undersideBase = vertexCountPerRow;
            vertexCountPerRow += 2;
        }
        if (haveCeiling)
        {
            ceilingBase = vertexCountPerRow;
            vertexCountPerRow += ceilingVertexCount;
        }

        int rowVertexCount = vertexCountPerRow * rowCount;

        // caps only ever render through an outer-wall submesh, so a side without
        // one gets no cap geometry at all
        int leftCapsBase = rowVertexCount;
        int leftCapVertexCount = leftOuter ? ((closedStartLeft ? 1 : 0) + (closedEndLeft ? 1 : 0)) * 4 : 0;
        int rightCapsBase = leftCapsBase + leftCapVertexCount;
        int rightCapVertexCount = rightOuter ? ((closedStartRight ? 1 : 0) + (closedEndRight ? 1 : 0)) * 4 : 0;

        int vertexCount = rowVertexCount + leftCapVertexCount + rightCapVertexCount;

        // ---- index layout ------------------------------------------------------
        int sidedMultiplier = doubleSidedWalls ? 2 : 1;
        int leftWallIndexCount = leftSide ? spanCount * (lrWallVertexCount - 1) * 6 * sidedMultiplier : 0;
        int rightWallIndexCount = rightSide ? spanCount * (lrWallVertexCount - 1) * 6 * sidedMultiplier : 0;

        int leftOuterIndexCount = leftOuter ? (spanCount * 12) + (leftCapVertexCount / 4 * 6) : 0;
        int rightOuterIndexCount = rightOuter ? (spanCount * 12) + (rightCapVertexCount / 4 * 6) : 0;
        int undersideIndexCount = isWall ? spanCount * 6 : 0;
        int ceilingIndexCount = haveCeiling ? spanCount * (ceilingVertexCount - 1) * 6 : 0;

        int leftWallStart = 0;
        int rightWallStart = leftWallStart + leftWallIndexCount;
        int leftOuterStart = rightWallStart + rightWallIndexCount;
        int rightOuterStart = leftOuterStart + leftOuterIndexCount;
        int undersideStart = rightOuterStart + rightOuterIndexCount;
        int ceilingStart = undersideStart + undersideIndexCount;
        int indexCount = ceilingStart + ceilingIndexCount;

        var data = dataArray[dataIndex];
        data.SetVertexBufferParams(vertexCount, SDLVertexLayout);
        data.SetIndexBufferParams(indexCount, IndexFormat.UInt16);

        var sdlVertices = data.GetVertexData<SDLVertex>();
        var sdlIndices = data.GetIndexData<ushort>();

        // ---- uv strips ---------------------------------------------------------
        var uvsLeft = VerticalStripMap(roadVerts, rowBreadth, 0, tunnelHeight);
        var uvsRight = VerticalStripMap(roadVerts, rowBreadth, rowBreadth - 1, tunnelHeight);

        Vector2[] wallUvsLeft = null;
        Vector2[] wallUvsRight = null;
        Vector2[] undersideUvs = null;
        Vector2[] ceilingUvs = null;

        if (isWall)
        {
            if (leftOuter) wallUvsLeft = VerticalStripMap(roadVerts, rowBreadth, 0, wallThickness);
            if (rightOuter) wallUvsRight = VerticalStripMap(roadVerts, rowBreadth, rowBreadth - 1, wallThickness);
            undersideUvs = StripMap(roadVerts, rowBreadth, 0, rowBreadth - 1);
        }
        if (haveCeiling)
            ceilingUvs = StripMap(roadVerts, rowBreadth, 0, rowBreadth - 1);

        // ---- row vertices ------------------------------------------------------
        for (int i = 0; i < rowCount; i++)
        {
            int vb = i * rowBreadth;
            int uvBase = i * 2;
            int w = i * vertexCountPerRow;

            var leftVertex = FlipVertex(roadVerts[vb]);
            var rightVertex = FlipVertex(roadVerts[vb + rowBreadth - 1]);
            var leftTopVertex = leftVertex + wallHeightVec;
            var rightTopVertex = rightVertex + wallHeightVec;

            var uvBottomLeft = uvsLeft[uvBase];
            var uvTopLeft = uvsLeft[uvBase + 1];
            var uvBottomRight = uvsRight[uvBase];
            var uvTopRight = uvsRight[uvBase + 1];

            var leftInnerVertex = FlipVertex(roadVerts[vb + 1]);
            var leftSideDir = -FlatDirectionBetween(leftVertex, leftInnerVertex);

            var rightInnerVertex = FlipVertex(roadVerts[vb + rowBreadth - 2]);
            var rightSideDir = -FlatDirectionBetween(rightVertex, rightInnerVertex);

            // the end/start left flag is used to determine if the roof should be flattened
            bool doCurvature = !((i == 0 && closedStartLeft) || (i == rowCount - 1 && closedEndLeft));

            if (leftSide)
            {
                if (curvedSides)
                {
                    var outerPos = leftVertex + (leftSideDir * wallThickness);
                    var uv25 = Vector2.Lerp(uvBottomLeft, uvTopLeft, 0.25f);
                    var uv75 = Vector2.Lerp(uvBottomLeft, uvTopLeft, 0.75f);
                    var basePos = doCurvature ? outerPos : leftVertex;

                    sdlVertices[w] = new SDLVertex() { pos = leftVertex, uv = uvBottomLeft };
                    sdlVertices[w + 1] = new SDLVertex() { pos = basePos + (wallHeightVec * 0.25f), uv = uv25 };
                    sdlVertices[w + 2] = new SDLVertex() { pos = basePos + (wallHeightVec * 0.75f), uv = uv75 };
                    sdlVertices[w + 3] = new SDLVertex() { pos = leftTopVertex, uv = uvTopLeft };
                    w += 4;

                    if (doubleSidedWalls)
                    {
                        sdlVertices[w] = new SDLVertex() { pos = leftTopVertex, uv = uvTopLeft };
                        sdlVertices[w + 1] = new SDLVertex() { pos = basePos + (wallHeightVec * 0.75f), uv = uv75 };
                        sdlVertices[w + 2] = new SDLVertex() { pos = basePos + (wallHeightVec * 0.25f), uv = uv25 };
                        sdlVertices[w + 3] = new SDLVertex() { pos = leftVertex, uv = uvBottomLeft };
                        w += 4;
                    }
                }
                else
                {
                    sdlVertices[w] = new SDLVertex() { pos = leftVertex, uv = uvBottomLeft };
                    sdlVertices[w + 1] = new SDLVertex() { pos = leftTopVertex, uv = uvTopLeft };
                    w += 2;

                    if (doubleSidedWalls)
                    {
                        sdlVertices[w] = new SDLVertex() { pos = leftTopVertex, uv = uvTopLeft };
                        sdlVertices[w + 1] = new SDLVertex() { pos = leftVertex, uv = uvBottomLeft };
                        w += 2;
                    }
                }
            }

            if (rightSide)
            {
                if (curvedSides)
                {
                    var outerPos = rightVertex + (rightSideDir * wallThickness);
                    var uv25 = Vector2.Lerp(uvBottomRight, uvTopRight, 0.25f);
                    var uv75 = Vector2.Lerp(uvBottomRight, uvTopRight, 0.75f);
                    var basePos = doCurvature ? outerPos : rightVertex;

                    sdlVertices[w] = new SDLVertex() { pos = rightVertex, uv = uvBottomRight };
                    sdlVertices[w + 1] = new SDLVertex() { pos = basePos + (wallHeightVec * 0.25f), uv = uv25 };
                    sdlVertices[w + 2] = new SDLVertex() { pos = basePos + (wallHeightVec * 0.75f), uv = uv75 };
                    sdlVertices[w + 3] = new SDLVertex() { pos = rightTopVertex, uv = uvTopRight };
                    w += 4;

                    if (doubleSidedWalls)
                    {
                        sdlVertices[w] = new SDLVertex() { pos = rightTopVertex, uv = uvTopRight };
                        sdlVertices[w + 1] = new SDLVertex() { pos = basePos + (wallHeightVec * 0.75f), uv = uv75 };
                        sdlVertices[w + 2] = new SDLVertex() { pos = basePos + (wallHeightVec * 0.25f), uv = uv25 };
                        sdlVertices[w + 3] = new SDLVertex() { pos = rightVertex, uv = uvBottomRight };
                        w += 4;
                    }
                }
                else
                {
                    sdlVertices[w] = new SDLVertex() { pos = rightVertex, uv = uvBottomRight };
                    sdlVertices[w + 1] = new SDLVertex() { pos = rightTopVertex, uv = uvTopRight };
                    w += 2;

                    if (doubleSidedWalls)
                    {
                        sdlVertices[w] = new SDLVertex() { pos = rightTopVertex, uv = uvTopRight };
                        sdlVertices[w + 1] = new SDLVertex() { pos = rightVertex, uv = uvBottomRight };
                        w += 2;
                    }
                }
            }

            if (isWall)
            {
                var uvUndersideLeft = undersideUvs[uvBase];
                var uvUndersideRight = undersideUvs[uvBase + 1];

                var leftOuterOffset = Vector3.zero;
                if (i == 0 && (flags & TunnelFlags.OffsetStartLeft) != 0)
                    leftOuterOffset = MitreOffset(leftSideDir, wallThickness, true, true);
                else if (i == rowCount - 1 && (flags & TunnelFlags.OffsetEndLeft) != 0)
                    leftOuterOffset = MitreOffset(leftSideDir, wallThickness, false, true);

                var rightOuterOffset = Vector3.zero;
                if (i == 0 && (flags & TunnelFlags.OffsetStartRight) != 0)
                    rightOuterOffset = MitreOffset(rightSideDir, wallThickness, true, false);
                else if (i == rowCount - 1 && (flags & TunnelFlags.OffsetEndRight) != 0)
                    rightOuterOffset = MitreOffset(rightSideDir, wallThickness, false, false);

                // a side with no wall has nothing to push the underside out to, so
                // it stops at the road edge
                var leftBottomOuterWallVertex = leftOuter
                    ? leftVertex + (leftSideDir * wallThickness) + leftOuterOffset - wallThirdHeightVec
                    : leftVertex - wallThirdHeightVec;

                var rightBottomOuterWallVertex = rightOuter
                    ? rightVertex + (rightSideDir * wallThickness) + rightOuterOffset - wallThirdHeightVec
                    : rightVertex - wallThirdHeightVec;

                if (leftOuter)
                {
                    var leftTopOuterWallVertex = leftTopVertex + (leftSideDir * wallThickness) + leftOuterOffset;

                    // outer face, then the strip back across the top to the inner wall
                    sdlVertices[w] = new SDLVertex() { pos = leftBottomOuterWallVertex, uv = uvBottomLeft };
                    sdlVertices[w + 1] = new SDLVertex() { pos = leftTopOuterWallVertex, uv = uvTopLeft };
                    sdlVertices[w + 2] = new SDLVertex() { pos = leftTopOuterWallVertex, uv = wallUvsLeft[uvBase] };
                    sdlVertices[w + 3] = new SDLVertex() { pos = leftTopVertex, uv = wallUvsLeft[uvBase + 1] };
                    w += 4;
                }

                if (rightOuter)
                {
                    var rightTopOuterWallVertex = rightTopVertex + (rightSideDir * wallThickness) + rightOuterOffset;

                    sdlVertices[w] = new SDLVertex() { pos = rightBottomOuterWallVertex, uv = uvBottomRight };
                    sdlVertices[w + 1] = new SDLVertex() { pos = rightTopOuterWallVertex, uv = uvTopRight };
                    sdlVertices[w + 2] = new SDLVertex() { pos = rightTopOuterWallVertex, uv = wallUvsRight[uvBase] };
                    sdlVertices[w + 3] = new SDLVertex() { pos = rightTopVertex, uv = wallUvsRight[uvBase + 1] };
                    w += 4;
                }

                // underside
                sdlVertices[w] = new SDLVertex() { pos = rightBottomOuterWallVertex, uv = uvUndersideRight };
                sdlVertices[w + 1] = new SDLVertex() { pos = leftBottomOuterWallVertex, uv = uvUndersideLeft };
                w += 2;
            }

            if (flatCeiling)
            {
                sdlVertices[w] = new SDLVertex() { pos = leftTopVertex, uv = ceilingUvs[uvBase] };
                sdlVertices[w + 1] = new SDLVertex() { pos = rightTopVertex, uv = ceilingUvs[uvBase + 1] };
                w += 2;
            }
            else if (curvedCeiling)
            {
                var uvCeilingLeft = ceilingUvs[uvBase];
                var uvCeilingRight = ceilingUvs[uvBase + 1];

                var centerCeilingVertex = Vector3.Lerp(leftTopVertex, rightTopVertex, 0.5f);
                var leftCeilingVertex = Vector3.Lerp(leftTopVertex, centerCeilingVertex, 0.5f);
                var rightCeilingVertex = Vector3.Lerp(rightTopVertex, centerCeilingVertex, 0.5f);

                var centerCeilingUv = Vector2.Lerp(uvCeilingLeft, uvCeilingRight, 0.5f);
                var leftCeilingUv = Vector2.Lerp(uvCeilingLeft, centerCeilingUv, 0.5f);
                var rightCeilingUv = Vector2.Lerp(uvCeilingRight, centerCeilingUv, 0.5f);

                if (doCurvature)
                {
                    leftCeilingVertex.y += 1.5f;
                    rightCeilingVertex.y += 1.5f;
                    centerCeilingVertex.y += 2f;
                }

                sdlVertices[w] = new SDLVertex() { pos = leftTopVertex, uv = uvCeilingLeft };
                sdlVertices[w + 1] = new SDLVertex() { pos = leftCeilingVertex, uv = leftCeilingUv };
                sdlVertices[w + 2] = new SDLVertex() { pos = centerCeilingVertex, uv = centerCeilingUv };
                sdlVertices[w + 3] = new SDLVertex() { pos = rightCeilingVertex, uv = rightCeilingUv };
                sdlVertices[w + 4] = new SDLVertex() { pos = rightTopVertex, uv = uvCeilingRight };
                w += 5;
            }
        }

        // ---- cap vertices ------------------------------------------------------
        int capCursor = leftCapsBase;

        if (leftOuter && closedStartLeft)
        {
            WriteTunnelCap(sdlVertices, capCursor, roadVerts[0], roadVerts[1],
                           wallHeightVec, wallThirdHeightVec, wallThickness,
                           true, true, (flags & TunnelFlags.OffsetStartLeft) != 0);
            capCursor += 4;
        }
        if (leftOuter && closedEndLeft)
        {
            int b = (rowCount - 1) * rowBreadth;
            WriteTunnelCap(sdlVertices, capCursor, roadVerts[b], roadVerts[b + 1],
                           wallHeightVec, wallThirdHeightVec, wallThickness,
                           false, true, (flags & TunnelFlags.OffsetEndLeft) != 0);
            capCursor += 4;
        }

        capCursor = rightCapsBase;

        if (rightOuter && closedStartRight)
        {
            int b = rowBreadth - 2;
            WriteTunnelCap(sdlVertices, capCursor, roadVerts[b + 1], roadVerts[b],
                           wallHeightVec, wallThirdHeightVec, wallThickness,
                           true, false, (flags & TunnelFlags.OffsetStartRight) != 0);
            capCursor += 4;
        }
        if (rightOuter && closedEndRight)
        {
            int b = ((rowCount - 1) * rowBreadth) + (rowBreadth - 2);
            WriteTunnelCap(sdlVertices, capCursor, roadVerts[b + 1], roadVerts[b],
                           wallHeightVec, wallThirdHeightVec, wallThickness,
                           false, false, (flags & TunnelFlags.OffsetEndRight) != 0);
            capCursor += 4;
        }

        // ---- row indices -------------------------------------------------------
        int leftWallCursor = leftWallStart;
        int rightWallCursor = rightWallStart;
        int leftOuterCursor = leftOuterStart;
        int rightOuterCursor = rightOuterStart;
        int undersideCursor = undersideStart;
        int ceilingCursor = ceilingStart;

        for (int i = 0; i < spanCount; i++)
        {
            int rowBase = i * vertexCountPerRow;
            int rowBaseNext = (i + 1) * vertexCountPerRow;

            if (leftSide)
            {
                for (int j = 0; j < lrWallVertexCount - 1; j++)
                {
                    int a = rowBase + leftWallBase + j;
                    int b = a + 1;
                    int c = rowBaseNext + leftWallBase + j;
                    int d = c + 1;

                    WriteTri(sdlIndices, leftWallCursor, b, c, a);
                    WriteTri(sdlIndices, leftWallCursor + 3, d, c, b);
                    leftWallCursor += 6;

                    if (doubleSidedWalls)
                    {
                        int o = lrWallVertexCount;
                        WriteTri(sdlIndices, leftWallCursor, b + o, c + o, a + o);
                        WriteTri(sdlIndices, leftWallCursor + 3, d + o, c + o, b + o);
                        leftWallCursor += 6;
                    }
                }
            }

            if (rightSide)
            {
                for (int j = 0; j < lrWallVertexCount - 1; j++)
                {
                    int a = rowBase + rightWallBase + j;
                    int b = a + 1;
                    int c = rowBaseNext + rightWallBase + j;
                    int d = c + 1;

                    WriteTri(sdlIndices, rightWallCursor, a, c, b);
                    WriteTri(sdlIndices, rightWallCursor + 3, b, c, d);
                    rightWallCursor += 6;

                    if (doubleSidedWalls)
                    {
                        int o = lrWallVertexCount;
                        WriteTri(sdlIndices, rightWallCursor, a + o, c + o, b + o);
                        WriteTri(sdlIndices, rightWallCursor + 3, b + o, c + o, d + o);
                        rightWallCursor += 6;
                    }
                }
            }

            if (leftOuter)
            {
                int aLO = rowBase + leftOuterWallBase;
                int cLO = rowBaseNext + leftOuterWallBase;

                WriteTri(sdlIndices, leftOuterCursor, aLO, cLO, aLO + 1);
                WriteTri(sdlIndices, leftOuterCursor + 3, aLO + 1, cLO, cLO + 1);
                WriteTri(sdlIndices, leftOuterCursor + 6, aLO + 2, cLO + 2, aLO + 3);
                WriteTri(sdlIndices, leftOuterCursor + 9, aLO + 3, cLO + 2, cLO + 3);
                leftOuterCursor += 12;
            }

            if (rightOuter)
            {
                int aRO = rowBase + rightOuterWallBase;
                int cRO = rowBaseNext + rightOuterWallBase;

                WriteTri(sdlIndices, rightOuterCursor, aRO + 1, cRO, aRO);
                WriteTri(sdlIndices, rightOuterCursor + 3, cRO + 1, cRO, aRO + 1);
                WriteTri(sdlIndices, rightOuterCursor + 6, aRO + 3, cRO + 2, aRO + 2);
                WriteTri(sdlIndices, rightOuterCursor + 9, cRO + 3, cRO + 2, aRO + 3);
                rightOuterCursor += 12;
            }

            if (isWall)
            {
                int aU = rowBase + undersideBase;
                int cU = rowBaseNext + undersideBase;

                WriteTri(sdlIndices, undersideCursor, aU, cU, aU + 1);
                WriteTri(sdlIndices, undersideCursor + 3, aU + 1, cU, cU + 1);
                undersideCursor += 6;
            }

            if (haveCeiling)
            {
                for (int j = 0; j < ceilingVertexCount - 1; j++)
                {
                    int a = rowBase + ceilingBase + j;
                    int b = a + 1;
                    int c = rowBaseNext + ceilingBase + j;
                    int d = c + 1;

                    WriteTri(sdlIndices, ceilingCursor, a, b, c);
                    WriteTri(sdlIndices, ceilingCursor + 3, b, d, c);
                    ceilingCursor += 6;
                }
            }
        }

        // ---- cap indices -------------------------------------------------------
        if (leftOuter)
        {
            int capVertex = leftCapsBase;
            if (closedStartLeft)
            {
                WriteCapQuad(sdlIndices, leftOuterCursor, capVertex);
                leftOuterCursor += 6;
                capVertex += 4;
            }
            if (closedEndLeft)
            {
                WriteCapQuad(sdlIndices, leftOuterCursor, capVertex);
                leftOuterCursor += 6;
                capVertex += 4;
            }
        }

        if (rightOuter)
        {
            int capVertex = rightCapsBase;
            if (closedStartRight)
            {
                WriteCapQuad(sdlIndices, rightOuterCursor, capVertex);
                rightOuterCursor += 6;
                capVertex += 4;
            }
            if (closedEndRight)
            {
                WriteCapQuad(sdlIndices, rightOuterCursor, capVertex);
                rightOuterCursor += 6;
                capVertex += 4;
            }
        }

        CalculateNormals(sdlVertices, sdlIndices);
        BakeLighting(sdlVertices);

        int subMesh = 0;
        data.subMeshCount = GetRoadTunnelSubMeshCount(flags);

        if (leftSide) SetTriSubMesh(data, subMesh++, leftWallStart, leftWallIndexCount, vertexCount);
        if (rightSide) SetTriSubMesh(data, subMesh++, rightWallStart, rightWallIndexCount, vertexCount);
        if (isWall)
        {
            if (leftOuter) SetTriSubMesh(data, subMesh++, leftOuterStart, leftOuterIndexCount, vertexCount);
            if (rightOuter) SetTriSubMesh(data, subMesh++, rightOuterStart, rightOuterIndexCount, vertexCount);
            SetTriSubMesh(data, subMesh++, undersideStart, undersideIndexCount, vertexCount);
        }
        if (haveCeiling) SetTriSubMesh(data, subMesh++, ceilingStart, ceilingIndexCount, vertexCount);
    }

    private void DrawFacadeBound(ref Mesh.MeshDataArray dataArray, int dataIndex, FacadeBoundElement bound)
    {
        var data = dataArray[dataIndex];

        data.SetVertexBufferParams(4, CollisionVertexLayout);
        data.SetIndexBufferParams(6, IndexFormat.UInt16);

        var positions = data.GetVertexData<Vector3>();
        var indices = data.GetIndexData<ushort>();

        var leftSrc = bound.Vertices[0];
        var rightSrc = bound.Vertices[1];

        positions[0] = new Vector3(-leftSrc.x, leftSrc.y, leftSrc.z);
        positions[1] = new Vector3(-rightSrc.x, rightSrc.y, rightSrc.z);
        positions[2] = new Vector3(-leftSrc.x, bound.Height, leftSrc.z);
        positions[3] = new Vector3(-rightSrc.x, bound.Height, rightSrc.z);

        // pairs are (bottom) 0,1 and (top) 2,3 - strip ordering, not perimeter
        WriteStripQuad(indices, 0, 0, 2);

        data.subMeshCount = 1;
        SetTriSubMesh(data, 0, 0, 6, 4);
    }

    private static void WriteTunnelCap(NativeArray<SDLVertex> sdlVertices, int at,
                                       Vertex innerSrc, Vertex refSrc,
                                       Vector3 wallHeightVec, Vector3 wallThirdHeightVec,
                                       float wallThickness, bool isStart, bool isLeft, bool mitre)
    {
        var innerWallVertex = FlipVertex(innerSrc);
        var innerWallTopVertex = innerWallVertex + wallHeightVec;

        var sideDir = -FlatDirectionBetween(innerWallVertex, FlipVertex(refSrc));
        var offset = mitre ? MitreOffset(sideDir, wallThickness, isStart, isLeft) : Vector3.zero;

        var outerWallTopVertex = innerWallTopVertex + (sideDir * wallThickness) + offset;
        var outerWallVertex = innerWallVertex + (sideDir * wallThickness) + offset - wallThirdHeightVec;

        if (isStart != isLeft)
        {
            sdlVertices[at] = new SDLVertex() { pos = outerWallVertex, uv = new Vector2(0f, 0f) };
            sdlVertices[at + 1] = new SDLVertex() { pos = innerWallVertex, uv = new Vector2(1f, 0f) };
            sdlVertices[at + 2] = new SDLVertex() { pos = innerWallTopVertex, uv = new Vector2(1f, 1f) };
            sdlVertices[at + 3] = new SDLVertex() { pos = outerWallTopVertex, uv = new Vector2(0f, 1f) };
        }
        else
        {
            sdlVertices[at] = new SDLVertex() { pos = innerWallVertex, uv = new Vector2(1f, 0f) };
            sdlVertices[at + 1] = new SDLVertex() { pos = outerWallVertex, uv = new Vector2(0f, 0f) };
            sdlVertices[at + 2] = new SDLVertex() { pos = outerWallTopVertex, uv = new Vector2(0f, 1f) };
            sdlVertices[at + 3] = new SDLVertex() { pos = innerWallTopVertex, uv = new Vector2(1f, 1f) };
        }
    }

    private static int GetRoadTunnelSubMeshCount(TunnelFlags flags)
    {
        bool leftSide = (flags & TunnelFlags.LeftSide) != 0;
        bool rightSide = (flags & TunnelFlags.RightSide) != 0;

        int count = 0;
        if (leftSide) count++;
        if (rightSide) count++;
        if ((flags & TunnelFlags.IsWall) != 0)
        {
            // outer faces only exist where there's a wall; the underside always does
            if (leftSide) count++;
            if (rightSide) count++;
            count++;
        }
        if ((flags & (TunnelFlags.FlatCeiling | TunnelFlags.CurvedCeiling)) != 0) count++;
        return count;
    }

    private Material[] GetTunnelMaterials(TunnelElement tunnel, int subMeshCount)
    {
        var textures = tunnel.Textures;
        int fallback = textures[0];

        int Texture(int slot)
            => (slot < textures.Length && textures[slot] >= 0) ? textures[slot] : fallback;

        var flags = (TunnelFlags)tunnel.Flags;
        bool isWall = (flags & TunnelFlags.IsWall) != 0;
        bool haveCeiling = (flags & (TunnelFlags.FlatCeiling | TunnelFlags.CurvedCeiling)) != 0;

        var materials = new Material[subMeshCount];
        int subMesh = 0;

        if (tunnel.IsJunctionTunnel)
        {
            materials[subMesh++] = GetOrCreateMaterial(Texture(0));      // inner walls
            if (isWall)
            {
                materials[subMesh++] = GetOrCreateMaterial(Texture(4));  // outer walls
                materials[subMesh++] = GetOrCreateMaterial(Texture(5));  // underside
            }
            if ((flags & TunnelFlags.FlatCeiling) != 0)
                materials[subMesh++] = GetOrCreateMaterial(Texture(2));  // ceiling
        }
        else
        {
            bool leftSide = (flags & TunnelFlags.LeftSide) != 0;
            bool rightSide = (flags & TunnelFlags.RightSide) != 0;

            if (leftSide)
                materials[subMesh++] = GetOrCreateMaterial(Texture(0));
            if (rightSide)
                materials[subMesh++] = GetOrCreateMaterial(Texture(1));
            if (isWall)
            {
                if (leftSide)
                    materials[subMesh++] = GetOrCreateMaterial(Texture(4));   // left outer
                if (rightSide)
                    materials[subMesh++] = GetOrCreateMaterial(Texture(3));   // right outer
                materials[subMesh++] = GetOrCreateMaterial(Texture(5));       // underside
            }
            if (haveCeiling)
                materials[subMesh++] = GetOrCreateMaterial(Texture(2));
        }

        return materials;
    }

    private bool ElementIsBuildable(Room room, int elementIndex)
    {
        var element = room.Elements[elementIndex];
        if (element.Type == ElementType.FacadeBound)
        {
            var bound = (FacadeBoundElement)element;
            float h1 = bound.Vertices[0].y;
            float h2 = bound.Vertices[1].y;
            return bound.Height > h1 && bound.Height > h2;
        }
        if (!ElementTexturesAreValid(element)) return false;

        switch (element.Type)
        {
            case ElementType.Tunnel:
                {
                    var tunnel = (TunnelElement)element;
                    if (tunnel.IsJunctionTunnel)
                        return room.Perimeter != null && room.Perimeter.Count >= 3 && tunnel.WallBits != 0;

                    var wallFlags = (TunnelFlags.LeftSide | TunnelFlags.RightSide);
                    if ((tunnel.Flags & wallFlags) == 0) return false;

                    var roadElement = FindTunnelRoadElement(room, elementIndex);
                    if (roadElement == null) return false;

                    return roadElement.RowCount >= 2;
                }
            case ElementType.TriangleFan:
                return ((TriangleFanElement)element).Vertices.Count >= 3 && TriangleFanValid(((TriangleFanElement)element).Vertices);
            case ElementType.RoofTriangleFan:
                return ((RoofTriangleFanElement)element).Vertices.Count >= 3 && TriangleFanValid(((RoofTriangleFanElement)element).Vertices);
            case ElementType.CulledTriangleFan:
                return ((CulledTriangleFanElement)element).Vertices.Count >= 3 && TriangleFanValid(((CulledTriangleFanElement)element).Vertices);
            case ElementType.Facade:
                {
                    var facade = (FacadeElement)element;
                    return facade.UTiling <= 128 && facade.VTiling <= 128 && facade.BottomHeight < facade.TopHeight;
                }
            case ElementType.Walkway: return ((WalkwayElement)element).Vertices.Count >= 4;
            case ElementType.Road: return ((RoadElement)element).Vertices.Count >= 8;
            case ElementType.DividedRoad: return ((DividedRoadElement)element).Vertices.Count >= 12;
            case ElementType.SidewalkStrip:
                {
                    var strip = (SidewalkStripElement)element;
                    return strip.Vertices.Count >= 4 || strip.IsStartCap || strip.IsEndCap;
                }
            case ElementType.Crosswalk: return true;
            case ElementType.Sliver:
                {
                    var sliver = (SliverElement)element;
                    // both ends sitting above the top edge means the sliver is underground
                    return !(sliver.Vertices[0].y > sliver.Height && sliver.Vertices[1].y > sliver.Height);
                }
        }
        return true;
    }

    private int GetBuildableElementCount(Room room)
    {
        int count = 0;
        for (int i = 0; i < room.Elements.Count; i++)
        {
            if (ElementIsBuildable(room, i))
                count++;
        }
        return count;
    }

    private GameObject BuildElement(Room room, int elementIndex, ref Mesh.MeshDataArray dataArray, int dataIndex)
    {
        var element = room.Elements[elementIndex];

        GameObject built = null;
        switch (element.Type)
        {
            case ElementType.RoofTriangleFan:
                built = new GameObject("RoofTriangleFan", typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider));
                DrawRoofTriangleFan(ref dataArray, dataIndex, (RoofTriangleFanElement)element);
                break;
            case ElementType.TriangleFan:
                built = new GameObject("TriangleFan", typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider));
                DrawTriangleFan(ref dataArray, dataIndex, (TriangleFanElement)element);
                break;
            case ElementType.CulledTriangleFan:
                built = new GameObject("CulledTriFan", typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider));
                DrawCulledTriangleFan(ref dataArray, dataIndex, (CulledTriangleFanElement)element);
                break;
            case ElementType.Facade:
                built = new GameObject("Facade", typeof(MeshFilter), typeof(MeshRenderer));
                DrawFacade(ref dataArray, dataIndex, (FacadeElement)element);
                break;
            case ElementType.Walkway:
                built = new GameObject("Walkway", typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider));
                DrawWalkway(ref dataArray, dataIndex, (WalkwayElement)element);
                break;
            case ElementType.Road:
                built = new GameObject("Road", typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider));
                DrawRoad(ref dataArray, dataIndex, (RoadElement)element);
                break;
            case ElementType.DividedRoad:
                built = new GameObject("DivRoad", typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider));
                DrawDividedRoad(ref dataArray, dataIndex, (DividedRoadElement)element);
                break;
            case ElementType.SidewalkStrip:
                built = new GameObject("SidewalkStrip", typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider));
                DrawSidewalkStrip(ref dataArray, dataIndex, (SidewalkStripElement)element);
                break;
            case ElementType.Crosswalk:
                built = new GameObject("Crosswalk", typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider));
                DrawCrosswalk(ref dataArray, dataIndex, (CrosswalkElement)element);
                break;
            case ElementType.Sliver:
                built = new GameObject("Sliver", typeof(MeshFilter), typeof(MeshRenderer));
                DrawSliver(ref dataArray, dataIndex, (SliverElement)element);
                break;
            case ElementType.Tunnel:
                {
                    var tunnel = (TunnelElement)element;
                    if (tunnel.IsJunctionTunnel)
                    {
                        built = new GameObject("JunctionTunnel", typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider));
                        DrawJunctionTunnel(ref dataArray, dataIndex, room, tunnel);
                    }
                    else
                    {
                        var roadElement = FindTunnelRoadElement(room, elementIndex);
                        if (roadElement == null)
                        {
                            Debug.LogWarning($"Tunnel element {elementIndex} has no road/walkway/divroad after it");
                            break;
                        }
                        built = new GameObject("RoadTunnel", typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider));
                        DrawRoadTunnel(ref dataArray, dataIndex, tunnel, roadElement);
                    }
                    break;
                }
            case ElementType.FacadeBound:
                built = new GameObject("FacadeBound", typeof(MeshCollider));
                DrawFacadeBound(ref dataArray, dataIndex, (FacadeBoundElement)element);
                break;
        }

        if (built != null)
        {
            built.isStatic = true;
            var builtRenderer = built.GetComponent<MeshRenderer>();
            if (builtRenderer != null)
            {
                int subMeshCount = dataArray[dataIndex].subMeshCount;

                if (element is TunnelElement tunnelElement)
                {
                    builtRenderer.sharedMaterials = GetTunnelMaterials(tunnelElement, subMeshCount);
                }
                else if (element is DividedRoadElement dividedRoad)
                {
                    builtRenderer.sharedMaterials = GetDividedRoadMaterials(dividedRoad, subMeshCount);
                }
                else
                {
                    int numMaterialsToLoad = Mathf.Min(element.Textures.Length, subMeshCount);
                    if (numMaterialsToLoad == 1)
                    {
                        builtRenderer.sharedMaterial = GetOrCreateMaterial(element.Textures[0]);
                    }
                    else
                    {
                        Material[] sharedMats = new Material[numMaterialsToLoad];
                        for (int i = 0; i < numMaterialsToLoad; i++)
                        {
                            sharedMats[i] = GetOrCreateMaterial(element.Textures[i]);
                        }
                        builtRenderer.sharedMaterials = sharedMats;
                    }
                }
            }
        }

        return built;
    }

    public GameObject BuildRoom(PSDL.Room room)
    {
        var root = new GameObject("Room");

        int drawable = GetBuildableElementCount(room);
        if (drawable == 0)
        {
            return root;
        }

        var meshes = new Mesh[drawable];
        var builtObjects = new GameObject[drawable];
        var dataArray = Mesh.AllocateWritableMeshData(drawable);
        int dataIndex = 0;

        for (int i = 0; i < room.Elements.Count; i++)
        {
            var element = room.Elements[i];
            if (ElementIsBuildable(room, i))
            {
                GameObject built = BuildElement(room, i, ref dataArray, dataIndex);
                if (built == null)
                    continue;

                built.transform.parent = root.transform;
                meshes[dataIndex] = new Mesh { name = built.name };
                builtObjects[dataIndex] = built;
                dataIndex++;
            }
        }

        for (int i = dataIndex; i < drawable; i++) meshes[i] = new Mesh();
        Mesh.ApplyAndDisposeWritableMeshData(dataArray, meshes, UpdateFlags);
        for (int i = 0; i < dataIndex; i++)
        {
            meshes[i].RecalculateBounds();

            // setup visuals now; collisions are cooked in bulk by ScheduleColliderBake
            var renderer = builtObjects[i].GetComponent<MeshRenderer>();
            var filter = builtObjects[i].GetComponent<MeshFilter>();
            if (filter != null)
            {
                filter.sharedMesh = meshes[i];
            }

            var collider = builtObjects[i].GetComponent<MeshCollider>();
            if (collider != null)
            {
                pendingColliders.Add(collider);
                pendingColliderMeshes.Add(meshes[i]);
                pendingNeedsBound.Add(city != null && renderer != null); // LevelBound for renderable items
            }
        }

        return root;
    }

    /// <summary>
    /// Starts cooking every collider mesh built so far on worker threads, without blocking.
    /// Call after the last BuildRoom. The meshes must not be modified or destroyed until
    /// CompleteColliderBake has run.
    /// </summary>
    public void ScheduleColliderBake()
    {
        if (colliderBakeScheduled || pendingColliderMeshes.Count == 0)
            return;

        int n = pendingColliderMeshes.Count;

        // Persistent rather than TempJob: the load may span more than 4 frames
        colliderBakeIds = new NativeArray<int>(n, Allocator.Persistent);
        for (int i = 0; i < n; i++)
            colliderBakeIds[i] = pendingColliderMeshes[i].GetInstanceID();

        colliderBakeHandle = new BakeMeshJob { MeshIds = colliderBakeIds }.Schedule(n, 4);
        JobHandle.ScheduleBatchedJobs(); // start now, not at the next sync point
        colliderBakeScheduled = true;
    }

    /// <summary>
    /// Waits for the bake (scheduling it first if needed), then assigns the collider meshes,
    /// which picks up the pre-baked data instead of cooking again.
    /// </summary>
    public void CompleteColliderBake()
    {
        if (pendingColliderMeshes.Count == 0)
            return;

        ScheduleColliderBake();

        colliderBakeHandle.Complete();
        colliderBakeIds.Dispose();
        colliderBakeScheduled = false;

        for (int i = 0; i < pendingColliders.Count; i++)
        {
            var collider = pendingColliders[i];
            if (collider == null) continue; // room destroyed mid-load

            collider.sharedMesh = pendingColliderMeshes[i];

            if (pendingNeedsBound[i])
                collider.gameObject.AddComponent<LevelBound>().Init(city, collider);
        }

        pendingColliders.Clear();
        pendingColliderMeshes.Clear();
        pendingNeedsBound.Clear();
    }

    public void Dispose()
    {
        CompleteColliderBake(); // safety net: never leave a job running or its array leaked
    }

    public SDLBuilder(PSDLFile psdl, SDLCity city)
    {
        this.psdl = psdl;
        this.city = city;

        int maxTexture = 0;
        foreach (var room in psdl.Rooms)
        {
            foreach (var element in room.Elements)
            {
                if (element is DividedRoadElement dr && dr.DividerTextures != null)
                {
                    foreach (var texture in dr.DividerTextures)
                    {
                        if (texture >= 0)
                            maxTexture = Mathf.Max(maxTexture, texture);
                    }
                }

                if (ElementTexturesAreValid(element))
                {
                    foreach (var texture in element.Textures)
                        maxTexture = Mathf.Max(maxTexture, texture);
                }
            }
        }

        materialCache = new Material[maxTexture + 1];
        CacheLights();
        cachedUpColor = CalculateLightingForNormal(Vector3.up);
    }
}