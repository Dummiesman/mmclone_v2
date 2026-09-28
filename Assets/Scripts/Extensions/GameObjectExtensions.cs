using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public static class GameObjectExtensions 
{
    public static void SetLayer(this GameObject obj, int layer, bool includeChildren = false)
    {
        obj.layer = layer;

        if (!includeChildren)
            return;

        foreach (var tsfxm in obj.GetComponentsInChildren<Transform>())
        {
            tsfxm.gameObject.layer = layer;
        }
    }
}
