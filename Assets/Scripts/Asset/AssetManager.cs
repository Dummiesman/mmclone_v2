using System;
using System.IO;

public class AssetManager
{
    public static string CombinePath(string folder, string file)
    {
        return "/" + folder + "/" + file;
    }

    public static string CombinePath(params string[] fileFolderChain)
    {
        string returnPath = "/";
        for (int i = 0; i < fileFolderChain.Length; i++)
        {
            returnPath += fileFolderChain[i];
            if (i != fileFolderChain.Length - 1)
            {
                returnPath += "/";
            }
        }
        return returnPath;
    }

    public static bool Exists(string folder, string file)
    {
        return Exists(CombinePath(folder, file));
    }

    public static bool Exists(params string[] fileFolderChain)
    {
        return Exists(CombinePath(fileFolderChain));
    }

    public static bool Exists(string file)
    {
        if (!file.StartsWith("/")) file = "/" + file;
        return FileSystem.VFS.Exists(file.ToLowerInvariant());
    }

    public static Stream Open(string file)
    {
        if (!file.StartsWith("/", StringComparison.Ordinal))
            file = "/" + file;

        return FileSystem.VFS.GetEntry(file)?.GetStream();
    }

    public static Stream Open(string folder, string file)
    {
        return Open(CombinePath(folder, file));
    }

    public static TokenFileParser OpenNode(params string[] fileFolderChain)
    {
        return OpenNode(CombinePath(fileFolderChain));
    }

    public static TokenFileParser OpenNode(string folder, string file)
    {
        return OpenNode(CombinePath(folder, file));
    }

    public static TokenFileParser OpenNode(string file)
    {
        using (var stream = Open(file))
        {
            if (stream == null) return null;
            return new TokenFileParser(stream);
        }
    }

    public static CSVParser OpenCSV(string path)
    {
        if (path.IndexOf(".") < 0) path += ".csv";
        using (var stream = Open(path))
        {
            if (stream != null)
            {
                return new CSVParser(stream);
            }
        }
        return null;
    }

    public static CSVParser OpenCSV(string folder, string file)
    {
        return OpenCSV(CombinePath(folder, file));
    }

    public static CSVParser OpenCSV(params string[] fileFolderChain)
    {
        return OpenCSV(CombinePath(fileFolderChain));
    }

    public static string ReadAllText(string folder, string file)
    {
        return ReadAllText(CombinePath(folder, file));
    }

    public static string ReadAllText(string file)
    {
        using (var strm = Open(file))
        {
            return new StreamReader(strm).ReadToEnd();
        }
    }

    public static string[] ReadAllLines(string folder, string file)
    {
        return ReadAllLines(CombinePath(folder, file));
    }

    public static string[] ReadAllLines(string file)
    {
        return ReadAllText(file).Split(new string[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
    }

    public static byte[] ReadAllBytes(string folder, string file)
    {
        return ReadAllBytes(CombinePath(folder, file));
    }

    public static byte[] ReadAllBytes(string file)
    {
        var stream = Open(file);
        if (stream == null)
            return null;


        byte[] buffer = new byte[stream.Length];
        stream.Read(buffer, 0, (int)stream.Length);
        stream.Close();
        return buffer;
    }

    public static Stream OpenBinary(string folder, string file, FileAccess mode)
    {
        return OpenBinary(CombinePath(folder, file), mode);
    }

    public static Stream OpenBinary(string file, FileAccess mode)
    {
        if (mode != FileAccess.Read)
        {
            throw new NotImplementedException();
        }

        return Open(file);
    }
}
