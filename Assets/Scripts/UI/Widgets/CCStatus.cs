using Dummiesman.TextureLoading;
using UnityEngine;

public enum CCStatusState
{
    None,
    Passed,
    Failed
}

public class CCStatus : UIWidget
{
    public CCStatusState State = CCStatusState.None;
    public override bool EnableNavigation => false;

    private Texture2D image;
    private int yPos;
    private int passedXPos;
    private int failedXPos;

    private void SetImage(string name)
    {
        if (image != null)
        {
            Object.Destroy(image);
            image = null;
        }

        if (AssetManager.Exists("jpg", $"{name}.jpg"))
        {
            using (var stream = AssetManager.Open("jpg", $"{name}.jpg"))
            {
                image = ImageLoader.LoadTexture(stream, ImageLoader.TextureFormat.JPG);
            }
        }
        else
        {
            image = TextureLoader.Load(name);
        }
    }

    public override void Draw()
    {
        base.Draw();

        if (image != null && State != CCStatusState.None)
        {
            int stateSize = image.height / 3; // integer division is intended, copies original game behavior
            int xPos = (State == CCStatusState.Passed) ? passedXPos : failedXPos;

            // rows are top/center/bottom; Passed = center, Failed = bottom
            int row = (State == CCStatusState.Passed) ? 1 : 2;

            // GUI.DrawTextureWithTexCoords uses bottom-left origin UVs
            Rect texCoords = new Rect(0f, (2 - row) / 3f, 1f, 1f / 3f);

            // we draw the full image size regardless of our control rect
            Rect drawRectPx = new Rect(xPos, yPos, image.width, stateSize);
            var drawRect = Menu.ToPixelCoordinates(Menu.MenuToWidgetCoords(drawRectPx));
            GUI.DrawTextureWithTexCoords(drawRect, image, texCoords);
        }
    }

    public override void Dispose()
    {
        base.Dispose();
        if (image != null) Object.Destroy(image);
    }

    public CCStatus(UIMenu menu, string name, int id, string bitmapName, int passedXPos, int failedXPos, int yPos) : base(menu, id, name, Rect.zero)
    {
        SetImage(bitmapName);
        this.failedXPos = failedXPos;
        this.yPos = yPos;
        this.passedXPos = passedXPos;
    }
}