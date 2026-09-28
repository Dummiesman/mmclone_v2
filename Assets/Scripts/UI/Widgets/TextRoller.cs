using System;
using UnityEngine;

public class TextRoller : UIWidget
{
    public Action<int> OnValueChanged;

    private const float rollerButtonScale = 0.9f;

    // Press-and-hold: the click itself gives the first step, then a pause, then
    // a steady repeat. Raise repeatInterval to slow the repeat down.
    private const float repeatInitialDelay = 0.35f;
    private const float repeatInterval = 0.08f;

    private readonly Color textColor = new Color(1f, 1f, 0f, 1f);
    private readonly Color textColorFocused = Color.red;

    private const int rollerStateCount = 3;
    private const int fallbackFontSize = 8;

    private LocFont font;

    // The size we asked MenuManager for, reused as the line height when
    // centring the number.
    private int fontSize = fallbackFontSize;

    /// <summary>
    /// Nudge in unscaled pixels, for fonts whose drawn height doesn't match
    /// their point size. Positive moves the number down.
    /// </summary>
    public float TextVerticalOffset = 0f;

    private readonly BMButton upRoller;
    private readonly BMButton downRoller;

    public BMButton UpRoller => upRoller;
    public BMButton DownRoller => downRoller;

    /// <summary>
    /// Read-only hides the rollers and refuses every user-driven change. Value,
    /// Min and Max still work, so code can keep driving it as a display.
    /// </summary>
    private bool readOnly = false;
    public bool ReadOnly
    {
        get { return readOnly; }
        set
        {
            upRoller.Visible = !value;
            downRoller.Visible = !value;
            readOnly = value;

            if (value)
                StopRepeat();
        }
    }

    public override bool EnableNavigation => !readOnly;

    public int Step = 1;

    private int minValue;
    private int maxValue;
    private int currentValue;

    // Cached so Draw doesn't allocate a string every frame.
    private string valueText;

    private bool focused;

    // Recalculated every Update, in pixels, like the slider's bar rects.
    private Rect textPixelRect;

    private int repeatDirection;
    private bool repeating;
    private float repeatCooldown;

    /// <summary>
    /// Out-of-range bounds are clamped rather than throwing, so the setters are
    /// safe to drive from config or from a menu that builds widgets in any order.
    /// </summary>
    public int Min
    {
        get => minValue;
        set
        {
            minValue = value;
            if (maxValue < minValue)
                maxValue = minValue;

            Value = currentValue; // reclamp
        }
    }

    public int Max
    {
        get => maxValue;
        set
        {
            maxValue = value;
            if (minValue > maxValue)
                minValue = maxValue;

            Value = currentValue; // reclamp
        }
    }

    public int Value
    {
        get => currentValue;
        set
        {
            int clamped = Mathf.Clamp(value, minValue, maxValue);
            if (clamped == currentValue)
                return;

            currentValue = clamped;
            valueText = clamped.ToString();
            OnValueChanged?.Invoke(clamped);
        }
    }

    // Layout

    private void AlignRollers()
    {
        Rect pixelRect = Menu.ToPixelCoordinates(Rect);

        if (readOnly)
        {
            // No rollers to place, so the number gets the whole widget.
            textPixelRect = BuildTextRect(pixelRect, 0f);
            return;
        }

        Vector2 hitRegion = upRoller.HitRegionSize * Menu.RenderScale;
        float rollerWidth = hitRegion.x * rollerButtonScale;
        float rollerHeight = hitRegion.y * rollerButtonScale;

        upRoller.Rect = Menu.ToWidgetCoords(new Rect(
            pixelRect.xMax - rollerWidth,
            pixelRect.y,
            rollerWidth,
            rollerHeight));

        downRoller.Rect = Menu.ToWidgetCoords(new Rect(
            pixelRect.xMax - rollerWidth,
            pixelRect.yMax - rollerHeight,
            rollerWidth,
            rollerHeight));

        textPixelRect = BuildTextRect(pixelRect, rollerWidth);
    }

    /// <summary>
    /// Centres the number vertically in the widget. Sizing the rect to the line
    /// height rather than the full widget height lands the text in the same
    /// place whether ScaledLabel aligns to the top of the rect or centres
    /// within it.
    /// </summary>
    private Rect BuildTextRect(Rect pixelRect, float rollerWidth)
    {
        float inset = 2f * Menu.RenderScale;
        float lineHeight = fontSize * Menu.RenderScale;

        float y = pixelRect.y
                  + ((pixelRect.height - lineHeight) * 0.5f)
                  + (TextVerticalOffset * Menu.RenderScale);

        return new Rect(
            pixelRect.x + inset,
            y,
            Mathf.Max(0f, pixelRect.width - rollerWidth - inset),
            lineHeight);
    }

    // Stepping

