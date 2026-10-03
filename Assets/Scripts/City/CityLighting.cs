using System.Collections.Generic;
using UnityEngine;

public class CityLightingPreset
{
    public class CityLight
    {
        public string Name = "null";
        public float Heading = 1f;
        public float Pitch = 1f;
        public Color Color = Color.white;

        public Color GetAdjustedColor()
        {
            float pitch = Pitch % Mathf.PI;
            bool pitchIsZero = (pitch >= 0.0f);
            float halfPI = Mathf.PI / 2f;
            return new Color(
                (float)((!pitchIsZero) ? Mathf.Max(Color.r, 0) * Mathf.Cos(pitch + halfPI) : 0.0f),
                (float)((!pitchIsZero) ? Mathf.Max(Color.g, 0) * Mathf.Cos(pitch + halfPI) : 0.0f),
                (float)((!pitchIsZero) ? Mathf.Max(Color.b, 0) * Mathf.Cos(pitch + halfPI) : 0.0f)
            );
        }

        public Vector3 CalculateEulers()
        {
            return new Vector3((Pitch / 3.14f) * -180, ((Heading / 3.14f) * 180) - 90, 0);
        }

        public static CityLight FromParser(string name, TokenFileParser reader)
        {
            return new CityLight()
            {
                Name = name,
                Heading = reader.Read(name + "Heading", 1f),
                Pitch = reader.Read(name + "Pitch", 1f),
                Color = reader.Read(name + "Color", Color.white),
            };
        }
    }

    public CityLight KeyLight;
    public CityLight Fill1Light;
    public CityLight Fill2Light;
    public bool Headlights = false; // mm2hook addition
    public string ReflectionMap = "refl_dc"; // mm2hook addition

    private Color32 ambientBase;

    public Color32 GetAmbientColor(int lightQuality)
    {
        switch (lightQuality)
        {
            case 2:
                return (Color)ambientBase * 0.3294f;
            case 1:
                return (Color)ambientBase * 0.6588f;
            case 0:
            default:
                return ambientBase;
        }
    }

    public static CityLightingPreset FromFile(string filename)
    {
        var preset = new CityLightingPreset();

        // open the lt file
        TokenFileParser lightNode;
        if (AssetManager.Exists("city", filename))
        {
            lightNode = AssetManager.OpenNode("city", filename);
        }
        else
        {
            // fallback to SF I guess
            lightNode = AssetManager.OpenNode("city", "sf.lt00");
        }
        lightNode.SeekToData();

        // read light data!!
        preset.KeyLight = CityLight.FromParser("Key", lightNode);
        preset.Fill1Light = CityLight.FromParser("Fill1", lightNode);
        preset.Fill2Light = CityLight.FromParser("Fill2", lightNode);

        // read ambient color
        int ambientColor = lightNode.Read("Ambient", 0x050505FF);
        byte[] ambientBytes = System.BitConverter.GetBytes(ambientColor);
        preset.ambientBase = new Color32(ambientBytes[2], ambientBytes[1], ambientBytes[0], ambientBytes[3]);

        // read mm2hook specifics
        preset.Headlights = lightNode.Read("Headlights", preset.Headlights);
        preset.ReflectionMap = lightNode.Read("ReflectionMap", preset.ReflectionMap);

        //
        return preset;
    }
}

public class CityLighting
{
    public CityLightingPreset preset { get; private set; }
    public Color AmbientColor { get; private set; }

    private GameObject LightingParent;
    private Light Fill1SceneLight;
    private Light Fill2SceneLight;
    private Light KeySceneLight;

    public IReadOnlyList<Light> ActiveLights => activeLights;
    private List<Light> activeLights = new List<Light>();

    public Light GetKeyLight()
    {
        return KeySceneLight;
    }

    public Light GetFill1Light()
    {
        return Fill1SceneLight;
    }

    public Light GetFill2Light()
    {
        return Fill2SceneLight;
    }

