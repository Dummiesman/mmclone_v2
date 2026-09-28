using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PathEvaluator
{
    private float[] pathPointPercentages;
    private PathSet.Path path;

    public void Init(PathSet.Path path)
    {
        this.path = path;
        pathPointPercentages= new float[path.Points.Count];

        float length = path.Length;
        float current = 0f;
        for (int i = 0; i < path.Points.Count - 1; i++)
        {
            pathPointPercentages[i] = current / length;
            current += Vector3.Distance(path.Points[i], path.Points[i + 1]);
        }
        pathPointPercentages[pathPointPercentages.Length - 1] = 1f;
    }

    public Vector3 Evaluate(float t)
    {
        t = Mathf.Clamp01(t);
        if (t <= 0.01f)
            return path.Points[0];
        if (t >= (1f - 0.01f))
            return path.Points[path.Points.Count - 1];

        //optimization
        if (pathPointPercentages.Length == 2)
        {
            return Vector3.Lerp(path.Points[0], path.Points[1], t);
        }

        //lerp between the path points
        int baseIndex = EvaluateIndex(t);
        float currentPercent = pathPointPercentages[baseIndex];
        float nextPercent = pathPointPercentages[baseIndex + 1];
        float lerpAmount = (t - currentPercent) / (nextPercent - currentPercent);
        return Vector3.Lerp(path.Points[baseIndex], path.Points[baseIndex + 1], lerpAmount);
    }

    public int EvaluateIndex(float t)
    {
        t = Mathf.Clamp01(t);
        if (t <= 0.01f)
            return 0;
        if (t >= (1f - 0.01f))
            return pathPointPercentages.Length - 1;

        for (int i = pathPointPercentages.Length - 1; i >= 0; i--)
        {
            if (pathPointPercentages[i] <= t)
            {
                return i;
            }
        }
        return -1;
    }

    public PathEvaluator() { }
    public PathEvaluator(PathSet.Path path)
    {
        Init(path);
    }
}
