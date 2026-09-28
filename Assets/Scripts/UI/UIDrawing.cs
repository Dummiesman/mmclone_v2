using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class UIDrawing 
{
    private static Texture2D lineTexture = null;
    private static Texture2D whiteTexture = null;

    private static readonly Dictionary<(string, int, FontStyle, TextAnchor, string), GUIStyle> _styleCache
        = new Dictionary<(string, int, FontStyle, TextAnchor, string), GUIStyle>();

    private const float EmPerCellHeight = 13f / 16f;

    public static GUIStyle GetStyle(LocFont loadedFont, float scale,
                                    TextAnchor alignment = TextAnchor.UpperLeft,
                                    GUIStyle baseStyle = null)
    {
        int scaledSize = Mathf.Max(1, Mathf.RoundToInt(loadedFont.referenceSize * EmPerCellHeight * scale));
        var key = (loadedFont.faceName, scaledSize, loadedFont.style, alignment, baseStyle?.name ?? "@label");

        if (_styleCache.TryGetValue(key, out GUIStyle cached))
            return cached;

        GUIStyle style = new GUIStyle(baseStyle ?? GUI.skin.label)
        {
            font = loadedFont.font,
            fontSize = scaledSize,
            fontStyle = loadedFont.style,
            alignment = alignment,      // explicit, never inherited
        };

        _styleCache[key] = style;
        return style;
    }

    public static void ScaledLabel(Rect rect, string text, LocFont loadedFont, float scale, GUIStyle baseStyle = null)
    {
        GUI.Label(rect, text, GetStyle(loadedFont, scale, GUI.skin.label.alignment, baseStyle));
    }

    public static void ScaledLabel(Rect rect, string text, LocFont loadedFont, Rect renderRegionRect, GUIStyle baseStyle = null)
    {
        float scale = Mathf.Min(renderRegionRect.height / UIConstants.ReferenceHeight, renderRegionRect.width / UIConstants.ReferenceWidth);
        ScaledLabel(rect, text, loadedFont, scale, baseStyle);
    }

    public static void DrawTextureTinted(Texture2D texture, Rect screenRect, Color color)
    {
        Graphics.DrawTexture(screenRect, texture, new Rect(0, 0, 1, 1), 0, 0, 0, 0, color);
    }

    private static void InitStatics()
    {
        if (whiteTexture == null)
        {
            whiteTexture = Texture2D.whiteTexture;
        }

        if (lineTexture == null)
        {
            lineTexture = new Texture2D(1, 3, TextureFormat.ARGB32, false);
            lineTexture.SetPixel(0, 0, new Color(1, 1, 1, 0));
            lineTexture.SetPixel(0, 1, Color.white);
            lineTexture.SetPixel(0, 2, new Color(1, 1, 1, 0));
            lineTexture.Apply();
        }
    }

    public static void DrawColor(Rect screenRect, Color color)
    {
        InitStatics();
        DrawTextureTinted(whiteTexture, screenRect, color);
    }

    public static void DrawLineTinted(Vector2 p1, Vector2 p2, float width, Color color)
    {
        //make sure static stuff exists
        InitStatics();

        //if this line is too small, dont draw it
        Vector2 d = p2 - p1;
        float length = d.magnitude;
        if (length <= Mathf.Epsilon)
            return;

        //account for texture height
        width *= lineTexture.height;

        //draw line
        Matrix4x4 oldMatrix = GUI.matrix;
        int width2 = (int)Mathf.Ceil(width / 2);
        float angle = Mathf.Rad2Deg * Mathf.Atan(d.y / d.x);
        if (d.x < 0)
            angle += 180;

        GUIUtility.RotateAroundPivot(angle, p1);

        DrawTextureTinted(lineTexture, new Rect(p1.x, p1.y - width2, length, width), color);

        GUI.matrix = oldMatrix;
    }

    public static void DrawLine(Vector2 p1, Vector2 p2, float width = 1f)
    {
        DrawLineTinted(p1, p2, width, GUI.color);
    }

    public static void DrawBorder(Rect rect, Color color)
    {
        DrawColor(new Rect(rect.x, rect.y, rect.width, 1f), color); //TOP
        DrawColor(new Rect(rect.x, rect.yMax - 1, rect.width, 1f), color); //BOTTOM
        DrawColor(new Rect(rect.x, rect.y, 1f, rect.height), color); //LEFT
        DrawColor(new Rect(rect.xMax - 1, rect.y, 1f, rect.height), color); //RIGHT
    }

    public static void DrawBorder(Rect rect, Texture2D tex)
    {
        GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, 1f), tex, ScaleMode.StretchToFill); //TOP
        GUI.DrawTexture(new Rect(rect.x, rect.yMax - 1, rect.width, 1f), tex, ScaleMode.StretchToFill); //BOTTOM
        GUI.DrawTexture(new Rect(rect.x, rect.y, 1f, rect.height), tex, ScaleMode.StretchToFill); //LEFT
        GUI.DrawTexture(new Rect(rect.xMax - 1, rect.y, 1f, rect.height), tex, ScaleMode.StretchToFill); //RIGHT
    }
}
