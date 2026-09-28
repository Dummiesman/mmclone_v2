using System.Collections.Generic;
using UnityEngine;

public class Hudmap : MonoBehaviour
{
    //const
    private const float ITEM_SCALE = 3f;
    private const ScreenSide DEFAULT_SCREEN_SIDE = ScreenSide.Right;

    //normal (small) map placement, as anchored to the right edge
    private static readonly Rect normalRectRight = new Rect(0.7794f, 0.0131f, 0.2022f, 0.2369f);

    //private stuff
    private FollowTarget TargetFollower;
    private bool followTargetRotation = false;
    private Camera mapCamera;
    private GameObject mapModel;

    private List<HudmapItem> items = new List<HudmapItem>();

    private bool followRotation = false;
    private bool zoomedIn = false;
    private float zoomLevel = 0f;

    private float zoomOutDist = 1200f / 2f;
    private float zoomInDist = 575f / 2f;
    private float zoomRate = 1.2f;
    private float targetZoomLevel = 0f;
    private float iconScaleMin = 34f;
    private float iconScaleMax = 52.4f;

    private MapState curMapState = 0;
    private bool isFullscreen = false;
    private ScreenSide curScreenSide = DEFAULT_SCREEN_SIDE;

    public enum MapState : int
    {
        Normal = 0,
        Half = 1,
        Off = 2
    }

    public enum ScreenSide : int
    {
        Left = 0,
        Right = 1
    }

    //API
    /// <summary>
    /// Instantly apply the zoom level to the camera, skipping the animation
    /// </summary>
    public void ApplyZoomLevel()
    {
        mapCamera.orthographicSize = Mathf.Lerp(zoomOutDist, zoomInDist, targetZoomLevel);
    }

    public void SetZoom(bool zoom)
    {
        targetZoomLevel = (zoom) ? 1f : 0f;
        zoomedIn = zoom;
    }

    public void ToggleZoom()
    {
        SetZoom(!zoomedIn);
    }

    public void SetFollowRotation(bool rotate)
    {
        followRotation = rotate;
        TargetFollower.RotationAxes.y = false; // FollowTarget no longer handles rotation
        if (!rotate)
            mapCamera.transform.localEulerAngles = new Vector3(90, 0, 180); // reset to north-fixed
    }

    public void ToggleFollowRotation()
    {
        SetFollowRotation(!followRotation);
    }

    /// <summary>
    /// Which edge the small (Normal) map is anchored to. Ignored in Half/Off.
    /// </summary>
    public void SetScreenSide(ScreenSide side)
    {
        if (curScreenSide == side)
            return;

        curScreenSide = side;

        //only the small map is edge-anchored; Half/Off are full width.
        //while fullscreen the viewports are swapped, so the new side is picked up on restore.
        if (curMapState == MapState.Normal && !isFullscreen)
            SetState(MapState.Normal);
    }

    public void ToggleScreenSide()
    {
        SetScreenSide(curScreenSide == ScreenSide.Right ? ScreenSide.Left : ScreenSide.Right);
    }

    private Rect GetNormalRect()
    {
        if (curScreenSide == ScreenSide.Right)
            return normalRectRight;

        //mirror horizontally, preserving the edge margin
        return new Rect(1f - (normalRectRight.x + normalRectRight.width),
                        normalRectRight.y,
                        normalRectRight.width,
                        normalRectRight.height);
    }

    public void SetState(MapState state)
    {
        curMapState = state;

        var hudVp = ViewportManager.GetViewport("HUDMAP");
        var mainVp = ViewportManager.MainViewport;

        if (state == MapState.Normal)
        {
            hudVp.ActiveCamera.enabled = true;
            hudVp.ScreenRect = GetNormalRect();
            mainVp.ScreenRect = new Rect(0, 0, 1, 1);
        }
        else if (state == MapState.Half)
        {
            hudVp.ActiveCamera.enabled = true;
            mainVp.ScreenRect = new Rect(0, 0.5f, 1, 0.5f);
            hudVp.ScreenRect = new Rect(0, 0, 1f, 0.5f);
        }
        else
        {
            hudVp.ActiveCamera.enabled = false;
            mainVp.ScreenRect = new Rect(0, 0, 1, 1);
        }

        hudVp.ApplySettingsToAllCameras(ViewportManager.ViewportApplyFlags.Rect);
        mainVp.ApplySettingsToAllCameras(ViewportManager.ViewportApplyFlags.Rect);
    }

    public void ToggleFullscreen()
    {
        var hudVp = ViewportManager.GetViewport("HUDMAP");
        var mainVp = ViewportManager.MainViewport;

        if (!isFullscreen)
        {
            // Establish the normal layout first so the swap is predictable.
            SetState(MapState.Normal);

            mainVp.SwapDisplayArea(hudVp);
            isFullscreen = true;
        }
        else
        {
            // Swap them back.
            mainVp.SwapDisplayArea(hudVp);
            isFullscreen = false;

            // Restore the actual map state.
            SetState(curMapState);
        }
    }

    public void SetTargetObject(Transform target)
    {
        TargetFollower.Target = target;
    }

    public void CycleModeNext()
    {
        curMapState++;

        if (curMapState > MapState.Off)
            curMapState = MapState.Normal;

        SetState(curMapState);
    }

