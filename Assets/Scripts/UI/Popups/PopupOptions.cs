public class PopupOptions : PopupMenuBase
{
    private float buttonLineOffset = 0.2f;
    private float currentButtonOffset = 0.2f;
    private float buttonHeight = 0.1f;

    private TextButton AddRowButton(string name, LocString text)
    {
        var btn = AddTextButton(name, 0.0f, currentButtonOffset, 1.0f, buttonHeight, Localization.GetString(text), TextNodeEffect.CenterBoth, MenuManager.Instance.GetFont(24));
        btn.Sound = MenuSound.BeepDouble;
        currentButtonOffset += buttonLineOffset;
        return btn;
    }

    public PopupOptions() : base(MenuID.PopupOptions)
    {
        var audioBtn = AddRowButton("Options.Audio", LocString.PopupOptionsAudio);
        var controlsBtn = AddRowButton("Options.Controls", LocString.PopupOptionsControl);
        var graphicsBtn = AddRowButton("Options.Graphics", LocString.PopupOptionsGraphics);

        // events
        audioBtn.OnClick = () =>
        {
            if (MenuManager.Instance != null) MenuManager.Instance.SwitchTo(MenuID.PopupAudio);
        };
        graphicsBtn.OnClick = () =>
        {
            if (MenuManager.Instance != null) MenuManager.Instance.SwitchTo(MenuID.PopupGraphics);
        };

        controlsBtn.Enabled = false; // enable once we have the options menu ready

        // add resume button
        AddPreviousButton(MenuID.PopupMain);
    }
}
