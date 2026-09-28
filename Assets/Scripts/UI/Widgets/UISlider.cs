using System;
using UnityEngine;

public class UISlider : UIWidget
{
    public Action<float> OnValueChanged;

    private float min = 0.0f;
    private float max = 1.0f;

    public float Min
    {
        get => min;
        set
        {
            min = value;
            Value = Mathf.Clamp(Value, min, max);
        }
    }
    public float Max
    {
        get => max;
        set
        {
            max = value;
            Value = Mathf.Clamp(Value, min, max);
        }
    }

    private bool readOnly = false;
    public bool ReadOnly
    {
        get { return readOnly; }
        set
        {
            leftButton.Visible = !value;
            rightButton.Visible = !value;
            readOnly = value;
        }
    }

    public override bool EnableNavigation => !readOnly;
    public float Step = 0.1f;
    public bool LockToStep = false;

    public BMButton LeftButton => leftButton;
    public BMButton RightButton => rightButton;

    private BMButton leftButton;
    private BMButton rightButton;

    private float value = 0.5f;
    public float Value
    {
        get { return value; }
        set
        {
            float v = Mathf.Clamp(value, Min, Max);

            if (LockToStep && Step > 0.0f)
                v = Mathf.Clamp(Min + Mathf.Round((v - Min) / Step) * Step, Min, Max);

            this.value = v;
            OnValueChanged?.Invoke(this.value);
        }
    }

    private bool focused;

    private Rect sliderRect = Rect.zero;
    private Rect sliderEventRect = Rect.zero;

    private Texture2D sliderReadOnlyActiveTexture;
    private Texture2D sliderReadOnlyInactiveTexture;
    private Texture2D sliderActiveTexture;
    private Texture2D sliderInactiveTexture;

    // Helpers
    private void AlignButtons()
    {
        if (LeftButton != null && RightButton != null)
        {
            var leftHitRegionSize = leftButton.HitRegionSize * Menu.RenderScale;
            var rightHitRegionSize = rightButton.HitRegionSize * Menu.RenderScale;
            var ourPixelCoords = Menu.ToPixelCoordinates(Rect);

            leftButton.Rect = Menu.ToWidgetCoords(new Rect(ourPixelCoords.x, ourPixelCoords.y, leftHitRegionSize.x, leftHitRegionSize.y));
            rightButton.Rect = Menu.ToWidgetCoords(new Rect(ourPixelCoords.xMax - rightHitRegionSize.x, ourPixelCoords.y, rightHitRegionSize.x, rightHitRegionSize.y));
        }
    }

    private void CalculateSliderAreas()
    {
        var pixelRect = Menu.ToPixelCoordinates(Rect);
        var barPixelRect = pixelRect;

        float fudgeWidth = (leftButton.HitRegionSize.x + rightButton.HitRegionSize.x) * Menu.RenderScale;
        fudgeWidth += 4 * Menu.RenderScale;

        barPixelRect.x += fudgeWidth / 2.0f;
        barPixelRect.width -= fudgeWidth;

        var barRect = Menu.ToWidgetCoords(barPixelRect);

        float barHeight = (!readOnly) ? Rect.height * 0.125f : Rect.height * 0.4f;
        sliderRect = new Rect(
            barRect.x,
            Rect.yMax - (Rect.height / 2) - (barHeight / 2),
            barRect.width,
            barHeight);

        sliderEventRect = new Rect(
            barRect.x,
            Rect.y,
            barRect.width,
            Rect.height);
    }

    private float GetValueAsPercentage()
    {
        return Mathf.InverseLerp(Min, Max, value);
    }

    private void SetValueFromPercentage(float percent)
    {
        Value = Mathf.Lerp(Min, Max, Mathf.Clamp01(percent));
    }

    private void LoadTextures()
    {
        sliderReadOnlyActiveTexture = TextureLoader.Load("slider_roactl");
        sliderReadOnlyInactiveTexture = TextureLoader.Load("slider_roinactl");
        sliderActiveTexture = TextureLoader.Load("slider_actl");
        sliderInactiveTexture = TextureLoader.Load("slider_inactl");
    }

