using UnityEngine;

[DisallowMultipleComponent]
public class MirrorCullingFlip : MonoBehaviour
{
    private void OnPreRender() { GL.invertCulling = true; }
    private void OnPostRender() { GL.invertCulling = false; }
    private void OnDisable() { GL.invertCulling = false; }
}