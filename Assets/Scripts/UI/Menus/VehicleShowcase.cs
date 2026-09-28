
public class VehicleShowcase : UIMenu
{
    public override bool CreatesHistoryEntry => false;
    public override bool HasBackButton => false;
    public override bool HasNavBar => false;

    public void SetVehicle(string basename)
    {
        AssignBackground($"{basename}_show");
    }

    public VehicleShowcase() : base(MenuID.VehicleShowcase)
    {
        var doneButton = AddBMButton("host_dn", "opt_done", 0.05f, 0.9f, 4);
        doneButton.Sound = MenuSound.BeepDouble;
        doneButton.OnClick += () =>
        {
            if (MenuManager.Instance != null) MenuManager.Instance.GoBack();
        };
    }
}
