using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

public enum GameSound
{
    None = 0,
    DoubleHorn,
    BeepShort,
    BeepLong,
    HitWaypoint,
    HitLastWaypoint,
    FinishRace,
    TimeWarning,
    YouLose,
    DamagedOut,
    MessageNote,
}

public class MMGame : MonoBehaviour
{
    /// <summary>True once Load() has run to completion.</summary>
    public bool IsLoaded { get; private set; }

    /// <summary>0..1, updated as each step finishes. Safe to poll from a UI.</summary>
    public float LoadProgress { get; private set; }

    /// <summary>Name of the step that just ran (or is running).</summary>
    public string LoadStageName { get; private set; }

    // game stuff
    public MMGamePlayer Player { get; protected set; }
    public MMWaypoints Waypoints { get; protected set; }
    public GameMusic Music => music;
    public SDLCity Level => city;
    public SpeechAudio SpeechAudio => speechAudio;

    private GameObject rainParticles;
    private GameMusic music;
    private SDLCity city;
    private SpeechAudio speechAudio;
   
    /// <summary>
    /// The player timer
    /// </summary>
    public MMTimer Timer => timer;

    /// <summary>
    /// Timer that runs from StartTimers, and doesn't stop with StopTimers. For use in results screen
    /// </summary>
    public MMTimer RunningTimer => runningTimer;

    private MMTimer timer;
    private MMTimer runningTimer;

    private AudioSource gameAudioSource;
    private static readonly (GameSound Sound, string Asset)[] SoundAssets =
    {
        (GameSound.DoubleHorn,      "carhorn1double"),
        (GameSound.BeepShort,       "startracelow"),
        (GameSound.BeepLong,        "startracehigh"),
        (GameSound.HitWaypoint,     "waypoint"),
        (GameSound.HitLastWaypoint, "lastwaypoint"),
        (GameSound.FinishRace,      "endofracetag"),
        (GameSound.TimeWarning,     "timerwarning"),
        (GameSound.YouLose,         "youlose"),
        (GameSound.DamagedOut,      "damgelose"),
        (GameSound.MessageNote,     "messagenote"),
    };
    private readonly Dictionary<GameSound, AudioClip> sounds = new Dictionary<GameSound, AudioClip>();

    private readonly struct LoadStep
    {
        public readonly string Name;
        public readonly Action Action;
        public readonly float Weight;

        public LoadStep(string name, Action action, float weight = 1.0f)
        {
            Name = name;
            Action = action;
            Weight = weight;
        }
    }

    // --------------------------------------------------------------------
    // Loading
    // --------------------------------------------------------------------

    /// <summary>
    /// Runs the whole game load, yielding progress in 0..1 after each step.
    /// Lazy: nothing happens until you enumerate it. Enumerate exactly once.
    ///
    ///   foreach (float p in game.Load())
    ///   {
    ///       bar.fillAmount = p;
    ///       label.text = game.LoadStageName;
    ///       yield return null;
    ///   }
    /// </summary>
    public IEnumerable<float> Load()
    {
        var steps = BuildLoadSteps();

        float total = 0.0f;
        for (int i = 0; i < steps.Count; i++)
            total += steps[i].Weight;
        if (total <= 0.0f)
            total = 1.0f;

        IsLoaded = false;
        LoadProgress = 0.0f;
        LoadStageName = null;

        // Let the caller paint an empty bar before the first hitch.
        yield return 0.0f;

        float done = 0.0f;

        foreach (var step in steps)
        {
            LoadStageName = step.Name;
            UnityEngine.Debug.Log($"[CityLoad] {step.Name}...");

            var stopwatch = Stopwatch.StartNew();
            step.Action();
            stopwatch.Stop();

            UnityEngine.Debug.Log(
                $"[CityLoad] {step.Name}: {stopwatch.Elapsed.TotalMilliseconds:F1} ms"
            );

            done += step.Weight;
            LoadProgress = done / total;
            yield return LoadProgress;
        }

        IsLoaded = true;
    }

