using UnityEngine;

public class MMDigiTach : MonoBehaviour
{
    private VehCar car;

    private const float DesignAspect = 4f / 3f;

    [Tooltip("Fraction of the damage texture visible at once.")]
    private const float DamageWindow = 0.2f;

    [Tooltip("Overall UI scale. 1 = original size.")]
    public float Scale = 1f;

    [Tooltip("Normalized point the UI scales around (0,0 = top left, 1,1 = bottom right).")]
    public Vector2 Pivot = new Vector2(0f, 1f); // bottom-left, where the tach lives

    [Tooltip("Normalized offset of the whole UI in screen space. (0,0) leaves it where it is; " +
             "x = fraction of screen width (+ right), y = fraction of screen height (+ down).")]
    public Vector2 ScreenLocation = new Vector2(0.0f, 0.0f);

    // textures
    private readonly Texture2D[] numberImages = new Texture2D[10];
    private readonly Texture2D[] gearImages = new Texture2D[13];
    private Texture2D damageTex;
    private Texture2D damageCoverTex;
    private Texture2D speedTicksTex;

    // layout (normalized, 0,0 = top left)
    private const float NumberWidth = 0.04001464f;
    private const float NumberHeight = 0.0729166f;
    private const float NumberX = 0.013177f;
    private const float NumberY = 0.8515f;

    private static readonly Rect DamageRect = new Rect(0.00585651f, 0.984375f, 0.12589972f, 0.015625f);
    private static readonly float TicksY = NumberY + NumberHeight; // speedNumbers[0].Rect.yMax
    private static readonly Rect SpeedTicksRect = new Rect(DamageRect.x, TicksY, DamageRect.width, 0.055f);
    private static readonly Rect GearRect = new Rect(DamageRect.x, TicksY, 0.0225f, 0.03f);

    // per-frame state (computed in Update, drawn in OnGUI)
    private readonly int[] speedDigits = new int[3];
    private int digitCount = 1;
    private float rpmFactor;
    private Texture2D currentGearTex;

    public void Init(VehCar car)
    {
        this.car = car;

        for (int i = 0; i < 10; i++)
        {
            numberImages[i] = TextureLoader.LoadNoPostprocess($"DIGITAC_{i}");
            numberImages[i].wrapMode = TextureWrapMode.Clamp;
        }

        for (int i = 0; i < 9; i++)
            gearImages[i] = TextureLoader.LoadNoPostprocess($"DIGITAC_GEAR_{i}");
        gearImages[9] = TextureLoader.LoadNoPostprocess("DIGITAC_GEAR_D");
        gearImages[10] = TextureLoader.LoadNoPostprocess("DIGITAC_GEAR_N");
        gearImages[11] = TextureLoader.LoadNoPostprocess("DIGITAC_GEAR_P");
        gearImages[12] = TextureLoader.LoadNoPostprocess("DIGITAC_GEAR_R");
        foreach (var tex in gearImages)
            tex.wrapMode = TextureWrapMode.Clamp;

        damageTex = TextureLoader.LoadNoPostprocess("DAMAGE");
        damageTex.wrapMode = TextureWrapMode.Clamp;

        damageCoverTex = TextureLoader.LoadNoPostprocess("DAMAGE_LABLE");
        damageCoverTex.wrapMode = TextureWrapMode.Clamp;

        speedTicksTex = TextureLoader.LoadNoPostprocess("SPEED_TICKS");
        speedTicksTex.wrapMode = TextureWrapMode.Clamp;

        currentGearTex = gearImages[0];
    }

    private void Update()
    {
        if (car == null)
            return;

        // speed digits
        int mph = Mathf.Clamp(Mathf.FloorToInt(car.VehCarSim.SpeedInMph), 0, 999);
        digitCount = mph >= 100 ? 3 : mph >= 10 ? 2 : 1;
        speedDigits[0] = mph / 100;
        speedDigits[1] = (mph / 10) % 10;
        speedDigits[2] = mph % 10;

        // rpm
        var engine = car.VehCarSim.Engine;
        rpmFactor = engine.MaxRPM > 0f ? Mathf.Clamp01(engine.CurrentRPM / engine.MaxRPM) : 0f;

        // gear
        int gear = car.VehCarSim.Transmission.CurrentGear;
        if (gear == 0)
            currentGearTex = gearImages[12]; // R
        else if (gear == 1)
            currentGearTex = gearImages[10]; // N
        else if (!car.VehCarSim.Transmission.IsAutomatic)
            currentGearTex = gearImages[Mathf.Min(gear, 9) - 1]; // 1-9
        else
            currentGearTex = gearImages[9]; // D
    }

    private void OnGUI()
    {
        if (car == null || Event.current.type != EventType.Repaint)
            return;

        // speed numbers (right-aligned: slot 2 always shown)
        for (int i = 0; i < 3; i++)
        {
            bool visible = i >= 3 - digitCount;
            if (!visible) continue;

            var r = new Rect(NumberX + i * NumberWidth, NumberY, NumberWidth, NumberHeight);
            GUI.DrawTexture(ToScreen(r), numberImages[speedDigits[i]], ScaleMode.StretchToFill);
        }

        // gear indicator
        GUI.DrawTexture(ToScreen(GearRect), currentGearTex, ScaleMode.StretchToFill);

        // damage bar, then cover on top
        float dmg = Mathf.Clamp01(car.Damage.DamagePercentage);
        var damageUV = new Rect(dmg * (1f - DamageWindow), 0f, DamageWindow, 1f);

        Rect damageScreen = ToScreen(DamageRect);
        GUI.DrawTextureWithTexCoords(damageScreen, damageTex, damageUV);
        GUI.DrawTexture(damageScreen, damageCoverTex, ScaleMode.StretchToFill);

        // speed ticks, cropped by rpm
        if (rpmFactor > 0f)
        {
            var ticks = new Rect(SpeedTicksRect.x, SpeedTicksRect.y,
                                 SpeedTicksRect.width * rpmFactor, SpeedTicksRect.height);
            GUI.DrawTextureWithTexCoords(ToScreen(ticks), speedTicksTex, new Rect(0f, 0f, rpmFactor, 1f));
        }
    }

    /// <summary>
    /// Converts a normalized rect (0,0 top-left, 1,1 bottom-right) to screen pixels,
    /// applying Scale around Pivot, then offsetting by ScreenLocation.
    /// </summary>
    private Rect ToScreen(Rect n)
    {
        // Fit a 4:3 box inside the screen
        float boxH = Mathf.Min(Screen.height, Screen.width / DesignAspect);
        float boxW = boxH * DesignAspect;

        // Pin to the left edge, and to the bottom if the screen is narrower than 4:3
        float boxX = 0f;
        float boxY = Screen.height - boxH;

        // User-authored offset, in fractions of the screen
        boxX += ScreenLocation.x * Screen.width;
        boxY += ScreenLocation.y * Screen.height;

        float x = Pivot.x + (n.x - Pivot.x) * Scale;
        float y = Pivot.y + (n.y - Pivot.y) * Scale;

        return new Rect(
            boxX + x * boxW,
            boxY + y * boxH,
            n.width * Scale * boxW,
            n.height * Scale * boxH);
    }
}