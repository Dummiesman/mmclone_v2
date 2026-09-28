using System.Collections.Generic;

public class TextureCache 
{
    private static Dictionary<string, AGETexture> textures = new Dictionary<string, AGETexture>();

    public static AGETexture Get(string name)
    {
        string key = name.ToLowerInvariant();
        if(textures.TryGetValue(key, out var existing))
        {
            return existing;
        }

        var texture = TextureLoader.LoadAnimated(name);
        textures[key] = texture;
        return texture;
    }

    public static void Clear()
    {
        foreach (var kvp in textures)
        {
            var ageTexture = kvp.Value;
            if (ageTexture == null)
                continue;
            ageTexture.Destroy();
        }

        textures.Clear();
    }
}
