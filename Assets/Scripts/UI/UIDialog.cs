using UnityEngine;

public class UIDialog : UIMenu
{
    private static Rect GetDialogRect(int textureWidth, int textureHeight)
    {
        var containWithin = GetLetterboxedOrPillarboxedRect();

        // containWithin is a 640x480-equivalent area on screen
        float scaleX = containWithin.width / UIConstants.ReferenceWidth;
        float scaleY = containWithin.height / UIConstants.ReferenceHeight;

        float scale = Mathf.Min(scaleX, scaleY);

        float width = textureWidth * scale;
        float height = textureHeight * scale;

        float x = containWithin.x + (containWithin.width - width) * 0.5f;
        float y = containWithin.y + (containWithin.height - height) * 0.5f;

        return new Rect(x, y, width, height);
    }

    public override void DrawBackground()
    {
        if (BackgroundImage != null)
        {
            // determine render area
            int width = (BackgroundImage != null) ? BackgroundImage.width : 0;
            int height = (BackgroundImage != null) ? BackgroundImage.height : 0;
            RenderArea = GetDialogRect(width, height);

            // draw background
            if (BackgroundImage != null)
            {
                GUI.DrawTexture(RenderArea, BackgroundImage);
            }

        }
    }

    public override void Draw()
    {
        // determine render area
        int width = (BackgroundImage != null) ? BackgroundImage.width : 0;
        int height = (BackgroundImage != null) ? BackgroundImage.height : 0;
        RenderArea = GetDialogRect(width, height);

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

    public UIDialog(MenuID id) : base(id)
    {
    }
}