    public void CycleModePrev()
    {
        curMapState--;

        if (curMapState < MapState.Normal)
            curMapState = MapState.Off;

        SetState(curMapState);
    }

    private void InitShaders()
    {
        Shader behindShader = Shader.Find("Custom/RenderBehindUnlit");
        foreach (var renderer in mapModel.GetComponentsInChildren<MeshRenderer>())
        {
            foreach (var material in renderer.materials)
            {
                material.shader = behindShader;
            }
        }
    }

    // Use this for initialization
    public void Init(string cityName)
    {
        //load up our model
        if (AssetManager.Exists("geometry", $"hudmap_{cityName}.pkg"))
        {
            mapModel = SimpleForm.Create($"hudmap_{cityName}", Shader.Find("Unlit/Texture")).gameObject;
            mapModel.SetLayer(LayerMask.NameToLayer("Hudmap"), true); //layer

            //setup shaders
            InitShaders();
        }

        var hudViewport = ViewportManager.GetViewport("HUDMAP");

        // hardcoded hack from original gmae
        if (GameState.SelectedCity.ToLowerInvariant() == "london")
        {
            hudViewport.ClearColor = new Color(0.92f, 0.83f, 0.778f);
        }
        else
        {
            hudViewport.ClearColor = new Color(0.084f, 0.68f, 0.92f);
        }

        mapCamera = hudViewport.AddCamera();
        mapCamera.cullingMask = LayerMask.GetMask("Hudmap");
        mapCamera.depth = 1;

        mapCamera.transform.localEulerAngles = new Vector3(90, 0, 180);
        mapCamera.orthographic = true;
        mapCamera.orthographicSize = 550;

        mapCamera.nearClipPlane = 1f;
        mapCamera.farClipPlane = 1000f;

        hudViewport.SetActiveCamera(mapCamera);

        TargetFollower = mapCamera.gameObject.AddComponent<FollowTarget>();
        TargetFollower.Offset = new Vector3(0, 200, 0);
        TargetFollower.RotationAxes.x = false;
        TargetFollower.RotationAxes.z = false;

        curScreenSide = DEFAULT_SCREEN_SIDE;

        SetState(0);
        SetZoom(false);
        SetFollowRotation(false);
        ApplyZoomLevel();
    }

    public T AddItem<T>() where T : HudmapItem
    {
        //create parent
        GameObject compParent = new GameObject($"HudmapItem{typeof(T).Name}");
        compParent.transform.parent = this.gameObject.transform;

        //add item component
        var hudmapItem = compParent.AddComponent<T>();

        //set scale
        hudmapItem.BaseScale *= ITEM_SCALE;

        //add to list and return
        items.Add(hudmapItem);
        return hudmapItem;
    }

    public HudmapItem AddPlayerArrow(Transform playerTransform)
    {
        var item = AddItem<HudmapItem>();

        item.Init(playerTransform, HudmapItem.ComponentType.Triangle, true);
        item.SetColor(new Color(1f, 1f, 0f, 1f));
        item.SetBorderColor(new Color(0f, 0f, 0f, 1f));
        item.Offset = Vector3.up * 50.0f;

        item.BaseScale = new Vector3(1.75f * item.transform.localScale.x, 1.2f * item.transform.localScale.y, 1.2f * item.transform.localScale.z);
        return item;
    }

    public HudmapItem AddOpponentArrow(Transform playerTransform)
    {
        var item = AddItem<HudmapItem>();

        item.Init(playerTransform, HudmapItem.ComponentType.Triangle, true);
        item.SetColor(new Color(0.7f, 0f, 1.0f, 1f));
        item.SetBorderColor(new Color(0f, 0f, 0f, 1f));
        item.Offset = Vector3.up * 35.0f;

        item.BaseScale = new Vector3(1.75f * item.transform.localScale.x, 1.2f * item.transform.localScale.y, 1.2f * item.transform.localScale.z);
        return item;
    }

    public void RemoveItem<T>(T item) where T : HudmapItem
    {
        items.Remove(item);
        Destroy(item.gameObject);
    }

    void Update()
    {
        //update zoom
        float currentZoomLevel = zoomLevel;
        float zoomDiscrepancy = Mathf.Abs(currentZoomLevel - targetZoomLevel);

        if (zoomDiscrepancy != 0.0f)
        {
            zoomLevel = Mathf.MoveTowards(currentZoomLevel, targetZoomLevel, zoomRate * Time.unscaledDeltaTime);

            //scale main camera
            mapCamera.orthographicSize = Mathf.Lerp(zoomOutDist, zoomInDist, zoomLevel);
        }
    }

    void LateUpdate()
    {
        if (!followRotation || TargetFollower.Target == null)
            return;

        // Player's heading flattened onto the ground plane
        Vector3 fwd = TargetFollower.Target.forward;
        fwd.y = 0f;
        if (fwd.sqrMagnitude < 0.0001f)
            return;

        // Look straight down, with the player's heading as "up" on screen
        mapCamera.transform.rotation = Quaternion.LookRotation(Vector3.down, fwd.normalized);
    }

    public void Reset()
    {
        SetScreenSide(DEFAULT_SCREEN_SIDE);
    }
}