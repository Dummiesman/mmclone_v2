#if UNITY_ANDROID && !UNITY_EDITOR
using System.Threading;
using System;
using UnityEngine;

static class AndroidNative
{
    class ClickListener : AndroidJavaProxy
    {
        readonly Action<int> _cb;
        public ClickListener(Action<int> cb)
            : base("android.content.DialogInterface$OnClickListener") { _cb = cb; }
        void onClick(AndroidJavaObject dialog, int which) { _cb(which); }
    }

    public static MessageBoxResult MessageBox(string title, string message, MessageBoxButtons buttons)
    {
        int which = 0;
        using (var gate = new ManualResetEventSlim(false))
        using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
        using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
        {
            activity.Call("runOnUiThread", new AndroidJavaRunnable(() =>
            {
                var builder = new AndroidJavaObject("android.app.AlertDialog$Builder", activity);
                builder.Call<AndroidJavaObject>("setTitle", title);
                builder.Call<AndroidJavaObject>("setMessage", message);
                builder.Call<AndroidJavaObject>("setCancelable", false);

                var onClick = new ClickListener(w => { which = w; gate.Set(); });

                switch (buttons)
                {
                    case MessageBoxButtons.Ok:
                        builder.Call<AndroidJavaObject>("setPositiveButton", "OK", onClick);
                        break;
                    case MessageBoxButtons.OkCancel:
                        builder.Call<AndroidJavaObject>("setPositiveButton", "OK", onClick);
                        builder.Call<AndroidJavaObject>("setNegativeButton", "Cancel", onClick);
                        break;
                    case MessageBoxButtons.YesNo:
                        builder.Call<AndroidJavaObject>("setPositiveButton", "Yes", onClick);
                        builder.Call<AndroidJavaObject>("setNegativeButton", "No", onClick);
                        break;
                }

                builder.Call<AndroidJavaObject>("show");
            }));

            gate.Wait();                  // <- the blocking part
        }

        bool accepted = which == -1;      // BUTTON_POSITIVE
        if (buttons == MessageBoxButtons.YesNo)
            return accepted ? MessageBoxResult.Yes : MessageBoxResult.No;
        return accepted ? MessageBoxResult.Ok : MessageBoxResult.Cancel;
    }
}
#endif