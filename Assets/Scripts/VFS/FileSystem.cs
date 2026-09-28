using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using BarebonesFileSystem;
using Dummiesman.VFS;

public class FileSystem
{
    public static MultiFilesystem VFS;
    public static Action<VFSSystem> OnSystemMounted;
    public static Action<VFSSystem> OnSystemUnMounted;

    public static bool Initialized { private set; get; } = false;
    public static bool UseUnpackedFilesystem = false;
    public static bool LimitToBaseGame = false;

    private static string _root = string.Empty;
    public static string Root
    {
        get
        {
            if (string.IsNullOrEmpty(_root))
                SetupDefaultRootPath();
            return _root;
        }
        set { _root = value; }
    }

    private static string[] defaultArchiveNames = new[] { "mm2aud", "mm2audex", "mm2core", "mm2tex" };

    private static void SetupDefaultRootPath()
    {
        _root = Environment.CurrentDirectory;

#if UNITY_EDITOR && !UNITY_ANDROID
        // load settings file if possible
        if (File.Exists("game_root.txt"))
        {
            _root = File.ReadAllLines("game_root.txt").First().Trim();
        }
#endif
    }

    // init vfs
    private static void InitEmbeddedFilesystem(List<VFSSystem> fileSystems)
    {
        foreach (var archiveName in defaultArchiveNames)
        {
            TextAsset asset = Resources.Load(archiveName) as TextAsset;
            if (asset != null)
            {
                var ms = new MemoryStream(asset.bytes);
                fileSystems.Add(new AngelFilesystem(ms));
            }
            else
            {
                Debug.LogError($"InitEmbeddedFilesystem: Failed to find {archiveName}");
            }
        }
    }

    private static void InitDesktopFilesystem(List<VFSSystem> fileSystems, bool physical)
    {
        // load physicalfs
        if (physical)
        {
            fileSystems.Add(new PhysicalFilesystem(_root));
            return;
        }

        // only load base game?
        if (ArgParser.GetFlag("base") || LimitToBaseGame)  
        {
            foreach (var archive in defaultArchiveNames)
            {
                fileSystems.Add(new AngelFilesystem(Path.Combine(_root, $"{archive}.ar")));
            }
            return;
        }

        // load all mod archives
        var archives = Directory.GetFiles(_root, @"*.ar");
        Array.Sort(archives, StringComparer.Ordinal);
        Array.Reverse(archives);
        foreach (var archive in archives)
        {
            Debug.Log($"FileSystem.Init(): Loading archive {Path.GetFileName(archive)}");
            fileSystems.Add(new AngelFilesystem(archive));
        }
    }

    public static void Init()
    {
        if(FileSystemWatcher.Instance == null)
        {
            // life cycle watcher
            new GameObject("FSWatcher").AddComponent<FileSystemWatcher>();
        }

        if (string.IsNullOrEmpty(_root))
        {
            SetupDefaultRootPath();
        }

        // collect fileystsems
        var fileSystems = new List<VFSSystem>();

        // mm2hook compatibility
        if (ArgParser.GetFlag("mods") || Application.isEditor)
        {
            string modsDirPath = Path.Combine(_root, "mods");
            if (Directory.Exists(modsDirPath))
            {
                fileSystems.Add(new PhysicalFilesystem(modsDirPath));
            }
        }

#if UNITY_ANDROID
        // todo: eventually merge this so mobile uses persistent data OR embedded
        InitEmbeddedFilesystem(fileSystems);
#else
        InitDesktopFilesystem(fileSystems, UseUnpackedFilesystem);
#endif

        VFS = new MultiFilesystem(fileSystems)
        {
            OnSystemMounted = (system) => { OnSystemMounted?.Invoke(system); },
            OnSystemUnMounted = (system) => { OnSystemUnMounted?.Invoke(system); }
        };

        Initialized = true;
    }
}