using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

public static class VectorExtensions  {

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector2 ToVec2XZ(this Vector3 src)
    {
        return new Vector2(src.x, src.z);
    }

    public static Vector3 Flatten(this Vector3 src)
    {
        return new Vector3(src.x, 0f, src.z);
    }

	public static Vector3 ConvertCoordinateSpace(this Vector3 src)
    {
        return new Vector3(src.x * -1, src.y, src.z);
    }

    public static Vector2 ConvertCoordinateSpace(this Vector2 src)
    {
        return new Vector2(src.x * -1, src.y);
    }

    public static Vector3 RotateAroundY(this Vector3 vec, Vector3 rotateAround, float angle)
    {
        float s = Mathf.Sin(angle * Mathf.Deg2Rad);
        float c = Mathf.Cos(angle * Mathf.Deg2Rad);

        // translate point back to origin:
        Vector3 p = vec;
        p.x -= rotateAround.x;
        p.z -= rotateAround.z;

        // rotate point
        float xnew = p.x * c - p.z * s;
        float znew = p.x * s + p.z * c;

        // translate point back:
        p.x = xnew + rotateAround.x;
        p.z = znew + rotateAround.z;
        return p;
    }
}
