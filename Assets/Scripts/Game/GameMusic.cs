using DirectMusicLite;
using UnityEngine;
using UnityEngine.Audio;

public class GameMusic : MonoBehaviour
{
    // The main gameplay player: start/return/idle/cop chase/pause all share
    // the same styles, so they can live on one player.
    private DirectMusicSegmentPlayer segmentPlayer;

    // Results uses different styles, so it gets its own player with the
    // results music as its primary (LoadSegment) segment.
    private DirectMusicSegmentPlayer resultsPlayer;

    // Big air is a one shot motif layered over whatever is playing, so it
    // also needs its own player. Nothing drives this yet.
    private DirectMusicSegmentPlayer bigairPlayer;

    public float volume
    {
        get => segmentPlayer.volume;
        set
        {
            segmentPlayer.volume = value;

            if (resultsPlayer != null)
                resultsPlayer.volume = value;

            if (bigairPlayer != null)
                bigairPlayer.volume = value;
        }
    }

    public float balance
    {
        get => segmentPlayer.balance;
        set
        {
            segmentPlayer.balance = value;

            if (resultsPlayer != null)
                resultsPlayer.balance = value;

            if (bigairPlayer != null)
                bigairPlayer.balance = value;
        }
    }

    private DmSegment startSegment;
    private DmSegment returnSegment;
    private DmSegment idleSegment;
    private DmSegment copChaseSegment;
    private DmSegment idleCopChaseSegment;
    private DmSegment pauseSegment;
    private DmSegment resultsSegment;
    private DmSegment bigairSegment;

    private const float IDLE_SPEED = 5.0f;
    private const float IDLE_TIME = 5.0f;
    private const float BIGAIR_HEIGHT = 10.0f;

    // The motif can't fire again until the car has dropped back below this.
    // Keeping it under BIGAIR_HEIGHT stops a car hovering right on the
    // threshold from retriggering every frame.
    private const float BIGAIR_RESET_HEIGHT = 7.5f;

    private MMGame game;

    private float timeSpentIdling = 0.0f;
    private bool idling = false;

    private float speed = 0.0f;
    private int numCops = 0;

    private float heightAboveGround = 0.0f;

    // False until the car has been back down near the ground, so a jump
    // already in progress when the music starts doesn't fire the motif.
    private bool bigairArmed = false;

    private enum MusicState { Normal, Idle, CopChase, IdleCopChase }

    private MusicState currentState = MusicState.Normal;
    private bool paused = false;
    private bool showingResults = false;
    private bool musicPlaying = false;

    // Which players have actually been started. The main player is stopped
    // while the results player has the floor, so these are tracked
    // separately from musicPlaying.
    private bool mainPlayerActive = false;
    private bool resultsPlayerActive = false;

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

    /// <summary>
    /// Feeds in the car's current height above the ground. Crossing
    /// BIGAIR_HEIGHT fires the big air motif once; it won't fire again
    /// until the car has come back down below BIGAIR_RESET_HEIGHT.
    /// Like the speed/cop updates, this is ignored while the music is
    /// stopped, paused or showing results.
    /// </summary>
    public void UpdateHeight(float height)
    {
        heightAboveGround = height;
    }

    private static byte[] ReadSegmentBytes(string name)
    {
        if (string.IsNullOrEmpty(name))
            return null;

        return AssetManager.ReadAllBytes(
            AssetManager.CombinePath(
                "aud",
                "dmusic",
                name + ".sgt"
            )
        );
    }

    /// <summary>
    /// Preloads a segment on the given player. Used for segments that are
    /// only needed when transitioning later.
    /// </summary>
    private static DmSegment LoadPreloadedSegment(DirectMusicSegmentPlayer player, string name)
    {
        byte[] bytes = ReadSegmentBytes(name);

        if (player == null || bytes == null)
            return null;

        return player.PreloadSegmentFile(bytes);
    }

