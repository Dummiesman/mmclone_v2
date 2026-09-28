using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

public class VehicleList 
{
    public static IReadOnlyList<VehicleInfo> Vehicles => vehicles;
    private static readonly List<VehicleInfo> vehicles = new List<VehicleInfo>();

    public static string[] DefaultVehicles = new string[] 
    {   
        "vpcoop",
        "vpbug",
        "vpcab",
        "vpcaddie",
        "vpford",
        "vpmustang99",
        "vpcop",
        "vpbullet",
        "vppanoz",
        "vpbus",
        "vpddbus",
        "vpcentury",
        "vpcoop2k",
        "vpdune",
        "vpvwcup",
        "vp4x4",
        "vpauditt",
        "vpdb7",
        "vppanozgt",
        "vpsemi"
    };

    public static int Find(string basename)
    {
        return vehicles.FindIndex(x => x.BaseName == basename);
    }

    public static VehicleInfo GetVehicle(string basename)
    {
        return vehicles.FirstOrDefault(x => x.BaseName == basename);
    }

    public static void Load(string basename)
    {
        using(var stream = AssetManager.Open("tune", $"{basename}.info"))
        {
            if (stream == null)
            {
                Debug.LogError($"Failed to load vehicle info: {basename} ");
            } 
            else
            {
                var info = new VehicleInfo();
                info.Load(stream);
                if (GetVehicle(info.BaseName) == null)
                {
                    vehicles.Add(info);
                }
            }
        }

    }

    public static void LoadAll()
    {
        vehicles.Clear();

        // load default vehicles
        foreach (string basename in DefaultVehicles)
        {
            Load(basename);
        }

        // load addon vehicles
        foreach (var child in FileSystem.VFS.GetEntries("/tune/"))
        {
            if (child.Name.EndsWith(".info", StringComparison.OrdinalIgnoreCase))
            {
                string basename = Path.GetFileNameWithoutExtension(child.Name);
                if (!string.IsNullOrEmpty(basename) && GetVehicle(basename) == null)
                {
                    Load(basename);
                }
            }
        }
    }
}
