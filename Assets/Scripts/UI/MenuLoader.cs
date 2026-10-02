using Dummiesman.TextureLoading;
using System.Collections;
using UnityEngine;

public class MenuTest : MonoBehaviour
{
    private Texture2D loadingScreenTexture;
    private float progress = 0.0f;

    private string GetLoadScreenName()
    {
        return "splash";
    }

    private void LoadBackground()
    {
        // load the loading screen
        string loadScreenName = GetLoadScreenName();
        if (!AssetManager.Exists("jpg", $"{loadScreenName}.jpg"))
        {
            loadScreenName = "splash";
        }
        using (var stream = AssetManager.Open("jpg", $"{loadScreenName}.jpg"))
        {
            if (stream != null)
            {
                loadingScreenTexture = ImageLoader.LoadTexture(stream, ImageLoader.TextureFormat.JPG);
                loadingScreenTexture.wrapMode = TextureWrapMode.Clamp;
            }
        }
    }

    private IEnumerator Start()
    {
        // first request storage
        yield return StoragePermission.Request();
        if (!StoragePermission.Granted)
        {
            NativeFunctions.MessageBox("Error",
                "Storage access is required to load game data.",
                MessageBoxButtons.Ok, MessageBoxIcon.Error);
            Application.Quit();
            yield break;
        }

        // then init fs
        var sw = System.Diagnostics.Stopwatch.StartNew();
        if (!FileSystem.Initialized) FileSystem.Init();

        // then load background and render it
        LoadBackground();
        progress = 0.15f;
        yield return null;

        // then load localization
        Localization.Init();
        progress = 0.25f;
        yield return null;

        // mobile fps target (default 30)
        if (Application.isMobilePlatform)
        {
            Application.targetFrameRate = 120;
        }

        // 3. Create menus
        var @interface = new GameObject("Interface").AddComponent<MMInterface>();
        @interface.Init();

        progress = 1.0f;
        yield return null;

        Debug.Log($"Loaded menu+music+mmlang+vehicle list+player profiles in {sw.ElapsedMilliseconds}ms");
        Destroy(this.gameObject);
    }

    private static Rect GetLetterboxedOrPillarboxedRect(float targetAspect = 4f / 3f)
    {
        float screenAspect = (float)Screen.width / Screen.height;

        if (screenAspect > targetAspect)
        {
            float width = targetAspect / screenAspect;
            float x = (1f - width) * 0.5f;
            return new Rect(x * Screen.width, 0f * Screen.height, width * Screen.width, 1f * Screen.height);
        }
        else
        {
            float height = screenAspect / targetAspect;
            float y = (1f - height) * 0.5f;
            return new Rect(0f * Screen.width, y * Screen.height, 1f * Screen.width, height * Screen.height);
        }
    }

    private static Rect MapToRect(Rect container, Rect normalized)
    {
        return new Rect(
            container.x + normalized.x * container.width,
            container.y + normalized.y * container.height,
            normalized.width * container.width,
            normalized.height * container.height);
    }

    private void OnGUI()
    {
        // draw over anything the game itself might put up
        GUI.depth = -1000;

        GUI.color = Color.black;
        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
        GUI.color = Color.white;

        // draw loading screen
        var loadRect = GetLetterboxedOrPillarboxedRect();
        if (loadingScreenTexture != null)
        {
            GUI.DrawTexture(loadRect, loadingScreenTexture);
        }

        // draw loading bar
        var loadingSubRect = new Rect(
            352 / UIConstants.ReferenceWidth,
            448 / UIConstants.ReferenceHeight,
            271 / UIConstants.ReferenceWidth,
            9 / UIConstants.ReferenceHeight);

        var loadingBarColor = new Color32(2, 48, 192, 255);

        // fit into loadrect and draw
        var barRect = MapToRect(loadRect, loadingSubRect);
        var fillRect = barRect;
        fillRect.width = Mathf.Round(barRect.width * Mathf.Clamp01(progress));

        GUI.color = loadingBarColor;
        if (fillRect.width >= 1.0f)
        {
            GUI.DrawTexture(fillRect, Texture2D.whiteTexture);
        }
        GUI.color = Color.white;
    }
}