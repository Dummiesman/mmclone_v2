using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class DialogNewPlayer : UIDialog
{
    private BMButtonGroup buttonGroup;
    private UITextField driverNameField;
    private bool lastWasDuplicate = false;

    public override void Activate()
    {
        base.Activate();
        SetWidgetFocus(driverNameField);
        if (!lastWasDuplicate)
        {
            driverNameField.Text = string.Empty;
            buttonGroup.ActiveButtonIndex = 0;
        }
        lastWasDuplicate = false;
    }

    private void CreateDriverAction()
    {
        string name = driverNameField.Text.Trim();
        if(string.IsNullOrWhiteSpace(name))
        {
            // just close
            if (MenuManager.Instance != null) MenuManager.Instance.CloseDialog();
            return;
        }

        bool alreadyExists = (PlayerManager.FindPlayer(name) >= 0);
        if(alreadyExists)
        {
            lastWasDuplicate = true;
            if (MenuManager.Instance != null) MenuManager.Instance.ShowDialog(MenuID.DuplicatePlayerNameDialog);
        }
        else
        {
            // create the new player and switch to it
            MMSkillLevel skillLevel = (buttonGroup.ActiveButtonIndex == 0) ? MMSkillLevel.Amateur : MMSkillLevel.Professional;
            PlayerManager.CreatePlayer(name, skillLevel);
            PlayerManager.SetActivePlayer(PlayerManager.FindPlayer(name));

            // close
            if (MenuManager.Instance != null) MenuManager.Instance.CloseDialog();
        }
    }

    private void CancelAction()
    {
        if (MenuManager.Instance != null) MenuManager.Instance.CloseDialog();
    }

    public DialogNewPlayer() : base(MenuID.NewPlayerDialog)
    {
        AssignBackground("newp_dlg");

        driverNameField = AddTextField("Enter New Driver Name", 0.05f, 0.11f, 0.8f, 0.06666667f);
        driverNameField.MaxLength = 18;

        // add difficulty selections
        var amateurButton = AddBMButton("checkbox", 0.05f, 0.31f, 5);
        var proButton = AddBMButton("checkbox", 0.05f, 0.31f, 5);
        amateurButton.Sound = MenuSound.BeepDouble;
        proButton.Sound = MenuSound.BeepDouble;

        // hack for now because shared name
        var amateurLoc = MenuToWidgetCoords(new Rect(72, 161, 0, 0));
        var proLoc =     MenuToWidgetCoords(new Rect(72, 211, 0, 0));
        amateurButton.Rect = new Rect(amateurLoc.position, amateurButton.Rect.size);
        proButton.Rect = new Rect(proLoc.position, proButton.Rect.size);

        // setup button group
        buttonGroup = new BMButtonGroup();
        buttonGroup.Buttons.Add(amateurButton);
        buttonGroup.Buttons.Add(proButton);
        amateurButton.Group = buttonGroup;
        proButton.Group = buttonGroup;
        buttonGroup.ActiveButton = amateurButton;

        // add ok/cancel button
        var cancelButton = AddBMButton("dlg_can", "popup_cancel", 0.65f, 0.75f, 4);
        var doneButton = AddBMButton("dlg_done", "popup_done", 0.15f, 0.75f, 4);
        cancelButton.Sound = MenuSound.BeepDouble;
        doneButton.Sound = MenuSound.BeepDouble;

        cancelButton.OnClick += CancelAction;
        doneButton.OnClick += CreateDriverAction;
    }
}
