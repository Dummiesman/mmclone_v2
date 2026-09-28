using System;
using UnityEngine;

public class TextButton : UIWidget
{
    public Action OnClick;
    public MenuSound Sound = MenuSound.None;

    private string text = string.Empty;
    public string Text
    {
        get => text;
        set
        {
            if (textNode != null) textNode.SetString(0, value);
            text = value;
        }
    }

    private readonly Color hoverColor = new Color(1f, 1f, 0f, 1f);
    private readonly Color normalColor = Color.white;
    private readonly Color disabledColor = Color.grey;

    private MMTextNode textNode;

    private bool focused;
    private bool pressed;

    // Drawing

    public override void Draw()
    {
        base.Draw();

        if (string.IsNullOrEmpty(Text))
            return;

        // Focus is what BMButton uses for its hover frame, so match it here.
        if(Enabled)
            textNode.ForegroundColor = (focused || pressed) ? hoverColor : normalColor;
        else
            textNode.ForegroundColor = disabledColor;

        var pixelCoords = Menu.ToPixelCoordinates(Rect);
        textNode.Draw(pixelCoords, Menu.RenderScale);
    }

    // Events

    public override void Focus()
    {
        base.Focus();
        focused = true;
    }

    public override void Unfocus()
    {
        base.Unfocus();
        focused = false;
    }

    private void PlayClickSound()
    {
        if (Sound != MenuSound.None && MenuManager.Instance != null)
            MenuManager.Instance.PlaySound(Sound);
    }

    public override bool HandleInput(UIEvent input)
    {
        if (base.HandleInput(input))
            return true;

        if (input == UIEventType.MouseDown && focused && MouseContained())
        {
            pressed = true;
            PlayClickSound();

            Menu.FocusLock(this);
            return true;
        }

        if (input == UIEventType.MouseUp && pressed)
        {
            pressed = false;
            Menu.ReleaseFocusLock(this);

            // Fire only if the mouse was released within the button bounds.
            if (MouseContained())
                OnClick?.Invoke();

            return true;
        }

        if (input == UIEventType.Enter && focused)
        {
            PlayClickSound();

            Menu.ReleaseFocusLock(this);
            OnClick?.Invoke();
            return true;
        }

        return false;
    }

    public override void Dispose()
    {
        if (pressed)
        {
            pressed = false;
            Menu.ReleaseFocusLock(this);
        }
    }

    // Construction
    public TextButton(UIMenu menu, string name, int id, string text, LocFont font, TextNodeEffect effects, Rect rect)
        : base(menu, id, name, rect)
    {
        this.text = text;
        textNode = new MMTextNode();
        textNode.AddText(font, text, effects, 0.0f, 0.0f);
    }

    public TextButton(UIMenu menu, string name, int id, string text, LocFont font, Rect rect)
       : this(menu, name, id, text, font, TextNodeEffect.None, rect)
    {
    }
}