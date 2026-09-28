public class CrashCourseIntro : UIMenu
{
    public CrashCourseIntro() : base(MenuID.CrashCourseIntro)
    {
        AssignBackground("ilon_bk");
        AssignSwitchAudio("UIraces");

        var btnLondon = AddBMButton("cci_lon", 0.078125f, 0.2f, 4);
        var btnSf = AddBMButton("cci_sf", 0.078125f, 0.2f, 4);

        // setup events
        btnLondon.OnClick += () =>
        {
            GameState.SelectedCity = "london";
            GameState.SelectedGameMode = MMGameMode.CrashCourse;
            if (MenuManager.Instance != null) MenuManager.Instance.SwitchTo(MenuID.CrashCourse);
        };
        btnSf.OnClick += () =>
        {
            GameState.SelectedCity = "sf";
            GameState.SelectedGameMode = MMGameMode.CrashCourse;
            if (MenuManager.Instance != null) MenuManager.Instance.SwitchTo(MenuID.CrashCourse);
        };

        // setup sounds
        btnLondon.Sound = MenuSound.BeepDouble;
        btnSf.Sound = MenuSound.BeepDouble;
    }
}
