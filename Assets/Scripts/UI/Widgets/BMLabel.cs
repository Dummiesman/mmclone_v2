
using Dummiesman.TextureLoading;
using UnityEngine;

public class BMLabel : UIWidget
{
    public int ImageIndex = -1;

    private Texture2D[] images;
    private Texture2D overrideImage;

    public override bool EnableNavigation => false;

    public void SetImage(string name)
    {
        if(overrideImage != null)
        {
            Object.Destroy(overrideImage);
        }

        if(AssetManager.Exists("jpg", $"{name}.jpg"))
        {
            using(var stream = AssetManager.Open("jpg", $"{name}.jpg"))
            {
                overrideImage = ImageLoader.LoadTexture(stream, ImageLoader.TextureFormat.JPG);
                if (overrideImage != null) overrideImage = overrideImage.MakeColorTransparent(Color.black);
            }
        }
    }

    /// <summary>
    /// Set images separated by pipe characters
    /// </summary>
    public void SetImages(string imageNames)
    {
        if(images != null)
        {
            foreach(var image in images)
            {
                Object.Destroy(image);
            }
            images = null;
        }

        string[] split = imageNames.Split('|');
        images = new Texture2D[split.Length];

        for(int i=0; i <split.Length; i++)
        {
            string name = split[i];
            if (AssetManager.Exists("jpg", $"{name}.jpg"))
            {
                using (var stream = AssetManager.Open("jpg", $"{name}.jpg"))
                {
                    images[i] = ImageLoader.LoadTexture(stream, ImageLoader.TextureFormat.JPG);
                    if(images[i] != null) images[i] = images[i].MakeColorTransparent(Color.black);
                }
            }
            else if(AssetManager.Exists("texture", $"{name}.tga"))
            {
                using (var stream = AssetManager.Open("texture", $"{name}.tga"))
                {
                    images[i] = ImageLoader.LoadTexture(stream, ImageLoader.TextureFormat.TGA);
                    if (images[i] != null) images[i] = images[i].MakeColorTransparent(Color.black);
                }
            }
        }
    }

    public override void Draw()
    {
        base.Draw();

        Texture2D imageToDraw = null;
        if (images != null && images.Length > 0 && ImageIndex >= 0 && ImageIndex < images.Length)
        {
            imageToDraw = images[ImageIndex];
        }
        if(overrideImage != null)
        {
            imageToDraw = overrideImage;
        }

        if (imageToDraw != null)
        {
            // we draw the full image size regardless of our control rect
            Rect sizeRect = Menu.MenuToWidgetCoords(new Rect(0, 0, imageToDraw.width, imageToDraw.height));
            Rect drawRect = Menu.ToPixelCoordinates(new Rect(Rect.x, Rect.y, sizeRect.width, sizeRect.height));
            GUI.DrawTexture(drawRect, imageToDraw);
        }
    }

    public override void Dispose()
    {
        base.Dispose();

        if (images != null)
        {
            for (int i = 0; i < images.Length; i++)
            {
                var image = images[i];
                if (image != null) Object.Destroy(image);
            }
        }
        if(overrideImage != null)
        {
            Object.Destroy(overrideImage);
        }
    }

    public BMLabel(UIMenu menu, string name, int id, Rect rect) : base(menu, id, name, rect)
    {
    }
}
