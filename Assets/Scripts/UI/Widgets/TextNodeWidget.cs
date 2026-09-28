using UnityEngine;

/// <summary>
/// Widget wrapper around MMTextNode. Owns the menu, converts the widget rect into
/// pixel coordinates, and forwards the same API the old TextNode exposed.
/// </summary>
public class MMTextNodeWidget : UIWidget
{
    private static int NumInstances = 0;

    public override bool EnableNavigation => false;

    public MMTextNode Node { get; } = new MMTextNode();

    public Color ForegroundColor
    {
        get => Node.ForegroundColor;
        set => Node.ForegroundColor = value;
    }

    public Color BackgroundColor
    {
        get => Node.BackgroundColor;
        set => Node.BackgroundColor = value;
    }

    public Color HighlightColor
    {
        get => Node.HighlightColor;
        set => Node.HighlightColor = value;
    }

    public Color BorderColor
    {
        get => Node.BorderColor;
        set => Node.BorderColor = value;
    }

    /// <summary>
    /// One bit per entry index. Bit i set means entry i is visible.
    /// Bits at or above EntryCount are ignored.
    /// </summary>
    public uint DrawBits
    {
        get => Node.DrawBits;
        set => Node.DrawBits = value;
    }

    public int EntryCount => Node.EntryCount;

    public MMTextNodeWidget(UIMenu menu, int id, Rect rect)
        : base(menu, id, $"TextNode{NumInstances++}", rect)
    {
    }

    public void AddText(LocFont font, string text, TextNodeEffect effects, float x, float y)
        => Node.AddText(font, text, effects, x, y);

    public void SetTextPosition(int index, float x, float y)
        => Node.SetTextPosition(index, x, y);

    public void SetString(int index, string text)
        => Node.SetString(index, text);

    public void Clear()
        => Node.Clear();

    /// <summary>
    /// Entry offsets are in the same layout space as the widget rect, so they need the
    /// scale factor of the menu's layout-to-pixel transform. Recovering it from a known
    /// rect keeps that transform entirely inside UIMenu.
    /// </summary>
    private Vector2 GetLayoutToPixelScale(Rect layoutRect, Rect pixelRect)
    {
        // Degenerate widget rects give no scale to measure, so probe a unit square instead.
        if (Mathf.Approximately(layoutRect.width, 0.0f) ||
            Mathf.Approximately(layoutRect.height, 0.0f))
        {
            var probe = Menu.ToPixelCoordinates(new Rect(layoutRect.x, layoutRect.y, 1.0f, 1.0f));
            return new Vector2(probe.width, probe.height);
        }

        return new Vector2(
            pixelRect.width / layoutRect.width,
            pixelRect.height / layoutRect.height);
    }

    public override void Draw()
    {
        base.Draw();

        var pixelRect = Menu.ToPixelCoordinates(Rect);
        Node.Draw(pixelRect, GetLayoutToPixelScale(Rect, pixelRect), Menu.RenderScale);
    }
}