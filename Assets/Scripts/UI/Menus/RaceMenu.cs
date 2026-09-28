
public class RaceMenu : RaceMenuBase
{
    public RaceMenu() : base(MenuID.RaceMenu, false)
    {
        AssignBackground("race_bk");

        // vehicles button
        var vehiclesButton = AddBMButton("race_veh", 1.0f, 1.0f, 4);
        vehiclesButton.OnClick += () =>
        {
            SetGameState();
            if (MenuManager.Instance != null) MenuManager.Instance.SwitchTo(MenuID.VehicleSelect);
        };
    }
}
