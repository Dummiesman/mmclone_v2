using Dummiesman.TextureLoading;
using UnityEngine;

public class UIIcon : UIWidget
{
    public Vector2 ImageSize => (Image != null) ? new Vector2(Image.width, Image.height) : Vector2.zero;
    public Texture2D Image => image;
    private Texture2D image;

    public override bool EnableNavigation => false;

    public void SetImage(string name)
    {
        if (image != null)
        {
            Object.Destroy(image);
            image = null;
        }

        if (!string.IsNullOrEmpty(name))
        {
            if (AssetManager.Exists("jpg", $"{name}.jpg"))
            {
                using (var stream = AssetManager.Open("jpg", $"{name}.jpg"))
                {
                    image = ImageLoader.LoadTexture(stream, ImageLoader.TextureFormat.JPG);
                }
                if (image != null) image = image.MakeColorTransparent(Color.black);
            }
            else
            {
                image = TextureLoader.Load(name);
                if (image != null) image = image.MakeColorTransparent(Color.black);
            }
        }
    }

    public override void Draw()
    {
        base.Draw();

        if (image != null)
        {
            // we draw the full image size regardless of our control rect
            Rect sizeRect = Menu.MenuToWidgetCoords(new Rect(0, 0, image.width, image.height));
            Rect drawRect = Menu.ToPixelCoordinates(new Rect(Rect.x, Rect.y, sizeRect.width, sizeRect.height));
            GUI.DrawTexture(drawRect, image);
        }
    }

    public override void Dispose()
    {
        base.Dispose();
        if(image != null) Object.Destroy(image);
    }

    public UIIcon(UIMenu menu, string name, int id, Rect rect) : base(menu, id, name, rect)
    {
        SetImage(name);
    }
}