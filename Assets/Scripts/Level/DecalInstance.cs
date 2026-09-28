using System.Collections.Generic;
using UnityEngine;

public class DecalInstance : LevelInstance
{
    private MeshRenderer decalRenderer;

    public override void Init(SDLCity level, string name)
    {
        base.Init(level, name);
        Flags |= LevelInstanceFlags.Static;
    }

    public void Init(PathSet pathset, PathSet.Path path)
    {
        var decalObj = DecalDrawer.DrawDecal(path);
        decalObj.transform.parent = this.transform;
        decalRenderer = decalObj.GetComponent<MeshRenderer>();

        if (decalObj.GetComponent<MeshFilter>().sharedMesh != null)
        { 
            int decalRoom = Level.FindRoomIdWithWarps(decalObj.GetComponent<MeshFilter>().sharedMesh.bounds.center);
            if (decalRoom >= 0)
            {
                Level.MoveToRoom(this, decalRoom);
            }
        }
    }

    public override IEnumerable<Renderer> GetRenderers()
    {
        yield return decalRenderer;
    }
}
