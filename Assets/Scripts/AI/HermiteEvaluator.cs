using System.Collections.Generic;
using UnityEngine;

public class HermiteEvaluator
{
    private float[] pathPointPercentages;
    private List<HermitePoint> points;
    private int lastIndex = -1;

    public void Init(List<HermitePoint> points)
    {
        this.points = points;
        pathPointPercentages = null;

        if (points.Count <= 2) return;

        pathPointPercentages = new float[points.Count];

        float length = 0f;
        for (int i = 0; i < points.Count - 1; i++)
        {
            length += HermitePoint.Distance(points[i], points[i + 1]);
        }

        float current = 0f;
        for (int i = 0; i < points.Count - 1; i++)
        {
            pathPointPercentages[i] = current / length;
            current += HermitePoint.Distance(points[i], points[i + 1]);
        }
        pathPointPercentages[pathPointPercentages.Length - 1] = 1f;
    }


    public Vector3 Evaluate(float t)
    {
        //empty path!
        if (points.Count < 2)
        {
            return Vector3.zero;
        }

        //optimization
        if (points.Count == 2)
        {
            return HermiteCurve.HermiteEvaluate(t, points[0], points[1]);
        }

        //lerp between the path points
        int baseIndex = EvaluateIndex(t);
        float currentPercent = pathPointPercentages[baseIndex];
        float nextPercent = pathPointPercentages[baseIndex + 1];
        float lerpAmount = (t - currentPercent) / (nextPercent - currentPercent);

        return HermiteCurve.HermiteEvaluate(lerpAmount, points[baseIndex], points[baseIndex + 1]);
    }

    public int EvaluateIndex(float t)
    {
        t = Mathf.Clamp01(t);
        int i = Mathf.Clamp(lastIndex, 0, pathPointPercentages.Length - 2);

        while (i > 0 && pathPointPercentages[i] > t) i--;
        while (i < pathPointPercentages.Length - 2 && pathPointPercentages[i + 1] <= t) i++;

        lastIndex = i;
        return i;
    }

    private HermiteEvaluator() { }
    public HermiteEvaluator(HermiteCurve curve)
    {
        Init(curve.Points);
    }

    public HermiteEvaluator(List<HermitePoint> points)
    {
        Init(points);
    }
}
