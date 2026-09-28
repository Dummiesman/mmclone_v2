using Dummiesman.TextureLoading;
using System.Collections.Generic;
using System;
using UnityEngine;
using MM2.AI;

public class TextureLoader 
{
    public static Func<string, AGETexture, AGETexture> PostprocessHook;

    public static int TexFrameSkip = 1;
    private const int MaxMovieFrames = 128;

    private static string[] textureFormats = new string[] { ".tex", ".tga", ".bmp" };

    public static AGETexture LoadAnimated(string name)
    {
        // Load the base version first. It's the fallback and the source of flags.
        var baseTexture = Load(name);

        string moviePath = AssetManager.CombinePath("tune", $"{name}.movie");
        var movieData = new AnimatedTextureData();
        movieData.Load(name);

        // load frames
        int skip = Math.Max(1, TexFrameSkip);
        var frames = new List<AGETexture>(MaxMovieFrames);
        for (int i = 0; i < MaxMovieFrames; i++)
        {
            string frameName = $"{name}-{(i * skip + 1):D4}";   // "%s-%04d"
            var frame = Load(frameName);
            if (frame == null)
                break;

            if (PostprocessHook != null)
            {
                AGETexture processed = PostprocessHook(name, frame);
                frame = processed;
            }
            frames.Add(frame);
        }

        if (frames.Count == 0)
        {
            return baseTexture;
        }

        // The base can be missing; fall back to the first frame for flags.
        var template = baseTexture ?? frames[0];
        var animated = AGETexture.CreateAnimated(name, template, frames.ToArray(), movieData.FrameRate);

        if (baseTexture != null)
            baseTexture.Destroy();

        return animated;
    }

    public static AGETexture Load(string name)
    {
        for(int i=0; i < textureFormats.Length; i++)
        {
            string fullPath = AssetManager.CombinePath("texture", $"{name}{textureFormats[i]}");
            var loaded = ImageLoader.LoadTexture(fullPath);
            if(loaded != null)
            {
                if(PostprocessHook != null)
                {
                    AGETexture processed = PostprocessHook(name, loaded);
                    return processed;
                }
                return loaded;
            }
        }
        return null;
    }

    public static AGETexture LoadNoPostprocess(string name)
    {
        for (int i = 0; i < textureFormats.Length; i++)
        {
            string fullPath = AssetManager.CombinePath("texture", $"{name}{textureFormats[i]}");
            var loaded = ImageLoader.LoadTexture(fullPath);
            if (loaded != null)
            {
                return loaded;
            }
        }
        return null;
    }
}
