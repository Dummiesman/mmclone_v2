using PSDL;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SDLLoadTest : MonoBehaviour
{
    public void SetCloudShadowIntensity(float intens)
    {
        Shader.SetGlobalFloat("_ShadowMapIntensity", intens);
    }

    public void SetCloudShadowOffset(Vector2 offset)
    {
        Shader.SetGlobalVector("_ShadowMapOffset", offset);
    }

    public void InitCloudShadows()
    {
        string shadowTextureName = "shadmap_day";
       /* if (GameState.timeOfDay == GameState.TimeOfDay.Evening || GameState.timeOfDay == GameState.TimeOfDay.Night)
            shadowTextureName = "shadmap_nite";
       */
        var shadowTexture = TextureCache.Get(shadowTextureName);
        Shader.SetGlobalTexture("_ShadowMap", shadowTexture);
        Shader.SetGlobalFloat("_ShadowMapScale", 1f / 128f);
        SetCloudShadowIntensity(1.0f);
        SetCloudShadowOffset(Vector2.zero);
    }

    private void Start()
    {        
        FileSystem.Init();

        PSDLFile file = null;
        using(var stream = AssetManager.Open("city", "london.psdl"))
        {
            file = new PSDLFile(stream);
        }

        var builder = new SDLBuilder(file, null);

        var sw = System.Diagnostics.Stopwatch.StartNew();

        for(int i=0; i < file.Rooms.Count; i++)
        {
            var obj = builder.BuildRoom(file.Rooms[i]);
            obj.name = $"Room{i}";
        }


        Debug.Log($"SDL load time {sw.ElapsedMilliseconds}ms");
        InitCloudShadows();
        builder.Dispose();
    }
}
