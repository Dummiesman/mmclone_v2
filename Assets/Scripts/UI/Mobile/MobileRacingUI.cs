using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Image-based OnGUI touch HUD for a mobile racing game.
///
/// Layout:
///   Top left     : camera button, horn button, dash cam toggle (steering wheel)
///   Top right    : map toggle, mirror toggle, pause button
///   Bottom left  : analog steering joystick (drag anywhere in the bottom-left quarter)
///   Bottom right : horizontal handbrake lever, with brake pedal (left) and accelerator (right) below
///
/// Exposed input values:
///   Steering       -1 (full left) .. +1 (full right)
///   Accelerator     0..1
///   Brake           0..1
///   Throttle       Accelerator - Brake, -1..1 convenience value
///   Handbrake       0..1 (HandbrakeHeld for the raw bool)
///   HornHeld        true while the horn button is held
///   DashCamOn / MapOn / MirrorOn   latched toggle states
///
/// Events: CameraPressed, PausePressed, HornDown, HornUp,
///         DashCamToggled(bool), MapToggled(bool), MirrorToggled(bool),
///         HandbrakeDown, HandbrakeUp.
///
/// Example car script:
///   public MobileRacingUI hud;
///   void FixedUpdate() {
///       transform.Rotate(0f, hud.Steering * turnSpeed * Time.fixedDeltaTime, 0f);
///       rb.AddForce(transform.forward * hud.Accelerator * power);
///       ApplyBrakes(hud.Brake, hud.Handbrake);
///   }
///
/// Notes:
///   - Uses the legacy Input Manager (Project Settings > Player > Active Input Handling
///     must be "Input Manager (Old)" or "Both").
///   - Multi-touch aware: steering, both pedals, the handbrake and the buttons can all be
///     used at once. Brake and accelerator are independent, so left-foot braking works.
///   - With slideBetweenControls on, one finger can drag across the handbrake, brake and
///     accelerator and engage each in turn without lifting. The steering thumb and the
///     top-row buttons never hand off, so a steering drag can't reach the pedals.
///   - Textures are optional; missing ones draw as a labelled grey placeholder.
/// </summary>
[DisallowMultipleComponent]
public class MobileRacingUI : MonoBehaviour
{
    // ---------------------------------------------------------------- textures

    [Header("Top-left buttons")]
    public Texture cameraIcon;
    public Texture hornIcon;
    public Texture dashCamIcon;

    [Header("Top-right buttons")]
    public Texture mapIcon;
    public Texture mirrorIcon;
    public Texture pauseIcon;

    [Header("Steering joystick")]
    public Texture joystickBase;
    public Texture joystickKnob;

    [Header("Bottom-right controls")]
    public Texture handbrakeIcon;
    public Texture brakePedalIcon;
    public Texture acceleratorPedalIcon;

    [Header("Tints (one asset per control, state shown by alpha)")]
    public Color buttonIdleTint = new Color(1f, 1f, 1f, 0.75f);
    public Color buttonPressedTint = new Color(1f, 1f, 1f, 1f);
    [Tooltip("Joystick is drawn at this tint at all times.")]
    public Color controlTint = new Color(1f, 1f, 1f, 0.75f);

    // ------------------------------------------------------------------ layout

    [Header("Layout (pixels at the reference height, scaled to the device)")]
    public float referenceHeight = 1080f;
    public float screenMargin = 36f;
    public float buttonSize = 120f;
    public float buttonSpacing = 24f;

    [Header("Joystick layout")]
    public float joystickRadius = 150f;
    public float joystickKnobSize = 130f;

    [Header("Pedal / handbrake layout")]
    public float pedalWidth = 190f;
    public float pedalHeight = 420f;
    public float pedalSpacing = 26f;
    public float handbrakeHeight = 110f;
    public float handbrakeSpacing = 26f;
    public float pedalsRightMargin = 60f;
    public float pedalsBottomMargin = 60f;
    [Tooltip("Extra invisible touch margin around the pedals and handbrake.")]
    public float pedalTouchPadding = 22f;

    // --------------------------------------------------------------- behaviour

    [Header("Steering behaviour")]
    [Tooltip("The joystick re-centres under the finger that touches the bottom-left quarter.")]
    public bool dynamicJoystick = true;
    public float steerReturnSpeed = 8f;
    [Range(0f, 0.5f)] public float steerDeadZone = 0.08f;

