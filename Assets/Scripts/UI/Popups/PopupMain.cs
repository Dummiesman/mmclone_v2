using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class PopupMain : PopupMenuBase
{
    private float buttonLineOffset = 0.125f;
    private float currentButtonOffset = 0.125f;
    private float buttonHeight = 0.1f;

    private TextButton AddRowButton(string name, LocString text)
    {
        var btn = AddTextButton(name, 0.0f, currentButtonOffset, 1.0f, buttonHeight, Localization.GetString(text), TextNodeEffect.CenterBoth, MenuManager.Instance.GetFont(24));
        btn.Sound = MenuSound.BeepDouble;
        currentButtonOffset += buttonLineOffset;
        return btn;
    }

    public PopupMain(MMGame game) : base(MenuID.PopupMain)
    {
        var restartBtn = AddRowButton("Main.Restart", LocString.PopupMainRestart);
        var optionsBtn = AddRowButton("Main.Options", LocString.PopupMainOptions);
        var quitToMenuBtn = AddRowButton("Main.QuitToMenu", LocString.PopupMainRaceMenu);
        var exitToDesktopBtn = AddRowButton("Main.ExitToDesktop", LocString.PopupMainExit);

        // events
        restartBtn.OnClick = () =>
        {
            game.Reset();
        };
        quitToMenuBtn.OnClick += () =>
        {
            GameState.EnterMenu();
        };
        exitToDesktopBtn.OnClick += () =>
        {
            NativeFunctions.Exit();
        };
        optionsBtn.OnClick += () =>
        {
            if (MenuManager.Instance != null) MenuManager.Instance.SwitchTo(MenuID.PopupOptions);
        };

        // add resume button
        AddExit();
    }
}
