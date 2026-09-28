using System.IO;
using Dummiesman.Wave;
using UnityEngine;

public class AudioAssetManager
{
    public static AudioClip LoadClip(string name)
    {
        AudioClip clip = null;

        string path22 = AssetManager.CombinePath("aud", "aud22", $"{name}.22k.wav");
        if (AssetManager.Exists(path22))
        {
            using (var stream = AssetManager.Open(path22))
            {
                clip = WAVLoader.Load(stream);
            }
        }

        if (clip == null)
        {
            string path11 = AssetManager.CombinePath("aud", "aud11", $"{name}.11k.wav");
            if (AssetManager.Exists(path11))
            {
                using (var stream = AssetManager.Open(path11))
                {
                    clip = WAVLoader.Load(stream);
                }
            }
        }

        if (clip != null)
        {
            clip.name = name;
        }

        return clip;
    }

    public static AudioClip LoadClip(string folder, string name)
    {
        return LoadClip(AssetManager.CombinePath(folder, name).Substring(1));
    }

    public static WAVStream LoadStream(string name)
    {
        string path22 = AssetManager.CombinePath("aud", "aud22", $"{name}.22k.wav");
        if (AssetManager.Exists(path22))
        {
            WAVStream stream = LoadWaveStream(path22);
            if (stream != null)
                return stream;
        }

        string path11 = AssetManager.CombinePath("aud", "aud11", $"{name}.11k.wav");
        if (AssetManager.Exists(path11))
        {
            WAVStream stream = LoadWaveStream(path11);
            if (stream != null)
                return stream;
        }

        Debug.LogError($"LoadStream failed: {name}");
        return null;
    }

    public static WAVStream LoadStream(string folder, string name)
    {
        return LoadStream(AssetManager.CombinePath(folder, name).Substring(1));
    }

    private static WAVStream LoadWaveStream(string path)
    {
        Stream stream = AssetManager.OpenBinary(path, FileAccess.Read);

        if (stream != null)
        {
            try
            {
                return new WAVStream(
                    stream,
                    Path.GetFileNameWithoutExtension(path)
                );
            }
            catch
            {
                stream.Dispose();
                throw;
            }
        }

        Debug.LogError($"LoadWaveStream NULL: {path}");
        return null;
    }
}