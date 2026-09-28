using System;
using UnityEngine;

public class UIWidget : IDisposable
{
    public string Name => name;
    public int ID => id;

    public UIMenu Menu => menu;

    // Rect, 0-1 space
    public virtual Rect Rect
    {
        get => rect;
        set => rect = value;
    }

    public Action OnFocus;
    public Action OnUnfocus;

    public bool Enabled
    {
        get => enabled;
        set 
        {
            if(value != enabled)
            {
                enabled = value;
                OnEnabledChanged();
            }
        }
    }

    public bool Visible
    {
        get => visible;
        set
        {
            if (value != visible)
            {
                visible = value;
                OnVisibilityChanged();
            }
        }
    }

    public virtual bool EnableNavigation => true;
    public virtual bool WantsTextInput => false;

    private int id;
    private Rect rect = Rect.zero;
    private string name = string.Empty;
    private UIMenu menu;
    private bool enabled = true;
    private bool visible = true;

    // Helper
    protected bool MouseContained(Rect rect)
    {
        return menu.WidgetContainsMouse(rect);
    }

    protected bool MouseContained(UIWidget widget)
    {
        return menu.WidgetContainsMouse(widget);
    }

    protected bool MouseContained()
    {
        return menu.WidgetContainsMouse(this);
    }

    // Events
    public virtual void OnVisibilityChanged() { }
    public virtual void OnEnabledChanged() { }
    public virtual void Activate() { }
    public virtual void Deactivate() { }
    public virtual void Focus() { OnFocus?.Invoke();  }
    public virtual void Unfocus() { OnUnfocus?.Invoke();  }
    public virtual bool HandleInput(UIEvent input) { return false; }
    public virtual void Update() { }
    public virtual void Draw() { }
    public virtual void DrawOverlay() { }
    public virtual void Dispose() { }

    public UIWidget(UIMenu menu, int id, string name, Rect rect)
    {
        this.menu = menu;
        this.id = id;
        this.name = name;
        Rect = rect;
    }

    public UIWidget(UIMenu menu, int id, string name)
    {
        this.menu = menu;
        this.id = id;
        this.name = name;
    }
}
