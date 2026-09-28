using System;
using UnityEngine;

[Flags]
public enum AGETexFlags
{
    ClampU = 0x01,
    CloudShadowsLow = 0x04,
    CloudShadowsHigh = 0x02,
    ClampV = 0x10000,
    Transparent = 0x20000,

    AnyCloudShadows = CloudShadowsHigh | CloudShadowsLow,
    Default = 0
}

public class AGETexture
{
    public string Name => (Texture != null) ? Texture.name : null;
    public AGETexFlags Flags;
    public Texture2D Texture { get; private set; }

    public static implicit operator Texture2D(AGETexture wrapper) => wrapper?.Texture;
    public bool IsAnimated => frames != null;

    private AGETexture[] frames;
    private float fps;
    private int currentFrame = -1;

    public static AGETexture CreateAnimated(string name, AGETexture template, AGETexture[] frames, float fps)
    {
        if (frames == null || frames.Length == 0)
            throw new ArgumentException("Animated texture needs at least one frame", nameof(frames));

        Texture2D f0 = frames[0].Texture;

        // Graphics.CopyTexture needs identical size, format and mip count on every frame.
        for (int i = 1; i < frames.Length; i++)
        {
            Texture2D f = frames[i].Texture;
            if (f.width != f0.width || f.height != f0.height ||
                f.format != f0.format || f.mipmapCount != f0.mipmapCount)
            {
                Debug.LogError($"Animated texture '{name}': frame {i} differs in size/format/mips from frame 0");
                break;
            }
        }

        // Flags and sampler state come from the base texture when there is one.
        template ??= frames[0];
        Texture2D src = template.Texture != null ? template.Texture : f0;

        var target = new Texture2D(f0.width, f0.height, f0.format, f0.mipmapCount, linear: false)
        {
            name = name,
            wrapModeU = src.wrapModeU,
            wrapModeV = src.wrapModeV,
            filterMode = src.filterMode,
            anisoLevel = src.anisoLevel
        };

        var tex = new AGETexture
        {
            Texture = target,
            Flags = template.Flags,
            frames = frames,
            fps = Mathf.Max(0.01f, fps)
        };

        tex.Tick(Time.time);
        AnimatedTextureDriver.Register(tex);
        return tex;
    }

    public void Tick(float time)
    {
        if (frames == null || Texture == null) return;

        int frame = (int)(time * fps) % frames.Length;
        if (frame == currentFrame) return;

        Graphics.CopyTexture(frames[frame].Texture, Texture);
    }

    public void Destroy()
    {
        if (IsAnimated)
        {
            AnimatedTextureDriver.Unregister(this);
            foreach (var f in frames) f?.Destroy();
            frames = null;
        }

        if (Texture != null)
            UnityEngine.Object.Destroy(Texture);
        Texture = null;
    }

    public AGETexture(Texture2D texture) : this(texture, AGETexFlags.Default) { }

    public AGETexture(Texture2D texture, AGETexFlags flags)
    {
        Texture = texture;
        Flags = flags;
    }

    private AGETexture() { }
}