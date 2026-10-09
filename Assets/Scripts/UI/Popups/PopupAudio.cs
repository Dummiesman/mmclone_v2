using UnityEngine;

public class PopupAudio : PopupMenuBase
{
    // layout constants (normalized rect space)
    const float kX = 0.1f;
    const float kWidth = 0.6f;
    const float kLabelH = 0.04f;
    const float kSliderH = 0.05f;
    const float kGap = 0.01f;   // label -> slider
    const float kRowGap = 0.04f;   // row -> next row
    const int kFontSize = 12;

    float m_nextY = 0.2f;   // first row starts here

    // sliders
    UISlider sfxVolSlider;
    UISlider musicVolSlider;
    UISlider balanceSlider;

    void AddAudioSlider(string header, out UISlider slider,
                    float min = 0f, float max = 1f, bool readOnly = false)
    {
        AddLabel(header + "Label", header,
                 new Rect(kX, m_nextY, kWidth, kLabelH), kFontSize);
        m_nextY += kLabelH + kGap;

        slider = AddSlider(header + "Slider",
                           new Rect(kX, m_nextY, kWidth, kSliderH),
                           min, max, readOnly);
        m_nextY += kSliderH + kRowGap;
    }

    private void OnSfxVolChanged(float value)
    {
        GameState.AudioVolume = value;
        MMAudioMixer.Volume = value;
    }
    
    private void OnMusicVolChanged(float value)
    {
        GameState.MusicVolme = value;
        MMAudioMixer.MusicVolume = value;
    }

    private void OnBalanceChanged(float value)
    {
        GameState.AudioBalance = value;
    }

    private void CancelAction()
    {
        var currentConfig = PlayerManager.CurrentPlayerConfig;
        currentConfig.SetAudio();

        MMAudioMixer.Volume = GameState.AudioVolume;
        MMAudioMixer.MusicVolume = GameState.MusicVolme;

        if (MenuManager.Instance != null) MenuManager.Instance.SwitchTo(MenuID.PopupOptions);
    }

    private void OkAction()
    {
        var currentConfig = PlayerManager.CurrentPlayerConfig;
        var audioConfig = currentConfig.Audio;
        audioConfig.sfxVolume = MMAudioMixer.Volume;
        audioConfig.musicVolume = MMAudioMixer.MusicVolume;
        currentConfig.Audio = audioConfig;

        currentConfig.SetAudio();
        PlayerManager.SavePlayer();

        if (MenuManager.Instance != null) MenuManager.Instance.SwitchTo(MenuID.PopupOptions);
    }

    public override void Activate()
    {
        base.Activate();

        var currentAudioSettings = PlayerManager.CurrentPlayerConfig.Audio;
        sfxVolSlider.Value = currentAudioSettings.sfxVolume;
        musicVolSlider.Value = currentAudioSettings.musicVolume;
        balanceSlider.Value = currentAudioSettings.audioBalance;
    }

    public PopupAudio() : base(MenuID.PopupAudio)
    {
        AddTitle(Localization.GetString(LocString.PopupAudioOptionsName));

        AddAudioSlider(Localization.GetString(LocString.PopupAudioSFX), out sfxVolSlider);
        AddAudioSlider(Localization.GetString(LocString.PopupAudioMusic), out musicVolSlider);
        AddAudioSlider(Localization.GetString(LocString.PopupAudioBalance), out balanceSlider);

        balanceSlider.Min = -1.0f;
        balanceSlider.Max = 1.0f;

        sfxVolSlider.OnValueChanged += OnSfxVolChanged;
        musicVolSlider.OnValueChanged += OnMusicVolChanged;
        balanceSlider.OnValueChanged += OnBalanceChanged;

        // add resume button
        AddOkCancel(OkAction, CancelAction);
    }
}
