using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// An explicit, replayable list of shader property overrides, recorded at
/// template-build time and baked into the shared Materials the template
/// creates.
///
/// This exists instead of MaterialPropertyBlock because a property block
/// cannot be read back selectively: GetColor/GetFloat on a property the block
/// never set returns a default (black / 0) rather than telling you it was
/// unset, so copying a block onto a Material silently stomps every property
/// the block *didn't* care about. This class remembers exactly which
/// properties were set, and touches nothing else.
///
/// Instances are meant to be shared singletons created once and reused for
/// every template that wants the same override - the loader treats two
/// different instances as two different material sets even if their contents
/// match.
/// </summary>
public sealed class MaterialProperties
{
    private enum ValueKind
    {
        Color,
        Float,
        Texture,
    }

    private readonly struct Property
    {
        public readonly int Id;
        public readonly ValueKind Kind;
        public readonly Color Color;
        public readonly float Float;
        public readonly Texture Texture;

        private Property(int id, ValueKind kind, Color color, float value, Texture texture)
        {
            Id = id;
            Kind = kind;
            Color = color;
            Float = value;
            Texture = texture;
        }

        public static Property OfColor(int id, Color value) =>
            new Property(id, ValueKind.Color, value, 0f, null);

        public static Property OfFloat(int id, float value) =>
            new Property(id, ValueKind.Float, default(Color), value, null);

        public static Property OfTexture(int id, Texture value) =>
            new Property(id, ValueKind.Texture, default(Color), 0f, value);
    }

    private readonly List<Property> properties = new List<Property>();

    public MaterialProperties SetColor(string name, Color value) => SetColor(Shader.PropertyToID(name), value);
    public MaterialProperties SetFloat(string name, float value) => SetFloat(Shader.PropertyToID(name), value);
    public MaterialProperties SetTexture(string name, Texture value) => SetTexture(Shader.PropertyToID(name), value);

    public MaterialProperties SetColor(int id, Color value)
    {
        Set(Property.OfColor(id, value));
        return this;
    }

    public MaterialProperties SetFloat(int id, float value)
    {
        Set(Property.OfFloat(id, value));
        return this;
    }

    public MaterialProperties SetTexture(int id, Texture value)
    {
        Set(Property.OfTexture(id, value));
        return this;
    }

    private void Set(Property property)
    {
        for (int i = 0; i < properties.Count; i++)
        {
            if (properties[i].Id != property.Id) continue;
            properties[i] = property;
            return;
        }
        properties.Add(property);
    }

    /// <summary>
    /// Applies every recorded override the material's shader actually
    /// declares. Properties the shader doesn't have are skipped rather than
    /// logged - the same override set is deliberately used across shaders
    /// that declare different subsets (e.g. the additive light shader has no
    /// _Reflection).
    /// </summary>
    public void ApplyTo(Material material)
    {
        for (int i = 0; i < properties.Count; i++)
        {
            var property = properties[i];
            if (!material.HasProperty(property.Id)) continue;

            switch (property.Kind)
            {
                case ValueKind.Color:
                    material.SetColor(property.Id, property.Color);
                    break;
                case ValueKind.Float:
                    material.SetFloat(property.Id, property.Float);
                    break;
                case ValueKind.Texture:
                    material.SetTexture(property.Id, property.Texture);
                    break;
            }
        }
    }
}