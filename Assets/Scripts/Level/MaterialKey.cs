using System;
using UnityEngine;

internal readonly struct MaterialKey : IEquatable<MaterialKey>
{
    public readonly Shader Shader;
    public readonly AGETexture MainTex;
    public readonly Color Diffuse;
    public readonly Color Ambient;
    public readonly Color Specular;
    public readonly float Reflectivity;
    public readonly bool? LightEnable;
    public readonly MaterialProperties Properties;

    public MaterialKey(Shader shader, AGETexture mainTex, Color diffuse, Color ambient,
        Color specular, float reflectivity, bool? lightEnable, MaterialProperties properties)
    {
        Shader = shader;
        MainTex = mainTex;
        Diffuse = diffuse;
        Ambient = ambient;
        Specular = specular;
        Reflectivity = reflectivity;
        LightEnable = lightEnable;
        Properties = properties;
    }

    public bool Equals(MaterialKey other) =>
        ReferenceEquals(Shader, other.Shader) &&
        ReferenceEquals(MainTex, other.MainTex) &&
        Diffuse.Equals(other.Diffuse) &&       // exact, unlike Color's == operator
        Ambient.Equals(other.Ambient) &&
        Specular.Equals(other.Specular) &&
        Reflectivity.Equals(other.Reflectivity) &&
        LightEnable == other.LightEnable &&
        Equals(Properties, other.Properties);

    public override bool Equals(object obj) => obj is MaterialKey k && Equals(k);

    public override int GetHashCode() => HashCode.Combine(
        Shader, MainTex, Diffuse, Ambient, Specular, Reflectivity, LightEnable, Properties);
}