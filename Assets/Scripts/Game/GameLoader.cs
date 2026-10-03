using Dummiesman.TextureLoading;
using System.Collections;
using UnityEngine;

public class GameLoader : MonoBehaviour
{
    private Texture2D loadingScreenTexture;
    private MMGame game;

    private float progress;
    private string stageName;
    private bool done;

    private string GetLoadScreenName()
    {
        string city = GameState.SelectedCity;
        int raceNum = GameState.SelectedRace;
        switch(GameState.SelectedGameMode)
        {
            case MMGameMode.Cruise:
                return $"{city}_roam";
            case MMGameMode.Blitz:
                return $"{city}_blitz{raceNum}";
            case MMGameMode.Checkpoint:
                return $"{city}_race{raceNum}";
            case MMGameMode.Circuit:
                return $"{city}_circuit{raceNum}";
            case MMGameMode.CrashCourse:
                return $"{city}_crash{raceNum}";
            case MMGameMode.CopsNRobbers:
                return $"{city}_multicop";
            default:
                return "splash";
        }
    }

    private void Awake()
    {
        // load the loading screen
        string loadScreenName = GetLoadScreenName();
        if(!AssetManager.Exists("jpg", $"{loadScreenName}.jpg"))
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

        // load the game
        // for now just use the base class
        var gameObj = new GameObject("Game");
        switch(GameState.SelectedGameMode)
        {
            case MMGameMode.CrashCourse:
                game = gameObj.AddComponent<SingleStunt>();
                break;
            case MMGameMode.Cruise:
                game = gameObj.AddComponent<SingleRoam>();
                break;
            case MMGameMode.Blitz:
                game = gameObj.AddComponent<SingleBlitz>();
                break;
            case MMGameMode.Checkpoint:
                game = gameObj.AddComponent<SingleRace>();
                break;
            case MMGameMode.Circuit:
                game = gameObj.AddComponent<SingleCircuit>();
                break;
            default:
                Debug.LogError($"GameLoader: Unknown game mode {GameState.SelectedGameMode}");
                game = gameObj.AddComponent<SingleRoam>(); // fallback
                break;

        }
    }

    private IEnumerator Start()
    {
        // mute audio during loading, and freeze everything
        MMAudioMixer.Mute();
        Physics.simulationMode = SimulationMode.Script;
        Time.timeScale = 0.0f;

        yield return null;

        foreach (float p in game.Load())
        {
            progress = p;
            stageName = game.LoadStageName;
            yield return null;
        }

        progress = 1.0f;
        done = true;

        // hold the full bar for a frame, then get out of the way
        yield return null;

        if (loadingScreenTexture != null)
        {
            Destroy(loadingScreenTexture);
            loadingScreenTexture = null;
        }

        // done, unmute audio and unfreeze
        MMAudioMixer.Unmute();
        Physics.SyncTransforms();
        Physics.simulationMode = SimulationMode.FixedUpdate;
        Time.timeScale = 1.0f;
        Destroy(gameObject);
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
            430 / UIConstants.ReferenceHeight,
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

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (!done && !string.IsNullOrEmpty(stageName))
        {
            var labelRect = new Rect(barRect.x, barRect.yMax + 4, barRect.width, 20);
            GUI.Label(labelRect, stageName);
        }
#endif
    }
}
