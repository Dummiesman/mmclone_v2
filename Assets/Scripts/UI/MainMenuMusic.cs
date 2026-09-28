using DirectMusicLite;
using UnityEngine;
using UnityEngine.Audio;

public class MainMenuMusic : MonoBehaviour
{
    private DirectMusicSegmentPlayer segmentPlayer;
    private string segmentName = string.Empty;

    public bool isPlaying => segmentPlayer.IsPlaying;

    public float balance
    {
        get => segmentPlayer.balance;
        set => segmentPlayer.balance = value;
    }

    public float volume
    {
        get => segmentPlayer.volume;
        set => segmentPlayer.volume = value;
    }

    private void LoadCsv()
    {
        string path = AssetManager.CombinePath("aud", "dmusic", "csv_files", "ui.csv");
        var reader = AssetManager.OpenCSV(path);

        reader.PrepareHeader(); // not needed, only one column
        reader.PrepareLine(); // read in one line

        segmentName = reader[0];
    }

    public void StopPlayback()
    {
        if (segmentPlayer != null)
        {
            segmentPlayer.Stop();
        }
    }

    public void StartPlayback()
    {
        if (segmentPlayer != null)
        {
            segmentPlayer.PlayFromSegmentStart();
        }
    }

    public void Init()
    {
        LoadCsv();

        byte[] segmentBytes = AssetManager.ReadAllBytes(AssetManager.CombinePath("aud", "dmusic", $"{segmentName}.sgt"));
        if (segmentBytes != null)
        {
            AudioSource source = gameObject.AddComponent<AudioSource>();
            source.outputAudioMixerGroup = Resources.Load<AudioMixerGroup>("Mixers/DirectMusicMixer");

            segmentPlayer = gameObject.AddComponent<DirectMusicSegmentPlayer>();
            segmentPlayer.BankResolver = new DirectMusicAssetResolver();
            segmentPlayer.playOnStart = false;

            segmentPlayer.LoadSegment(segmentBytes);
        }
    }
}