    [Header("Pedal behaviour")]
    [Tooltip("Pedal travel follows how far up the pad the finger sits, instead of always going to full.")]
    public bool analogPedalTravel = false;
    [Tooltip("How fast a pedal presses in, in units per second (1 = one second to full).")]
    public float pedalPressSpeed = 6f;
    [Tooltip("How fast a pedal releases, in units per second.")]
    public float pedalReleaseSpeed = 8f;
    [Tooltip("A finger can slide between the handbrake, brake and accelerator and engage each in turn without lifting.")]
    public bool slideBetweenControls = true;

    [Header("Debug")]
    public bool showValues = false;

    // --------------------------------------------------------- exposed values

    /// <summary>-1 = full left, 0 = centred, +1 = full right.</summary>
    public float Steering { get; private set; }

    /// <summary>Accelerator pedal travel, 0..1.</summary>
    public float Accelerator { get; private set; }

    /// <summary>Brake pedal travel, 0..1.</summary>
    public float Brake { get; private set; }

    /// <summary>Convenience combination: Accelerator - Brake, -1..1.</summary>
    public float Throttle { get { return Accelerator - Brake; } }

    /// <summary>Handbrake as a 0..1 value.</summary>
    public float Handbrake { get { return HandbrakeHeld ? 1f : 0f; } }

    public bool HandbrakeHeld { get; private set; }
    public bool HornHeld { get; private set; }

    /// <summary>Latched state of the steering-wheel button.</summary>
    public bool DashCamOn { get; private set; }

    /// <summary>Latched state of the map button.</summary>
    public bool MapOn { get; private set; }

    /// <summary>Latched state of the mirror button.</summary>
    public bool MirrorOn { get; private set; }

    public bool SteeringActive { get { return steerId != NoId; } }

    public event Action CameraPressed;
    public event Action PausePressed;
    public event Action HornDown;
    public event Action HornUp;
    public event Action HandbrakeDown;
    public event Action HandbrakeUp;
    public event Action MapPressed;
    public event Action<bool> DashCamToggled;
    public event Action<bool> MirrorToggled;

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
    private int mapId = NoId;
    private int mirrorId = NoId;
    private int pauseId = NoId;
    private int steerId = NoId;
    private int handbrakeId = NoId;
    private int brakeId = NoId;
    private int accelId = NoId;

    // ------------------------------------------------------------------- state

    private float scale = 1f;
    private int lastWidth, lastHeight;

    private Rect cameraRect, hornRect, dashCamRect;
    private Rect mapRect, mirrorRect, pauseRect;
    private Rect steerArea;
    private Rect handbrakeRect, brakeRect, accelRect;
    private Rect handbrakeHit, brakeHit, accelHit;

    private Vector2 joystickHome;
    private Vector2 joystickCentre;
    private Vector2 knobOffset;

