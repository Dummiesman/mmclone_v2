using System;
using UnityEngine;

public static class Texture2DExtensions
{
    public static Texture2D ToTexture2D(this RenderTexture rTex, TextureFormat format, bool mipChain)
    {
        Texture2D tex = new Texture2D(rTex.width, rTex.height, format, mipChain);
        var oldActiveRT = RenderTexture.active;
        RenderTexture.active = rTex;
        tex.ReadPixels(new Rect(0, 0, rTex.width, rTex.height), 0, 0);
        tex.Apply(true);
        RenderTexture.active = oldActiveRT;
        return tex;
    }

    public static Texture2D Circle(this Texture2D tex, Color color, float maxAlpha = 1f, float alphaStartRadius = 0.5f)
    {
        float rSquared = tex.width/* * tex.width*/;
        int x = tex.width / 2;
        int y = tex.height / 2;

        for (int u = 0; u < tex.width; u++)
        {
            for (int v = 0; v < tex.height; v++)
            {
                float curPixRadius = (x - u) * (x - u) + (y - v) * (y - v);
                if (curPixRadius > rSquared)
                    continue;

                float pixRadiusPercentage = curPixRadius / rSquared;
                float alpha = (pixRadiusPercentage > alphaStartRadius) ? ((pixRadiusPercentage - alphaStartRadius) / (1f - alphaStartRadius)) * maxAlpha : maxAlpha;
                tex.SetPixel(u, v, new Color(color.r, color.g, color.b, alpha));
            }
        }

        return tex;
    }

    public static void ResizeAndApply(this Texture2D original, int newWidth, int newHeight)
    {
        original.Reinitialize(newWidth, newHeight);
        original.Apply(true);
    }

    public static Texture2D FastDownscale(this Texture2D original, int factor)
    {
        return original.FastDownscale(original.width / factor, original.height / factor);
    }

    public static Texture2D FastDownscale(this Texture2D original, int newWidth, int newHeight)
    {
        //size check
        if (newWidth >= original.width || newHeight >= original.height)
        {
            throw new Exception("New height/width must not equal or exceed original height/width.");
        }
        if (original.width % 2 > 0 || original.height % 2 > 0)
        {
            throw new Exception("Original image size must be a power of two.");
        }
        if (newWidth % 2 > 0 || newHeight % 2 > 0)
        {
            throw new Exception("Must downscale by a multiple of two.");
        }

        //create new texture, and get color arays
        Texture2D newTex = new Texture2D(newWidth, newHeight, original.format, original.mipmapCount > 0);

        Color32[] originalColors = original.GetPixels32();
        Color32[] newColors = new Color32[newWidth * newHeight];

        //compute multipliers
        int heightMultiplier = original.height / newHeight;
        int widthMultiplier = original.width / newWidth;

        //downscale
        for (int y = 0; y < newHeight; y++)
        {
            for (int x = 0; x < newWidth; x++)
            {
                newColors[y * newWidth + x] = originalColors[(y * heightMultiplier) * original.width + (x * widthMultiplier)];
            }
        }

        //apply and return
        newTex.SetPixels32(newColors);
        newTex.Apply(true);
        return newTex;
    }

    public static Texture2D FlipHorizontal(this Texture2D original)
    {
        Texture2D newTex = new Texture2D(original.width, original.height, original.format, original.mipmapCount > 0);
        for(int y=0; y < original.height; y++)
        {
            for(int x=0; x < original.width; x++)
            {
                var pix = original.GetPixel(x, y);
                newTex.SetPixel(original.width - x - 1, y, pix);
            }
        }

        newTex.Apply(true);
        return newTex;
    }

    public static Texture2D FlipVertical(this Texture2D original)
    {
        Texture2D newTex = new Texture2D(original.width, original.height, original.format, original.mipmapCount > 0);
        for (int y = 0; y < original.height; y++)
        {
            for (int x = 0; x < original.width; x++)
            {
                var pix = original.GetPixel(x, y);
                newTex.SetPixel(x, original.height - y - 1, pix);
            }
        }

        newTex.Apply(true);
        return newTex;
    }

