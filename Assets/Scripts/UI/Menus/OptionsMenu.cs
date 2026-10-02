using UnityEngine;

public class OptionsMenu : UIMenu
{
    public override bool IsAnOptionMenu => true;

    public OptionsMenu() : base(MenuID.Options)
    {
        AssignBackground("opt_bk");
        AssignSwitchAudio("UIoptions");

        var aboutButton = AddBMButton("opt_abt", 0.078125f, 0.2f, 4);
        var audOptions = AddBMButton("opt_aud", 0.078125f, 0.4f, 4);
        var ctrlOptions = AddBMButton("opt_ctl", 0.078125f, 0.6f, 4);
        var gfxOptions = AddBMButton("opt_gfx", 0.078125f, 0.8f, 4);

        // not implemneted yet
        ctrlOptions.Enabled = false;
        gfxOptions.Enabled = false;

        // setup events
        audOptions.OnClick += () =>
        {
            if (MenuManager.Instance != null) MenuManager.Instance.SwitchTo(MenuID.AudioOptions);
        };
        aboutButton.OnClick += () =>
        {
            if (MenuManager.Instance != null) MenuManager.Instance.SwitchTo(MenuID.About);
        };

        // setup tooltips
        var descLabel = AddBMLabel("desc icons", 0.4844f, 0.25f, "opt_tabt|opt_taud|opt_tctl|opt_tgfx");
        SetDescriptionLabel(descLabel);
        SetupDescriptionLabelEvents(aboutButton, audOptions, ctrlOptions, gfxOptions);
    }
}
