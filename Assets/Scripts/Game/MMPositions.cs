using System.Collections.Generic;
using UnityEngine;

public class MMPositions 
{
    public readonly List<Vector4> Positions = new List<Vector4>();
    public readonly List<float> Scales = new List<float>();

    public void Reverse()
    {
        Positions.Reverse();
        Scales.Reverse();
    }

    public void Recall(int index, out Vector4 position, out float scale)
    {
        position = Positions[index];
        scale = Scales[index];
        if(scale == 0.0f)
        {
            scale = 15.0f;
        }
    }

    public void Load(string path)
    {
        var csv = AssetManager.OpenCSV(path);
        if(csv == null)
        {
            return;
        }
        else
        {
            csv.PrepareHeader();
            while(!csv.EOF())
            {
                csv.PrepareLine();

                Vector4 pos = Vector3.zero;
                FastFloatParser.TryParse(csv[0], out pos.x);
                FastFloatParser.TryParse(csv[1], out pos.y);
                FastFloatParser.TryParse(csv[2], out pos.z);
                FastFloatParser.TryParse(csv[3], out pos.w);

                float scale = 15.0f;
                if(csv.TokenCount >= 4) FastFloatParser.TryParse(csv[4], out scale);

                pos.x = -pos.x;
                Positions.Add(pos);
                Scales.Add(scale);
            }
        }
    }

    public MMPositions() { }
}