    /// <summary>
    /// Loads a segment as the given player's primary segment, so that
    /// Play() on that player starts it directly.
    /// </summary>
    private static DmSegment LoadPrimarySegment(DirectMusicSegmentPlayer player, string name)
    {
        byte[] bytes = ReadSegmentBytes(name);

        if (player == null || bytes == null)
            return null;

        return player.LoadSegment(bytes);
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

        int choice = Random.Range(0, numLines - 1);
        int current = 0;

        reader.PrepareHeader();
        while (!reader.EOF())
        {
            reader.PrepareLine();
            if (current == choice)
            {
                // Start music is the main player's primary segment so it is
                // immediately ready to be played.
                startSegment =
                    LoadPrimarySegment(segmentPlayer, reader.GetColumnCaseInsensitive("Start Music"));

                // Everything else on the main player is preloaded because
                // these segments are only needed when transitioning later.
                returnSegment =
                    LoadPreloadedSegment(segmentPlayer, reader.GetColumnCaseInsensitive("Return Music"));

                idleSegment =
                    LoadPreloadedSegment(segmentPlayer, reader.GetColumnCaseInsensitive("Idle Music"));

                copChaseSegment =
                    LoadPreloadedSegment(segmentPlayer, reader.GetColumnCaseInsensitive("Cop Chase Music"));

                idleCopChaseSegment =
                    LoadPreloadedSegment(segmentPlayer, reader.GetColumnCaseInsensitive("idle cop music"));

                pauseSegment =
                    LoadPreloadedSegment(segmentPlayer, reader.GetColumnCaseInsensitive("Pause Music"));

                // Results has its own player and its own styles, so the
                // results music is loaded as that player's primary segment.
                if (isRaceMode)
                {
                    resultsSegment =
                        LoadPrimarySegment(resultsPlayer, reader.GetColumnCaseInsensitive("Race results Music"));
                }

                // Big air is a one shot on its own player, so it is the
                // primary segment there too.
                bigairSegment =
                    LoadPrimarySegment(bigairPlayer, reader.GetColumnCaseInsensitive("Big Air Motif name"));

                break;
            }

            current++;
        }
    }

    private DirectMusicSegmentPlayer CreatePlayer(string name, AudioMixerGroup mixerGroup)
    {
        var holder = new GameObject(name);
        holder.transform.SetParent(transform, false);

        AudioSource source = holder.AddComponent<AudioSource>();
        source.outputAudioMixerGroup = mixerGroup;

        var player = holder.AddComponent<DirectMusicSegmentPlayer>();
        player.volume = 1.0f;

        player.BankResolver = new DirectMusicAssetResolver();
        player.playOnStart = false;

        return player;
    }

    public void Init(MMGame game, MMGameMode gameMode)
    {
        this.game = game;

        AudioMixerGroup mixerGroup =
            Resources.Load<AudioMixerGroup>("Mixers/DirectMusicMixer");

        segmentPlayer = CreatePlayer("MusicPlayer", mixerGroup);
        resultsPlayer = CreatePlayer("ResultsMusicPlayer", mixerGroup);
        bigairPlayer = CreatePlayer("BigAirMusicPlayer", mixerGroup);

        // Big air is a one shot motif, it must not loop.
        bigairPlayer.volume = 2.0f;
        bigairPlayer.respectSegmentLoop = false;

        LoadMusic(gameMode);

        // Music is not started here; call StartMusic() when ready.
    }

    private void PlayOnMainPlayer(DmSegment segment, bool immediate)
    {
        if (segmentPlayer == null || segment == null)
            return;

        if (!mainPlayerActive)
        {
            mainPlayerActive = true;

            // Play() starts the segment loaded with LoadSegment (the start
            // segment). For anything else, cut over to it immediately.
            segmentPlayer.Play();

            if (segment != startSegment)
                segmentPlayer.TransitionTo(segment, null, DmCommandType.Groove, DmComposeFlags.Immediate);

            return;
        }

        if (immediate)
            segmentPlayer.TransitionTo(segment, null, DmCommandType.Groove, DmComposeFlags.Immediate);
        else
            segmentPlayer.TransitionTo(segment, null);
    }

