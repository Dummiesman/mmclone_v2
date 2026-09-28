using UnityEngine;

public class Vehicle3DWidget : UIWidget
{
    public Camera Camera => camera;

    /// <summary>Distance the camera eases toward, in world units.</summary>
    public float TargetPolarDistance
    {
        get => targetPolarDistance;
        set => targetPolarDistance = Mathf.Max(0.01f, value);
    }

    /// <summary>Rate at which PolarDistance approaches TargetPolarDistance, units/sec.</summary>
    public float PolarApproach
    {
        get => polarApproach;
        set => polarApproach = Mathf.Max(0.0f, value);
    }

    /// <summary>Current distance from the target. Snaps when set directly.</summary>
    public float PolarDistance
    {
        get => polarDistance;
        set { polarDistance = Mathf.Max(0.01f, value); targetPolarDistance = polarDistance; }
    }

    /// <summary>Vertical angle above the horizon, in degrees.</summary>
    public float PolarInclination
    {
        get => polarInclination;
        set { polarInclination = Mathf.Clamp(value, -89.0f, 89.0f); }
    }

    public Vector3 CameraOffset = new Vector3(0.0f, 0.86f, 0.0f);

    public override bool EnableNavigation => false;

    private float targetPolarDistance = 10.0f;
    private float polarApproach = 21.0f;
    private float polarDistance = 10.0f;
    private float polarInclination = 10.0f; // source:  0.18 rad

    private Camera camera;
    private GameObject rootObject;
    private GameObject cameraObject;
    private GameObject underlayQuad;
    private Texture2D backgroundTexture;
    private RenderTexture renderTexture;

    private void ClipBackground()
    {
        var background = Menu.BackgroundImage;
        if (background == null)
        {
            Debug.LogError($"Vehicle3DWidget: menu has no background?");
            return;
        }

        var relativeAreaToClip = Rect;
        Rect textureAreaToClip = new Rect(relativeAreaToClip.x * background.width,
                                          relativeAreaToClip.y * background.height,
                                          relativeAreaToClip.width * background.width,
                                          relativeAreaToClip.height * background.height);

        int x = Mathf.Clamp(Mathf.RoundToInt(textureAreaToClip.x), 0, background.width);
        int y = Mathf.Clamp(Mathf.RoundToInt(textureAreaToClip.y), 0, background.height);
        int w = Mathf.Clamp(Mathf.RoundToInt(textureAreaToClip.width), 1, background.width - x);
        int h = Mathf.Clamp(Mathf.RoundToInt(textureAreaToClip.height), 1, background.height - y);

        // Texture coordinates are bottom-up, Rect is top-down.
        int readY = background.height - (y + h);

        backgroundTexture = new Texture2D(w, h, TextureFormat.RGBA32, false)
        {
            name = $"Vehicle3DWidget:{Menu.ID}-{ID}:Background",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };

        var pixels = background.GetPixels(x, readY, w, h);
        backgroundTexture.SetPixels(pixels);
        backgroundTexture.Apply(false, false);
    }

    private void CreateObjects()
    {
        rootObject = new GameObject($"Vehicle3DWidget:{Menu.ID}-{ID}");

        // --- Camera ---
        cameraObject = new GameObject("Camera");
        cameraObject.transform.SetParent(rootObject.transform, false);

        float aspect = (Rect.width / Rect.height) * (UIConstants.ReferenceWidth / UIConstants.ReferenceHeight);
        int rtWidth = 1024;
        int rtHeight = Mathf.Max(2, Mathf.RoundToInt(1024 / aspect));

        renderTexture = new RenderTexture(rtWidth, rtHeight, 24, RenderTextureFormat.ARGB32)
        {
            name = $"Vehicle3DWidget:{Menu.ID}-{ID}:RT",
            antiAliasing = QualitySettings.antiAliasing > 0 ? QualitySettings.antiAliasing : 1,
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            useMipMap = false
        };
        renderTexture.Create();

        camera = cameraObject.AddComponent<Camera>();
        camera.targetTexture = renderTexture;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.clear;
        camera.nearClipPlane = 0.05f;
        camera.farClipPlane = 100.0f;
        camera.fieldOfView = 40.0f;
        camera.allowHDR = false;
        camera.allowMSAA = true;
        camera.transform.localPosition = new Vector3(0.0f, 1.0f, -6.0f);
        camera.transform.localRotation = Quaternion.Euler(5.0f, 0.0f, 0.0f);

        // --- Background quad, drawn at the far plane behind the vehicle ---
        underlayQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        underlayQuad.name = "BackgroundQuad";
        underlayQuad.transform.SetParent(cameraObject.transform, false);
        Object.Destroy(underlayQuad.GetComponent<Collider>());

        var renderer = underlayQuad.GetComponent<MeshRenderer>();
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

        var material = new Material(Shader.Find("Unlit/Texture"));
        if (backgroundTexture != null)
            material.mainTexture = backgroundTexture;
        renderer.material = material;

        rootObject.SetActive(false);
    }

    private void ApplyCameraParameters()
    {
        if (camera == null)
            return;

        float inclination = polarInclination * Mathf.Deg2Rad;

        Vector3 direction = new Vector3(0.0f,
                                        Mathf.Sin(inclination),
                                        Mathf.Cos(inclination));

        cameraObject.transform.position = (direction * polarDistance) + CameraOffset;
        cameraObject.transform.rotation = Quaternion.LookRotation(-direction, Vector3.up);
    }

    public override void Update()
    {
        base.Update();

        if (camera == null || underlayQuad == null)
            return;

        // polar math
        if (!Mathf.Approximately(polarDistance, targetPolarDistance))
        {
            float step = Time.deltaTime * polarApproach;

            if (polarDistance > targetPolarDistance)
            {
                polarDistance -= step;
                if (polarDistance < targetPolarDistance)
                    polarDistance = targetPolarDistance;
            }
            else
            {
                polarDistance += step;
                if (polarDistance > targetPolarDistance)
                    polarDistance = targetPolarDistance;
            }
        }
        ApplyCameraParameters();

        // position background quad at far plane, and scale accordingly
        float distance = camera.farClipPlane * 0.99f;
        float height = 2.0f * distance * Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
        float width = height * camera.aspect;

        var quadTransform = underlayQuad.transform;
        quadTransform.localPosition = new Vector3(0.0f, 0.0f, distance);
        quadTransform.localRotation = Quaternion.identity;
        quadTransform.localScale = new Vector3(width, height, 1.0f);
    }

    public override void Activate()
    {
        base.Activate();
        rootObject.SetActive(true);
    }

    public override void Deactivate()
    {
        base.Deactivate();
        rootObject.SetActive(false);
    }

    public override void Dispose()
    {
        base.Dispose();
        Object.Destroy(rootObject);

        if (renderTexture != null)
        {
            renderTexture.Release();
            Object.Destroy(renderTexture);
            renderTexture = null;
        }

        if (backgroundTexture != null)
        {
            Object.Destroy(backgroundTexture);
            backgroundTexture = null;
        }
    }

    public override void Draw()
    {
        base.Draw();

        // draw render texture that the camera is rendering to, into Rect
        if (renderTexture != null)
        {
            var rtDrawRect = Menu.ToPixelCoordinates(Rect);
            GUI.DrawTexture(rtDrawRect, renderTexture, ScaleMode.StretchToFill, true);
        }
    }

    public Vehicle3DWidget(UIMenu menu, int id, string name, Rect rect) : base(menu, id, name, rect)
    {
        ClipBackground();
        CreateObjects();
    }
}
