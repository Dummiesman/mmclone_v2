using UnityEngine;
using System.IO;
using UnityEngine.Video;

public class Logos : MonoBehaviour 
{
    [SerializeField]
    private string menuSceneName = "MainMenu";
    [SerializeField]
    private Camera targetCamera;

    private string[] videoFormats = new[] { "mp4", "avi" };
    private VideoPlayer videoPlayer;
    private VideoChapterSystem chapterSystem;

    // init stuff
    private static string GetVideoRootPath()
    {
        string persistentRoot = (Application.isMobilePlatform) ? Application.persistentDataPath
                                                          : FileSystem.Root;
        return persistentRoot;
    }

    string GetVideoPath()
    {
        string rootPath = GetVideoRootPath();
        foreach (var fmt in videoFormats)
        {
            string path = Path.Combine(rootPath, $"logos.{fmt}");
            if (File.Exists(path))
            {
                return path;
            }
        }
        return string.Empty;
    }

    // actions
    void GoToMenu()
    {
        UnityEngine.SceneManagement.SceneManager.LoadScene(menuSceneName);
    }

    private void InitVideoPlayer()
    {
        videoPlayer = this.gameObject.AddComponent<VideoPlayer>();

        //setup audio
        var videoAudio = this.gameObject.AddComponent<AudioSource>();
        videoAudio.spatialBlend = 0;

        //setup events
        videoPlayer.loopPointReached += VideoPlayer_loopPointReached;

        //setup video
        videoPlayer.audioOutputMode = VideoAudioOutputMode.AudioSource;
        videoPlayer.controlledAudioTrackCount = 1;
        videoPlayer.EnableAudioTrack(0, true);
        videoPlayer.SetTargetAudioSource(0, videoAudio);

        videoPlayer.targetCamera = targetCamera;
        videoPlayer.renderMode = VideoRenderMode.CameraNearPlane;
        videoPlayer.aspectRatio = VideoAspectRatio.FitInside;
        videoPlayer.Prepare();
        videoPlayer.Play();

        //setup chapters
        chapterSystem = this.gameObject.AddComponent<VideoChapterSystem>();
        chapterSystem.Player = videoPlayer;

        chapterSystem.Chapters.Add(new VideoChapterSystem.Chapter() { Name = "MSFT", StartTime = 0f });
        chapterSystem.Chapters.Add(new VideoChapterSystem.Chapter() { Name = "ANGL", StartTime = 14.25f });
    }

    private void Play()
    {
        chapterSystem.PlayFromBeginning();
    }

    private void LoadEmbedded(VideoClip clip)
    {
        InitVideoPlayer();
        videoPlayer.source = UnityEngine.Video.VideoSource.VideoClip;
        videoPlayer.clip = clip;
        Play();
    }

    private void LoadFromFile(string videoPath)
    {
        InitVideoPlayer();
        videoPlayer.source = UnityEngine.Video.VideoSource.Url;
        videoPlayer.url = videoPath;
        Play();
    }

    void Start () 
    {
        if(ArgParser.GetFlag("nomovie"))
        {
            GoToMenu();
            return;
        }

        string videoPath = GetVideoPath();
        if (string.IsNullOrEmpty(videoPath))
        {
            var embeddedClip = Resources.Load("logos") as VideoClip;
            if (embeddedClip != null)
            {
                LoadEmbedded(embeddedClip);
            }
            else
            {
                Debug.LogError("Logos movie does not exist.");
                GoToMenu();
                return;
            }
        }
        else
        {
            LoadFromFile(videoPath);
        }
    }

    private void VideoPlayer_loopPointReached(VideoPlayer source)
    {
        GoToMenu();
    }

    void OnKeyPress()
    {
        //
        if (videoPlayer == null)
        {
            GoToMenu();
            return;
        }

        //
        if (!chapterSystem.NextChapter())
        {
            GoToMenu();
        }
    }

    void Update()
    {
        if (!Input.anyKeyDown)
        {
            return;
        }
        OnKeyPress();
    }
}
