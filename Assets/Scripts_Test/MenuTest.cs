using UnityEngine;

public class MenuTest : MonoBehaviour
{
    private void Start()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        FileSystem.Init();
        Localization.Init();


        // setup rewards

        // mobile thing
        if(Application.isMobilePlatform)
        {
            Application.targetFrameRate = 120;
        }

        // 3. Create mens
        var @interface = this.gameObject.AddComponent<MMInterface>();
        @interface.Init();

        Debug.Log($"Loaded menu+music+mmlang+vehicle list+player profiles in {sw.ElapsedMilliseconds}ms");
    }

}
