using System.Collections;
using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

public static class StoragePermission
{
    public static bool Granted
    {
        get
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (GetSdkInt() >= 30)
                return HasManageAllFiles();
            return Permission.HasUserAuthorizedPermission(Permission.ExternalStorageRead);
#else
            return true;
#endif
        }
    }

    public static IEnumerator Request()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (Granted) yield break;

        if (GetSdkInt() >= 30)
        {
            yield return RequestManageAllFiles();
            yield break;
        }

        bool done = false;
        var cb = new PermissionCallbacks();
        cb.PermissionGranted += _ => done = true;
        cb.PermissionDenied += _ => done = true;
        cb.PermissionDeniedAndDontAskAgain += _ => done = true;
        Permission.RequestUserPermission(Permission.ExternalStorageRead, cb);

        // Callbacks don't always fire on some OEM builds, so cap the wait.
        float t = 0f;
        while (!done && t < 120f)
        {
            t += Time.unscaledDeltaTime;
            yield return null;
        }
#else
        yield break;
#endif
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    private static IEnumerator RequestManageAllFiles()
    {
        OpenManageAllFilesSettings();

        // Wait for the Settings activity to actually take focus. If it never
        // does (no activity handled the intent), bail out rather than hang.
        float t = 0f;
        while (Application.isFocused && t < 3f)
        {
            t += Time.unscaledDeltaTime;
            yield return null;
        }

        if (Application.isFocused)
        {
            Debug.LogWarning("StoragePermission: Settings screen never opened.");
            yield break;
        }

        // Now wait for the user to come back to us.
        while (!Application.isFocused)
            yield return null;

        // Give the OS a moment to commit the grant before we read it.
        yield return null;
        yield return null;
    }

    private static int GetSdkInt()
    {
        using (var version = new AndroidJavaClass("android.os.Build$VERSION"))
            return version.GetStatic<int>("SDK_INT");
    }

    private static bool HasManageAllFiles()
    {
        using (var env = new AndroidJavaClass("android.os.Environment"))
            return env.CallStatic<bool>("isExternalStorageManager");
    }

    private static void OpenManageAllFilesSettings()
    {
        try
        {
            using (var unity = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = unity.GetStatic<AndroidJavaObject>("currentActivity"))
            using (var uriClass = new AndroidJavaClass("android.net.Uri"))
            {
                string pkg = activity.Call<string>("getPackageName");
                var uri = uriClass.CallStatic<AndroidJavaObject>("parse", "package:" + pkg);

                using (var intent = new AndroidJavaObject(
                    "android.content.Intent",
                    "android.settings.MANAGE_APP_ALL_FILES_ACCESS_PERMISSION",
                    uri))
                {
                    activity.Call("startActivity", intent);
                }
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"StoragePermission: failed to open settings: {e}");
        }
    }
#endif
}