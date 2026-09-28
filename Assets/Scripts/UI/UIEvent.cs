using System;
using UnityEngine;

public enum UIEventType
{
    // Nav/Events
    NavNext,
    NavPrev,
    Left,
    Right,
    Back,
    MouseDown,
    MouseUp,
    Enter,

    // Text editing
    Text,
    Backspace,
    Delete,
}

[Flags]
public enum UIModifiers
{
    None = 0,
    Shift = 1 << 0,
    Ctrl = 1 << 1,
    Alt = 1 << 2,
}

public readonly struct UIEvent : IEquatable<UIEvent>
{
    public readonly UIEventType Type;
    public readonly char Character;
    public readonly KeyCode Key;
    public readonly UIModifiers Modifiers;

    public bool Shift => (Modifiers & UIModifiers.Shift) != 0;
    public bool Ctrl => (Modifiers & UIModifiers.Ctrl) != 0;
    public bool Alt => (Modifiers & UIModifiers.Alt) != 0;

    public UIEvent(UIEventType type, UIModifiers modifiers = UIModifiers.None)
    {
        Type = type;
        Character = '\0';
        Key = KeyCode.None;
        Modifiers = modifiers;
    }

    public UIEvent(UIEventType type, KeyCode key, UIModifiers modifiers)
    {
        Type = type;
        Character = '\0';
        Key = key;
        Modifiers = modifiers;
    }

    public static UIEvent Text(char c, UIModifiers modifiers = UIModifiers.None)
        => new UIEvent(c, modifiers);

    private UIEvent(char c, UIModifiers modifiers)
    {
        Type = UIEventType.Text;
        Character = c;
        Key = KeyCode.None;
        Modifiers = modifiers;
    }

    // adapter for old code
    public static implicit operator UIEvent(UIEventType type) => new UIEvent(type);
    public static bool operator ==(UIEvent e, UIEventType t) => e.Type == t;
    public static bool operator !=(UIEvent e, UIEventType t) => e.Type != t;

    public bool Equals(UIEvent other)
        => Type == other.Type && Character == other.Character
        && Key == other.Key && Modifiers == other.Modifiers;

    public override bool Equals(object obj)
    {
        if (obj is UIEvent e) return Equals(e);
        if (obj is UIEventType t) return Type == t;
        return false;
    }

    public override int GetHashCode()
        => ((int)Type * 397) ^ (Character * 31) ^ ((int)Key * 17) ^ (int)Modifiers;

    public override string ToString()
        => Type == UIEventType.Text ? $"Text('{Character}') {Modifiers}" : $"{Type} {Key} {Modifiers}";
}