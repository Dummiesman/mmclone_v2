using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public static class RectExtensions {
    public static Rect Round(this Rect rect)
    {
        return Rect.MinMaxRect(Mathf.Round(rect.x), Mathf.Round(rect.y), Mathf.Round(rect.xMax), Mathf.Round(rect.yMax));
    }

    public static Rect Floor(this Rect rect)
    {
        return Rect.MinMaxRect(Mathf.Floor(rect.x), Mathf.Floor(rect.y), Mathf.Floor(rect.xMax), Mathf.Floor(rect.yMax));
    }

    public static Rect Ceil(this Rect rect)
    {
        return Rect.MinMaxRect(Mathf.Ceil(rect.x), Mathf.Ceil(rect.y), Mathf.Ceil(rect.xMax), Mathf.Ceil(rect.yMax));
    }
}
