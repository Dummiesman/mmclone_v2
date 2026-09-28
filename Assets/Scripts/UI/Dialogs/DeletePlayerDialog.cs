using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class DeletePlayerDialog : DialogMessage
{
    private void OnOkAction()
    {
        // this should never happen as this dialog should never be launched
        // with just one player, but just in case
        if(PlayerManager.PlayerCount > 1)
        {
            int index = PlayerManager.FindPlayer(PlayerManager.CurrentPlayer.Name);
            PlayerManager.DeletePlayer(index);
        }
    }

    public DeletePlayerDialog() : base(MenuID.DeletePlayerDialog, "delp_dlg", DialogMessageType.YesNo)
    {
        OnOk += OnOkAction;
    }
}
