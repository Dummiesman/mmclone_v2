#if UNITY_STANDALONE_LINUX && !UNITY_EDITOR

using System;
using System.Runtime.InteropServices;

public static class LinuxNative
{
    [DllImport("libX11.so.6")]
    private static extern IntPtr XOpenDisplay(IntPtr display);

    [DllImport("libX11.so.6")]
    private static extern int XCloseDisplay(IntPtr display);

    [DllImport("libX11.so.6")]
    private static extern IntPtr XGetInputFocus(
        IntPtr display,
        out IntPtr focus,
        out int revertTo);

    [DllImport("libX11.so.6")]
    private static extern int XIconifyWindow(
        IntPtr display,
        IntPtr window,
        int screen);

    [DllImport("libX11.so.6")]
    private static extern int XDefaultScreen(IntPtr display);

    public static MessageBoxResult MessageBox(string title, string message, MessageBoxButtons buttons)
    {
        int code;

        if (Posix.Run("zenity", ZenityArgs(title, message, buttons), out code))
            return Map(code == 0, buttons);

        if (Posix.Run("kdialog", KDialogArgs(title, message, buttons), out code))
            return Map(code == 0, buttons);

        if (Posix.Run("xmessage", XMessageArgs(message, buttons), out code))
            return Map(code == 100, buttons);

        UnityEngine.Debug.LogWarning($"No dialog backend available. {title}: {message}");
        return buttons == MessageBoxButtons.YesNo ? MessageBoxResult.No : MessageBoxResult.Cancel;
    }

    private static string[] ZenityArgs(string t, string m, MessageBoxButtons b) => b switch
    {
        MessageBoxButtons.Ok =>
            new[] { "--info", $"--title={t}", $"--text={m}" },
        MessageBoxButtons.OkCancel =>
            new[] { "--question", "--ok-label=OK", "--cancel-label=Cancel", $"--title={t}", $"--text={m}" },
        _ =>
            new[] { "--question", "--ok-label=Yes", "--cancel-label=No", $"--title={t}", $"--text={m}" }
    };

    private static string[] KDialogArgs(string t, string m, MessageBoxButtons b) => b == MessageBoxButtons.Ok
        ? new[] { "--title", t, "--msgbox", m }
        : new[] { "--title", t, "--yesno", m };

    private static string[] XMessageArgs(string m, MessageBoxButtons b) => new[]
    {
        "-center", "-buttons", b == MessageBoxButtons.Ok ? "OK:100" : "OK:100,Cancel:101", m
    };

    private static MessageBoxResult Map(bool accepted, MessageBoxButtons b)
    {
        if (b == MessageBoxButtons.YesNo)
            return accepted ? MessageBoxResult.Yes : MessageBoxResult.No;
        return accepted ? MessageBoxResult.Ok : MessageBoxResult.Cancel;
    }

    public static void Minimize()
    {
        IntPtr display = XOpenDisplay(IntPtr.Zero);

        if (display == IntPtr.Zero)
            return;

        try
        {
            IntPtr window;
            int revertTo;

            XGetInputFocus(display, out window, out revertTo);

            if (window == IntPtr.Zero)
                return;

            XIconifyWindow(
                display,
                window,
                XDefaultScreen(display));
        }
        finally
        {
            XCloseDisplay(display);
        }
    }
}

#else

using System;

public static class LinuxNative
{
    public static void Minimize()
    {
        throw new NotImplementedException();
    }
}

#endif