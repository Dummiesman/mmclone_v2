using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerLimitReachedDialog : DialogMessage
{
    public PlayerLimitReachedDialog() : base(MenuID.PlayerLimitReachedDialog, "plim_dlg", false)
    {
    }
}