    // Weights are relative, not seconds. Tune them from the [CityLoad] timings
    // so the bar moves evenly instead of sitting on InitInstances forever.
    private List<LoadStep> BuildLoadSteps()
    {
        return new List<LoadStep>
        {
            new LoadStep("Globals",          InitGlobals),
            new LoadStep("Systems",          InitSystems),
            new LoadStep("Viewports",        InitViewports),
            new LoadStep("Game Sounds",      InitSounds),
            new LoadStep("CreateCity",       CreateCity),

            new LoadStep("Init",             () => city.Init(GameState.SelectedCity)),
            new LoadStep("InitMaterials",    () => city.InitMaterials()),
            new LoadStep("InitLighting",     () => city.InitLighting()),
            new LoadStep("InitRooms",        () => city.InitRooms()),
            new LoadStep("InitCloudShadows", () => city.InitCloudShadows()),
            new LoadStep("InitDecals",       () => city.InitDecals()),
            new LoadStep("InitInstances",    () => city.InitInstances()),
            new LoadStep("InitProps",        () => city.InitProps()),
            new LoadStep("InitGizmos",       () => city.InitGizmos()),
            new LoadStep("InitSky",          () => city.InitSky()),
            new LoadStep("InitWaterOfDeath", () => city.InitWaterOfDeath()),
            new LoadStep("InitAmbientAudio", () => city.InitAmbientAudio()),
            new LoadStep("InitAI",           () => city.InitAI()),
            
            new LoadStep("PostCity",         PostCitySetup),
            new LoadStep("InitSpeech",       InitSpeech),
            new LoadStep("InitPlayer",       InitPlayer),
            new LoadStep("InitGameObjects",  InitGameObjects),
            new LoadStep("InitWeather",      InitWeather),
            new LoadStep("InitAudio",        InitAudio),
            new LoadStep("Reset",            Reset),
        };
    }

    private void InitGlobals()
    {
        // make sure some crucial things are setup (mainly for editor testing)
        if (!FileSystem.Initialized) FileSystem.Init();
        Localization.Init();

        if (string.IsNullOrEmpty(GameState.SelectedCity))
        {
            GameState.SelectedCity = "sf";
        }

        // hook tex loading and clear cache
        TextureLoader.PostprocessHook = TextureLoadHook;
        TextureCache.Clear();

        // load vehicle types
        VehicleTypeRegistry.Load();
    }

    private void InitSystems()
    {
        // timer
        timer = this.gameObject.AddComponent<MMTimer>();
        
        runningTimer = this.gameObject.AddComponent<MMTimer>();
        runningTimer.Init(false, 0.0f);

        // light renderer
        gameObject.AddComponent<LightGlowRenderer>();
        LightGlow.InitLights();
    }

    private void InitSounds()
    {
        gameAudioSource = gameObject.AddComponent<AudioSource>();
        gameAudioSource.volume = AudioUtils.AdjustVolumeCurve(0.98f);

        sounds.Clear();
        foreach (var (sound, asset) in SoundAssets)
        {
            var clip = AudioAssetManager.LoadClip(asset);
            if (clip == null)
            {
                UnityEngine.Debug.LogWarning($"[MMGame] Missing clip '{asset}' for {sound}");
                continue;
            }
            sounds[sound] = clip;
        }
    }

    public void PlaySound(GameSound sound, float volumeScale = 1.0f)
    {
        if (sound == GameSound.None || gameAudioSource == null)
            return;

        if (!sounds.TryGetValue(sound, out var clip) || clip == null)
        {
            UnityEngine.Debug.LogWarning($"[MMGame] PlaySound: {sound} not loaded");
            return;
        }

        gameAudioSource.PlayOneShot(clip, volumeScale * GameState.AudioVolume);
    }

    private void InitViewports()
    {
        ViewportManager.AddViewport(
            "MAIN",
            new Rect(0, 0, 1, 1),
            LayerMask.GetMask("Default", "Sky", "Banger", "BangerStatic", "PlayerVehicleBody", "VehicleBody"));
    }

    private void CreateCity()
    {
        string cityName = GameState.SelectedCity;
        VehicleAudioContainer.SirenCSVName = $"{cityName}policesiren";

        city = gameObject.AddComponent<SDLCity>();
    }

    private void InitWeather()
    {
        // init rain particles
        if (GameState.SelectedWeather == MMWeather.Raining)
        {
            var br = new ParticleBirthRule(AssetManager.OpenNode("tune", "rain.asBirthRule"));
            var ps = new GameObject("Rain Particles").AddComponent<ParticleSim>();
            ps.Init();

            ps.SetTextureSheet("ptx_rain");
            ps.TextureHeightTiles = 4;
            ps.TextureWidthTiles = 4;
            ps.BirthRule = br;

            ps.BirthRule.SpewTimeLimit = 0; //no loop end
            ps.EmitOverTime = true; //loop

            rainParticles = ps.gameObject;
        }

        // init wheel friction
        float weatherFriction = 1.0f;
        if (GameState.SelectedWeather == MMWeather.Raining)
        {
            weatherFriction = 0.75f;
            if (GameState.SelectedTimeOfDay != MMTimeOfDay.Night)
            {
                weatherFriction = 0.8f;
            }
        }
        VehWheel.WeatherFriction = weatherFriction;

        // setup wheel particles
        if(GameState.SelectedWeather == MMWeather.Raining)
        {
            VehWheelPtx.SetRainyWeatherMode();
        }
    }

