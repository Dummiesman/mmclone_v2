using PSDL;
using UnityEngine;

public static class PSDLLibExtensions 
{
    public static Vector3 ToUnity(this Vertex vtx)
    {
        return new Vector3(vtx.x, vtx.y, vtx.z);
    }
}
