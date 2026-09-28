using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public class ViewportManager : MonoBehaviour
{
    public static bool UseVertexLitOnMobile = false;
    public static bool RenderManually = false;

    [Flags]
    public enum ViewportApplyFlags
    {
        None = 0,
        Rect = 1,
        ClearColor = 2,
        Depth = 4,
        Clip = 8,
        ClearFlags = 16,
        CullMask = 32,
        SortAxis = 64,
        Aspect = 128,
        All = Rect | ClearColor | Depth | Clip | ClearFlags | CullMask  | SortAxis | Aspect
    }

    public class Viewport
    {
        public string Name;
        public Rect ScreenRect;
        public Rect PixelRect
        {
            get
            {
                return new Rect(ScreenRect.x * Screen.width, ScreenRect.y * Screen.width, ScreenRect.width * Screen.width, ScreenRect.height * Screen.height);
            }
        }

        public int LayerMask;
        public IReadOnlyList<Camera> Cameras => ownedCameras;
        public Color ClearColor = Color.blue;
        public int DefaultDepth;
        public float NearClip = 0.1f;
        public float FarClip = 1000f;
        public Action<Viewport> OnActiveCamChangeCB;
        public CameraClearFlags ClearFlags = CameraClearFlags.Color;
        public Camera ActiveCamera { private set; get; }
        public bool UseAudioListener = false;

        public bool UseCustomSortAxis = false;
        public Vector3 CustomSortAxis;

        public bool UseAspect = false;
        public float Aspect = 1.0f;

        private bool _transitioning = false;
        private CameraTransition _currentTransition;
        private readonly HashSet<int> ownedInstanceIds = new HashSet<int>();
        private readonly List<Camera> ownedCameras = new List<Camera>();

        public void Render()
        {
            if (ActiveCamera != null)
                ActiveCamera.Render();
        }

        public void Destroy()
        {
            foreach (var cam in ownedCameras)
            {
                if (cam != null)
                    UnityEngine.Object.Destroy(cam.gameObject);
            }

            ownedCameras.Clear();
            ownedInstanceIds.Clear();
            ActiveCamera = null;
        }

        public void RemoveCamera(Camera remove)
        {
            if (!OwnsCamera(remove))
            {
                Debug.LogError(
                    $"Cannot remove camera {remove.gameObject.name}. It's not a member of the {Name} viewport.");
                return;
            }

            if (ActiveCamera == remove)
                ActiveCamera = null;

            ownedInstanceIds.Remove(remove.GetInstanceID());
            ownedCameras.Remove(remove);
            UnityEngine.Object.Destroy(remove.gameObject);
        }

        public void ApplySettingsToCamera(Camera camera, ViewportApplyFlags flags)
        {
            if ((flags & ViewportApplyFlags.ClearFlags) != 0)
                camera.clearFlags = this.ClearFlags;
            if ((flags & ViewportApplyFlags.Clip) != 0)
            {
                camera.nearClipPlane = this.NearClip;
                camera.farClipPlane = this.FarClip;
            }
            if ((flags & ViewportApplyFlags.CullMask) != 0)
                camera.cullingMask = this.LayerMask;
            if ((flags & ViewportApplyFlags.ClearColor) != 0)
                camera.backgroundColor = this.ClearColor;
            if ((flags & ViewportApplyFlags.Rect) != 0)
                camera.rect = this.ScreenRect;
            if ((flags & ViewportApplyFlags.Depth) != 0)
                camera.depth = this.DefaultDepth;
            if((flags & ViewportApplyFlags.SortAxis) != 0)
            {
                camera.transparencySortAxis = (UseCustomSortAxis) ? CustomSortAxis : GraphicsSettings.transparencySortAxis;
                camera.transparencySortMode = (UseCustomSortAxis) ? TransparencySortMode.CustomAxis : GraphicsSettings.transparencySortMode;
            }
            if((flags & ViewportApplyFlags.Aspect) != 0)
            {
                if(UseAspect)
                {
                    camera.aspect = Aspect;
                }
                else
                {
                    camera.ResetAspect();
                }
            }
            if (Application.isMobilePlatform && UseVertexLitOnMobile)
                camera.renderingPath = RenderingPath.VertexLit;
        }

        public void ApplySettingsToAllCameras(ViewportApplyFlags flags)
        {
            foreach (var cmra in ownedCameras)
            {
                ApplySettingsToCamera(cmra, flags);
            }
        }

        public Camera AddCamera()
        {
            var cam = new GameObject($"Viewport{Name}Camera").AddComponent<Camera>();
            ApplySettingsToCamera(cam, ViewportApplyFlags.All);
            cam.enabled = false;

            ownedInstanceIds.Add(cam.GetInstanceID());
            ownedCameras.Add(cam);
            return cam;
        }

        public void AddCamera(Camera camera, ViewportApplyFlags applyFlags)
        {
            foreach (var vp in ViewportManager.Viewports.Values)
            {
                if (vp.OwnsCamera(camera))
                    throw new Exception($"This camera is already owned by viewport {vp.Name}.");
            }

            camera.enabled = false;
            ApplySettingsToCamera(camera, applyFlags);
            ownedInstanceIds.Add(camera.GetInstanceID());
            this.ownedCameras.Add(camera);
        }

        public void AddCamera(Camera camera)
        {
            AddCamera(camera, ViewportApplyFlags.All);
        }

        public void SwapDisplayArea(Viewport other)
        {
            Rect tempRect = ScreenRect;
            ScreenRect = other.ScreenRect;
            other.ScreenRect = tempRect;

            int tempDepth = DefaultDepth;
            DefaultDepth = other.DefaultDepth;
            other.DefaultDepth = tempDepth;

            foreach (var camera in ownedCameras)
            {
                camera.rect = ScreenRect;
                camera.depth = DefaultDepth;
            }

            foreach (var camera in other.ownedCameras)
            {
                camera.rect = other.ScreenRect;
                camera.depth = other.DefaultDepth;
            }
        }

        public bool OwnsCamera(Camera camera)
        {
            return ownedInstanceIds.Contains(camera.GetInstanceID());
        }

        private void SetActiveCameraInternal(Camera newActive)
        {
            if (!OwnsCamera(newActive))
            {
                Debug.LogError(
                    $"Cannot set {newActive.gameObject.name} as active camera for the viewport {Name}. It's not a member of the viewport.");
                return;
            }


            //disable old audio listener
            if (ActiveCamera != null)
            {
                if (UseAudioListener)
                {
                    var listener = ActiveCamera.gameObject.GetComponent<AudioListener>();
                    if (listener != null)
                        listener.enabled = false;
                }
                ActiveCamera.enabled = false;
            }

            //enable new audio listener
            if (UseAudioListener)
            {
                AudioListener newListener = newActive.GetComponent<AudioListener>();
                if (newListener != null)
                {
                    newListener.enabled = true;
                }
                else
                {
                    newActive.gameObject.AddComponent<AudioListener>();
                }
            }

            if (!newActive.enabled && !RenderManually)
                newActive.enabled = true;

            ActiveCamera = newActive;
            OnActiveCamChangeCB?.Invoke(this);
        }

        private void DestroyActiveTransition()
        {
            if (_transitioning)
            {
                RemoveCamera(_currentTransition.GetComponent<Camera>());
                UnityEngine.Object.Destroy(_currentTransition);
                _transitioning = false;
                _currentTransition = null;
            }
        }

        public void DeactivateAllCameras()
        {
            foreach (var camera in ownedCameras)
            {
                if (camera != null)
                    camera.enabled = false;
            }

            ActiveCamera = null;
        }

        public void SetActiveCamera(Camera newActive)
        {
            //kill active transitions
            DestroyActiveTransition();
            SetActiveCameraInternal(newActive);
        }

        public void TransitionToCamera(Camera other, float time)
        {
            //create camera
            Camera transitionCamera = AddCamera();
            CameraTransition transition = transitionCamera.gameObject.AddComponent<CameraTransition>();

            //setup transition camera
            transitionCamera.transform.position = ActiveCamera.transform.position;
            transitionCamera.transform.rotation = ActiveCamera.transform.rotation;
            transitionCamera.fieldOfView = ActiveCamera.fieldOfView;

            //setup transition
            if (_transitioning)
            {
                transition.Init(_currentTransition.FromCamera, other,
                    _currentTransition.Camera.transform.position, _currentTransition.Camera.transform.rotation,
                    time - (time * _currentTransition.Progress));
                DestroyActiveTransition();
            }
            else
            {
                transition.Init(ActiveCamera, other, time);
            }

            //set current transition
            _transitioning = true;
            _currentTransition = transition;

            //callback
            transition.OnCompletedCb = () =>
            {
                SetActiveCameraInternal(other);
                RemoveCamera(transitionCamera);
                _transitioning = false;
                _currentTransition = null;
            };

            //move to transition camera
            SetActiveCameraInternal(transitionCamera);
        }

        /// <summary>
        /// Sets the rect of this camera scaled to the rect of the viewport
        /// </summary>
        public void SetCameraRect(Camera camera, Rect rect)
        {
            var cRect = new Rect(this.ScreenRect.x + (rect.x * this.ScreenRect.width),
                                 this.ScreenRect.y + (rect.y * this.ScreenRect.height),
                                 rect.width * this.ScreenRect.width, rect.height * this.ScreenRect.height);
            camera.rect = cRect;
        }
    }

    //
    private static Dictionary<string, Viewport> viewports = new Dictionary<string, Viewport>();

    public static Viewport MainViewport { get; private set; }

    public static IReadOnlyDictionary<string, Viewport> Viewports => viewports;

    public static void Clear()
    {
        foreach (var viewport in viewports.Values)
        {
            viewport.Destroy();
        }
        viewports.Clear();
    }

    public static Viewport GetViewport(string name)
    {
        if (viewports.TryGetValue(name.ToLowerInvariant(), out Viewport result))
        {
            return result;
        }
        return null;
    }

    public static Viewport AddViewport(string name, Rect rect, int layerMask)
    {
        var vp = new Viewport { LayerMask = layerMask, ScreenRect = rect, Name = name };
        viewports[name.ToLowerInvariant()] = vp;

        if (name.ToUpperInvariant() == "MAIN")
        {
            MainViewport = vp;
            vp.UseAudioListener = true;
        }

        return vp;
    }

    public static void DeleteViewport(string name)
    {
        if (viewports.TryGetValue(name.ToLowerInvariant(), out Viewport result))
        {
            result.Destroy();
            viewports.Remove(name);
        }
        else
        {
            Debug.LogError($"DeleteViewport({name}) failed: Viewport doesn't exist.");
        }
    }

    public static void DeleteViewport(Viewport viewport)
    {
        DeleteViewport(viewport.Name);
    }


    //Mono bits
    private bool renderedThisFrame = false;
    public static float RenderTime { get; private set; }

    void Awake()
    {
        //viewport manager is persistent
        DontDestroyOnLoad(this.gameObject);
    }

    void OnRenderObject()
    {
        if (!RenderManually || renderedThisFrame)
            return;
        renderedThisFrame = true;
#if UNITY_EDITOR

#endif
        //render cameras
        float preRenderTime = Time.time;
        foreach (var kvp in Viewports)
        {
            var vp = kvp.Value;
            vp.Render();
        }

        float postRenderTime = Time.time;
        RenderTime = postRenderTime - preRenderTime;
        if (RenderTime > 0f)
        {
            Debug.Log($"Render Time {RenderTime}");
        }
    }

    void Update()
    {
        //manual rendering
        if (!RenderManually)
            return;
        renderedThisFrame = false;
    }
}
