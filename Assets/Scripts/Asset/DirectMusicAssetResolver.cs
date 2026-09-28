using System.IO;
using DirectMusicLite;

public class DirectMusicAssetResolver : DmBankResolver
{
    private string[] baseDirectory = { "aud", "dmusic" };

    public override string Resolve(string reference)
    {
        string name = ReferenceFileName(reference);
        if (string.IsNullOrEmpty(name)) return null;

        string path = AssetManager.CombinePath(baseDirectory[0], baseDirectory[1],
                                               EnsureExtension(name, ".dls"));
        if (AssetManager.Exists(path)) return path;

        // reference that names General MIDI can fall through 
        if (LooksLikeGeneralMidi(reference)) return DmFileBankResolver.SystemGeneralMidiPath();
        return null;
    }

    protected override string ResolveDefaultCollection()
    {
        // reached when a band instrument carries no DLS reference at all.
        string path = AssetManager.CombinePath(baseDirectory[0], baseDirectory[1], "gm.dls");
        if (AssetManager.Exists(path)) return path;
        return DmFileBankResolver.SystemGeneralMidiPath();
    }

    protected override bool IsSystemGeneralMidi(string resolvedKey)
    {
        string system = DmFileBankResolver.SystemGeneralMidiPath();
        return system != null &&
               string.Equals(system, resolvedKey, System.StringComparison.OrdinalIgnoreCase);
    }

    public override byte[] Open(string resolvedKey)
    {
        if (IsSystemGeneralMidi(resolvedKey)) return File.ReadAllBytes(resolvedKey);
        return AssetManager.ReadAllBytes(resolvedKey);
    }

    public override string ResolveStyle(string reference)
    {
        string name = ReferenceFileName(reference);
        if (string.IsNullOrEmpty(name)) return null;

        string path = AssetManager.CombinePath(baseDirectory[0], baseDirectory[1],
                                               EnsureExtension(name, ".sty"));
        return AssetManager.Exists(path) ? path : null;
    }
}