    private float brakeTarget, accelTarget;

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
        UpdatePedals();
    }

    private void OnGUI()
    {
        if (Event.current.type != EventType.Repaint) return;

        GUI.depth = -100;

        DrawJoystick();

        DrawImage(handbrakeRect, handbrakeIcon, TintFor(HandbrakeHeld), "HANDBRAKE");
        DrawImage(brakeRect, brakePedalIcon, TintFor(Brake), "BRAKE");
        DrawImage(accelRect, acceleratorPedalIcon, TintFor(Accelerator), "GAS");

        DrawImage(cameraRect, cameraIcon, TintFor(cameraId != NoId), "CAM");
        DrawImage(hornRect, hornIcon, TintFor(HornHeld), "HORN");
        DrawImage(dashCamRect, dashCamIcon, TintFor(DashCamOn || dashCamId != NoId), "DASH");

        DrawImage(mapRect, mapIcon, TintFor(MapOn || mapId != NoId), "MAP");
        DrawImage(mirrorRect, mirrorIcon, TintFor(MirrorOn || mirrorId != NoId), "MIRROR");
        DrawImage(pauseRect, pauseIcon, TintFor(pauseId != NoId), "II");

        if (showValues)
        {
            Rect r = new Rect(cameraRect.x, dashCamRect.yMax + 10f * scale, 640f * scale, 120f * scale);
            GUI.Label(r, string.Format("Steer {0:+0.00;-0.00; 0.00}   Gas {1:0.00}   Brake {2:0.00}   HB {3}",
                                       Steering, Accelerator, Brake, HandbrakeHeld ? "ON" : "off"), ValueStyle);
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

        // Top-left row, left to right.
        cameraRect = new Rect(safe.x + m, safe.y + m, b, b);
        hornRect = new Rect(cameraRect.xMax + gap, cameraRect.y, b, b);
        dashCamRect = new Rect(hornRect.xMax + gap, cameraRect.y, b, b);

        // Top-right row, laid out from the right edge inwards.
        pauseRect = new Rect(safe.xMax - m - b, safe.y + m, b, b);
        mirrorRect = new Rect(pauseRect.x - gap - b, pauseRect.y, b, b);
        mapRect = new Rect(mirrorRect.x - gap - b, pauseRect.y, b, b);

        // Steering.
        steerArea = new Rect(0f, Screen.height * 0.5f, Screen.width * 0.5f, Screen.height * 0.5f);
        float radius = joystickRadius * scale;
        joystickHome = new Vector2(safe.x + m + radius, safe.yMax - m - radius);

        // Bottom-right cluster: accelerator hugs the edge, brake sits to its left,
        // handbrake spans both of them above.
        float pw = pedalWidth * scale;
        float ph = pedalHeight * scale;
        float pgap = pedalSpacing * scale;
        float hbH = handbrakeHeight * scale;
        float hbGap = handbrakeSpacing * scale;

        float right = safe.xMax - pedalsRightMargin * scale;
        float bottom = safe.yMax - pedalsBottomMargin * scale;

        accelRect = new Rect(right - pw, bottom - ph, pw, ph);
        brakeRect = new Rect(accelRect.x - pgap - pw, accelRect.y, pw, ph);
        handbrakeRect = new Rect(brakeRect.x, brakeRect.y - hbGap - hbH, pw * 2f + pgap, hbH);

        float pad = pedalTouchPadding * scale;
        accelHit = Inflate(accelRect, pad);
        brakeHit = Inflate(brakeRect, pad);
        handbrakeHit = Inflate(handbrakeRect, pad);

        placeholderStyle = null;
        valueStyle = null;

        if (steerId == NoId) joystickCentre = joystickHome;
    }

    private static Rect Inflate(Rect r, float pad)
    {
        return new Rect(r.x - pad, r.y - pad, r.width + pad * 2f, r.height + pad * 2f);
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
            || id == mapId || id == mirrorId || id == pauseId
            || id == steerId || id == handbrakeId || id == brakeId || id == accelId;
    }

    private enum DriveControl { None, Handbrake, Brake, Accelerator }

    private void ClaimNewPointers()
    {
        for (int i = 0; i < pointers.Count; i++)
        {
            Pointer p = pointers[i];
            if (p.ended) continue;

            if (IsClaimed(p.id))
            {
                if (slideBetweenControls) TrySlide(p);
                continue;
            }

            if (p.began)
            {
                if (cameraId == NoId && cameraRect.Contains(p.pos)) { cameraId = p.id; continue; }
                if (hornId == NoId && hornRect.Contains(p.pos)) { hornId = p.id; SetHorn(true); continue; }
                if (dashCamId == NoId && dashCamRect.Contains(p.pos)) { dashCamId = p.id; continue; }
                if (mapId == NoId && mapRect.Contains(p.pos)) { mapId = p.id; continue; }
                if (mirrorId == NoId && mirrorRect.Contains(p.pos)) { mirrorId = p.id; continue; }
                if (pauseId == NoId && pauseRect.Contains(p.pos)) { pauseId = p.id; continue; }

                if (TryClaimDrive(ControlUnder(p.pos), p.id)) continue;

                if (steerId == NoId && steerArea.Contains(p.pos))
                {
                    steerId = p.id;
                    if (dynamicJoystick) joystickCentre = ClampCentre(p.pos);
                }
            }
            else if (slideBetweenControls)
            {
                // A finger that went down on empty space can still slide onto a pedal or the lever.
                TryClaimDrive(ControlUnder(p.pos), p.id);
            }
        }
    }

    /// <summary>
    /// Which of the three bottom-right controls a point is over. Their padded touch rects
    /// can overlap, so the one the finger sits deepest inside wins.
    /// </summary>
    private DriveControl ControlUnder(Vector2 pos)
    {
        DriveControl best = DriveControl.None;
        float bestDepth = float.MaxValue;

        Consider(handbrakeHit, DriveControl.Handbrake, pos, ref best, ref bestDepth);
        Consider(brakeHit, DriveControl.Brake, pos, ref best, ref bestDepth);
        Consider(accelHit, DriveControl.Accelerator, pos, ref best, ref bestDepth);

        return best;
    }

    private static void Consider(Rect r, DriveControl control, Vector2 pos,
                                 ref DriveControl best, ref float bestDepth)
    {
        if (!r.Contains(pos)) return;

        // Normalised offset from the centre, so a wide rect isn't unfairly favoured.
        Vector2 n = new Vector2((pos.x - r.center.x) / (r.width * 0.5f),
                                (pos.y - r.center.y) / (r.height * 0.5f));
        float depth = n.sqrMagnitude;
        if (depth < bestDepth)
        {
            bestDepth = depth;
            best = control;
        }
    }

    private bool IsDriveFree(DriveControl control)
    {
        switch (control)
        {
            case DriveControl.Handbrake: return handbrakeId == NoId;
            case DriveControl.Brake: return brakeId == NoId;
            case DriveControl.Accelerator: return accelId == NoId;
            default: return false;
        }
    }

    private bool TryClaimDrive(DriveControl control, int pointerId)
    {
        if (!IsDriveFree(control)) return false;

        switch (control)
        {
            case DriveControl.Handbrake:
                handbrakeId = pointerId;
                SetHandbrake(true);
                return true;
            case DriveControl.Brake:
                brakeId = pointerId;
                return true;
            case DriveControl.Accelerator:
                accelId = pointerId;
                return true;
            default:
                return false;
        }
    }

    private void ReleaseDrive(DriveControl control)
    {
        switch (control)
        {
            case DriveControl.Handbrake:
                handbrakeId = NoId;
                SetHandbrake(false);
                break;
            case DriveControl.Brake:
                brakeId = NoId;
                break;
            case DriveControl.Accelerator:
                accelId = NoId;
                break;
        }
    }

    /// <summary>Hands a finger over from one bottom-right control to another mid-drag.</summary>
    private void TrySlide(Pointer p)
    {
        DriveControl held;
        if (p.id == handbrakeId) held = DriveControl.Handbrake;
        else if (p.id == brakeId) held = DriveControl.Brake;
        else if (p.id == accelId) held = DriveControl.Accelerator;
        else return;   // steering and the top-row buttons never hand off

        DriveControl now = ControlUnder(p.pos);
        if (now == DriveControl.None || now == held) return;
        if (!IsDriveFree(now)) return;   // another finger owns it; keep what we have

        ReleaseDrive(held);
        TryClaimDrive(now, p.id);
    }

    private Vector2 ClampCentre(Vector2 pos)
    {
        float r = joystickRadius * scale;
        return new Vector2(Mathf.Clamp(pos.x, steerArea.xMin + r, steerArea.xMax - r),
                           Mathf.Clamp(pos.y, steerArea.yMin + r, steerArea.yMax - r));
    }

    // ------------------------------------------------------------------ buttons

    private void UpdateButtons()
    {
        UpdateMomentary(ref cameraId, cameraRect, CameraPressed);
        UpdateMomentary(ref pauseId, pauseRect, PausePressed);
        UpdateMomentary(ref mapId, mapRect, MapPressed);

        UpdateToggle(ref dashCamId, dashCamRect, SetDashCam, DashCamOn);
        UpdateToggle(ref mirrorId, mirrorRect, SetMirror, MirrorOn);

        Pointer p;

        // Horn: held for as long as the finger is down.
        if (hornId != NoId && (!TryGetPointer(hornId, out p) || p.ended))
        {
            hornId = NoId;
            SetHorn(false);
        }

        // Handbrake: held, and releases if the finger slides off the lever.
        if (handbrakeId != NoId)
        {
            if (!TryGetPointer(handbrakeId, out p) || p.ended)
            {
                handbrakeId = NoId;
                SetHandbrake(false);
            }
            else
            {
                SetHandbrake(handbrakeHit.Contains(p.pos));
            }
        }
    }

    private void UpdateMomentary(ref int id, Rect rect, Action onPressed)
    {
        if (id == NoId) return;

        Pointer p;
        if (!TryGetPointer(id, out p)) { id = NoId; return; }

        if (p.ended)
        {
            if (rect.Contains(p.pos) && onPressed != null) onPressed();
            id = NoId;
        }
    }

    private void UpdateToggle(ref int id, Rect rect, Action<bool, bool> setter, bool current)
    {
        if (id == NoId) return;

        Pointer p;
        if (!TryGetPointer(id, out p)) { id = NoId; return; }

        if (p.ended)
        {
            if (rect.Contains(p.pos)) setter(!current, true);
            id = NoId;
        }
    }

    /// <summary>Sets the dash cam toggle. Pass notify = false to sync the HUD without raising the event.</summary>
    public void SetDashCam(bool on, bool notify = true)
    {
        if (DashCamOn == on) return;
        DashCamOn = on;
        if (notify && DashCamToggled != null) DashCamToggled(on);
    }

    /// <summary>Sets the mirror toggle. Pass notify = false to sync the HUD without raising the event.</summary>
    public void SetMirror(bool on, bool notify = true)
    {
        if (MirrorOn == on) return;
        MirrorOn = on;
        if (notify && MirrorToggled != null) MirrorToggled(on);
    }

    private void SetHorn(bool down)
    {
        if (HornHeld == down) return;
        HornHeld = down;
        if (down) { if (HornDown != null) HornDown(); }
        else { if (HornUp != null) HornUp(); }
    }

    private void SetHandbrake(bool down)
    {
        if (HandbrakeHeld == down) return;
        HandbrakeHeld = down;
        if (down) { if (HandbrakeDown != null) HandbrakeDown(); }
        else { if (HandbrakeUp != null) HandbrakeUp(); }
    }

    // ----------------------------------------------------------------- steering

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

    private static float ApplyDeadZone(float value, float deadZone)
    {
        value = Mathf.Clamp(value, -1f, 1f);
        if (deadZone <= 0f) return value;

        float mag = Mathf.Abs(value);
        if (mag <= deadZone) return 0f;
        return Mathf.Sign(value) * ((mag - deadZone) / (1f - deadZone));
    }

    // ------------------------------------------------------------------- pedals

    private void UpdatePedals()
    {
        accelTarget = PedalTarget(ref accelId, accelHit, accelRect);
        brakeTarget = PedalTarget(ref brakeId, brakeHit, brakeRect);

        float dt = Time.unscaledDeltaTime;
        Accelerator = MovePedal(Accelerator, accelTarget, dt);
        Brake = MovePedal(Brake, brakeTarget, dt);
    }

    private float PedalTarget(ref int id, Rect hit, Rect visual)
    {
        if (id == NoId) return 0f;

        Pointer p;
        if (!TryGetPointer(id, out p) || p.ended)
        {
            id = NoId;
            return 0f;
        }

        // The finger keeps the claim until it lifts, but the pedal only stays
        // engaged while it is actually over the pad.
        if (!hit.Contains(p.pos)) return 0f;

        if (!analogPedalTravel) return 1f;

        // Bottom of the pad = light pressure, top = full.
        return Mathf.Clamp01(Mathf.InverseLerp(visual.yMax, visual.yMin, p.pos.y));
    }

    private float MovePedal(float current, float target, float dt)
    {
        float speed = target > current ? pedalPressSpeed : pedalReleaseSpeed;
        if (speed <= 0f) return target;
        return Mathf.MoveTowards(current, target, speed * dt);
    }

    /// <summary>Drops every active touch and zeroes the inputs (call when pausing or respawning).</summary>
    public void ResetInput()
    {
        cameraId = hornId = dashCamId = mapId = mirrorId = pauseId = NoId;
        steerId = handbrakeId = brakeId = accelId = NoId;

        SetHorn(false);
        SetHandbrake(false);

        Steering = 0f;
        Accelerator = 0f;
        Brake = 0f;
        accelTarget = 0f;
        brakeTarget = 0f;
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

    private Color TintFor(bool pressed)
    {
        return pressed ? buttonPressedTint : buttonIdleTint;
    }

    /// <summary>Pedal tint fades between idle and pressed with travel.</summary>
    private Color TintFor(float amount01)
    {
        return Color.Lerp(buttonIdleTint, buttonPressedTint, Mathf.Clamp01(amount01));
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
