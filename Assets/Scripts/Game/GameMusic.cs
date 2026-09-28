using DirectMusicLite;
using UnityEngine;
using UnityEngine.Audio;

public class GameMusic : MonoBehaviour
{
    private DirectMusicSegmentPlayer segmentPlayer;

    public float volume
    {
        get => segmentPlayer.volume;
        set
        {
            segmentPlayer.volume = value;
        }
    }

    public float balance
    {
        get => segmentPlayer.balance;
        set => segmentPlayer.balance = value;
    }

    private DmSegment startSegment;
    private DmSegment returnSegment;
    private DmSegment idleSegment;
    private DmSegment copChaseSegment;
    private DmSegment idleCopChaseSegment;
    private DmSegment pauseSegment;
    private DmSegment resultsSegment;

    private const float IDLE_SPEED = 5.0f;
    private const float IDLE_TIME = 5.0f;
    private const float BIGAIR_HEIGHT = 10.0f;

    private MMGame game;

    private float timeSpentIdling = 0.0f;
    private bool idling = false;

    private float speed = 0.0f;
    private int numCops = 0;

    private enum MusicState { Normal, Idle, CopChase, IdleCopChase }

    private MusicState currentState = MusicState.Normal;
    private bool paused = false;
    private bool showingResults = false;
    private bool musicPlaying = false;

    private bool disableAutoIdleSegmentChange = false;

    /// <summary>
    /// True between StartMusic() and StopMusic().
    /// </summary>
    public bool IsPlaying => musicPlaying;

    /// <summary>
    /// When true, standing still never switches to the idle segments.
    /// If idle music is currently playing, the next update moves back to
    /// the non-idle equivalent. The idle timer is cleared so re-enabling
    /// doesn't instantly drop into idle music.
    /// </summary>
    public bool DisableAutoIdleSegmentChange
    {
        get => disableAutoIdleSegmentChange;
        set
        {
            disableAutoIdleSegmentChange = value;
            timeSpentIdling = 0.0f;
            idling = false;
        }
    }

    /// <summary>
    /// When true, the cop chase segments are used even if no cops are
    /// present. The change is picked up on the next update. If set before
    /// StartMusic(), the music starts directly in the cop chase segment.
    /// </summary>
    public bool AlwaysUseHighIntensityMusic { get; set; } = false;

    public void UpdateParams(float speed, int numCops)
    {
        this.speed = speed;
        this.numCops = numCops;
    }

    private DmSegment LoadPreloadedSegment(string name)
    {
        if (string.IsNullOrEmpty(name))
            return null;

        byte[] bytes = AssetManager.ReadAllBytes(
            AssetManager.CombinePath(
                "aud",
                "dmusic",
                name + ".sgt"
            )
        );

        return segmentPlayer.PreloadSegmentFile(bytes);
    }

    private void LoadMusic(MMGameMode gameMode)
    {
        bool isRaceMode = (gameMode != MMGameMode.Cruise);
        string csv = isRaceMode ? "singlerace.csv" : "singleroam.csv";
        string path = AssetManager.CombinePath(
            "aud",
            "dmusic",
            "csv_files",
            csv
        );

        var reader = AssetManager.OpenCSV(path);

        int numLines = reader.LineCount;

        if (numLines <= 0)
            return;

        int choice = Random.Range(0, numLines-1);
        int current = 0;

        reader.PrepareHeader();
        while (!reader.EOF())
        {
            reader.PrepareLine();
            if (current == choice)
            {
                // Start music is loaded normally so it is immediately
                // ready to be played.
                string startSegmentName = reader.GetColumnCaseInsensitive("Start Music");

                byte[] startSegmentBytes = AssetManager.ReadAllBytes(
                    AssetManager.CombinePath(
                        "aud",
                        "dmusic",
                        startSegmentName + ".sgt"
                    )
                );

                startSegment = segmentPlayer.LoadSegment(startSegmentBytes);

                // Everything else is preloaded because these segments
                // are only needed when transitioning later.
                returnSegment =
                    LoadPreloadedSegment(reader.GetColumnCaseInsensitive("Return Music"));

                idleSegment =
                    LoadPreloadedSegment(reader.GetColumnCaseInsensitive("Idle Music"));

                copChaseSegment =
                    LoadPreloadedSegment(reader.GetColumnCaseInsensitive("Cop Chase Music"));

                idleCopChaseSegment =
                    LoadPreloadedSegment(reader.GetColumnCaseInsensitive("idle cop music"));

                pauseSegment =
                    LoadPreloadedSegment(reader.GetColumnCaseInsensitive("Pause Music"));

                if (isRaceMode)
                {
                    resultsSegment =
                        LoadPreloadedSegment(reader.GetColumnCaseInsensitive("Race results Music"));
                }

                break;
            }

            current++;
        }
    }

    public void Init(MMGame game, MMGameMode gameMode)
    {
        this.game = game;
        AudioSource source = gameObject.AddComponent<AudioSource>();

        source.outputAudioMixerGroup =
            Resources.Load<AudioMixerGroup>("Mixers/DirectMusicMixer");

        segmentPlayer =
            gameObject.AddComponent<DirectMusicSegmentPlayer>();

        segmentPlayer.BankResolver =
            new DirectMusicAssetResolver();

        segmentPlayer.playOnStart = false;

        LoadMusic(gameMode);

        // Music is not started here; call StartMusic() when ready.
    }