    public void InitObjects()
    {
        if (LightingParent == null)
            LightingParent = new GameObject("Lighting");


        //destroy any existing lighting
        if (KeySceneLight == null)
        {
            var keyLightObj = new GameObject("KeyLight");
            keyLightObj.transform.SetParent(LightingParent.transform, false);
            KeySceneLight = keyLightObj.AddComponent<Light>();
            SetupLight(KeySceneLight);
        }
        if (Fill1SceneLight == null)
        {
            var fill1Obj = new GameObject("Fill1");
            fill1Obj.transform.SetParent(LightingParent.transform, false);
            Fill1SceneLight = fill1Obj.AddComponent<Light>();
            SetupLight(Fill1SceneLight);
        }
        if (Fill2SceneLight == null)
        {
            var fill2Obj = new GameObject("Fill2");
            fill2Obj.transform.SetParent(LightingParent.transform, false);
            Fill2SceneLight = fill2Obj.AddComponent<Light>();
            SetupLight(Fill2SceneLight);
        }
    }

    public void LoadPreset(string city, MMTimeOfDay timeOfDay, MMWeather weather)
    {
        string fileID = (((int)timeOfDay * 4) + (int)weather).ToString("00");
        preset = CityLightingPreset.FromFile(city + ".lt" + fileID);

        // add default headlight settings in case of unspecified values
        bool defHeadlights = (timeOfDay == MMTimeOfDay.Night || timeOfDay == MMTimeOfDay.Evening);
        preset.Headlights |= defHeadlights;
    }

    /// <summary>
    /// Quality 0 uses no fill lights
    /// Quality 1 uses Fill1Light
    /// Quality 2 uses Fill2Light
    /// </summary>
    /// <param name="quality"></param>
    public void SetQuality(int quality)
    {
        AmbientColor = preset.GetAmbientColor(quality);
        RenderSettings.ambientLight = AmbientColor;
        Fill1SceneLight.enabled = quality >= 1;
        Fill2SceneLight.enabled = quality >= 2;

        activeLights.Clear();
        if(KeySceneLight.enabled) activeLights.Add(KeySceneLight);
        if (Fill1SceneLight.enabled) activeLights.Add(Fill1SceneLight);
        if (Fill2SceneLight.enabled) activeLights.Add(Fill2SceneLight);
    }

    public void SetShadowing(bool shadows)
    {
        if (KeySceneLight == null )
            return;
        KeySceneLight.shadows = shadows ? LightShadows.Soft : LightShadows.None;
    }
    private void SetupLight(Light light)
    {
        light.type = LightType.Directional;
        light.renderMode = Application.isMobilePlatform ? LightRenderMode.ForceVertex : LightRenderMode.Auto;
        light.cullingMask = ~LayerMask.GetMask("BangerUnlit");
    }

    private float ColorIntensity(Color color)
    {
        return (color.r + color.g + color.b) / 3f;
    }

    protected void NormalizeIntensities()
    {
        float rTot = preset.KeyLight.Color.r +  preset.Fill1Light.Color.r +  preset.Fill2Light.Color.r;
        float gTot = preset.KeyLight.Color.g +  preset.Fill1Light.Color.g + preset.Fill2Light.Color.g;
        float bTot = preset.KeyLight.Color.b +  preset.Fill1Light.Color.b + preset.Fill2Light.Color.b;

        var keyColor = KeySceneLight.color;
        keyColor.r /= rTot;
        keyColor.g /= gTot;
        keyColor.b /= bTot;
        KeySceneLight.color = keyColor;

        var fill1Color = Fill1SceneLight.color;
        fill1Color.r /= rTot;
        fill1Color.g /= gTot;
        fill1Color.b /= bTot;
        Fill1SceneLight.color = fill1Color;

        var fill2Color = Fill2SceneLight.color;
        fill2Color.r /= rTot;
        fill2Color.g /= gTot;
        fill2Color.b /= bTot;
        Fill2SceneLight.color = fill2Color;
    }

    public void Apply()
    {
        //setup lights
        KeySceneLight.color = preset.KeyLight.Color;
        Fill1SceneLight.color = preset.Fill1Light.Color;
        Fill2SceneLight.color = preset.Fill2Light.Color;

        KeySceneLight.transform.eulerAngles = preset.KeyLight.CalculateEulers();
        Fill1SceneLight.transform.eulerAngles = preset.Fill1Light.CalculateEulers();
        Fill2SceneLight.transform.eulerAngles = preset.Fill2Light.CalculateEulers();

        //normalize
        //NormalizeIntensities();

        //set ambient color
        RenderSettings.ambientLight = preset.GetAmbientColor(0);
    }
}
