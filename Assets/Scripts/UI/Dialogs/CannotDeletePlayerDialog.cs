using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CannotDeletePlayerDialog : DialogMessage
{
    public CannotDeletePlayerDialog() : base(MenuID.CannotDeleteLastPlayerDialog, "lstp_dlg", false)
    {
    }
}