    /// <summary>
    /// Starts the music. Plays the start segment, unless
    /// AlwaysUseHighIntensityMusic is set, in which case it goes straight
    /// into the cop chase segment. Does nothing if already playing.
    /// </summary>
    public void StartMusic()
    {
        if (segmentPlayer == null || musicPlaying)
            return;

        timeSpentIdling = 0.0f;
        idling = false;

        DmSegment segment;

        if (AlwaysUseHighIntensityMusic && copChaseSegment != null)
        {
            currentState = MusicState.CopChase;
            segment = copChaseSegment;
        }
        else
        {
            currentState = MusicState.Normal;
            segment = startSegment ?? returnSegment;
        }

        if (segment == null)
            return;

        musicPlaying = true;

        // Play() starts the segment loaded with LoadSegment (the start
        // segment). For anything else, cut over to it immediately.
        segmentPlayer.Play();

        if (segment != startSegment)
            segmentPlayer.TransitionTo(segment, null, DmCommandType.Groove, DmComposeFlags.Immediate);
    }

    /// <summary>
    /// Stops the music. Speed/cop updates are ignored until StartMusic()
    /// is called again.
    /// </summary>
    public void StopMusic()
    {
        if (segmentPlayer == null || !musicPlaying)
            return;

        musicPlaying = false;
        segmentPlayer.Stop();

        timeSpentIdling = 0.0f;
        idling = false;
        currentState = MusicState.Normal;
    }

    private void Update()
    {
        // While results are showing, the state machine is frozen so
        // speed/cop changes don't pull the music away from the results.
        if (!musicPlaying || paused || showingResults || segmentPlayer == null)
            return;

        // Track idle time (ignored entirely while auto idle is disabled)
        if (disableAutoIdleSegmentChange || speed >= IDLE_SPEED)
            timeSpentIdling = 0.0f;
        else
            timeSpentIdling += Time.deltaTime;

        idling = timeSpentIdling >= IDLE_TIME;

        MusicState desired = ComputeDesiredState();

        if (desired != currentState)
        {
            currentState = desired;
            PlayStateSegment(currentState);
        }
    }

    private MusicState ComputeDesiredState()
    {
        bool highIntensity = numCops > 0 || AlwaysUseHighIntensityMusic;
        bool idle = idling && !disableAutoIdleSegmentChange;

        if (highIntensity)
            return idle ? MusicState.IdleCopChase : MusicState.CopChase;

        return idle ? MusicState.Idle : MusicState.Normal;
    }

    private DmSegment GetSegmentForState(MusicState state)
    {
        switch (state)
        {
            case MusicState.Idle: return idleSegment;
            case MusicState.CopChase: return copChaseSegment;
            case MusicState.IdleCopChase: return idleCopChaseSegment ?? copChaseSegment;
            default: return returnSegment;
        }
    }

    private void PlayStateSegment(MusicState state)
    {
        DmSegment segment = GetSegmentForState(state);
        if (segment != null)
            segmentPlayer.TransitionTo(segment, null, DmCommandType.Groove, DmComposeFlags.Immediate);
    }

    public void SetPaused(bool pause)
    {
        if (paused == pause)
            return;

        paused = pause;

        // The flag is still tracked while stopped, but nothing is played.
        if (!musicPlaying)
            return;

        if (pause)
        {
            if (pauseSegment != null)
                segmentPlayer.TransitionTo(pauseSegment, null);
        }
        else if (showingResults)
        {
            // Unpausing on the results screen goes back to the results music.
            if (resultsSegment != null)
                segmentPlayer.TransitionTo(resultsSegment, null);
        }
        else
        {
            // Resume whatever fits the current situation, e.g. back into
            // the chase if cops are still after you. Recomputed here in case
            // the flags changed while paused.
            currentState = ComputeDesiredState();
            PlayStateSegment(currentState);
        }
    }

    /// <summary>
    /// Switches to the race results music and stops reacting to
    /// speed/cop updates until ResetMusic() is called.
    /// Does nothing in Cruise mode, where no results segment is loaded.
    /// </summary>
    public void PlayResults()
    {
        if (segmentPlayer == null || resultsSegment == null || showingResults)
            return;

        showingResults = true;

        if (musicPlaying && !paused)
            segmentPlayer.TransitionTo(resultsSegment, null);
    }

    /// <summary>
    /// Puts the music back into its initial racing state (e.g. for a
    /// race restart): clears results/pause/idle/cop state, turns off
    /// DisableAutoIdleSegmentChange and AlwaysUseHighIntensityMusic,
    /// and replays the start segment if music is playing. If music is
    /// stopped, it stays stopped.
    /// </summary>
    public void ResetMusic()
    {
        if (segmentPlayer == null)
            return;

        showingResults = false;
        paused = false;

        disableAutoIdleSegmentChange = false;
        AlwaysUseHighIntensityMusic = false;

        timeSpentIdling = 0.0f;
        idling = false;
        speed = 0.0f;
        numCops = 0;
        currentState = MusicState.Normal;

        if (!musicPlaying)
            return;

        DmSegment segment = startSegment ?? returnSegment;
        if (segment != null)
            segmentPlayer.TransitionTo(segment, null);
    }
}