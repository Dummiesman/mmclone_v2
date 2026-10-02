using System;
using UnityEngine;

public enum MessageBoxButtons { Ok, OkCancel, YesNo }
public enum MessageBoxIcon { None, Info, Warning, Error, Question }
public enum MessageBoxResult { Ok, Cancel, Yes, No }

public static class NativeFunctions
{
    public static MessageBoxResult MessageBox(
    string title,
    string message,
    MessageBoxButtons buttons = MessageBoxButtons.Ok,
    MessageBoxIcon icon = MessageBoxIcon.None)
    {
#if UNITY_EDITOR
        if (buttons == MessageBoxButtons.Ok)
        {
            UnityEditor.EditorUtility.DisplayDialog(title, message, "OK");
            return MessageBoxResult.Ok;
        }
        bool ok = UnityEditor.EditorUtility.DisplayDialog(
            title, message,
            buttons == MessageBoxButtons.YesNo ? "Yes" : "OK",
            buttons == MessageBoxButtons.YesNo ? "No" : "Cancel");
        if (buttons == MessageBoxButtons.YesNo)
            return ok ? MessageBoxResult.Yes : MessageBoxResult.No;
        return ok ? MessageBoxResult.Ok : MessageBoxResult.Cancel;

#elif UNITY_STANDALONE_WIN
        uint flags = buttons switch
        {
            MessageBoxButtons.OkCancel => Win32.MB_OKCANCEL,
            MessageBoxButtons.YesNo    => Win32.MB_YESNO,
            _                          => Win32.MB_OK
        };
        flags |= icon switch
        {
            MessageBoxIcon.Info     => Win32.MB_ICONINFORMATION,
            MessageBoxIcon.Warning  => Win32.MB_ICONWARNING,
            MessageBoxIcon.Error    => Win32.MB_ICONERROR,
            MessageBoxIcon.Question => Win32.MB_ICONQUESTION,
            _                       => 0u
        };
        flags |= Win32.MB_TOPMOST;

        int id = Win32.MessageBox(Win32.GetActiveWindow(), message, title, flags);
        return id switch
        {
            Win32.IDYES => MessageBoxResult.Yes,
            Win32.IDNO  => MessageBoxResult.No,
            Win32.IDOK  => MessageBoxResult.Ok,
            _           => MessageBoxResult.Cancel
        };

#elif UNITY_STANDALONE_LINUX
        return LinuxNative.MessageBox(title, message, buttons);

#elif UNITY_ANDROID
        return AndroidNative.MessageBox(title, message, buttons);

#else
        Debug.LogWarning($"{title}: {message}");
        return MessageBoxResult.Ok;
#endif
    }

public static void Minimize()
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        IntPtr hwnd = Win32.GetActiveWindow();

        if (hwnd == IntPtr.Zero)
            return;

        Win32.ShowWindow(hwnd, Win32.ShowWindowCommands.Minimize);

#elif UNITY_STANDALONE_LINUX && !UNITY_EDITOR
        LinuxNative.Minimize();

#elif UNITY_ANDROID && !UNITY_EDITOR
        using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
        using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
        {
            activity.Call<bool>("moveTaskToBack", true);
        }
#endif
    }

    public static void Exit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}