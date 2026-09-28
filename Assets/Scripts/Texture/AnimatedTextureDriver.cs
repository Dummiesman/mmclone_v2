using System.Collections.Generic;
using UnityEngine;
using UnityEngine.LowLevel;

public static class AnimatedTextureDriver
{
    private static readonly List<AGETexture> active = new List<AGETexture>();

    public static void Register(AGETexture t) { if (!active.Contains(t)) active.Add(t); }
    public static void Unregister(AGETexture t) => active.Remove(t);

    private static void Tick()
    {
        float t = Time.unscaledTime;
        for (int i = 0; i < active.Count; i++)
            active[i].Tick(t);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Install()
    {
        active.Clear();
        var loop = PlayerLoop.GetCurrentPlayerLoop();
        for (int i = 0; i < loop.subSystemList.Length; i++)
        {
            if (loop.subSystemList[i].type != typeof(UnityEngine.PlayerLoop.Update)) continue;

            var update = loop.subSystemList[i];
            var list = new List<PlayerLoopSystem>(update.subSystemList);

            list.RemoveAll(s => s.type == typeof(AnimatedTextureDriver)); // safe with domain reload off
            list.Add(new PlayerLoopSystem { type = typeof(AnimatedTextureDriver), updateDelegate = Tick });

            update.subSystemList = list.ToArray();
            loop.subSystemList[i] = update;
        }
        PlayerLoop.SetPlayerLoop(loop);
    }
}