using UnityEngine;

public static class Mat3
{
    /// <summary>Zero matrix with m33 = 1 so it stays a valid Matrix4x4.</summary>
    public static Matrix4x4 Zero()
    {
        Matrix4x4 m = new Matrix4x4();
        m.m33 = 1f;
        return m;
    }

    /// <summary>Matrix34::Add3x3 — element-wise add of the upper-left 3x3.</summary>
    public static Matrix4x4 Add3x3(Matrix4x4 a, Matrix4x4 b)
    {
        Matrix4x4 r = a;
        r.m00 = a.m00 + b.m00; r.m01 = a.m01 + b.m01; r.m02 = a.m02 + b.m02;
        r.m10 = a.m10 + b.m10; r.m11 = a.m11 + b.m11; r.m12 = a.m12 + b.m12;
        r.m20 = a.m20 + b.m20; r.m21 = a.m21 + b.m21; r.m22 = a.m22 + b.m22;
        return r;
    }

    /// <summary>Matrix34::Dot3x3 — a * b, upper-left 3x3 only.</summary>
    public static Matrix4x4 Mul3x3(Matrix4x4 a, Matrix4x4 b)
    {
        Matrix4x4 r = Zero();
        r.m00 = a.m00 * b.m00 + a.m01 * b.m10 + a.m02 * b.m20;
        r.m01 = a.m00 * b.m01 + a.m01 * b.m11 + a.m02 * b.m21;
        r.m02 = a.m00 * b.m02 + a.m01 * b.m12 + a.m02 * b.m22;
        r.m10 = a.m10 * b.m00 + a.m11 * b.m10 + a.m12 * b.m20;
        r.m11 = a.m10 * b.m01 + a.m11 * b.m11 + a.m12 * b.m21;
        r.m12 = a.m10 * b.m02 + a.m11 * b.m12 + a.m12 * b.m22;
        r.m20 = a.m20 * b.m00 + a.m21 * b.m10 + a.m22 * b.m20;
        r.m21 = a.m20 * b.m01 + a.m21 * b.m11 + a.m22 * b.m21;
        r.m22 = a.m20 * b.m02 + a.m21 * b.m12 + a.m22 * b.m22;
        return r;
    }

    public static Matrix4x4 Transpose3x3(Matrix4x4 a)
    {
        Matrix4x4 r = Zero();
        r.m00 = a.m00; r.m01 = a.m10; r.m02 = a.m20;
        r.m10 = a.m01; r.m11 = a.m11; r.m12 = a.m21;
        r.m20 = a.m02; r.m21 = a.m12; r.m22 = a.m22;
        return r;
    }

    public static Matrix4x4 Scale3x3(Matrix4x4 a, float s)
    {
        Matrix4x4 r = Zero();
        r.m00 = a.m00 * s; r.m01 = a.m01 * s; r.m02 = a.m02 * s;
        r.m10 = a.m10 * s; r.m11 = a.m11 * s; r.m12 = a.m12 * s;
        r.m20 = a.m20 * s; r.m21 = a.m21 * s; r.m22 = a.m22 * s;
        return r;
    }

    /// <summary>Skew-symmetric cross-product matrix: Skew(r) * v == cross(r, v).</summary>
    public static Matrix4x4 Skew(Vector3 r)
    {
        Matrix4x4 m = Zero();
        m.m00 = 0f; m.m01 = -r.z; m.m02 = r.y;
        m.m10 = r.z; m.m11 = 0f; m.m12 = -r.x;
        m.m20 = -r.y; m.m21 = r.x; m.m22 = 0f;
        return m;
    }

    /// <summary>K = s * (n ⊗ n). Your DampingCoefficient * (normal outer normal).</summary>
    public static Matrix4x4 Outer(Vector3 n, float s)
    {
        Matrix4x4 m = Zero();
        m.m00 = n.x * n.x * s; m.m01 = n.x * n.y * s; m.m02 = n.x * n.z * s;
        m.m10 = n.y * n.x * s; m.m11 = n.y * n.y * s; m.m12 = n.y * n.z * s;
        m.m20 = n.z * n.x * s; m.m21 = n.z * n.y * s; m.m22 = n.z * n.z * s;
        return m;
    }

    public static Vector3 Mul(Matrix4x4 a, Vector3 v)
    {
        return new Vector3(
            a.m00 * v.x + a.m01 * v.y + a.m02 * v.z,
            a.m10 * v.x + a.m11 * v.y + a.m12 * v.z,
            a.m20 * v.x + a.m21 * v.y + a.m22 * v.z);
    }

    /// <summary>Transposed multiply — aᵀ * v, without building the transpose.</summary>
    public static Vector3 MulT(Matrix4x4 a, Vector3 v)
    {
        return new Vector3(
            a.m00 * v.x + a.m10 * v.y + a.m20 * v.z,
            a.m01 * v.x + a.m11 * v.y + a.m21 * v.z,
            a.m02 * v.x + a.m12 * v.y + a.m22 * v.z);
    }

    /// <summary>Cofactor inverse of the upper-left 3x3. Returns false if singular.</summary>
    public static bool Inverse3x3(Matrix4x4 a, out Matrix4x4 inv)
    {
        float c00 = a.m11 * a.m22 - a.m12 * a.m21;
        float c01 = -(a.m10 * a.m22 - a.m12 * a.m20);
        float c02 = a.m10 * a.m21 - a.m11 * a.m20;

        float det = a.m00 * c00 + a.m01 * c01 + a.m02 * c02;
        if (Mathf.Abs(det) < 1e-12f) { inv = Matrix4x4.identity; return false; }

        float d = 1f / det;
        inv = Zero();
        inv.m00 = c00 * d;
        inv.m01 = -(a.m01 * a.m22 - a.m02 * a.m21) * d;
        inv.m02 = (a.m01 * a.m12 - a.m02 * a.m11) * d;
        inv.m10 = c01 * d;
        inv.m11 = (a.m00 * a.m22 - a.m02 * a.m20) * d;
        inv.m12 = -(a.m00 * a.m12 - a.m02 * a.m10) * d;
        inv.m20 = c02 * d;
        inv.m21 = -(a.m00 * a.m21 - a.m01 * a.m20) * d;
        inv.m22 = (a.m00 * a.m11 - a.m01 * a.m10) * d;
        return true;
    }

    public static Matrix4x4 Identity()
    {
        Matrix4x4 m = Zero();
        m.m00 = 1f; m.m11 = 1f; m.m22 = 1f;
        return m;
    }

    /// <summary>
    /// Stand-in for Matrix34::SolveSVD. M is symmetric by construction, and
    /// singular whenever fewer than three wheels are loaded - hence the fallback
    /// rather than a straight inverse.
    /// </summary>
    public static Vector3 SolveSymmetric(Matrix4x4 M, Vector3 b)
    {
        if (Inverse3x3(M, out Matrix4x4 inv))
            return Mul(inv, b);

        // Rank-deficient: diagonal pseudo-inverse keeps it stable on two wheels
        // or a kerb. Replace with Jacobi + truncated pseudo-inverse if you see
        // this path firing often.
        float tol = 1e-6f * Mathf.Max(Mathf.Abs(M.m00),
                    Mathf.Max(Mathf.Abs(M.m11), Mathf.Abs(M.m22)));
        return new Vector3(
            Mathf.Abs(M.m00) > tol ? b.x / M.m00 : 0f,
            Mathf.Abs(M.m11) > tol ? b.y / M.m11 : 0f,
            Mathf.Abs(M.m22) > tol ? b.z / M.m22 : 0f);
    }
}