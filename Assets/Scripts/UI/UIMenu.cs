using Dummiesman.TextureLoading;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class UIMenu : IDisposable
{
    public MenuID ID => id;
    private readonly MenuID id;

    public virtual bool CreatesHistoryEntry => true;
    public virtual bool HasBackButton => true;
    public virtual bool HasNavBar => true;
    public virtual bool IsAnOptionMenu => false;

    public IReadOnlyList<UIWidget> Widgets => widgets;
    private List<UIWidget> widgets = new List<UIWidget>();

    public UIWidget FocusedWidget => focusedWidget;
    private UIWidget focusedWidget = null;
    private bool lockFocus;

    public Vector2 PointerPosition => pointerPosition;

    private bool drawWidgetDebug;

    // Render area in screen pixels
    public Rect RenderArea
    {
        get => renderArea;
        protected set => renderArea = value;
    }
    private Rect renderArea = Rect.zero;

    public virtual float RenderScale
    {
        get
        {
            float baseWidth = backgroundImage != null ? backgroundImage.width : UIConstants.ReferenceWidth;
            float baseHeight = backgroundImage != null ? backgroundImage.height : UIConstants.ReferenceHeight;

            if (baseWidth <= 0f || baseHeight <= 0f)
                return 1f;

            return Mathf.Min(renderArea.width / baseWidth, renderArea.height / baseHeight);
        }
    }

    public Texture2D BackgroundImage
    {
        get => backgroundImage;
    }
    private Texture2D backgroundImage = null;

    // input handling
    private Vector2 pointerPosition;
    private bool pointerDown;
    private bool pointerUp;

    private BMLabel descriptionLabels;

    private AudioClip entranceAudio;

    // Description
    protected void SetupDescriptionLabelEvents(params UIWidget[] buttons)
    {
        for(int i=0; i < buttons.Length; i++)
        {
            int i2 = i;
            var widget = buttons[i];
            widget.OnFocus += () => { SetDescriptionLabelIndex(i2); };
            widget.OnUnfocus += ClearDescriptionLabel;
        }
    }

    protected void SetDescriptionLabel(BMLabel descriptionLabel)
    {
        descriptionLabels = descriptionLabel;
    }

    public void SetDescriptionLabelIndex(int index)
    {
        if(descriptionLabels != null)
        {
            descriptionLabels.ImageIndex = index;
        }
    }

    public void ClearDescriptionLabel()
    {
        if (descriptionLabels != null)
        {
            descriptionLabels.ImageIndex = -1;
        }
    }

    // Widget helper
    public bool WidgetContainsMouse(Rect rect)
    {
        return ToPixelCoordinates(rect).Contains(pointerPosition);
    }

    public bool WidgetContainsMouse(UIWidget widget)
    {
        return ToPixelCoordinates(widget.Rect).Contains(pointerPosition);
    }

    // Focusing
    protected void SetWidgetFocus(UIWidget widget)
    {
        if(lockFocus)
        {
            Debug.LogError($"SetWidgetFocus called with focus lock enabled.");
            return;
        }
        if(focusedWidget == widget) return; // already focusing
        if(focusedWidget != null)
        {
            focusedWidget.Unfocus();
        }
        focusedWidget = widget;
        if(focusedWidget != null)
        {
            focusedWidget.Focus();
        }
    }

    public void FocusLock(UIWidget focusedWidget)
    {
        // only allow unlocking from the source widget
        if (focusedWidget == this.focusedWidget)
        {
            lockFocus = true;
        }
        else
        {
            Debug.LogWarning("UIMenu.FocusLock - called from a widget which is not the current focus");
        }
    }

    public void ReleaseFocusLock(UIWidget focusedWidget)
    {
        // only allow unlocking from the source widget
        // but also allow null for ensurance calls
        if (focusedWidget == this.focusedWidget || this.focusedWidget == null)
        {
            lockFocus = false;
        }
        else
        {
            Debug.LogWarning("UIMenu.ReleaseFocusLock - called from a widget which is not the current focus");
        }
    }

    // Navigation
    private List<UIWidget> GetNavigableWidgets()
    {
        return widgets.Where(w => w.EnableNavigation && w.Enabled && w.Visible).ToList();
    }

    public void AdvanceNav()
    {
        var navigable = GetNavigableWidgets();
        if (navigable.Count == 0) return;

        if (focusedWidget == null || !navigable.Contains(focusedWidget))
        {
            SetWidgetFocus(navigable.First());
        }
        else
        {
            int index = navigable.IndexOf(focusedWidget);
            SetWidgetFocus(navigable[(index + 1) % navigable.Count]);
        }
    }

    public void PreviousNav()
    {
        var navigable = GetNavigableWidgets();
        if (navigable.Count == 0) return;

        if (focusedWidget == null || !navigable.Contains(focusedWidget))
        {
            SetWidgetFocus(navigable.Last());
        }
        else
        {
            int index = navigable.IndexOf(focusedWidget);
            int prevIndex = (index - 1 + navigable.Count) % navigable.Count;
            SetWidgetFocus(navigable[prevIndex]);
        }
    }

    // Coordinate translation
    protected static Rect GetLetterboxedOrPillarboxedRect(float targetAspect = 4f / 3f)
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

    public Rect ToPixelCoordinates(Rect widgetCoords)
    {
        // relative to screen
        return new Rect((widgetCoords.x * renderArea.width) + renderArea.x,
                        (widgetCoords.y * renderArea.height) + renderArea.y,
                        widgetCoords.width * renderArea.width, widgetCoords.height * renderArea.height);
    }

    public Rect ToWidgetCoords(Rect pixelCoords)
    {
        // screen to relative
        return new Rect((pixelCoords.x - renderArea.x) / renderArea.width,
                        (pixelCoords.y - renderArea.y) / renderArea.height,
                        pixelCoords.width / renderArea.width,
                        pixelCoords.height / renderArea.height);
    }

    public Rect MenuToWidgetCoords(Rect menuCoords)
    {
        if(backgroundImage != null)
        {
            var dialogSize = new Vector2(BackgroundImage.width, BackgroundImage.height);

            // dialog to relative
            return new Rect(menuCoords.x / dialogSize.x, menuCoords.y / dialogSize.y,
                            menuCoords.width / dialogSize.x, menuCoords.height / dialogSize.y);
        }
        else
        {
            // menu to relative
            // the menu system in the original game ran at this fixed resolution
            return new Rect(menuCoords.x / UIConstants.ReferenceWidth, menuCoords.y / UIConstants.ReferenceHeight,
                            menuCoords.width / UIConstants.ReferenceWidth, menuCoords.height / UIConstants.ReferenceHeight);
        }
    }

    public void AssignSwitchAudio(string name)
    {
        if(entranceAudio != null)
        {
            UnityEngine.Object.Destroy(entranceAudio);
        }
        entranceAudio = AudioAssetManager.LoadClip(name);
    }

    public void AssignBackground(string name)
    {
        if(backgroundImage != null)
        {
            UnityEngine.Object.Destroy(backgroundImage);
        }

        using (var stream = AssetManager.Open("jpg", $"{name}.jpg"))
        {
            if (stream != null)
            {
                backgroundImage = ImageLoader.LoadTexture(stream, ImageLoader.TextureFormat.JPG);
                backgroundImage.wrapMode = TextureWrapMode.Clamp;
            }
        }
    }

    public virtual void Activate()
    {
        // set mouse position so widget triggers don't happen on frame 1
        // thereby deactivating a widget activated on a derived Activate()
        UpdatePointerState();

        // play entrance audio
        if(entranceAudio != null && MenuManager.Instance != null)
        {
            MenuManager.Instance.PlaySound(entranceAudio);
        }

        // notify widgets
        foreach(var widget in widgets)
        {
            widget.Activate();
        }
    }

    public virtual void Deactivate()
    {
        // release widget focus on deactivation
        if(focusedWidget != null)
        {
            focusedWidget.Unfocus();
            focusedWidget = null;
            lockFocus = false;
        }

        // notify widgets
        foreach (var widget in widgets)
        {
            widget.Deactivate();
        }
    }

    public virtual void DrawBackground()
    {
        if (BackgroundImage != null)
        {
            // determine render area
            RenderArea = GetLetterboxedOrPillarboxedRect();

            // draw background
            GUI.DrawTexture(renderArea, BackgroundImage);
        }
    }

    public virtual void Draw()
    {
        // determine render area
        RenderArea = GetLetterboxedOrPillarboxedRect();

        // draw widgets
        foreach(var widget in Widgets)
        {
            if (widget.Visible)
            {
                widget.Draw();
                if (drawWidgetDebug)
                {
                    var borderArea = this.ToPixelCoordinates(widget.Rect);
                    var textArea = new Rect(borderArea.x + 4, borderArea.y + 1, borderArea.width - 4, borderArea.height - 1);
                    UIDrawing.DrawBorder(borderArea, (widget == focusedWidget) ? Color.yellow : Color.white);
                    GUI.Label(textArea, widget.Name);
                }
            }
        }
        foreach (var widget in Widgets)
        {
            if (widget.Visible)
            {
                widget.DrawOverlay();
            }
        }
    }

    private void UpdatePointerState()
    {
        if (Application.isMobilePlatform)
        {
            if (Input.touchCount > 0)
            {
                pointerPosition = Input.GetTouch(0).position;
                pointerUp = Input.GetTouch(0).phase == TouchPhase.Ended || Input.GetTouch(0).phase == TouchPhase.Canceled;
                pointerDown = Input.GetTouch(0).phase == TouchPhase.Began;
                pointerPosition.y = Screen.height - pointerPosition.y;
            }
            else if (pointerDown)
            {
                pointerDown = false;
                pointerUp = true;
            }
            else if (pointerUp)
            {
                pointerUp = false;
            }
        }
        else
        {
            pointerPosition = Input.mousePosition;
            pointerUp = Input.GetMouseButtonUp(0);
            pointerDown = Input.GetMouseButtonDown(0);
            pointerPosition.y = Screen.height - pointerPosition.y;
        }
    }

    public virtual void Update()
    {
        // handle when a focused widget becomes disabled
        if(focusedWidget != null && (!focusedWidget.Enabled || !focusedWidget.Visible))
        {
            ReleaseFocusLock(focusedWidget);
            SetWidgetFocus(null);
        }

        // handle inputs
        var lastPointerPosition = pointerPosition;
        UpdatePointerState();

        bool mouseMoved = pointerPosition != lastPointerPosition;
        if (mouseMoved || pointerUp)
        {
            // check focused widget first
            // unfocus if mouse is no longer in it when focus is locked
            if (focusedWidget != null)
            {
                if (!WidgetContainsMouse(focusedWidget) && !lockFocus)
                {
                    SetWidgetFocus(null);
                }
            }

            // check for new focused widget
            if (focusedWidget == null)
            {
                foreach (var widget in widgets)
                {
                    if (widget.Enabled && widget.Visible)
                    {
                        if (WidgetContainsMouse(widget))
                        {
                            SetWidgetFocus(widget);
                            break;
                        }
                    }
                }
            }
        }

        // handle inputs (clicks, key presses)
        bool typing = focusedWidget != null && focusedWidget.WantsTextInput;
        if (!typing && Input.GetKeyDown(KeyCode.W))
        {
            drawWidgetDebug = !drawWidgetDebug;
        }

        if (focusedWidget != null)
        {
            if (pointerDown)
            {
                focusedWidget.HandleInput(UIEventType.MouseDown);
            }
            if (pointerUp)
            {
                focusedWidget.HandleInput(UIEventType.MouseUp);
            }
            if(Input.GetKeyDown(KeyCode.Return))
            {
                focusedWidget.HandleInput(UIEventType.Enter);
            }
            if (Input.GetKeyUp(KeyCode.UpArrow))
            {
                focusedWidget.HandleInput(UIEventType.NavPrev);
            }
            if (Input.GetKeyUp(KeyCode.DownArrow) || Input.GetKeyUp(KeyCode.Tab))
            {
                focusedWidget.HandleInput(UIEventType.NavNext);
            }
            if(Input.GetKeyUp(KeyCode.LeftArrow))
            {
                focusedWidget.HandleInput(UIEventType.Left);
            }
            if(Input.GetKeyUp(KeyCode.RightArrow))
            {
                focusedWidget.HandleInput(UIEventType.Right);
            }
            if (typing)
            {
                var mods = UIModifiers.None;
                if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) mods |= UIModifiers.Shift;
                if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)
                 || Input.GetKey(KeyCode.LeftCommand) || Input.GetKey(KeyCode.RightCommand)) mods |= UIModifiers.Ctrl;


                foreach (char c in Input.inputString)
                {
                    if (c == '\b')                      
                        focusedWidget.HandleInput(new UIEvent(UIEventType.Backspace, mods));
                    else if (c == '\n' || c == '\r')    // already sent as Enter above
                        continue;
                    else if (!char.IsControl(c))
                        focusedWidget.HandleInput(UIEvent.Text(c, mods));
                }
                if (Input.GetKeyDown(KeyCode.Delete)) focusedWidget.HandleInput(new UIEvent(UIEventType.Delete, mods));
            }
        }
        if (!lockFocus)
        {
            if (Input.GetKeyUp(KeyCode.UpArrow))
            {
                PreviousNav();
            }
            if (Input.GetKeyUp(KeyCode.DownArrow) || Input.GetKeyUp(KeyCode.Tab))
            {
                AdvanceNav();
            }
        }

        // update widgets
        foreach(var widget in widgets)
        {
            widget.Update();
        }
    }

    // Widget helpers
    public UIWidget AddWidget(UIWidget widget)
    {
        widgets.Add(widget);
        return widget;
    }

    public MMTextNodeWidget AddLabel(string name, string text, Rect rect, int fontSize)
    {
        int widgetId = widgets.Count;

        var textNodeWidget = new MMTextNodeWidget(this, widgetId, rect);
        var font = MenuManager.Instance.GetFont(fontSize);
        textNodeWidget.AddText(font, text, TextNodeEffect.None, 0.0f, 0.0f); 
        AddWidget(textNodeWidget);

        return textNodeWidget;
    }

    public TextRoller AddTextRoller(string name, Rect rect, int min, int max, int defaultValue = 0)
    {
        int widgetId = widgets.Count;
        var roller = new TextRoller(this, widgetId, name, rect, min, max, defaultValue);

        if (WidgetTuning.RetrieveWidgetData((int)this.ID, name, out var tunedRect))
        {
            var menuCoords = new Rect(tunedRect.x, tunedRect.y, tunedRect.width, tunedRect.height);
            roller.Rect = MenuToWidgetCoords(menuCoords);
        }

        widgets.Add(roller);
        return roller;
    }

    public UISlider AddSlider(string name, Rect rect, float min, float max, bool readOnly)
    {
        int widgetId = widgets.Count;
        var slider = new UISlider(this, widgetId, name, rect, readOnly);

        slider.Min = min;
        slider.Max = max;
        slider.Value = min;

        if (WidgetTuning.RetrieveWidgetData((int)this.ID, name, out var tunedRect))
        {
            var menuCoords = new Rect(tunedRect.x, tunedRect.y, tunedRect.width, tunedRect.height);
            slider.Rect = MenuToWidgetCoords(menuCoords);
        }

        widgets.Add(slider);
        return slider;
    }

    public UISlider AddSlider(string name, Rect rect, bool readOnly)
    {
        return AddSlider(name, rect, 0.0f, 1.0f, readOnly);
    }

    public UIIcon AddIcon(string name, float x, float y)
    {
        int widgetId = widgets.Count;
        var icon = new UIIcon(this, name, widgetId, Rect.zero);
        var iconHitArea = icon.ImageSize;

        if (WidgetTuning.RetrieveWidgetData((int)this.ID, name, out var tunedRect))
        {
            var menuCoords = new Rect(tunedRect.x, tunedRect.y, iconHitArea.x, iconHitArea.y);
            icon.Rect = MenuToWidgetCoords(menuCoords);
        }
        else
        {
            var hitAreaMenuCoords = MenuToWidgetCoords(new Rect(0, 0, iconHitArea.x, iconHitArea.y));
            icon.Rect = new Rect(x, y, hitAreaMenuCoords.width, hitAreaMenuCoords.height);
        }

        widgets.Add(icon);
        return icon;
    }

    public BMButton AddBMButton(string name, string widgetTuneName, float x, float y, int numStates)
    {
        int widgetId = widgets.Count;
        var button = new BMButton(this, name, widgetId, numStates, Rect.zero);
        var buttonHitArea = button.HitRegionSize;

        if (WidgetTuning.RetrieveWidgetData((int)this.ID, widgetTuneName, out var tunedRect))
        {
            var menuCoords = new Rect(tunedRect.x, tunedRect.y, buttonHitArea.x, buttonHitArea.y);
            button.Rect = MenuToWidgetCoords(menuCoords);
        }
        else
        {
            var hitAreaMenuCoords = MenuToWidgetCoords(new Rect(0, 0, buttonHitArea.x, buttonHitArea.y));
            button.Rect = new Rect(x, y, hitAreaMenuCoords.width, hitAreaMenuCoords.height);
        }

        widgets.Add(button);
        return button;
    }

    public BMButton AddBMButton(string name, float x, float y, int numStates)
    {
        return AddBMButton(name, name, x, y, numStates);
    }

    public BMLabel AddBMLabel(string name, float x, float y, string images)
    {
        int widgetId = widgets.Count;
        var label = new BMLabel(this, name, widgetId, Rect.zero);

        if (WidgetTuning.RetrieveWidgetData((int)this.ID, name, out var tunedRect))
        {
            var menuCoords = new Rect(tunedRect.x, tunedRect.y, 0.0f, 0.0f);
            label.Rect = MenuToWidgetCoords(menuCoords);
        }
        else
        {
            label.Rect = MenuToWidgetCoords(new Rect(x, y, 0.0f, 0.0f));
        }
        
        if (!string.IsNullOrEmpty(images))
        {
            label.SetImages(images);
        }

        widgets.Add(label);
        return label;
    }

    public TextDropdown AddTextDropdown(string name, float x, float y, float width, float height, string items)
    {
        return AddTextDropdown(name, x, y, width, height, items, "dropdown_bx");
    }

    public TextDropdown AddTextDropdown(string name, float x, float y, float width, float height, string items, string background)
    {
        int widgetId = widgets.Count;
        var dropdown = new TextDropdown(this, name, widgetId, background, new Rect(x, y, width, height));

        if (WidgetTuning.RetrieveWidgetData((int)this.ID, name, out var tunedRect))
        {
            dropdown.Rect = MenuToWidgetCoords(tunedRect);
        }

        dropdown.SetItems(items);
        widgets.Add(dropdown);
        return dropdown;
    }

    public TextButton AddTextButton(string name, float x, float y, float w, float h, string text, TextNodeEffect effects, LocFont font)
    {
        int widgetId = widgets.Count;
        var button = new TextButton(this, name, widgetId, text, font, effects, new Rect(x, y, w, h));

        if (WidgetTuning.RetrieveWidgetData((int)this.ID, name, out var tunedRect))
        {
            button.Rect = MenuToWidgetCoords(tunedRect);
        }

        widgets.Add(button);
        return button;
    }

    public TextButton AddTextButton(string name, float x, float y, float w, float h, string text, LocFont font)
    {
        return AddTextButton(name, x, y, w, h, text, TextNodeEffect.None, font);
    }

    public TextButton AddTextButton(string name, float x, float y, float w, float h, string text, TextNodeEffect effects, int fontSize)
    {
        var font = MenuManager.Instance.GetFont(fontSize);
        return AddTextButton(name, x, y, w, h, text, effects, font);
    }

    public TextButton AddTextButton(string name, float x, float y, float w, float h, string text, int fontSize)
    {
        return AddTextButton(name, x, y, w, h, text, TextNodeEffect.None, fontSize);
    }

    public UITextField AddTextField(string name, float x, float y, float width, float height, int maxLength = int.MaxValue)
    {
        int widgetId = widgets.Count;
        var textField = new UITextField(this, name, widgetId, new Rect(x, y, width, height));
        textField.MaxLength = maxLength;

        if (WidgetTuning.RetrieveWidgetData((int)this.ID, name, out var tunedRect))
        {
            textField.Rect = MenuToWidgetCoords(tunedRect);
        }

        widgets.Add(textField);
        return textField;
    }

    // Cleanup
    public virtual void Dispose()
    {
        foreach (var widget in widgets)
        {
            widget.Dispose();
        }
        widgets.Clear();

        if(backgroundImage != null)
        {
            UnityEngine.Object.Destroy(backgroundImage);
        }
        if(entranceAudio != null)
        {
            UnityEngine.Object.Destroy(entranceAudio);
        }
    }

    // Constructor
    protected UIMenu(MenuID id)
    {
        this.id = id;
        if(MenuManager.Instance != null)
        {
            MenuManager.Instance.AddMenu(this);
        }
    }
}
