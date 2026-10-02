using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Locates the game data directory on device by probing the common Android
/// storage mount points. A candidate is only accepted if it actually contains
/// the marker file.
/// </summary>
public static class AndroidDataPathLocator
{
    /// <summary>Folder names to try inside each storage root, in priority order.</summary>
    public static readonly string[] FolderNames =
    {
        "MMClone",
        "Midtown Madness 2",
    };

    /// <summary>File that must be present for a directory to count as a valid data path.</summary>
    public const string MarkerFile = "mmlang.dll";

    /// <summary>Storage roots to probe, in priority order.</summary>
    static readonly string[] StorageRoots =
    {
        "/sdcard",
        "/storage/emulated/0",
        "/storage/emulated/1",
        "/storage/emulated/legacy",
        "/storage/sdcard0",
        "/storage/sdcard1",
        "/storage/extSdCard",
    };

    /// <summary>
    /// Finds the data directory.
    /// </summary>
    /// <param name="path">The resolved absolute path, or null if none was found.</param>
    /// <returns>True if a directory containing <see cref="MarkerFile"/> was found.</returns>
    public static bool TryGetDataPath(out string path)
    {
        foreach (string candidate in EnumerateCandidates())
        {
            if (!IsValidDataPath(candidate)) continue;

            path = candidate;
            return true;
        }

        path = null;
        return false;
    }

    /// <summary>True if the directory exists and holds the marker file.</summary>
    public static bool IsValidDataPath(string directory)
    {
        if (string.IsNullOrEmpty(directory)) return false;

        try
        {
            if (!Directory.Exists(directory)) return false;

            // Fast path: exact-case match.
            if (File.Exists(Path.Combine(directory, MarkerFile))) return true;

            // Some mounts are case-sensitive, so fall back to a case-insensitive scan.
            foreach (string file in Directory.GetFiles(directory))
            {
                if (string.Equals(Path.GetFileName(file), MarkerFile, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        catch (UnauthorizedAccessException)
        {
            // No permission for this mount point; treat as not found.
        }
        catch (IOException)
        {
            // Unmounted or otherwise unreadable; treat as not found.
        }

        return false;
    }

    static IEnumerable<string> EnumerateCandidates()
    {
        foreach (string root in EnumerateRoots())
        {
            foreach (string folder in FolderNames)
            {
                string resolved = ResolveFolder(root, folder);
                if (resolved != null) yield return resolved;
            }
        }
    }

    static IEnumerable<string> EnumerateRoots()
    {
        foreach (string root in StorageRoots)
            yield return root;

        // Last resort: the app's own sandboxed storage, which never needs permissions.
        yield return Application.persistentDataPath;
    }

    /// <summary>
    /// Returns the real path of <paramref name="folder"/> inside <paramref name="root"/>,
    /// matching case-insensitively so "midtown madness 2" is found on case-sensitive
    /// mounts, or null if the root holds no such directory.
    /// </summary>
    static string ResolveFolder(string root, string folder)
    {
        string direct = Path.Combine(root, folder);

        try
        {
            if (Directory.Exists(direct)) return direct;
            if (!Directory.Exists(root)) return null;

            foreach (string child in Directory.GetDirectories(root))
            {
                if (string.Equals(Path.GetFileName(child), folder, StringComparison.OrdinalIgnoreCase))
                    return child;
            }
        }
        catch (UnauthorizedAccessException)
        {
            // Root is not listable; fall through.
        }
        catch (IOException)
        {
            // Unmounted or otherwise unreadable; fall through.
        }

        return null;
    }
}