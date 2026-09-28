using System.Collections.Generic;
using UnityEngine;

[System.Flags]
public enum TextNodeEffect
{
    None = 0,

    /// <summary>0x01 - DT_CENTER | DT_VCENTER. Takes priority over Center and RightAlign.</summary>
    CenterBoth = 1 << 0,

    /// <summary>0x02 - DT_CENTER (horizontal only).</summary>
    Center = 1 << 1,

    /// <summary>0x04 - draws a filled/outlined rect behind the entry, before indenting.</summary>
    Box = 1 << 2,

    /// <summary>0x08 - drop shadow, offset by textHeight/9 down and right.</summary>
    Shadow = 1 << 3,

    /// <summary>0x10 - shifts the entry 5px right.</summary>
    Indent = 1 << 4,

    /// <summary>0x20 - DT_RIGHT, and halves the shadow offset.</summary>
    RightAlign = 1 << 5,

    /// <summary>0x40 - draw this entry even when Text is empty (box/background only).</summary>
    DrawIfEmpty = 1 << 6,

    /// <summary>0x80000000 - use HighlightColor instead of ForegroundColor.</summary>
    Highlight = 1 << 31
}

/// <summary>
/// A positioned list of text entries that renders itself into a pixel rect.
/// Knows nothing about menus, widgets or coordinate spaces - the caller converts.
/// </summary>
public class MMTextNode
{
    private struct Entry
    {
        public Vector2 Position;
        public string Text;
        public LocFont Font;
        public TextNodeEffect Effects;
    }

    private static readonly Color ShadowColor = new Color32(0x0F, 0x0F, 0x0F, 0xFF);
    private const float IndentAmount = 5.0f;
    private const float ShadowDivisor = 9.0f;

    public Color ForegroundColor = Color.white;
    public Color BackgroundColor = Color.black;
    public Color HighlightColor = Color.white;
    public Color BorderColor = Color.white;

    /// <summary>
    /// One bit per entry index. Bit i set means entry i is visible.
    /// Bits at or above EntryCount are ignored.
    /// </summary>
    public uint DrawBits = 0xFFFFFFFF;

    public int EntryCount => entries.Count;
    private readonly List<Entry> entries = new List<Entry>();

    /// <summary>
    /// Entry positions are offsets from the top-left of the draw rect, in whatever units
    /// the geometry scale passed to Draw converts to pixels.
    /// </summary>
    public void AddText(LocFont font, string text, TextNodeEffect effects, float x, float y)
    {
        entries.Add(new Entry()
        {
            Effects = effects,
            Position = new Vector2(x, y),
            Font = font,
            Text = text,
        });
    }

    public void SetTextPosition(int index, float x, float y)
    {
        if (index >= 0 && index < entries.Count)
        {
            var entry = entries[index];
            entry.Position = new Vector2(x, y);
            entries[index] = entry;
        }
    }

    public void SetString(int index, string text)
    {
        if (index >= 0 && index < entries.Count)
        {
            var entry = entries[index];
            entry.Text = text;
            entries[index] = entry;
        }
    }

    public void Clear()
    {
        entries.Clear();
    }

    /// <summary>
    /// Draws every enabled entry into <paramref name="pixelRect"/>, which is already in
    /// pixel coordinates.
    /// <paramref name="geometryScale"/> converts entry offsets, the indent and the shadow
    /// offset into pixels - it is the scale factor of whatever transform produced
    /// <paramref name="pixelRect"/>, NOT a font size multiplier.
    /// <paramref name="fontScale"/> is handed to the font renderer.
    /// </summary>
    public void Draw(Rect pixelRect, Vector2 geometryScale, float fontScale)
    {
        var originalColor = GUI.color;

        for (int i = 0; i < entries.Count; i++)
        {
            if (i < 32 && (DrawBits & (1u << i)) == 0u) continue;
            DrawEntry(entries[i], pixelRect, geometryScale, fontScale);
        }

        GUI.color = originalColor;
    }

    /// <summary>
    /// Convenience overload for callers whose entry offsets are already in pixels
    /// (the scale is then only used for the font).
    /// </summary>
    public void Draw(Rect pixelRect, float fontScale = 1.0f)
        => Draw(pixelRect, Vector2.one, fontScale);

    private static TextAnchor GetAlignment(TextNodeEffect effects)
    {
        if ((effects & TextNodeEffect.CenterBoth) != 0) return TextAnchor.MiddleCenter;
        if ((effects & TextNodeEffect.RightAlign) != 0) return TextAnchor.UpperRight;
        if ((effects & TextNodeEffect.Center) != 0) return TextAnchor.UpperCenter;
        return TextAnchor.UpperLeft;
    }

    private static float GetShadowOffset(Entry entry)
    {
        float lineHeight = entry.Font.referenceSize;

        float offset = lineHeight / ShadowDivisor;
        if ((entry.Effects & TextNodeEffect.RightAlign) != 0) offset *= 0.5f;

        // The original floored in layout space, before the pixel conversion.
        return Mathf.Max(1.0f, Mathf.Floor(offset));
    }

    private static void DrawLabel(Rect pixelRect, Entry entry, TextAnchor alignment, float fontScale)
    {
        var previousAlignment = GUI.skin.label.alignment;
        GUI.skin.label.alignment = alignment;

        UIDrawing.ScaledLabel(pixelRect, entry.Text, entry.Font, fontScale);

        GUI.skin.label.alignment = previousAlignment;
    }

    private void DrawEntry(Entry entry, Rect bounds, Vector2 scale, float fontScale)
    {
        var pos = bounds.position + Vector2.Scale(entry.Position, scale);

        // The original always stretched the rect out to the far edge of the target bitmap,
        // so alignment resolves against the node bounds rather than the text's own extents.
        var rect = new Rect(pos, new Vector2(bounds.xMax - pos.x, bounds.yMax - pos.y));

        // Box is drawn against the un-indented rect - the original calls Rectangle()
        // before it adds the indent to rc.left.
        if ((entry.Effects & TextNodeEffect.Box) != 0)
            UIDrawing.DrawBorder(rect, BorderColor);

        if ((entry.Effects & TextNodeEffect.Indent) != 0)
        {
            float indent = IndentAmount * scale.x;
            rect.x += indent;
            rect.width -= indent;
        }

        var alignment = GetAlignment(entry.Effects);

        if ((entry.Effects & TextNodeEffect.Shadow) != 0)
        {
            float offset = GetShadowOffset(entry);
            var shadowRect = new Rect(
                rect.x + offset * scale.x,
                rect.y + offset * scale.y,
                rect.width,
                rect.height);

            GUI.color = ShadowColor;
            DrawLabel(shadowRect, entry, alignment, fontScale);
        }

        GUI.color = (entry.Effects & TextNodeEffect.Highlight) != 0
            ? HighlightColor
            : ForegroundColor;

        DrawLabel(rect, entry, alignment, fontScale);
    }
}