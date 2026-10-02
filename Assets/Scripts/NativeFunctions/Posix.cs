using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

internal static class Posix
{
#if UNITY_STANDALONE_OSX
    private const string LIBC = "libSystem.dylib";
#else
    private const string LIBC = "libc.so.6";
#endif

    [DllImport(LIBC, SetLastError = true)]
    private static extern int posix_spawnp(
        out int pid, IntPtr file, IntPtr fileActions, IntPtr attr, IntPtr[] argv, IntPtr[] envp);

    [DllImport(LIBC, SetLastError = true)]
    private static extern int waitpid(int pid, out int status, int options);

    private const int ENOENT = 2;
    private const int EINTR = 4;
    private const int ECHILD = 10;

    /// <summary>Returns false if the executable was not found. Blocks until exit otherwise.</summary>
    public static bool Run(string exe, string[] args, out int exitCode)
    {
        exitCode = -1;

        var owned = new List<IntPtr>();
        try
        {
            IntPtr file = Utf8(exe, owned);

            var argv = new IntPtr[args.Length + 2];
            argv[0] = file;
            for (int i = 0; i < args.Length; i++)
                argv[i + 1] = Utf8(args[i], owned);
            argv[argv.Length - 1] = IntPtr.Zero;   // required NULL terminator

            IntPtr[] envp = BuildEnvironment(owned);

            int rc = posix_spawnp(out int pid, file, IntPtr.Zero, IntPtr.Zero, argv, envp);
            if (rc == ENOENT)
                return false;
            if (rc != 0)
                return false;

            int status;
            while (waitpid(pid, out status, 0) < 0)
            {
                int err = Marshal.GetLastWin32Error();
                if (err == EINTR) continue;
                if (err == ECHILD) { exitCode = -1; return true; }  // reaped elsewhere
                return false;
            }

            bool exitedNormally = (status & 0x7F) == 0;
            exitCode = exitedNormally ? (status >> 8) & 0xFF : -1;
            return true;
        }
        finally
        {
            foreach (IntPtr p in owned)
                Marshal.FreeHGlobal(p);
        }
    }

    private static IntPtr[] BuildEnvironment(List<IntPtr> owned)
    {
        var vars = new List<IntPtr>();
        foreach (System.Collections.DictionaryEntry kv in Environment.GetEnvironmentVariables())
            vars.Add(Utf8($"{kv.Key}={kv.Value}", owned));
        vars.Add(IntPtr.Zero);
        return vars.ToArray();
    }

    private static IntPtr Utf8(string s, List<IntPtr> owned)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(s ?? string.Empty);
        IntPtr ptr = Marshal.AllocHGlobal(bytes.Length + 1);
        Marshal.Copy(bytes, 0, ptr, bytes.Length);
        Marshal.WriteByte(ptr, bytes.Length, 0);
        owned.Add(ptr);
        return ptr;
    }
}