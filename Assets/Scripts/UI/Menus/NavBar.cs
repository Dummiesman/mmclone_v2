using UnityEngine;

public class NavBar : UIMenu
{
    private BMButton previousButton;
    private BMButton optionsButton;
    private BMButtonGroup dummyGroup; // used for options

    private void OnMenuChanged(MenuID id, UIMenu menu)
    {
        if(MenuManager.Instance != null)
        {
            bool visible = ((menu?.HasBackButton ?? true) && MenuManager.Instance.CanGoBack);
            previousButton.Visible = visible;
            dummyGroup.ActiveButton = (menu.IsAnOptionMenu) ? optionsButton : null;
        }
        else
        {
            previousButton.Visible = false;
            dummyGroup.ActiveButton = null;
        }
    }

    private void GoBack()
    {
        if(MenuManager.Instance != null)
        {
            MenuManager.Instance.GoBack();
        }
    }

    public NavBar() : base(MenuID.NavBar)
    {
        var optionsButton = AddBMButton("mnav_opt", "mnav_opt", 0.65f, 0.005f, 5);
        var helpButton = AddBMButton("mnav_hlp", "mnav_help", 0.65f, 0.005f, 3);
        var minimizeButton = AddBMButton("mnav_sto", "mnav_stow", 0.65f, 0.005f, 3);
        var exitButton = AddBMButton("mnav_ext", "mnav_exit", 0.65f, 0.005f, 3);
        
        // setup nav bar events
        optionsButton.Sound = MenuSound.BeepDouble;
        helpButton.Sound = MenuSound.BeepDouble;
        minimizeButton.Sound = MenuSound.BeepDouble;
        exitButton.Sound = MenuSound.BeepDouble;

        exitButton.OnClick += () =>
        {
            if (MenuManager.Instance != null) MenuManager.Instance.ShowDialog(MenuID.Quit);
        };
        minimizeButton.OnClick += () =>
        {
            NativeFunctions.Minimize();
        };

        // setup previous button
        var previousButton = AddBMButton("mnav_prv", "mnav_prev", 0.9f, 0.9f, 4);
        previousButton.OnClick += GoBack;
        previousButton.Visible = false;
        previousButton.Sound = MenuSound.BeepDouble;

        // setup options button
        dummyGroup = new BMButtonGroup();
        dummyGroup.Buttons.Add(optionsButton);
        optionsButton.OnClick += () =>
        {
            if (MenuManager.Instance != null) MenuManager.Instance.SwitchTo(MenuID.Options);
        };
        optionsButton.Group = dummyGroup;

        this.previousButton = previousButton;
        this.optionsButton = optionsButton;

        if (MenuManager.Instance != null)
        {
            MenuManager.Instance.OnMenuChanged += OnMenuChanged;
        }
    }
}
