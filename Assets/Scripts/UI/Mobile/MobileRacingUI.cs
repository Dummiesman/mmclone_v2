using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class MobileRacingUI : MonoBehaviour
{
    // ---------------------------------------------------------------- textures

    [Header("Top-left buttons")]
    public Texture cameraIcon;
    public Texture hornIcon;
    public Texture dashCamIcon;

    [Header("Top-right button")]
    public Texture pauseIcon;

    [Header("Steering joystick")]
    public Texture joystickBase;
    public Texture joystickKnob;

    [Header("Throttle slider")]
    public Texture throttleTrack;
    public Texture throttleFill;
    public Texture throttleKnob;

    [Header("Tints (one asset per control, state shown by alpha)")]
    public Color buttonIdleTint = new Color(1f, 1f, 1f, 0.75f);
    public Color buttonPressedTint = new Color(1f, 1f, 1f, 1f);
    [Tooltip("Joystick and throttle are drawn at this tint at all times.")]
    public Color controlTint = new Color(1f, 1f, 1f, 0.75f);

    // ------------------------------------------------------------------ layout

    [Header("Layout (pixels at the reference height, scaled to the device)")]
    public float referenceHeight = 1080f;
    public float screenMargin = 36f;
    public float buttonSize = 120f;
    public float buttonSpacing = 24f;
    public float joystickRadius = 150f;
    public float joystickKnobSize = 130f;
    public float throttleSliderWidth = 110f;
    public float throttleSliderHeight = 440f;
    public float throttleKnobHeight = 90f;
    public float throttleRightMargin = 70f;
    public float throttleBottomMargin = 70f;

    // --------------------------------------------------------------- behaviour

    [Header("Behaviour")]
    [Tooltip("The joystick re-centres under the finger that touches the bottom-left quarter.")]
    public bool dynamicJoystick = true;
    [Tooltip("Slider centre = coast, up = gas, down = brake/reverse. Off means 0..1 gas only.")]
    public bool throttleIsBidirectional = true;
    [Tooltip("Throttle springs back to its rest value when released.")]
    public bool throttleSpringsBack = true;
    public float steerReturnSpeed = 8f;
    public float throttleReturnSpeed = 6f;
    [Range(0f, 0.5f)] public float steerDeadZone = 0.08f;
    [Range(0f, 0.5f)] public float throttleDeadZone = 0.1f;

    [Header("Debug")]
    public bool showValues = false;

    // --------------------------------------------------------- exposed values

    /// <summary>-1 = full left, 0 = centred, +1 = full right.</summary>
    public float Steering { get; private set; }

    /// <summary>-1 = full brake/reverse, 0 = coast, +1 = full gas (0..1 when not bidirectional).</summary>
    public float Throttle { get; private set; }

    /// <summary>Positive half of <see cref="Throttle"/>, 0..1.</summary>
    public float Accelerator { get { return Mathf.Clamp01(Throttle); } }

    /// <summary>Negative half of <see cref="Throttle"/>, 0..1.</summary>
    public float Brake { get { return Mathf.Clamp01(-Throttle); } }

    public bool HornHeld { get; private set; }

    /// <summary>Latched state of the steering-wheel button (dash cam on/off).</summary>
    public bool DashCamOn { get; private set; }

    public bool SteeringActive { get { return steerId != NoId; } }
    public bool ThrottleActive { get { return throttleId != NoId; } }

    public event Action CameraPressed;
    public event Action PausePressed;
    public event Action HornDown;
    public event Action HornUp;
    public event Action<bool> DashCamToggled;

    // ----------------------------------------------------------------- pointers

    private struct Pointer
    {
        public int id;
        public Vector2 pos;   // GUI space: origin top-left, y grows downwards
        public bool began;
        public bool ended;
    }

    private const int NoId = int.MinValue;
    private const int MouseId = -100;

    private readonly List<Pointer> pointers = new List<Pointer>(10);

    private int cameraId = NoId;
    private int hornId = NoId;
    private int dashCamId = NoId;
    private int pauseId = NoId;
    private int steerId = NoId;
    private int throttleId = NoId;

    // ------------------------------------------------------------------- state

    private float scale = 1f;
    private int lastWidth, lastHeight;

    private Rect cameraRect, hornRect, dashCamRect, pauseRect;
    private Rect steerArea, throttleArea;
    private Rect throttleRect;

    private Vector2 joystickHome;
    private Vector2 joystickCentre;
    private Vector2 knobOffset;

    private Texture2D placeholderTex;
    private GUIStyle placeholderStyle;
    private GUIStyle valueStyle;

    // ------------------------------------------------------------------ unity

    private void Awake()
    {
        Input.multiTouchEnabled = true;
        Input.simulateMouseWithTouches = false;

        placeholderTex = new Texture2D(1, 1);
        placeholderTex.SetPixel(0, 0, Color.white);
        placeholderTex.Apply();
        placeholderTex.hideFlags = HideFlags.HideAndDontSave;

        BuildLayout();
        joystickCentre = joystickHome;
    }

    private void OnDestroy()
    {
        if (placeholderTex != null) Destroy(placeholderTex);
    }

    private void Update()
    {
        if (Screen.width != lastWidth || Screen.height != lastHeight) BuildLayout();

        CollectPointers();
        ClaimNewPointers();
        UpdateButtons();
        UpdateSteering();
        UpdateThrottle();
    }

    private void OnGUI()
    {
        if (Event.current.type != EventType.Repaint) return;

        GUI.depth = -100;

        DrawJoystick();
        DrawThrottle();

        DrawImage(cameraRect, cameraIcon, TintFor(cameraId != NoId), "CAM");
        DrawImage(hornRect, hornIcon, TintFor(HornHeld), "HORN");
        DrawImage(dashCamRect, dashCamIcon, TintFor(DashCamOn || dashCamId != NoId), "DASH");
        DrawImage(pauseRect, pauseIcon, TintFor(pauseId != NoId), "II");

        if (showValues)
        {
            Rect r = new Rect(cameraRect.x, hornRect.yMax + 10f * scale, 420f * scale, 60f * scale);
            GUI.Label(r, string.Format("Steering {0:+0.00;-0.00; 0.00}   Throttle {1:+0.00;-0.00; 0.00}",
                                       Steering, Throttle), ValueStyle);
        }
    }

    // ------------------------------------------------------------------ layout

    private void BuildLayout()
    {
        lastWidth = Screen.width;
        lastHeight = Screen.height;
        scale = Screen.height / Mathf.Max(1f, referenceHeight);

        // Safe area converted from screen space (y up) to GUI space (y down).
        Rect sa = Screen.safeArea;
        Rect safe = new Rect(sa.x, Screen.height - sa.yMax, sa.width, sa.height);

        float m = screenMargin * scale;
        float b = buttonSize * scale;
        float gap = buttonSpacing * scale;

        cameraRect = new Rect(safe.x + m, safe.y + m, b, b);
        hornRect = new Rect(cameraRect.xMax + gap, cameraRect.y, b, b);
        dashCamRect = new Rect(hornRect.xMax + gap, cameraRect.y, b, b);
        pauseRect = new Rect(safe.xMax - m - b, safe.y + m, b, b);

        steerArea = new Rect(0f, Screen.height * 0.5f, Screen.width * 0.5f, Screen.height * 0.5f);
        throttleArea = new Rect(Screen.width * 0.5f, Screen.height * 0.5f, Screen.width * 0.5f, Screen.height * 0.5f);

        float radius = joystickRadius * scale;
        joystickHome = new Vector2(safe.x + m + radius, safe.yMax - m - radius);

        float tw = throttleSliderWidth * scale;
        float th = throttleSliderHeight * scale;
        throttleRect = new Rect(safe.xMax - throttleRightMargin * scale - tw,
                                safe.yMax - throttleBottomMargin * scale - th,
                                tw, th);

        placeholderStyle = null;
        valueStyle = null;

        if (steerId == NoId) joystickCentre = joystickHome;
    }

    // ----------------------------------------------------------------- pointers

    private void CollectPointers()
    {
        pointers.Clear();

#if UNITY_EDITOR || UNITY_STANDALONE || UNITY_WEBGL
        if (Input.GetMouseButton(0) || Input.GetMouseButtonUp(0))
        {
            pointers.Add(new Pointer
            {
                id = MouseId,
                pos = ToGui(Input.mousePosition),
                began = Input.GetMouseButtonDown(0),
                ended = Input.GetMouseButtonUp(0)
            });
        }
#endif

        for (int i = 0; i < Input.touchCount; i++)
        {
            Touch t = Input.GetTouch(i);
            pointers.Add(new Pointer
            {
                id = t.fingerId,
                pos = ToGui(t.position),
                began = t.phase == TouchPhase.Began,
                ended = t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled
            });
        }
    }

    private static Vector2 ToGui(Vector2 screenPos)
    {
        return new Vector2(screenPos.x, Screen.height - screenPos.y);
    }

    private bool TryGetPointer(int id, out Pointer result)
    {
        for (int i = 0; i < pointers.Count; i++)
        {
            if (pointers[i].id == id) { result = pointers[i]; return true; }
        }
        result = default(Pointer);
        return false;
    }

    private bool IsClaimed(int id)
    {
        return id == cameraId || id == hornId || id == dashCamId
            || id == pauseId || id == steerId || id == throttleId;
    }

    private void ClaimNewPointers()
    {
        for (int i = 0; i < pointers.Count; i++)
        {
            Pointer p = pointers[i];
            if (!p.began || p.ended || IsClaimed(p.id)) continue;

            if (cameraId == NoId && cameraRect.Contains(p.pos))
            {
                cameraId = p.id;
            }
            else if (hornId == NoId && hornRect.Contains(p.pos))
            {
                hornId = p.id;
                SetHorn(true);
            }
            else if (dashCamId == NoId && dashCamRect.Contains(p.pos))
            {
                dashCamId = p.id;
            }
            else if (pauseId == NoId && pauseRect.Contains(p.pos))
            {
                pauseId = p.id;
            }
            else if (steerId == NoId && steerArea.Contains(p.pos))
            {
                steerId = p.id;
                if (dynamicJoystick) joystickCentre = ClampCentre(p.pos);
            }
            else if (throttleId == NoId && throttleArea.Contains(p.pos))
            {
                throttleId = p.id;
            }
        }
    }

    private Vector2 ClampCentre(Vector2 pos)
    {
        float r = joystickRadius * scale;
        return new Vector2(Mathf.Clamp(pos.x, steerArea.xMin + r, steerArea.xMax - r),
                           Mathf.Clamp(pos.y, steerArea.yMin + r, steerArea.yMax - r));
    }

    // ------------------------------------------------------------------ controls

    private void UpdateButtons()
    {
        Pointer p;

        // Camera: fires on release inside the button.
        if (cameraId != NoId)
        {
            if (!TryGetPointer(cameraId, out p)) cameraId = NoId;
            else if (p.ended)
            {
                if (cameraRect.Contains(p.pos) && CameraPressed != null) CameraPressed();
                cameraId = NoId;
            }
        }

        // Pause: fires on release inside the button.
        if (pauseId != NoId)
        {
            if (!TryGetPointer(pauseId, out p)) pauseId = NoId;
            else if (p.ended)
            {
                if (pauseRect.Contains(p.pos) && PausePressed != null) PausePressed();
                pauseId = NoId;
            }
        }

        // Dash cam: latching toggle, flips on release inside the button.
        if (dashCamId != NoId)
        {
            if (!TryGetPointer(dashCamId, out p)) dashCamId = NoId;
            else if (p.ended)
            {
                if (dashCamRect.Contains(p.pos)) SetDashCam(!DashCamOn, true);
                dashCamId = NoId;
            }
        }

        // Horn: held for as long as the finger is down.
        if (hornId != NoId)
        {
            if (!TryGetPointer(hornId, out p) || p.ended)
            {
                hornId = NoId;
                SetHorn(false);
            }
        }
    }

    /// <summary>Sets the dash cam toggle. Pass notify = false to sync the HUD without raising the event.</summary>
    public void SetDashCam(bool on, bool notify = true)
    {
        if (DashCamOn == on) return;
        DashCamOn = on;
        if (notify && DashCamToggled != null) DashCamToggled(on);
    }

    private void SetHorn(bool down)
    {
        if (HornHeld == down) return;
        HornHeld = down;
        if (down) { if (HornDown != null) HornDown(); }
        else { if (HornUp != null) HornUp(); }
    }

    private void UpdateSteering()
    {
        float radius = joystickRadius * scale;
        Pointer p;

        if (steerId != NoId)
        {
            if (TryGetPointer(steerId, out p) && !p.ended)
            {
                Vector2 delta = p.pos - joystickCentre;
                if (delta.sqrMagnitude > radius * radius) delta = delta.normalized * radius;
                knobOffset = delta;
                Steering = ApplyDeadZone(delta.x / radius, steerDeadZone);
                return;
            }
            steerId = NoId;
        }

        float dt = Time.unscaledDeltaTime;
        Steering = Mathf.MoveTowards(Steering, 0f, steerReturnSpeed * dt);
        knobOffset = Vector2.MoveTowards(knobOffset, Vector2.zero, radius * steerReturnSpeed * dt);
        joystickCentre = Vector2.MoveTowards(joystickCentre, joystickHome, radius * steerReturnSpeed * 2f * dt);
    }

    private void UpdateThrottle()
    {
        Pointer p;

        if (throttleId != NoId)
        {
            if (TryGetPointer(throttleId, out p) && !p.ended)
            {
                float t = Mathf.InverseLerp(throttleRect.yMax, throttleRect.yMin, p.pos.y); // 0 bottom -> 1 top
                Throttle = throttleIsBidirectional
                    ? ApplyDeadZone(t * 2f - 1f, throttleDeadZone)
                    : Mathf.Clamp01(t);
                return;
            }
            throttleId = NoId;
        }

        if (throttleSpringsBack)
        {
            Throttle = Mathf.MoveTowards(Throttle, 0f, throttleReturnSpeed * Time.unscaledDeltaTime);
        }
    }

    private static float ApplyDeadZone(float value, float deadZone)
    {
        value = Mathf.Clamp(value, -1f, 1f);
        if (deadZone <= 0f) return value;

        float mag = Mathf.Abs(value);
        if (mag <= deadZone) return 0f;
        return Mathf.Sign(value) * ((mag - deadZone) / (1f - deadZone));
    }

    /// <summary>Drops every active touch and zeroes the inputs (call when pausing or respawning).</summary>
    public void ResetInput()
    {
        cameraId = hornId = dashCamId = pauseId = steerId = throttleId = NoId;
        SetHorn(false);
        Steering = 0f;
        Throttle = 0f;
        knobOffset = Vector2.zero;
        joystickCentre = joystickHome;
    }

    // ------------------------------------------------------------------ drawing

    private void DrawJoystick()
    {
        float radius = joystickRadius * scale;
        float knob = joystickKnobSize * scale;

        Rect baseRect = new Rect(joystickCentre.x - radius, joystickCentre.y - radius, radius * 2f, radius * 2f);
        DrawImage(baseRect, joystickBase, controlTint, "STEER");

        Vector2 knobCentre = joystickCentre + knobOffset;
        Rect knobRect = new Rect(knobCentre.x - knob * 0.5f, knobCentre.y - knob * 0.5f, knob, knob);
        DrawImage(knobRect, joystickKnob, controlTint, "");
    }

    private void DrawThrottle()
    {
        DrawImage(throttleRect, throttleTrack, controlTint, "THROTTLE");

        // Fill: from the centre when bidirectional, from the bottom otherwise.
        float value01 = throttleIsBidirectional ? (Throttle + 1f) * 0.5f : Mathf.Clamp01(Throttle);
        float origin01 = throttleIsBidirectional ? 0.5f : 0f;

        if (throttleFill != null && !Mathf.Approximately(value01, origin01))
        {
            Color prev = GUI.color;
            GUI.color = controlTint;
            DrawVerticalFill(throttleRect, throttleFill, origin01, value01);
            GUI.color = prev;
        }

        float knobH = throttleKnobHeight * scale;
        float knobY = Mathf.Lerp(throttleRect.yMax, throttleRect.yMin, value01) - knobH * 0.5f;
        knobY = Mathf.Clamp(knobY, throttleRect.yMin - knobH * 0.25f, throttleRect.yMax - knobH * 0.75f);
        Rect knobRect = new Rect(throttleRect.x, knobY, throttleRect.width, knobH);
        DrawImage(knobRect, throttleKnob, controlTint, "");
    }

    private static void DrawVerticalFill(Rect rect, Texture tex, float from01, float to01)
    {
        float lo = Mathf.Min(from01, to01);
        float hi = Mathf.Max(from01, to01);
        float yBottom = Mathf.Lerp(rect.yMax, rect.yMin, lo);
        float yTop = Mathf.Lerp(rect.yMax, rect.yMin, hi);

        Rect clip = new Rect(rect.x, yTop, rect.width, yBottom - yTop);
        if (clip.height <= 0f) return;

        GUI.BeginGroup(clip);
        GUI.DrawTexture(new Rect(0f, rect.y - clip.y, rect.width, rect.height), tex, ScaleMode.StretchToFill, true);
        GUI.EndGroup();
    }

    private Color TintFor(bool pressed)
    {
        return pressed ? buttonPressedTint : buttonIdleTint;
    }

    private void DrawImage(Rect rect, Texture tex, Color tint, string placeholderLabel)
    {
        Color prev = GUI.color;

        if (tex != null)
        {
            GUI.color = tint;
            GUI.DrawTexture(rect, tex, ScaleMode.StretchToFill, true);
            GUI.color = prev;
            return;
        }

        GUI.color = new Color(tint.r, tint.g, tint.b, tint.a * 0.25f);
        GUI.DrawTexture(rect, placeholderTex);
        GUI.color = prev;

        if (!string.IsNullOrEmpty(placeholderLabel)) GUI.Label(rect, placeholderLabel, PlaceholderStyle);
    }

    private GUIStyle PlaceholderStyle
    {
        get
        {
            if (placeholderStyle == null)
            {
                placeholderStyle = new GUIStyle(GUI.skin.label);
                placeholderStyle.alignment = TextAnchor.MiddleCenter;
                placeholderStyle.fontSize = Mathf.Max(10, Mathf.RoundToInt(24f * scale));
                placeholderStyle.normal.textColor = Color.white;
                placeholderStyle.wordWrap = true;
            }
            return placeholderStyle;
        }
    }

    private GUIStyle ValueStyle
    {
        get
        {
            if (valueStyle == null)
            {
                valueStyle = new GUIStyle(GUI.skin.label);
                valueStyle.alignment = TextAnchor.UpperLeft;
                valueStyle.fontSize = Mathf.Max(10, Mathf.RoundToInt(28f * scale));
                valueStyle.normal.textColor = Color.white;
            }
            return valueStyle;
        }
    }
}