    private void PostCitySetup()
    {
        // setup reflection intensity (todo: should this go here or in city?)
        Shader.SetGlobalFloat("_ReflectionIntensity", 1.0f);
        Shader.SetGlobalTexture("_ReflTex", TextureCache.Get(city.Lighting.preset.ReflectionMap));

        // set graphics settings
        city.SetViewDistance(GameState.ViewDistance);
        city.SetObjectDetail(GameState.ObjectDetail);

        // log stats
        SharedMaterialCache.LogStats();
        SharedMaterialCache.LogSingletonCauses();
    }

    private void InitSpeech()
    {
        var speechObj = new GameObject("Speech");
        speechObj.transform.parent = transform;

        speechAudio = speechObj.AddComponent<SpeechAudio>();
        speechAudio.Init(
            GameState.SelectedCity,
            GameState.SelectedVehicle,
            GameState.SelectedGameMode,
            GameState.SelectedTimeOfDay,
            GameState.SelectedWeather,
            SpeechAudio.NoVoice,
            GameState.SelectedRace);
    }

    protected void InitHudmapObjects()
    {
        // init common hudmap objects (police force, opponents, hookmen, waypoints)
        var hudmap = Player.HUD.Map;

        hudmap.AddPlayerArrow(Player.Car.transform);
        if (Waypoints != null)
        {
            foreach (var obj in Waypoints.WaypointObjects)
            {
                var item = hudmap.AddItem<HudmapItemWaypoint>();
                item.Init(Waypoints, obj);
            }
        }
        if (Level != null && Level.AINetwork != null)
        {
            foreach (var obj in Level.AINetwork.PoliceCars)
            {
                var item = hudmap.AddItem<CopHudmapItem>();
                item.Init(obj.Car.transform, obj);
            }
            foreach (var obj in Level.AINetwork.Opponents)
            {
                hudmap.AddOpponentArrow(obj.Car.transform);
            }
        }
    }

    protected virtual void InitPlayer()
    {
        var playerObj = new GameObject("Player");
        var player = playerObj.AddComponent<MMGamePlayer>();
        player.Init(this, GameState.SelectedVehicle, GameState.SelectedPaintjob);
        Player = player;
    }

    protected virtual void InitGameObjects()
    {
    }

    protected virtual void UpdateGame()
    {

    }

    public virtual void NextRace()
    {

    }

    public virtual void PlayerHitWaterHandler()
    {

    }

    private void InitAudio()
    {
        PlayerManager.CurrentPlayerConfig.SetAudio();
        float balance = GameState.AudioBalance;

        MMAudioMixer.Volume = GameState.AudioVolume;
        MMAudioMixer.MusicVolume = GameState.MusicVolme;

        var musicRoot = new GameObject("Music");
        musicRoot.transform.parent = transform;

        music = musicRoot.AddComponent<GameMusic>();
        music.Init(this, GameState.SelectedGameMode);
        music.balance = balance;

        speechAudio.enabled = GameState.AudioFlags.HasFlag(MMAudioFlags.CommentaryEnabled);
    }

    // Public API
    public void StartMusic()
    {
        if(GameState.AudioFlags.HasFlag(MMAudioFlags.MusicEnabled))
        {
            Music.StartMusic();
        }
    }

    public void StopMusic()
    {
        if (GameState.AudioFlags.HasFlag(MMAudioFlags.MusicEnabled))
        {
            Music.StopMusic();
        }
    }

    public void StartTimers()
    {
        if (timer != null) timer.StartTimer();
        if (runningTimer != null) runningTimer.StartTimer();
    }

    public void StopTimers()
    {
        if (timer != null) timer.StopTimer();
    }

    public void ResetTimers()
    {
        if (timer != null) timer.Reset();
        if(runningTimer != null) runningTimer.Reset();
    }

    public void ShowResults()
    {
        if (GameState.AudioFlags.HasFlag(MMAudioFlags.MusicEnabled))
        {
            music.PlayResults();
            music.StartMusic();
        }
        Player.HUD.ShowResults();
    }

    // --------------------------------------------------------------------
    // Textures
    // --------------------------------------------------------------------

