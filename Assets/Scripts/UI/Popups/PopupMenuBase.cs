using System;
using System.Runtime.InteropServices.WindowsRuntime;
using UnityEngine;

public class PopupMenuBase : UIMenu
{
    private static Color32 PopupBgColor = new Color32(0x10, 0x1F, 0x5D, 0x80);
    
    public override float RenderScale => renderScale;
    public override bool HasNavBar => false;

    private float renderScale = 1.0f;
    private Vector2 referenceSize = new Vector2(384, 384); // Reference size in pixels

    public void AddTitle(string text)
    {
        AddLabel("PU.Title", text, new Rect(0.0f, 0.0f, 1.0f, 0.1f), 32);
    }

    public TextButton AddExit()
    {
        string text = Localization.GetString(LocString.PopupExit);
        var button = new TextButton(this, "Popup.Exit", 101, text, MenuManager.Instance.GetFont(24), 
                                                                   TextNodeEffect.CenterBoth | TextNodeEffect.Box, 
                                                                   new Rect(0.5f, 0.9f, 0.5f, 0.1f));
        AddWidget(button);

        button.Sound = MenuSound.BeepDouble;
        button.OnClick += () =>
        {
            if (MenuManager.Instance != null)
            {
                MenuManager.Instance.DeactivateMenu();
                MenuManager.Instance.enabled = false;
            }

            // unpause game
            Time.timeScale = 1.0f;
            MMAudioMixer.Unmute();
        };

        return button;
    }

    public void AddOkCancel(Action okAction, Action cancelAction)
    {
        string text = Localization.GetString(LocString.PopupCancel);
        var cancelButton = new TextButton(this, "Popup.Cancel", 102, text, MenuManager.Instance.GetFont(24),
                                                                   TextNodeEffect.CenterBoth | TextNodeEffect.Box,
                                                                   new Rect(0.0f, 0.9f, 0.4f, 0.1f));
        AddWidget(cancelButton);

        cancelButton.Sound = MenuSound.BeepDouble;
        if (cancelAction != null) cancelButton.OnClick += cancelAction;

        text = Localization.GetString(LocString.PopupOk);
        var okButton = new TextButton(this, "Popup.Ok", 103, text, MenuManager.Instance.GetFont(24),
                                                                   TextNodeEffect.CenterBoth | TextNodeEffect.Box,
                                                                   new Rect(0.6f, 0.9f, 0.4f, 0.1f));
        AddWidget(okButton);

        okButton.Sound = MenuSound.BeepDouble;
        if (okAction != null) okButton.OnClick += okAction;

    }

    public void AddPreviousButton(MenuID returnTo)
    {
        string text = Localization.GetString(LocString.PopupPreviousMenu);
        var button = new TextButton(this, "Popup.Previous", 100, text, MenuManager.Instance.GetFont(24),
                                                                   TextNodeEffect.CenterBoth | TextNodeEffect.Box,
                                                                   new Rect(0.5f, 0.9f, 0.5f, 0.1f));
        AddWidget(button);

        button.Sound = MenuSound.BeepDouble;
        button.OnClick += () =>
        {
            if (MenuManager.Instance != null)
            {
                MenuManager.Instance.SwitchTo(returnTo);
            }
        };
    }

    public override void Draw()
    {
        // determine render scale and area
        float maxScale = 1000.0f;
        renderScale = Mathf.Min(maxScale, Screen.height / UIConstants.ReferenceHeight);

        Vector2 size = referenceSize * renderScale;

        Rect bgRect = new Rect(
                (Screen.width - size.x) * 0.5f,
                (Screen.height - size.y) * 0.5f,
                size.x,
                size.y);
        RenderArea = bgRect; // for controls

        Color prevColor = GUI.color;
        GUI.color = PopupBgColor;
        GUI.DrawTexture(bgRect, Texture2D.whiteTexture, ScaleMode.StretchToFill, true);
        GUI.color = prevColor;

        // draw widgets
        foreach (var widget in Widgets)
        {
            if (widget.Visible)
            {
                widget.Draw();
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

    public PopupMenuBase(MenuID id) : base(id)
    {
    }

    public PopupMenuBase(MenuID id, Vector2 referenceSize) : base(id)
    {
        this.referenceSize = referenceSize;   
    }
}
