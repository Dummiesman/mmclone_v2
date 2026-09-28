using UnityEngine;

public class LevelBound : BoundBase
{
    private SDLCity _level;
    private Renderer _renderer;
    private Mesh _mesh;
    private short[] _submeshIndexCounts;

    public void Init(SDLCity level, MeshCollider collider)
    {
        _level = level;
        _renderer = collider.GetComponent<Renderer>();

        Mesh mesh = collider.sharedMesh;
        if (mesh == null)
            return;

        _mesh = mesh;
        _submeshIndexCounts = new short[mesh.subMeshCount];

        for (int i = 0; i < mesh.subMeshCount; i++)
            _submeshIndexCounts[i] = (short)mesh.GetIndexCount(i);
    }

    public override bool HasPhysicsMaterial(LevelPhysMaterial material)
    {
        if (_mesh == null || _renderer == null || material == null)
            return false;

        Material[] materials = _renderer.sharedMaterials;

        for (int i = 0; i < _mesh.subMeshCount && i < materials.Length; i++)
        {
            Material indexMaterial = materials[i];
            if (indexMaterial == null)
                continue;

            if (_level.LookupTextureMaterial(indexMaterial.name) == material)
                return true;
        }

        return false;
    }

    public override LevelPhysMaterial GetMaterial(int triangleIndex)
    {
        if (_submeshIndexCounts == null || _renderer == null)
            return LevelMaterialManager.GetDefault();

        int limit = triangleIndex * 3;

        for (int submesh = 0; submesh < _submeshIndexCounts.Length; submesh++)
        {
            int numIndices = _submeshIndexCounts[submesh];

            if (numIndices > limit)
            {
                Material material = _renderer.sharedMaterials[submesh];
                if (material == null) return LevelMaterialManager.GetDefault();
                return _level.LookupTextureMaterial(material.name);
            }

            limit -= numIndices;
        }

        return LevelMaterialManager.GetDefault();
    }


    public void DeletePolysWithMaterial(LevelPhysMaterial material)
    {
        if (_mesh == null || _renderer == null || material == null)
            return;

        // Find the submesh using this physics material.
        int materialIndex = -1;
        Material[] materials = _renderer.sharedMaterials;

        for (int i = 0; i < _mesh.subMeshCount && i < materials.Length; i++)
        {
            Material indexMaterial = materials[i];
            if (indexMaterial == null)
                continue;

            if (_level.LookupTextureMaterial(indexMaterial.name) == material)
            {
                materialIndex = i;
                break;
            }
        }

        // Material isn't present on this collider.
        if (materialIndex < 0)
            return;

        // Clone the mesh so we don't modify the shared asset.
        Mesh colliderMesh = Instantiate(_mesh);
        colliderMesh.SetTriangles(System.Array.Empty<int>(), materialIndex);

        _submeshIndexCounts[materialIndex] = 0;

        // Check whether anything remains in the mesh.
        bool hasTriangles = false;

        for (int i = 0; i < _submeshIndexCounts.Length; i++)
        {
            if (_submeshIndexCounts[i] > 0)
            {
                hasTriangles = true;
                break;
            }
        }

        MeshCollider collider = GetComponent<MeshCollider>();

        if (!hasTriangles)
        {
            // Nothing remains, so don't assign the empty mesh.
            collider.sharedMesh = null;

            Destroy(colliderMesh);

            _mesh = null;
            _submeshIndexCounts = null;
            return;
        }

        collider.sharedMesh = null;
        collider.sharedMesh = colliderMesh;

        _mesh = colliderMesh;
    }
}