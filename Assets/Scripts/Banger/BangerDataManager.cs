using System.Collections.Generic;
using UnityEngine;

public class BangerDataManager : System.IDisposable
{
    public static BangerDataManager Instance => instance;
    private static BangerDataManager instance;

    private List<BangerData> dataList = new List<BangerData>();
    private Dictionary<string, int> dataIndices = new Dictionary<string, int>();

    public BangerDataManager()
    {
        instance = this;
    }

    public void Dispose()
    {
        foreach(var data in dataList)
        {
            if(data.CollisionMesh != null)
            {
                UnityEngine.Object.Destroy(data.CollisionMesh);
            }
        }
        instance = null;
    }

    public BangerData GetEntry(int index)
    {
        return dataList[index];
    }

    public BangerData GetEntry(string name)
    {
        if (dataIndices.TryGetValue(name, out int index))
        {
            return dataList[index];
        }
        return null;
    }

    public BangerData GetEntry(string name, string part)
    {
        return GetEntry($"{name}_{part}");
    }

    private void LoadColMesh(BangerData data)
    {
        var temp = new GameObject("TempBound");
        var bound = MMBound.LoadFresh(temp, data.Name);

        if (bound != null && bound.Collider.sharedMesh != null)
        {
            var mesh = Object.Instantiate(bound.Collider.sharedMesh);
            mesh.name = $"{data.Name}_col";

            // collision is in the banger's original space, but the transform gets
            // shifted up by CG - so bake the offset out once here instead of per instance
            if (data.CG != Vector3.zero)
            {
                var verts = mesh.vertices;
                for (int i = 0; i < verts.Length; i++)
                    verts[i] -= data.CG;

                mesh.vertices = verts;
                mesh.RecalculateBounds();
            }

            // cook now so the first MeshCollider assignment doesn't hitch mid-race
            Physics.BakeMesh(mesh.GetInstanceID(), false);
            Physics.BakeMesh(mesh.GetInstanceID(), true);

            data.CollisionMesh = mesh;
        }

        Object.Destroy(temp);
    }

    public int AddEntry(string name)
    {
        if (dataIndices.TryGetValue(name, out int index))
        {
            return index;
        }
        else
        {
            string bangerDataPath = AssetManager.CombinePath("tune", "banger", $"{name}.dgBangerData");
            var node = AssetManager.OpenNode(bangerDataPath);
            if (node == null)
            {
                return -1;
            }
            else
            {
                var data = new BangerData(name);
                data.ReadSettings(node);

                // tree hack prsent in original game
                if (name.Contains("_tree"))
                {
                    data.BillFlags |= BangerDataFlags.Unlit;
                }

                if (data.CollisionPrim == BangerCollisionPrimitive.Mesh)
                {
                    LoadColMesh(data);
                    if (data.CollisionMesh == null) data.CollisionPrim = BangerCollisionPrimitive.Box;
                }

                int newIndex = dataIndices.Count;
                data.ID = newIndex;
                dataIndices[name] = newIndex;
                dataList.Add(data);
                return newIndex;
            }
        }
    }

    public int AddEntry(string name, string part)
    {
        return AddEntry($"{name}_{part}");
    }
}