    private void ApplyStep(int direction)
    {
        if (readOnly)
            return;

        int step = Mathf.Max(1, Step);
        int next = Mathf.Clamp(currentValue + (direction * step), minValue, maxValue);
        if (next == currentValue)
            return;

        Value = next;

        if (MenuManager.Instance != null)
            MenuManager.Instance.PlaySound(MenuSound.SliderChange);
    }

    public void Increment() => ApplyStep(1);

    public void Decrement() => ApplyStep(-1);

    /// <summary>
    /// Records which way to step while a roller is held. Timing lives in Update.
    /// </summary>
    private void BeginRepeat(int direction)
    {
        repeatDirection = direction;
        repeating = false;
        repeatCooldown = repeatInitialDelay; // the click already gave step one
    }

    private void StopRepeat()
    {
        repeatDirection = 0;
        repeating = false;
        repeatCooldown = 0f;
    }

    public override void Update()
    {
        base.Update();
        AlignRollers();

        if (repeatDirection == 0)
            return;

        // Holding against the limit should stop repeating rather than spin.
        if ((repeatDirection > 0 && currentValue >= maxValue) ||
            (repeatDirection < 0 && currentValue <= minValue))
        {
            StopRepeat();
            return;
        }

        repeatCooldown -= Time.unscaledDeltaTime;
        if (repeatCooldown > 0f)
            return;

        ApplyStep(repeatDirection);

        repeatCooldown = repeating ? repeatInterval : repeatInitialDelay;
        repeating = true;
    }

    // Drawing

    public override void Draw()
    {
        base.Draw();

        var oldColor = GUI.color;

        GUI.color = (focused && !readOnly) ? textColorFocused : textColor;
        UIDrawing.ScaledLabel(textPixelRect, valueText, font, Menu.RenderScale);

        GUI.color = Color.white;

        if (upRoller.Visible)
            upRoller.Draw();

        if (downRoller.Visible)
            downRoller.Draw();

        GUI.color = oldColor;
    }

    // Events

    public override void Focus()
    {
        base.Focus();
        upRoller.Focus();
        downRoller.Focus();

        focused = true;
    }

    public override void Unfocus()
    {
        base.Unfocus();
        upRoller.Unfocus();
        downRoller.Unfocus();

        focused = false;
        StopRepeat();
    }

    public override bool HandleInput(UIEvent input)
    {
        if (base.HandleInput(input))
            return true;

        if (input == UIEventType.Right)
        {
            if (!readOnly)
                Increment();

            return true;
        }

        if (input == UIEventType.Left)
        {
            if (!readOnly)
                Decrement();

            return true;
        }

        if (input.Type == UIEventType.MouseDown || input.Type == UIEventType.MouseUp)
        {
            // Release anywhere ends the hold, including off the widget.
            if (input.Type == UIEventType.MouseUp)
                StopRepeat();

            // Hidden rollers keep their last rects, so don't hit-test them.
            if (!readOnly)
            {
                if (MouseContained(upRoller))
                {
                    if (input.Type == UIEventType.MouseDown)
                        BeginRepeat(1);

                    return upRoller.HandleInput(input);
                }

                if (MouseContained(downRoller))
                {
                    if (input.Type == UIEventType.MouseDown)
                        BeginRepeat(-1);

                    return downRoller.HandleInput(input);
                }
            }

            // Swallow clicks on the number itself so they don't fall through
            // to whatever is behind the widget.
            if (MouseContained())
                return true;
        }

        return false;
    }

    public override void Dispose()
    {
        base.Dispose();

        StopRepeat();

        upRoller?.Dispose();
        downRoller?.Dispose();
    }

    // Construction

    public TextRoller(UIMenu menu, int id, string name, int min, int max, int value, bool readOnly = false)
        : this(menu, id, name, Rect.zero, min, max, value, readOnly)
    {
    }

    public TextRoller(UIMenu menu, int id, string name, Rect rect, int min, int max, int value, bool readOnly = false)
        : base(menu, id, name, rect)
    {
        if (MenuManager.Instance != null)
        {
            fontSize = MenuManager.Instance.DropdownFontSize;
            font = MenuManager.Instance.GetFont(fontSize);
        }

        upRoller = new BMButton(menu, "roller_up", id * 10000, rollerStateCount, Rect.zero);
        downRoller = new BMButton(menu, "roller_down", id * 100000, rollerStateCount, Rect.zero);

        // Set the backing fields directly: the property setters clamp against
        // each other, and at this point neither bound is meaningful yet.
        minValue = Mathf.Min(min, max);
        maxValue = Mathf.Max(min, max);
        currentValue = Mathf.Clamp(value, minValue, maxValue);
        valueText = currentValue.ToString();

        upRoller.OnClick = () => ApplyStep(1);
        downRoller.OnClick = () => ApplyStep(-1);

        // Through the property so the rollers' visibility is set with it.
        ReadOnly = readOnly;
    }
}