    // Events
    public override void Focus()
    {
        base.Focus();
        leftButton.Focus();
        rightButton.Focus();

        focused = true;
    }

    public override void Unfocus()
    {
        base.Unfocus();
        leftButton.Unfocus();
        rightButton.Unfocus();

        focused = false;
    }

    public override bool HandleInput(UIEvent input)
    {
        if (base.HandleInput(input))
            return true;

        if (input == UIEventType.Right)
        {
            if (!readOnly)
            {
                if (MenuManager.Instance != null) MenuManager.Instance.PlaySound(MenuSound.SliderChange);
                Value += Step;
            }
            return true;
        }

        if (input == UIEventType.Left)
        {
            if (!readOnly)
            {
                if (MenuManager.Instance != null) MenuManager.Instance.PlaySound(MenuSound.SliderChange);
                Value -= Step;
            }
            return true;
        }

        if(input.Type == UIEventType.MouseDown ||  input.Type == UIEventType.MouseUp)
        {
            if (MouseContained(leftButton)) 
                return leftButton.HandleInput(input);
            if(MouseContained(rightButton)) 
                return rightButton.HandleInput(input);

            if (MouseContained(sliderEventRect))
            {
                if (input.Type == UIEventType.MouseDown && !readOnly)
                {
                    var sliderRectInPx = Menu.ToPixelCoordinates(sliderEventRect);
                    float percent = (Menu.PointerPosition.x - sliderRectInPx.x) / sliderRectInPx.width;
                    SetValueFromPercentage(percent);
                    if (MenuManager.Instance != null) MenuManager.Instance.PlaySound(MenuSound.SliderChange);
                }

                return true;
            }
        }

        return false;
    }

    public override void Update()
    {
        base.Update();
        AlignButtons();
        CalculateSliderAreas();
    }

