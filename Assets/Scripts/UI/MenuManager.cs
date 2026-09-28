using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class MenuManager : MonoBehaviour
{
    public static MenuManager Instance => instance;
    private static MenuManager instance;

    public Action<MenuID, UIMenu> OnMenuChanged;

    public UIMenu ActiveMenu => activeMenu;
    public UIMenu ActiveDialog => activeDialog;

    /// <summary>
    /// Draw a solid back background on the whole screen before drawing the menus
    /// </summary>
    public bool DrawSolidBackground
    {
        get => drawSolidBackground;
        set => drawSolidBackground = value;
    }

    public bool CanGoBack
    {
        get
        {
            if (history.Count == 0)
                return false;

            if(activeMenu== null)
            {
                return (history.Count > 0);
            }
            else
            {
                var currentMenuId = activeMenu.ID;
                if (history[history.Count - 1] == currentMenuId)
                {
                    return history.Count >= 2 && history[history.Count - 2] != currentMenuId;
                }
                return true;
            }
        }
    }

    // settings
    public int DropdownFontSize => 16;

    // background
    private bool drawSolidBackground = true;
    private Texture2D defaultBackground;

    // fonts
    private LocFont Font12;
    private LocFont Font14;
    private LocFont Font20;
    private LocFont Font24;
    private LocFont Font32;
    private LocFont Font48;
    private LocFont Font64;
    private LocFont Font16;

    // audio
    private AudioSource soundSource;
    
    private AudioClip clipBeepSingle;
    private AudioClip clipBeepDouble;
    private AudioClip clipGoDrive;
    private AudioClip clipSliderChange;

    // cursor
    private Texture2D cursorTexture;

    // menus storage
    private UIMenu navBarMenu = null;
    private UIMenu activeMenu = null;
    private UIMenu activeDialog = null;
    private readonly List<UIMenu> menus = new List<UIMenu>();
    private readonly List<MenuID> history = new List<MenuID>();
    private bool firstSwitch = true;

    // Event hook
    private void OnPlayerProfileChanged(PlayerData data, PlayerConfig config)
    {
        if(soundSource != null)
        {
            if(config.Audio.audioFlags.HasFlag(MMAudioFlags.SfxEnabled))
            {
                soundSource.panStereo = config.Audio.audioBalance;
                soundSource.volume = config.Audio.sfxVolume;
            }
            else
            {
                soundSource.volume = 0.0f;
            }
        }
    }

    // Menu management
    public void AddMenu(UIMenu menu)
    {
        menus.Add(menu);
    }

    public UIMenu FindMenu(MenuID id)
    {
        return menus.FirstOrDefault(menu => menu.ID == id);
    }

    public T FindMenu<T>() where T : UIMenu
    {
        return menus.FirstOrDefault(x => x is T) as T;
    }

    public void SwitchTo(MenuID id)
    {
        var menu = FindMenu(id);
        bool changing = (menu != activeMenu);

        // do the switch
        if (activeMenu != null && changing)
        {
            activeMenu.Deactivate();
        }
        activeMenu = menu;
        if (activeMenu != null && changing)
        {
            activeMenu.Activate();
        }

        // handle history and events
        if (activeMenu != null)
        {
            OnMenuChanged?.Invoke(activeMenu.ID, activeMenu);
            if (activeMenu.CreatesHistoryEntry && (history.Count == 0 || history.Last() != menu.ID))
            {
                history.Add(menu.ID);
            }
        }
        if(firstSwitch && history.Count == 0)
        {
            // always add first switch at top of history stack so nav can happen
            history.Add(menu.ID);
            firstSwitch = false;
        }
    }

    public void DeactivateMenu()
    {
        if (activeMenu != null)
        {
            activeMenu.Deactivate();
        }
        activeMenu = null;
    }

    public void GoBack()
    {
        if (!CanGoBack)
            return;

        // Only pop if the current menu is sitting on top of history
        if (activeMenu != null && history[history.Count - 1] == activeMenu.ID)
        {
            history.RemoveAt(history.Count - 1);
        }

        var target = history[history.Count - 1];
        SwitchTo(target);
    }

    // Dialogs
    public void CloseDialog()
    {
        if(activeDialog != null)
        {
            var dialog = activeDialog;
            activeDialog = null;
            dialog.Deactivate();
        }
    }

    public void ShowDialog(MenuID id)
    {
        if(activeDialog != null)
        {
            CloseDialog();
        }

        var dialog = menus.FirstOrDefault(menu => menu.ID == id);
        if(dialog != null)
        {
            activeDialog = dialog;
            dialog.Activate();
        }
    }

    // Sound
    public void PlaySound(MenuSound sound)
    {
        float volume = 0.75f * GameState.AudioVolume;
        switch(sound)
        {
            case MenuSound.BeepSingle:
                if(clipBeepSingle != null) soundSource.PlayOneShot(clipBeepSingle, volume);
                break;
            case MenuSound.BeepDouble:
                if(clipBeepDouble != null) soundSource.PlayOneShot(clipBeepDouble, volume);
                break;
            case MenuSound.GoDrive:
                if(clipGoDrive != null) soundSource.PlayOneShot(clipGoDrive, volume);
                break;
            case MenuSound.SliderChange:
                if(clipSliderChange != null) soundSource.PlayOneShot(clipSliderChange, volume);
                break;
        }
    }

    public void PlaySound(AudioClip clip)
    {
        float volume = 0.75f;
        if (clip != null) soundSource.PlayOneShot(clip, volume);
    }

    // Fonts
    public LocFont GetFont(int size)
    {
        switch (size)
        {
            case 12:
                return Font12;
            case 14:
                return Font14;
            case 20:
                return Font20;
            case 24:
                return Font24;
            case 32:
                return Font32;
            case 48:
                return Font48;
            case 64:
                return Font64;
            default:
                return Font16;
        }
    }

    // Events
    private void Update()
    {
        // exclusive updates
        if (activeDialog != null)
        {
            activeDialog.Update();
        }
        else
        {
            bool updateNavBar = true;
            if(activeMenu != null)
            {
                updateNavBar = activeMenu.HasNavBar && (activeMenu.FocusedWidget == null);
            }
            if (updateNavBar && navBarMenu != null)
            {
                navBarMenu.Update();
            }
            if (activeMenu != null)
            {
                activeMenu.Update();
            }
        }

        // input update
        if(Input.GetKeyDown(KeyCode.Escape))
        {
            if(activeDialog != null)
            {
                CloseDialog();
            }
            else if(activeMenu != null && activeMenu.ID == MenuID.Main)
            {
                ShowDialog(MenuID.Quit);
            }
            else if(CanGoBack)
            {
                GoBack();
            }
        }
    }

    private void OnGUI()
    {
        // draw over anything the game itself might put up
        GUI.depth = -500;

        if (DrawSolidBackground)
        {
            var color = GUI.color;
            GUI.color = Color.black;
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = color;
        }
        
        bool drawNavBar = true;
        if (activeMenu != null)
        {
            activeMenu.DrawBackground();
            drawNavBar = activeMenu.HasNavBar;
        }
        if (navBarMenu != null && drawNavBar)
        {
            navBarMenu.Draw();
        }
        if (activeMenu != null)
        {
            activeMenu.Draw();
        }
        if (activeDialog != null)
        {
            activeDialog.DrawBackground();
            activeDialog.Draw();
        }
        if ((Application.isEditor || !Application.isMobilePlatform) && cursorTexture != null)
        {
            float scale = Mathf.Min(Screen.width / UIConstants.ReferenceWidth, Screen.height / UIConstants.ReferenceHeight);
            Vector2 mousePos = Input.mousePosition;
            mousePos.y = Screen.height - mousePos.y;
            GUI.DrawTexture(new Rect(mousePos.x, mousePos.y, cursorTexture.width * scale, cursorTexture.height * scale), cursorTexture);
        }
    }

    private void Awake()
    {
        if (instance != null)
        {
            Debug.LogError($"WARNING: have two menu managers, destroying this one in favor of the other one", instance);
            Destroy(this);
            return;
        }
        
        // setup instance
        instance = this;

        // load widget tuning
        WidgetTuning.Load();

        // setup sounds
        var soundSourceObject = new GameObject("Sounds");
        soundSourceObject.transform.parent = this.transform;

        soundSource = soundSourceObject.AddComponent<AudioSource>();
        soundSource.spatialBlend = 0.0f;

        clipBeepSingle = AudioAssetManager.LoadClip("moveselector");
        clipBeepDouble = AudioAssetManager.LoadClip("selectionmade");
        clipGoDrive = AudioAssetManager.LoadClip("UIgo");
        clipSliderChange = AudioAssetManager.LoadClip("switch");

        // create nav bar
        navBarMenu = new NavBar();

        // load cursor
        cursorTexture = TextureLoader.Load("midcursor");
        if ((Application.isEditor || !Application.isMobilePlatform) && cursorTexture != null)
        {
            cursorTexture.wrapMode = TextureWrapMode.Clamp;
            Cursor.visible = false; // hide the OS cursor
        }

        // setup things based on the player profile and subscribe to future changes
        PlayerManager.OnActiveProfileChanged += OnPlayerProfileChanged;
        OnPlayerProfileChanged(PlayerManager.CurrentPlayer, PlayerManager.CurrentPlayerConfig);
    }

    private void OnDestroy()
    {
        foreach(var menu in menus)
        {
            menu.Dispose();
        }
        menus.Clear();

        if (defaultBackground != null) Destroy(defaultBackground);
        if (clipBeepDouble != null) Destroy(clipBeepDouble);
        if (clipBeepSingle != null) Destroy(clipBeepSingle);
        if (clipGoDrive != null) Destroy(clipGoDrive);
        if (clipSliderChange != null) Destroy(clipSliderChange);
        if (cursorTexture != null) Destroy(cursorTexture);

        PlayerManager.OnActiveProfileChanged -= OnPlayerProfileChanged;
        instance = null;
    }

    // Init
    public void LoadInterfaceFonts()
    {
        Font12 = FontLoader.LoadFont(Localization.GetString(LocString.Font_Main_ArialBold_12_12));
        Font14 = FontLoader.LoadFont(Localization.GetString(LocString.Font_Main_ArialBold_14_14));
        Font20 = FontLoader.LoadFont(Localization.GetString(LocString.Font_Main_ArialBold_18_18));
        Font24 = FontLoader.LoadFont(Localization.GetString(LocString.Font_Main_ArialBold_18_24));
        Font32 = FontLoader.LoadFont(Localization.GetString(LocString.Font_Main_ArialBold_12_16));
        Font48 = FontLoader.LoadFont(Localization.GetString(LocString.Font_Main_ArialBold_24_48));
        Font64 = FontLoader.LoadFont(Localization.GetString(LocString.Font_Main_ArialBold_32_64));
        Font16 = FontLoader.LoadFont(Localization.GetString(LocString.Font_Main_ArialBold_16_16));
    }

    public void LoadInGameFonts()
    {
        Font12 = FontLoader.LoadFont(Localization.GetString(LocString.Font_Popup_ArialBold_12_12));
        Font14 = FontLoader.LoadFont(Localization.GetString(LocString.Font_Popup_ArialBold_12_14));
        Font20 = FontLoader.LoadFont(Localization.GetString(LocString.Font_Popup_ArialBold_14_16));
        Font24 = FontLoader.LoadFont(Localization.GetString(LocString.Font_Popup_ArialBold_16_20));
        Font32 = FontLoader.LoadFont(Localization.GetString(LocString.Font_Popup_ArialBold_16_24));
        Font48 = FontLoader.LoadFont(Localization.GetString(LocString.Font_Popup_ArialBold_20_32));
        Font64 = FontLoader.LoadFont(Localization.GetString(LocString.Font_Popup_ArialBold_20_40));
        Font16 = FontLoader.LoadFont(Localization.GetString(LocString.Font_Popup_ArialBold_32_64));
    }
}
