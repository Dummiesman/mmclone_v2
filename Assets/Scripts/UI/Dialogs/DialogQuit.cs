using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class DialogQuit : DialogMessage
{
    private void Quit()
    {
        NativeFunctions.Exit();
    }

    public DialogQuit() : base(MenuID.Quit, "quit_dlg")
    {
        OnOk += Quit;
    }
}