    private void DesaturateTexture(Texture2D texture)
    {
        Color32[] pixels = texture.GetPixels32();

        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i].r >>= 1;
            pixels[i].g >>= 1;
            pixels[i].b >>= 1;
        }

        texture.SetPixels32(pixels);
        texture.Apply(true);
    }

    private AGETexture TextureLoadHook(string name, AGETexture texture)
    {
        // Variants loaded through TextureLoader.Load come back through this hook,
        // so pass them through untouched to avoid recursion
        if(Level != null && Level.TextureVariantSettings != null)
        {
            foreach(var variant in Level.TextureVariantSettings.Variants)
            {
                if(name.EndsWith(variant.Suffix, StringComparison.Ordinal))
                {
                    return texture;
                }
            }
        }
        if (name.EndsWith("_fa", StringComparison.Ordinal) || name.EndsWith("_ni", StringComparison.Ordinal))
            return texture;

        bool isNight = GameState.SelectedTimeOfDay == MMTimeOfDay.Night;
        bool isRaining = GameState.SelectedWeather == MMWeather.Raining;

        // 1. Custom variants take priority
        if (Level != null && Level.TextureVariantSettings != null)
        {
            foreach (var variant in Level.TextureVariantSettings.Variants)
            {
                var customVariantVersion = TextureLoader.Load($"{name}{variant.Suffix}");
                if (customVariantVersion != null)
                {
                    texture.Destroy();
                    if (variant.Desaturate)
                    {
                        DesaturateTexture(customVariantVersion.Texture);
                    }
                    return customVariantVersion;
                }
            }
        }

        // 2. Rain takes priority, even at night
        if (isRaining)
        {
            var faVersion = TextureLoader.Load($"{name}_fa");
            if (faVersion != null)
            {
                texture.Destroy();
                if (isNight)
                    DesaturateTexture(faVersion.Texture); // original leaves desaturation allowed for _fa
                return faVersion;
            }
        }

        // 3. Night variant (only reached if not raining or no _fa exists)
        if (isNight)
        {
            var niVersion = TextureLoader.Load($"{name}_ni");
            if (niVersion != null)
            {
                texture.Destroy();
                return niVersion; // original disables desaturation for _ni
            }

            // 3. Fallback: base texture, desaturated at night
            DesaturateTexture(texture.Texture);
        }

        return texture;
    }

    // --------------------------------------------------------------------
    // Lifecycle
    // --------------------------------------------------------------------
    public virtual void Reset()
    {
        city.Reset();
        if(Player != null) Player.Reset();
        if(Waypoints != null) Waypoints.Reset();
        Time.timeScale = 1.0f;

        if (runningTimer != null)
        {
            // stop here, start when StartTimers is called
            runningTimer.Reset();
            runningTimer.StopTimer();
        }

        music.ResetMusic();
        MMAudioMixer.Unmute();
    }

    protected virtual void Update()
    {
        if(IsLoaded)
        {
            UpdateGame();
        }

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            Player.HUD.TogglePopup();
        }

        if (IsLoaded && Input.GetKeyDown(KeyCode.F4))
        {
            Reset();
        }

        if (Input.GetKeyDown(KeyCode.F2))
        {
            Time.timeScale = (Time.timeScale > 0.5f) ? 0.0f : 1.0f;
            MMAudioMixer.ToggleMute();
        }
    }

    private void LateUpdate()
    {
        // match up rain to the camera
        Vector3 rainPosition = Vector3.zero;
        bool shouldShowRain = false;
        if (Level != null)
        {
            var mainViewport = ViewportManager.MainViewport;
            if (mainViewport != null && mainViewport.ActiveCamera != null)
            {
                int roomId = Level.FindRoomIdWithWarps(mainViewport.ActiveCamera.transform.position);
                var room = Level.GetRoom(roomId);
                if (room != null)
                {
                    if (room.Flags.HasFlag(PSDL.RoomFlags.Subterranean))
                    {
                        shouldShowRain = false;
                    }
                    else
                    {
                        if (room.Flags.HasFlag(PSDL.RoomFlags.SpecialBound))
                        {
                            var cameraPos = mainViewport.ActiveCamera.transform.position;
                            rainPosition = cameraPos + (Vector3.up * 10.0f) + (mainViewport.ActiveCamera.transform.forward.Flatten() * 10.0f);
                            shouldShowRain = true;
                        }
                        else
                        {
                            var cameraPos = mainViewport.ActiveCamera.transform.position;
                            var rayOrigin = cameraPos + (Vector3.up * 100.0f);
                            var layer = LayerMask.GetMask("Default");

                            if (!Physics.Raycast(rayOrigin, Vector3.down, 100.0f, layer))
                            {
                                shouldShowRain = true;
                                rainPosition = cameraPos + (Vector3.up * 10.0f) + (mainViewport.ActiveCamera.transform.forward.Flatten() * 10.0f);
                            }
                        }
                    }
                }
            }
        }

        if (rainParticles != null)
        {
            rainParticles.SetActive(shouldShowRain);
            rainParticles.transform.position = rainPosition;
        }
    }

    protected virtual void OnDestroy()
    {
        LightGlow.ShutdownLights();
        TextureLoader.PostprocessHook = null;
        TextureCache.Clear();
        ImpactAudioDataManager.Unload();
    }
}