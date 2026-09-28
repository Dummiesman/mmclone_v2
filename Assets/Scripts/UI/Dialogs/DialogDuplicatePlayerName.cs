using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class DialogDuplicatePlayerName : DialogMessage
{
    public override void Deactivate()
    {
        base.Deactivate();

        // hook this on deactivate so both escape (close dialog) and ok (close dialog) return to the new player dialog
        if (MenuManager.Instance != null) MenuManager.Instance.ShowDialog(MenuID.NewPlayerDialog);
    }

    public DialogDuplicatePlayerName() : base(MenuID.DuplicatePlayerNameDialog, "dupp_dlg", false)
    {
    }
}