    public static Texture2D ConvertToProjector(this Texture2D original)
    {
        if (original == null)
            return original;

        Texture2D newTex = new Texture2D(original.width, original.height, TextureFormat.RGBA32, false);
        Color[] pixels = original.GetPixels();

        for(int i=0; i < pixels.Length; i++)
        {
            var pixel = pixels[i];
            float alphaCalc = pixel.a - 1.0f;
            float targetR = Mathf.Min((pixel.r * alphaCalc) + 1f, 1.0f);
            float targetG = Mathf.Min((pixel.g * alphaCalc) + 1f, 1.0f);
            float targetB = Mathf.Min((pixel.b * alphaCalc) + 1f, 1.0f);
            pixels[i] = new Color(targetR, targetG, targetB, 1.0f);
        }

        newTex.wrapMode = TextureWrapMode.Repeat;
        newTex.SetPixels(pixels);
        newTex.Apply();
        return newTex;
    }

    public static Texture2D ConvertToCursor(this Texture2D original)
    {
        if (original == null)
        {
            Debug.LogError("ConvertToCursor : input was NULL");
            return Texture2D.whiteTexture;
        }

        var texture = new Texture2D(original.width, original.height, TextureFormat.ARGB32, false);
        texture.SetPixels(original.GetPixels());
        return texture;
    }

    public static Texture2D MakeColorTransparent(this Texture2D main, Color32 transparentColor, bool mipmaps = false)
    {
        var newTex = new Texture2D(main.width, main.height, TextureFormat.RGBA32, mipmaps);
        var pixels = main.GetPixels32();
        byte r = transparentColor.r, g = transparentColor.g, b = transparentColor.b;

        for (int i = 0; i < pixels.Length; i++)
        {
            if (pixels[i].r == r && pixels[i].g == g && pixels[i].b == b)
                pixels[i] = new Color32(r, g, b, 0);
        }

        newTex.SetPixels32(pixels);
        newTex.Apply(mipmaps);
        return newTex;
    }

    public static Texture2D SetMask(this Texture2D main, Texture2D mask)
    {
        if (mask == null)
            return main;

        Texture2D newTex = new Texture2D(main.width, main.height, TextureFormat.ARGB32, true);
        Color[] newColors = new Color[newTex.width * newTex.height];
        Color[] mainColors = main.GetPixels();
        Color[] maskColors = mask.GetPixels();

        //cache for improved speed
        int width = mask.width;
        int height = mask.height;

        for(int x=0; x < width; x++)
        {
            for(int y=0; y < height; y++)
            {
                int index = y * width + x;
                var px = maskColors[index];
                var mpx = mainColors[index];
                float avg = (px.r + px.g + px.b) / 3f;
                newColors[index] = new Color(mpx.r, mpx.g, mpx.b, avg);
            }
        }

        //apply and return
        newTex.SetPixels(newColors);
        newTex.Apply(true);
        return newTex;
    }

    public static int GLToDXYCoord(this Texture2D texture, int yCoord)
    {
        return texture.height - 1 - yCoord;
    }

    public static Texture2D GetRegion(this Texture2D original, Rect normalizedRect)
    {
        return original.GetRegion((int)(normalizedRect.x * original.width), (int)(normalizedRect.y * original.height),
            (int)(normalizedRect.width * original.width), (int)(normalizedRect.height * original.height));
    }

    public static Texture2D GetRegion(this Texture2D original, int xOrigin, int yOrigin, int width, int height)
    {
        Texture2D newTex = new Texture2D(width, height, original.format, true);

        yOrigin = original.height - yOrigin; // :/ why
        for(int x = 0; x < width; x++)
        {
            for(int y = 0; y < height; y++)
            {
                Color originalPixel = original.GetPixel(xOrigin + x, yOrigin - y);
                newTex.SetPixel(x, height - y, originalPixel);
            }
        }
        
        newTex.Apply(true);
        return newTex;
    }
}
