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