using System;
using UnityEngine;

public static class NativeFunctions
{
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