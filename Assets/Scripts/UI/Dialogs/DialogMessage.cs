using System;
using UnityEngine;

public enum DialogMessageType
{
    OkCancel,
    YesNo
}

public class DialogMessage : UIDialog
{
    private BMButton btnCancel;
    private BMButton btnOk;

    public Action OnCancel;
    public Action OnOk;

    private void CloseDialog()
    {
        if (MenuManager.Instance != null)
        {
            // close the dialog if the currently open dialog is us. 
            // the currently open dialog can be something else if the OnOk action spawned a different dialog
            if (MenuManager.Instance.ActiveDialog != null && MenuManager.Instance.ActiveDialog == this)
            {
                MenuManager.Instance.CloseDialog();
            }
        }
    }

    private void ClickedCancel()
    {
        OnCancel?.Invoke();
        CloseDialog();
    }

    private void ClickedOk()
    {
        OnOk?.Invoke();
        CloseDialog();
    }

    private void CreateButtons(DialogMessageType type)
    {
        string leftButtonImage = (type == DialogMessageType.OkCancel) ? "dlg_can" : "dlg_no";
        string rightButtonImage = (type == DialogMessageType.OkCancel) ? "dlg_ok" : "dlg_yes";

        btnCancel = AddBMButton(leftButtonImage, "popup_cancel", 0.31f, 0.04f, 4);
        btnOk = AddBMButton(rightButtonImage, "popup_ok", 0.49f, 0.04f, 4);

        // dlg_can is sometimes dlg_cancel
        if (WidgetTuning.RetrieveWidgetData((int)this.ID, "dlg_cancel", out var tunedRect))
        {
            var buttonHitArea = btnCancel.HitRegionSize;
            var menuCoords = new Rect(tunedRect.x, tunedRect.y, buttonHitArea.x, buttonHitArea.y);
            btnCancel.Rect = MenuToWidgetCoords(menuCoords);
        }

        // dlg_ok is sometimes dlg_done
        if (WidgetTuning.RetrieveWidgetData((int)this.ID, "dlg_done", out tunedRect))
        {
            var buttonHitArea = btnOk.HitRegionSize;
            var menuCoords = new Rect(tunedRect.x, tunedRect.y, buttonHitArea.x, buttonHitArea.y);
            btnOk.Rect = MenuToWidgetCoords(menuCoords);
        }

        btnCancel.Sound = MenuSound.BeepDouble;
        btnOk.Sound = MenuSound.BeepDouble;

        btnCancel.OnClick += ClickedCancel;
        btnOk.OnClick += ClickedOk;
    }

    public DialogMessage(MenuID id, string background, DialogMessageType type, bool hasCancelButton) : base(id)
    {
        AssignBackground(background);
        CreateButtons(type);

        btnCancel.Visible = hasCancelButton;
    }

    public DialogMessage(MenuID id, string background, bool hasCancelButton) : this(id, background, DialogMessageType.OkCancel, hasCancelButton)
    {
    }

    public DialogMessage(MenuID id, string background) : this(id, background, true)
    {
    }

    public DialogMessage(MenuID id, string background, DialogMessageType type) : this(id, background, type, true)
    {
    }
}