    public override void Draw()
    {
        base.Draw();

        // The buttons must not depend on the slider textures loading.
        if (LeftButton.Visible)
            LeftButton.Draw();

        if (RightButton.Visible)
            RightButton.Draw();

        var oldColor = GUI.color;
        GUI.color = Color.white;

        Rect barRect = Menu.ToPixelCoordinates(sliderRect);
        float percent = GetValueAsPercentage();

        if (readOnly)
        {
            // ---------------------------------------------------------
            // READ ONLY
            //
            // ROINACTL = 1 pixel high track
            // ROACTL   = filled portion, full texture height
            // ---------------------------------------------------------

            Texture2D inactiveTexture = sliderReadOnlyInactiveTexture;
            Texture2D activeTexture = sliderReadOnlyActiveTexture;

            if (inactiveTexture == null || activeTexture == null)
            {
                GUI.color = oldColor;
                return;
            }

            float inactiveHeight = 1f * Menu.RenderScale;
            float activeHeight = activeTexture.height * Menu.RenderScale;

            Rect inactiveRect = new Rect(
                barRect.x,
                barRect.y,
                barRect.width,
                inactiveHeight);

            float inactiveTexHeight = inactiveTexture.height;

            // Sample the first pixel row.
            GUI.DrawTextureWithTexCoords(
                inactiveRect,
                inactiveTexture,
                new Rect(
                    0f,
                    (inactiveTexHeight - 1f) / inactiveTexHeight,
                    1f,
                    1f / inactiveTexHeight));

            if (percent > 0f)
            {
                Rect activeRect = new Rect(
                    barRect.x,
                    barRect.y,
                    barRect.width * percent,
                    activeHeight);

                GUI.DrawTextureWithTexCoords(
                    activeRect,
                    activeTexture,
                    new Rect(0f, 0f, percent, 1f));
            }
        }
        else
        {
            // ---------------------------------------------------------
            // NORMAL
            //
            // INACTL = 1 pixel high
            // ACTL   = 6 pixels high, above INACTL
            //
            // Hovered state samples the alternate half of each texture.
            // ---------------------------------------------------------

            Texture2D inactiveTexture = sliderInactiveTexture;
            Texture2D activeTexture = sliderActiveTexture;

            if (inactiveTexture == null || activeTexture == null)
                return;

            // INACTL --------------------------------------------------
            float inactiveHeight = 1f * Menu.RenderScale;
            float activeHeight = 6f * Menu.RenderScale;

            Rect inactiveRect = new Rect(
                barRect.x,
                barRect.y,
                barRect.width,
                inactiveHeight);

            // Texture is 1px high visually, but sample either:
            //
            //   normal: first pixel row
            //   hover:  second half of texture
            //
            float inactiveTexHeight = inactiveTexture.height;

            if (focused)
            {
                // Sample the lower half of the texture.
                GUI.DrawTextureWithTexCoords(
                    inactiveRect,
                    inactiveTexture,
                    new Rect(
                        0f,
                        0.5f,
                        1f,
                        0.5f));
            }
            else
            {
                // Sample the first pixel row.
                GUI.DrawTextureWithTexCoords(
                    inactiveRect,
                    inactiveTexture,
                    new Rect(
                        0f,
                        (inactiveTexHeight - 1f) / inactiveTexHeight,
                        1f,
                        1f / inactiveTexHeight));
            }

            // ACTL
            if (percent > 0f)
            {
                Rect activeRect = new Rect(
                    barRect.x,
                    barRect.y + inactiveHeight,
                    barRect.width * percent,
                    activeHeight);

                float activeTexHeight = activeTexture.height;

                if (focused)
                {
                    // Hovered: texture pixels 6-12.
                    GUI.DrawTextureWithTexCoords(
                        activeRect,
                        activeTexture,
                        new Rect(
                            0f,
                            (activeTexHeight - 12f) / activeTexHeight,
                            1f,
                            6f / activeTexHeight));
                }
                else
                {
                    // Normal: texture pixels 0-6.
                    GUI.DrawTextureWithTexCoords(
                        activeRect,
                        activeTexture,
                        new Rect(
                            0f,
                            (activeTexHeight - 6f) / activeTexHeight,
                            1f,
                            6f / activeTexHeight));
                }
            }
        }

        GUI.color = oldColor;
    }

    public override void Dispose()
    {
        base.Dispose();

        if (sliderReadOnlyActiveTexture != null)
            UnityEngine.Object.Destroy(sliderReadOnlyActiveTexture);

        if (sliderReadOnlyInactiveTexture != null)
            UnityEngine.Object.Destroy(sliderReadOnlyInactiveTexture);

        if (sliderActiveTexture != null)
            UnityEngine.Object.Destroy(sliderActiveTexture);

        if (sliderInactiveTexture != null)
            UnityEngine.Object.Destroy(sliderInactiveTexture);

        sliderReadOnlyActiveTexture = null;
        sliderReadOnlyInactiveTexture = null;
        sliderActiveTexture = null;
        sliderInactiveTexture = null;
    }

    public UISlider(UIMenu menu, int id, string name, bool readOnly)
        : this(menu, id, name, Rect.zero, readOnly)
    {
    }

    public UISlider(
        UIMenu menu,
        int id,
        string name,
        Rect rect,
        bool readOnly)
        : base(menu, id, name, rect)
    {
        this.readOnly = readOnly;

        // create the buttons
        leftButton = new BMButton(menu, "slider_larr", id * 10000, 5, Rect.zero);
        rightButton = new BMButton(menu, "slider_rarr", id * 100000, 5, Rect.zero);
        
        LoadTextures();

        leftButton.Visible = !readOnly;
        rightButton.Visible = !readOnly;

        rightButton.OnClick = () =>
        {
            if (MenuManager.Instance != null) MenuManager.Instance.PlaySound(MenuSound.SliderChange);
            Value += Step;
        };
        leftButton.OnClick = () =>
        {
            if (MenuManager.Instance != null) MenuManager.Instance.PlaySound(MenuSound.SliderChange);
            Value -= Step;
        };
    }
}