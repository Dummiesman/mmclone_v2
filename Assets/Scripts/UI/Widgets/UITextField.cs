using System;
using UnityEngine;

public class UITextField : UIWidget
{
    public Action<string> OnTextChanged;

    public string Text
    {
        get => text;
        set
        {
            text = value;
            OnTextChanged?.Invoke(text);
        }
    }

    public int MaxLength
    {
        get => maxLength;
        set
        {
            maxLength = value;
            if (Text.Length > maxLength) Text = Text.Substring(0, maxLength);
        }
    }

    public override bool WantsTextInput => editing;

    private int maxLength = int.MaxValue;
    private string text = string.Empty;

    private Texture2D borderTexture;
    
    private LocFont font;
    private Color textColor = new Color(1f, 1f, 0f, 1f);
    private Color activeColor = Color.red;

    private bool editing = false;

    public override void Focus()
    {
        base.Focus();
        editing = true;
    }

    public override void Unfocus()
    {
        base.Unfocus();
        editing = false;
    }

    public override bool HandleInput(UIEvent input)
    {
        if (base.HandleInput(input)) return true;
        if(input.Type == UIEventType.Backspace)
        {
            if(text.Length > 0)
            {
                Text = Text.Substring(0, text.Length - 1);
            }
            return true;
        }
        else if(input.Type == UIEventType.Text)
        {
            if (text.Length < maxLength)
            {
                Text += input.Character;
            }
            return true;
        }
        return false;
    }

    public override void Draw()
    {
        var pixelCoords = Menu.ToPixelCoordinates(Rect);
        UIDrawing.DrawBorder(pixelCoords, borderTexture);

        var oldColor = GUI.color;
        if (editing)
        {
            GUI.color = activeColor;
        }
        else
        {
            GUI.color = textColor;
        }

        var textLocation = new Rect(pixelCoords.x + 2, pixelCoords.y + 0,
                            pixelCoords.width - 2, pixelCoords.height - 0);
        UIDrawing.ScaledLabel(textLocation, Text, font, Menu.RenderScale);

        GUI.color = oldColor;
    }

    public override void Dispose()
    {
        if (borderTexture != null) UnityEngine.Object.Destroy(borderTexture);
    }

    public UITextField(UIMenu menu, string name, int id) : this(menu, name, id, Rect.zero)
    {
    }

    public UITextField(UIMenu menu, string name, int id, Rect rect) : base(menu, id, name, rect)
    {   
        // get font
        if (MenuManager.Instance != null)
        {
            font = MenuManager.Instance.GetFont(MenuManager.Instance.DropdownFontSize);
        }

        // init textures
        borderTexture = new Texture2D(1, 1);
        borderTexture.SetPixel(0, 0, Color.white);
        borderTexture.Apply(false);
    }
}
