using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

public class CityList
{
    public static IReadOnlyList<CityInfo> Cities => cities;
    private static readonly List<CityInfo> cities = new List<CityInfo>();

    public static string[] DefaultCities = new string[]
    {
        "sf",
        "london"
    };

    public static bool IsDefaultCity(string basename)
    {
        return Array.IndexOf(DefaultCities, basename) != -1;
    }

    public static CityInfo GetCity(string basename)
    {
        return cities.FirstOrDefault(x => x.RaceDir == basename);
    }

    public static void Load(string basename)
    {
        using (var stream = AssetManager.Open("tune", $"{basename}.cinfo"))
        {
            if (stream == null)
            {
                Debug.LogError($"Failed to load city info: {basename} ");
            }
            else
            {
                var info = new CityInfo();
                info.Load(stream);
                if (GetCity(info.RaceDir) == null)
                {
                    cities.Add(info);
                }
            }
        }

    }

    public static void LoadAll()
    {
        cities.Clear();

        // load default cities
        foreach (string basename in DefaultCities)
        {
            Load(basename);
        }

        // load addon cities
        foreach (var child in FileSystem.VFS.GetEntries("/tune/"))
        {
            if (child.Name.EndsWith(".cinfo", StringComparison.OrdinalIgnoreCase))
            {
                string basename = Path.GetFileNameWithoutExtension(child.Name);
                if (!string.IsNullOrEmpty(basename) && GetCity(basename) == null)
                {
                    Load(basename);
                }
            }
        }
    }
}