    private void StopMainPlayer()
    {
        if (segmentPlayer == null || !mainPlayerActive)
            return;

        mainPlayerActive = false;
        segmentPlayer.Stop();
    }

    private void StartResultsPlayer()
    {
        if (resultsPlayer == null || resultsSegment == null || resultsPlayerActive)
            return;

        resultsPlayerActive = true;

        // The results music is this player's primary segment, so Play()
        // starts it directly.
        resultsPlayer.Play();
    }

    private void StopResultsPlayer()
    {
        if (resultsPlayer == null || !resultsPlayerActive)
            return;

        resultsPlayerActive = false;
        resultsPlayer.Stop();
    }

    /// <summary>
    /// Fires the big air motif as a one shot on its own player, over
    /// whatever else is currently playing. Normally driven by
    /// UpdateHeight(), but safe to call directly for a scripted stinger.
    /// </summary>
    public void PlayBigAir()
    {
        if (bigairPlayer == null || bigairSegment == null)
            return;

        bigairPlayer.PlayAtStart();
    }

    /// <summary>
    /// Cuts the big air motif short if it is still playing.
    /// </summary>
    public void StopBigAir()
    {
        if (bigairPlayer == null)
            return;

        bigairPlayer.Stop();
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

        heightAboveGround = 0.0f;
        bigairArmed = false;

        // If PlayResults() ran while the music was stopped, come back up on
        // the results player rather than on the gameplay music.
        if (showingResults)
        {
            if (resultsSegment == null)
                return;

            musicPlaying = true;

            if (paused)
                PlayOnMainPlayer(pauseSegment, true);
            else
                StartResultsPlayer();

            return;
        }

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

        PlayOnMainPlayer(segment, true);
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

        StopMainPlayer();
        StopResultsPlayer();
        StopBigAir();

        timeSpentIdling = 0.0f;
        idling = false;
        currentState = MusicState.Normal;

        heightAboveGround = 0.0f;
        bigairArmed = false;
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

        // Runs independently of the state machine: the motif is a one shot
        // layered over whatever the main player is doing.
        UpdateBigAir();
    }

    private void UpdateBigAir()
    {
        if (bigairPlayer == null || bigairSegment == null)
            return;

        if (heightAboveGround <= BIGAIR_RESET_HEIGHT)
        {
            bigairArmed = true;
            return;
        }

        if (bigairArmed && heightAboveGround >= BIGAIR_HEIGHT)
        {
            bigairArmed = false;
            PlayBigAir();
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
        PlayOnMainPlayer(GetSegmentForState(state), true);
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
            // The pause segment lives on the main player, so the results
            // player hands the floor back over for the duration.
            if (pauseSegment != null)
            {
                StopResultsPlayer();
                PlayOnMainPlayer(pauseSegment, false);
            }
        }
        else if (showingResults)
        {
            // Unpausing on the results screen goes back to the results music.
            if (resultsSegment != null)
            {
                StopMainPlayer();
                StartResultsPlayer();
            }
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
    /// Switches to the race results music, which runs on its own player,
    /// and stops reacting to speed/cop updates until ResetMusic() is called.
    /// Does nothing in Cruise mode, where no results segment is loaded.
    /// </summary>
    public void PlayResults()
    {
        if (segmentPlayer == null || resultsSegment == null || showingResults)
            return;

        showingResults = true;

        if (musicPlaying && !paused)
        {
            StopMainPlayer();
            StartResultsPlayer();
        }
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

        timeSpentIdling = IDLE_TIME;
        idling = false;
        speed = 0.0f;
        numCops = 0;
        currentState = MusicState.Normal;

        heightAboveGround = 0.0f;
        bigairArmed = false;

        StopResultsPlayer();
        StopBigAir();

        if (!musicPlaying)
        {
            StopMainPlayer();
            return;
        }

        StopMainPlayer();
        PlayOnMainPlayer(startSegment ?? returnSegment, true);
    }
}