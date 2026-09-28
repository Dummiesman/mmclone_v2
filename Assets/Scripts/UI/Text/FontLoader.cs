using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

public struct FontDescriptor
{
    public string faceName;
    public int smallHeight;
    public int largeHeight;
    public int charset;
    public int weight;

    public static FontDescriptor Parse(string line)
    {
        string[] parts = line.Split(',');
        if (parts.Length != 5)
        {
            throw new FormatException($"Bad font descriptor: '{line}'");
        }

        return new FontDescriptor
        {
            faceName = parts[0].Trim(),
            smallHeight = int.Parse(parts[1].Trim(), CultureInfo.InvariantCulture),
            largeHeight = int.Parse(parts[2].Trim(), CultureInfo.InvariantCulture),
            charset = int.Parse(parts[3].Trim(), CultureInfo.InvariantCulture),
            weight = int.Parse(parts[4].Trim(), CultureInfo.InvariantCulture)
        };
    }

    // if (screenWidth >= 640) height = largeHeight; else height = smallHeight;
    public int HeightFor(int screenWidth) => screenWidth >= 640 ? largeHeight : smallHeight;
}

public struct LocFont
{
    public Font font;
    public string faceName;
    public int referenceSize;
    public FontStyle style;

    public bool IsValid => font != null;
}

public class FontLoader
{
    private static readonly Dictionary<string, string[]> fallbackFonts = new Dictionary<string, string[]>
    {
        { "Arial",      new[] { "Arial", "Liberation Sans", "Helvetica", "Roboto" } },
        { "Arial Bold", new[] { "Arial Bold", "Liberation Sans Bold", "Helvetica Bold", "Roboto Bold" } },
    };
    private static readonly string[] boldTokens =
    {
        "semibold", "demibold", "extrabold", "ultrabold", "bold", "black", "heavy"
    };

    private struct ResolvedFont
    {
        public Font font;
        public FontStyle style;
    }

    private static readonly Dictionary<string, LocFont> _cache = new Dictionary<string, LocFont>();
    private static readonly Dictionary<string, ResolvedFont> _osFontCache = new Dictionary<string, ResolvedFont>();

    public static LocFont LoadFont(string descriptorString)
    {
        return LoadFont(descriptorString, Screen.width);
    }

    public static LocFont LoadFont(string descriptorString, int screenWidth)
    {
        bool large = true;
        string cacheKey = $"{descriptorString}|{(large ? "L" : "S")}";

        if (_cache.TryGetValue(cacheKey, out LocFont cached))
        {
            return cached;
        }

        FontDescriptor desc = FontDescriptor.Parse(descriptorString);
        int height = desc.HeightFor(screenWidth);

        Font osFont = ResolveOSFont(desc.faceName, desc.weight, height, out FontStyle style);

        LocFont loaded = new LocFont
        {
            font = osFont,
            faceName = desc.faceName,
            referenceSize = height,
            style = style
        };

        // Only cache successes, so a one-off failure is not permanent for the session.
        if (loaded.IsValid)
        {
            _cache[cacheKey] = loaded;
        }

        return loaded;
    }

    private static Font ResolveOSFont(string faceName, int weight, int height, out FontStyle syntheticStyle)
    {
        string cacheKey = $"{faceName}@{weight}";

        if (_osFontCache.TryGetValue(cacheKey, out ResolvedFont hit))
        {
            syntheticStyle = hit.style;
            return hit.font;
        }

        string[] candidates = fallbackFonts.TryGetValue(faceName, out var chain)
            ? chain
            : new[] { faceName };

        string[] installed = Font.GetOSInstalledFontNames();
        string resolved = FindInstalled(candidates, installed);

        Font font;
        if (resolved != null)
        {
            font = Font.CreateDynamicFontFromOSFont(resolved, height);

            if (font == null)
            {
                Debug.LogWarning($"ResolveOSFont: '{resolved}' is listed by the OS but CreateDynamicFontFromOSFont returned null (wanted '{faceName}').");
            }
            else if (string.Equals(resolved, faceName, StringComparison.OrdinalIgnoreCase))
            {
                Debug.Log($"ResolveOSFont: '{faceName}' found in OS installed fonts.");
            }
            else
            {
                Debug.Log($"ResolveOSFont: '{faceName}' is not installed; using '{resolved}'.");
            }
        }
        else
        {
            // The array overload does not fail in the usual sense — Unity hands back
            // some default face, so font.name is the only way to see what we got.
            font = Font.CreateDynamicFontFromOSFont(candidates, height);
            Debug.LogWarning($"ResolveOSFont: nothing installed for [{string.Join(", ", candidates)}]. Unity substituted '{(font != null ? font.name : "nothing")}'.");
        }

        // GDI only synthesises bold when the requested weight exceeds the matched
        // face's own weight, which is why "Arial Bold" at 400 comes out bold and not
        // double-bold. Approximate the face's weight from its name.
        syntheticStyle = (weight >= 600 && !LooksBold(resolved ?? faceName))
            ? FontStyle.Bold
            : FontStyle.Normal;

        if (font != null)
        {
            _osFontCache[cacheKey] = new ResolvedFont { font = font, style = syntheticStyle };
        }

        return font;
    }

    // Matches on a normalised form so separator style does not matter: "Roboto-Bold"
    // satisfies a "Roboto Bold" candidate. Returns the OS spelling, which is what
    // CreateDynamicFontFromOSFont wants.
    private static string FindInstalled(string[] candidates, string[] installed)
    {
        foreach (string candidate in candidates)
        {
            string normalised = Normalise(candidate);
            foreach (string i in installed)
            {
                if (Normalise(i) == normalised)
                {
                    return i;
                }
            }
        }

        return null;
    }

    private static string Normalise(string name)
    {
        var sb = new System.Text.StringBuilder(name.Length);
        foreach (char c in name)
        {
            if (c != ' ' && c != '-' && c != '_')
            {
                sb.Append(char.ToLowerInvariant(c));
            }
        }

        return sb.ToString();
    }

    private static bool LooksBold(string faceName)
    {
        string normalised = Normalise(faceName);
        foreach (string token in boldTokens)
        {
            if (normalised.EndsWith(token, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}