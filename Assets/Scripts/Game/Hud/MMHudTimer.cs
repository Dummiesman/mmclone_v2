using UnityEngine;

public class MMHudTimer : MonoBehaviour
{
    private const float DesignAspect = 4f / 3f;

    // Original HUD art was laid out in 640x480 pixels; texture sizes are converted
    // to normalized units against this.
    private const float DesignWidth = 640f;
    private const float DesignHeight = 480f;

    // Texture names - replace with the real ones from the game data.
    private const string DigitTexturePrefix = "DIGI_";      // TIMER_0 .. TIMER_9
    private const string ColonTextureName = "DIGI_COLON";

    private const int ColonIndex = 10; // DigitTextures[10] is the colon
    private const int SlotCount = 8;   // MM:SS:HH

    [Tooltip("Overall UI scale. 1 = original size.")]
    public float Scale = 0.75f;

    [Tooltip("Normalized point the UI scales around (0,0 = top left, 1,1 = bottom right).")]
    public Vector2 Pivot = new Vector2(0.5f, 0f); // top-center, where the timer lives

    [Tooltip("Distance from the top of the 4:3 box, normalized (original: dword_98c / 480).")]
    public float TopOffset = 1f / DesignHeight; // 1px in the original 640x480 layout

    [Tooltip("Timer opacity (original: Transparency).")]
    [Range(0f, 1f)] public float Opacity = 1f;

    [Tooltip("If true, shows CountdownSeconds; otherwise ElapsedSeconds.")]
    public bool UseCountdownTimer;

    // Feed these from your race logic (equivalent of countdownTimer / Timer).
    public float CountdownSeconds;
    public float ElapsedSeconds;

    // textures: 0-9 digits, 10 colon
    private readonly Texture2D[] digitTextures = new Texture2D[11];

    // normalized sizes (computed once in Init)
    private float digitW, digitH;
    private float colonW, colonH;

    // per-frame state (computed in Update, drawn in OnGUI)
    private readonly int[] timerDigitIndices = new int[SlotCount];
    private bool initialized;

    public void Init()
    {
        for (int i = 0; i < 10; i++)
        {
            digitTextures[i] = TextureLoader.LoadNoPostprocess($"{DigitTexturePrefix}{i}");
            if (digitTextures[i] != null)
            {
                digitTextures[i] = digitTextures[i].MakeColorTransparent(Color.black);
                digitTextures[i].wrapMode = TextureWrapMode.Clamp;
            }
        }

        digitTextures[ColonIndex] = TextureLoader.LoadNoPostprocess(ColonTextureName);
        if (digitTextures[ColonIndex] != null)
        {
            digitTextures[ColonIndex] = digitTextures[ColonIndex].MakeColorTransparent(Color.black);
            digitTextures[ColonIndex].wrapMode = TextureWrapMode.Clamp;
        }

        // The original draws every digit at DigitTextures[0]'s size and every colon at ColonTexture's size
        digitW = digitTextures[0].width / DesignWidth;
        digitH = digitTextures[0].height / DesignHeight;
        colonW = digitTextures[ColonIndex].width / DesignWidth;
        colonH = digitTextures[ColonIndex].height / DesignHeight;

        // colon slots never change
        timerDigitIndices[2] = ColonIndex;
        timerDigitIndices[5] = ColonIndex;

        initialized = true;
    }

    private void Update()
    {
        if (!initialized)
            return;

        float time = UseCountdownTimer ? CountdownSeconds : ElapsedSeconds;
        if (time < 0f)
            time = 0f;

        // +0.005 rounds hundredths to nearest, like the original
        double t = time + 0.005;
        int wholeSeconds = (int)System.Math.Floor(t);
        int hundredths = (int)((t - wholeSeconds) * 100.0);
        int seconds = wholeSeconds % 60;
        int minutes = (wholeSeconds / 60) % 100;

        // (the original's "> 60" clamp on the tens-of-minutes digit can never trigger, so it's omitted)
        timerDigitIndices[0] = minutes / 10;
        timerDigitIndices[1] = minutes % 10;
        timerDigitIndices[3] = seconds / 10;
        timerDigitIndices[4] = seconds % 10;
        timerDigitIndices[6] = hundredths / 10;
        timerDigitIndices[7] = hundredths % 10;
    }

    private void OnGUI()
    {
        if (!initialized || Event.current.type != EventType.Repaint)
            return;

        Color prevColor = GUI.color;
        GUI.color = new Color(prevColor.r, prevColor.g, prevColor.b, prevColor.a * Opacity);

        // Original: x = screenWidth/2 - 3*digitWidth - colonWidth  (6 digits + 2 colons, centered)
        float x = 0.5f - (3f * digitW + colonW);

        for (int i = 0; i < SlotCount; i++)
        {
            bool isColon = i == 2 || i == 5;
            Texture2D tex = digitTextures[timerDigitIndices[i]];

            var r = new Rect(x, TopOffset,
                             isColon ? colonW : digitW,
                             isColon ? colonH : digitH);
            GUI.DrawTexture(ToScreen(r), tex, ScaleMode.StretchToFill);

            // Original advances by the drawn texture's own width
            x += tex.width / DesignWidth;
        }

        GUI.color = prevColor;
    }

    /// <summary>
    /// Converts a normalized rect (0,0 top-left, 1,1 bottom-right) to screen pixels,
    /// applying Scale around Pivot.
    /// </summary>
    private Rect ToScreen(Rect n)
    {
        // Fit a 4:3 box inside the screen
        float boxH = Mathf.Min(Screen.height, Screen.width / DesignAspect);
        float boxW = boxH * DesignAspect;

        // Center horizontally, pin to the top
        float boxX = (Screen.width - boxW) * 0.5f;
        float boxY = 0f;

        float x = Pivot.x + (n.x - Pivot.x) * Scale;
        float y = Pivot.y + (n.y - Pivot.y) * Scale;

        return new Rect(
            boxX + x * boxW,
            boxY + y * boxH,
            n.width * Scale * boxW,
            n.height * Scale * boxH);
    }
}