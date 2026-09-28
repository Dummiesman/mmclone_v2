using UnityEngine;

/// <summary>
/// Picks the Unity shader for one ShaderSet entry, given the entry itself and
/// the texture that will be bound to _MainTex. Called once per (variant,
/// material set, ShaderSet entry) while the template builds its shared
/// materials - never per renderer and never per frame.
/// </summary>
public delegate Shader ShaderSelector(ShaderSet.Shader entry, Texture mainTex);

/// <summary>
/// Where a material set gets its shader: either one fixed Shader for every
/// ShaderSet entry, or a ShaderSelector that chooses per entry (e.g. the
/// one-pass vs two-pass vehicle shader, depending on whether the entry's
/// texture has an alpha channel).
///
/// An unset source means "no ShaderSet-driven materials" - those renderers
/// keep Unity's default material.
/// </summary>
public readonly struct ShaderSource
{
    public readonly Shader Shader;
    public readonly ShaderSelector Selector;

    private ShaderSource(Shader shader, ShaderSelector selector)
    {
        Shader = shader;
        Selector = selector;
    }

    public static readonly ShaderSource None = default(ShaderSource);

    public static ShaderSource Of(Shader shader) =>
        shader == null ? None : new ShaderSource(shader, null);

    public static ShaderSource Of(ShaderSelector selector) =>
        selector == null ? None : new ShaderSource(null, selector);

    public bool IsSet => Selector != null || Shader != null;

    public Shader Resolve(ShaderSet.Shader entry, Texture mainTex) =>
        Selector != null ? Selector(entry, mainTex) : Shader;

    /// <summary>
    /// Whether two sources would produce identical materials, used to decide
    /// if geometry can share a material set. Delegates compare by target and
    /// method, so a cached selector field - or the same static method group -
    /// matches itself across calls; a freshly allocated closure will not, and
    /// costs a duplicate Material[] rather than wrong rendering.
    /// </summary>
    public bool Matches(ShaderSource other) =>
        Shader == other.Shader && Selector == other.Selector;
}