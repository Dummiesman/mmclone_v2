using System.Collections.Generic;
using UnityEngine;

public class AIVehicleDataManager 
{
    private List<AIVehicleData> dataList = new List<AIVehicleData>();
    private Dictionary<string, int> dataIndices = new Dictionary<string, int>();

    private Vector3 GetPivot(string basename, string part)
    {
        string fileName = $"{basename}_{part}.mtx";
        if (!AssetManager.Exists("geometry", fileName))
            return Vector3.zero;

        var matrixFile = new MatrixFile();
        using (var stream = AssetManager.Open("geometry", fileName))
        {
            matrixFile.Load(stream);
            matrixFile = matrixFile.FlipXZ();
        }
        return matrixFile.Origin;
    }

    public AIVehicleData GetEntry(int index)
    {
        return dataList[index];
    }

    public AIVehicleData GetEntry(string name)
    {
        if (dataIndices.TryGetValue(name, out int index))
        {
            return dataList[index];
        }
        return null;
    }

    public AIVehicleData GetEntry(string name, string part)
    {
        return GetEntry($"{name}_{part}");
    }

    public int AddVehicleDataEntry(string vehicleName)
    {
        if(dataIndices.TryGetValue(vehicleName, out int existingId))
        {
            return existingId;
        }
        else
        {
            string dataPath = AssetManager.CombinePath("tune", "vehicle", $"{vehicleName}.aiVehicleData");
            if(AssetManager.Exists(dataPath))
            {
                var data = new AIVehicleData(AssetManager.OpenNode(dataPath));
                int newDataId = dataList.Count;
                
                data.ID = newDataId;
                data.Name = vehicleName;

                for(int i=0; i < 6; i++)
                {
                    data.WheelPositions[i] = GetPivot(vehicleName, $"WHL{i}");
                }

                dataIndices[vehicleName] = dataList.Count;
                dataList.Add(data);
                return data.ID;
            }
            else
            {
                return -1;
            }
        }
    }
}
