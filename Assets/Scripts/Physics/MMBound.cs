using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

[RequireComponent(typeof(MeshCollider))]
public class MMBound : BoundBase
{
    public MeshCollider Collider { get; private set; }

    private List<LevelPhysMaterial> _materials = new List<LevelPhysMaterial>();
    private Mesh _mesh;
    private int[] _submeshIndexCounts;

    /// <summary>
    /// Material count as reported by bound file
    /// </summary>
    public int MaterialCount => _materials.Count;
    /// <summary>
    /// Vertex count reported by bound file
    /// </summary>
    public int VertexCount { get; private set; }
    /// <summary>
    /// Edge count reported by bound file.
    /// </summary>
    public int EdgeCount { get; private set; }
    /// <summary>
    /// Polygon count reported by bound file. May not match mesh tri count
    /// </summary>
    public int PolygonCount { get; private set; }

    public static readonly MeshUpdateFlags UpdateFlags = MeshUpdateFlags.DontNotifyMeshUsers | MeshUpdateFlags.DontResetBoneBounds | MeshUpdateFlags.DontValidateIndices;

    public static readonly VertexAttributeDescriptor[] CollisionVertexLayout = new[]
    {
            new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3)
    };

    private void LoadASCII(string name, Stream stream)
    {
        //read header
        var boundReader = new TokenFileParser(stream);

        float version = boundReader.Read<float>("version:");
        if(version != 1.01f)
        {
            Debug.LogError($"Ascii bound {name} is an unsupported version (expected 1.01, got {version})");
            return;
        }

        int numVertices = boundReader.Read<int>("verts:");
        int numMaterials = boundReader.Read<int>("materials:");
        int numEdges = boundReader.Read<int>("edges:");
        int numPolys = boundReader.Read<int>("polys:");
        
        var submeshIndices = new List<int>[numMaterials];
        for (int i = 0; i < submeshIndices.Length; i++)
            submeshIndices[i] = new List<int>();

        //set info
        this.VertexCount = numVertices;
        this.PolygonCount = numPolys;
        this.EdgeCount = numEdges;

        //load
        Vector3[] vertices = new Vector3[numVertices];
        
        //read vertices
        for(int i=0; i < numVertices; i++)
        {
            if (!boundReader.SkipTo("v"))
                break;

            var tokens = boundReader.ReadTokens();
            vertices[i] = new Vector3(-tokens[1].ToFloat(), tokens[2].ToFloat(), tokens[3].ToFloat());
        }

        //read materials
        for(int i=0; i < numMaterials; i++)
        {
           submeshIndices[i] = new List<int>();

            boundReader.SkipTo("mtl");
            string materialName = boundReader.ReadToken(true, 1);
            _materials.Add(LevelMaterialManager.Load(materialName, boundReader));
        }

        //read polys
        for(int i=0; i < numPolys; i++)
        {
            if (!boundReader.SkipTo(8, "tri", "quad"))
                break;

            var tokens = boundReader.ReadTokens();
            string type = tokens[0].ToString();
            if(type == "tri")
            {
                int idx0 = tokens[1].ToInt();
                int idx1 = tokens[2].ToInt();
                int idx2 = tokens[3].ToInt();
                int mtl = tokens[4].ToInt();
                submeshIndices[mtl].AddRange(new int[] { idx2, idx1, idx0 });
            }
            else if(type == "quad")
            {
                int idx0 = tokens[1].ToInt();
                int idx1 = tokens[2].ToInt();
                int idx2 = tokens[3].ToInt();
                int idx3 = tokens[4].ToInt();
                int mtl = tokens[5].ToInt();
                submeshIndices[mtl].AddRange(new int[] { idx2, idx1, idx0, idx0, idx3, idx2 });
            }
        }

        //setup mesh
        Mesh mesh = new Mesh
        {
            subMeshCount = numMaterials,
            vertices = vertices
        };

        _submeshIndexCounts = new int[numMaterials];
        for (int i = 0; i < numMaterials; i++)
            _submeshIndexCounts[i] = submeshIndices[i].Count;

        for (int i=0; i < submeshIndices.Length; i++)
            mesh.SetTriangles(submeshIndices[i], i);
        mesh.RecalculateBounds();

        //finalize
        _mesh = mesh;
    }

    private bool LoadBinary(string name, Stream stream)
    {
        //read bound file
        var bbndReader = new BinaryReader(stream, System.Text.Encoding.ASCII, true);
        byte version = bbndReader.ReadByte();
        if (version != 1)
        {
            Debug.LogError($"Binary bound {name} is an unsupported version (expected 1, got {version})");
            return false;
        }

        int numVertices = bbndReader.ReadInt32();
        int numMaterials = bbndReader.ReadInt32();
        int numFaces = bbndReader.ReadInt32();

        var submeshIndices = new List<int>[numMaterials];
        for (int i = 0; i < submeshIndices.Length; i++)
            submeshIndices[i] = new List<int>();

        if (numVertices == 0 || numFaces == 0)
        {
            Debug.LogError($"Binary bound {name} is malformed. Has {numVertices} verts and {numFaces} faces.");
            return false;
        }

        //set info
        this.VertexCount = numVertices;
        this.PolygonCount = numFaces;

        //read verts
        Vector3[] vertices = new Vector3[numVertices];
        for (int i = 0; i < vertices.Length; i++)
        {
            vertices[i] = bbndReader.ReadVector3Flipped();
        }

        //read materials
        for (int i = 0; i < numMaterials; i++)
        {
            submeshIndices[i] = new List<int>();
            _materials.Add(LevelMaterialManager.Load(bbndReader));
        }

        //read faces
        for (int i = 0; i < numFaces; i++)
        {
            int index1 = bbndReader.ReadUInt16();
            int index2 = bbndReader.ReadUInt16();
            int index3 = bbndReader.ReadUInt16();
            int index4 = bbndReader.ReadUInt16();
            int material = bbndReader.ReadInt16();

            //invalid material?
            if (material >= numMaterials)
                material = 0;

            // if idx4 == 0, then we have a tri
            // else, we have a quad
            if (index4 != 0)
            {
                submeshIndices[material].AddRange(new int[] { index3, index2, index1, index1, index4, index3 });
            }
            else
            {
                submeshIndices[material].AddRange(new int[] { index3, index2, index1 });
            }
        }

        //setup and return mesh
        Mesh mesh = new Mesh
        {
            subMeshCount = numMaterials,
            vertices = vertices
        };

        _submeshIndexCounts = new int[numMaterials];
        for (int i = 0; i < numMaterials; i++)
            _submeshIndexCounts[i] = submeshIndices[i].Count;

        for (int i = 0; i < submeshIndices.Length; i++)
            mesh.SetTriangles(submeshIndices[i], i);
        mesh.RecalculateBounds();

        //finalize
        bbndReader.Close();
        _mesh = mesh;
        return true;
    }

    public bool LoadBinarySafe(string name, Stream stream)
    {
        try
        {
            return LoadBinary(name, stream);
        }
        catch (EndOfStreamException)
        {
            return false;
        }
    }

    private void SetupCollider()
    {
        Collider = GetComponent<MeshCollider>();
        Collider.sharedMesh = _mesh;
    }

    public static MMBound LoadFresh(GameObject obj, string name, bool required = false)
    {
        var stream = AssetManager.OpenBinary("bound", $"{name}_BOUND.bbnd", FileAccess.Read);

        if (stream != null)
        {
            var result = obj.AddComponent<MMBound>();
            if (result.LoadBinarySafe(name, stream))
            {
                result.SetupCollider();
                return result;
            }
        }

        stream = AssetManager.OpenBinary("bound", $"{name}_BOUND.bnd", FileAccess.Read);
        if (stream != null)
        {
            var result = obj.AddComponent<MMBound>();
            
            result.LoadASCII(name, stream);
            result.SetupCollider();
            return result;
        }

        if (required)
            Debug.LogError($"Missing required bound \"{name}\".");
        return null;
    }

    public void FlipXZ()
    {
        _mesh.FlipXZ();
        GetComponent<MeshCollider>().sharedMesh = _mesh;
    }

    public void DeletePolysWithMaterial(LevelPhysMaterial material)
    {
        //find the index of this material
        int materialIndex = -1;
        for (int i = 0; i < _mesh.subMeshCount; i++)
        {
            var indexMaterial = _materials[i];
            if (indexMaterial == material)
            {
                materialIndex = i;
                break;
            }
        }

        // bail if this material is not on this collider
        if (materialIndex < 0)
        {
            return;
        }

        //'delete' this submesh
        var colliderMesh = Instantiate(_mesh);
        colliderMesh.SetTriangles(new int[] { }, materialIndex);
    }

    public override bool HasPhysicsMaterial(LevelPhysMaterial material)
    {
        if (material == null || _materials == null || _submeshIndexCounts == null)
            return false;

        for (int i = 0; i < _materials.Count; i++)
        {
            if (_submeshIndexCounts[i] > 0 && _materials[i] == material)
                return true;
        }

        return false;
    }

    public override LevelPhysMaterial GetMaterial(int triangleIndex)
    {
        if (_submeshIndexCounts == null)
            return LevelMaterialManager.GetDefault();

        int limit = triangleIndex * 3;

        for (int submesh = 0; submesh < _submeshIndexCounts.Length; submesh++)
        {
            int numIndices = _submeshIndexCounts[submesh];

            if (numIndices > limit)
                return _materials[submesh];

            limit -= numIndices;
        }

        return LevelMaterialManager.GetDefault();
    }

    //monobehavior
    private void OnDisable()
    {
        GetComponent<MeshCollider>().enabled = false;
    }

    private void OnEnable()
    {
        GetComponent<MeshCollider>().enabled = true;
    }

    private void OnDestroy()
    {
        Destroy(GetComponent<MeshCollider>());
    }
}
