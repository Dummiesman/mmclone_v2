using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public struct HermitePoint
{
    public Vector3 Position;
    public Vector3 Tangent;

    public static float Distance(HermitePoint first, HermitePoint second)
    {
        //rough distance estimate based on multiple samples
        Vector3 p1 = first.Position;
        Vector3 p2 = second.Position;

        float tangentScale = Vector3.Distance(p1, p2);
        Vector3 p025 = HermiteCurve.HermiteEvaluate(0.25f, first, second, tangentScale);
        Vector3 p050 = HermiteCurve.HermiteEvaluate(0.50f, first, second, tangentScale);
        Vector3 p075 = HermiteCurve.HermiteEvaluate(0.75f, first, second, tangentScale);
        

        float length = Vector3.Distance(p1, p025);
        length += Vector3.Distance(p025, p050);
        length += Vector3.Distance(p050, p075);
        length += Vector3.Distance(p075, p2);

        return length;
    }
}

public class HermiteCurve
{
    public readonly List<HermitePoint> Points = new List<HermitePoint>();

    public static Vector3 HermiteEvaluate(float t, HermitePoint start, HermitePoint end, float tangentScale)
    {
        Vector3 p0 = start.Position, p1 = end.Position, m0 = start.Tangent * tangentScale, m1 = end.Tangent * tangentScale;
        float t2 = t * t;
        float t3 = t2 * t;
        float t2x3 = t2 * 3f;
        float t3x2 = t3 * 2f;
        return (p0 * ((t3x2) - (t2x3) + 1.0f))
            + (m0 * (t3 + (-2.0f * t2) + t))
            + (p1 * (-t3x2 + (t2x3)))
            + (m1 * (t3 - t2));
    }

    public static Vector3 HermiteEvaluate(float t, HermitePoint start, HermitePoint end)
    {
        float d = Vector3.Distance(start.Position, end.Position);
        return HermiteEvaluate(t, start, end, d);
    }

    public Vector3 GetPositionAt(float t)
    {
        if (t < 0.01f)
            return Points[0].Position;
        if (t >= 1f)
            return Points[Points.Count - 1].Position;

        float mt = t * (Points.Count - 1);
        float lt = mt % 1f;
        int baseIndex = Mathf.FloorToInt(mt);

         return HermiteEvaluate(lt, Points[baseIndex], Points[baseIndex + 1]);
    }

    public Vector3[] Build(float resolution)
    {
        List<Vector3> builtPoints = new List<Vector3>();

        //add section points
        builtPoints.Add(GetPositionAt(0f));
        for (float f = resolution; f < 1f; f += resolution)
        {
            builtPoints.Add(GetPositionAt(f));
        }
        builtPoints.Add(GetPositionAt(1f));

        return builtPoints.ToArray();
    }

    public HermiteCurve(IEnumerable<HermitePoint> points)
    {
        Points.AddRange(points);
    }

    public HermiteCurve()
    {

    }
}
