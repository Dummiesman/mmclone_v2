using System.Collections;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class FileSystemWatcher : MonoBehaviour
{
    public static FileSystemWatcher Instance => instance;
    private static FileSystemWatcher instance;

    private void Dispose()
    {
        if (FileSystem.Initialized)
            FileSystem.VFS.Dispose();
    }

    private void OnBeforeAssemblyReload()
    {
        Debug.Log("FileSystemWatcher::OnBeforeAssemblyReload() - Disposing of VFS file systems.");
        Dispose();
    }

    private void Awake()
    {
        instance = this;
        DontDestroyOnLoad(this.gameObject);
#if UNITY_EDITOR
        AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
#endif
    }

    private void OnDestroy()
    {
#if UNITY_EDITOR
        AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeAssemblyReload;
#endif
    }

    void OnApplicationQuit()
    {
        Debug.Log("FileSystemWatcher::OnApplicationQuit() - Disposing of VFS file systems.");
        Dispose();
    }
}