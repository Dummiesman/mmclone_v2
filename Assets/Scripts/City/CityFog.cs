using UnityEngine;

public class CityFog
{
    private CSVParser fogFileParser;

    private Color color = Color.white;
    private float fogStart = 100f;
    private float fogEnd = 1000f;

    private float lastViewDistance = 1000f;

    private void Apply()
    {
        const float FogStartMargin = 30f;
        float farClip = lastViewDistance;
        float start = Mathf.Min(farClip - FogStartMargin, fogStart);
        float end = Mathf.Min(farClip, fogEnd);

        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogStartDistance = start;
        RenderSettings.fogEndDistance = end;
        RenderSettings.fog = true;
        RenderSettings.fogColor = color;

        var mainVp = ViewportManager.MainViewport;
        if (mainVp != null)
        {
            mainVp.ClearColor = RenderSettings.fogColor;
            mainVp.ApplySettingsToAllCameras(ViewportManager.ViewportApplyFlags.ClearColor);
        }
    }

    public void SetViewDistance(float viewDist)
    {
        bool isDifferent = viewDist != lastViewDistance;
        lastViewDistance = viewDist;
        if (isDifferent)
        {
            Apply();
        }
    }

    public void SetTimeAndWeather(MMTimeOfDay timeOfDay, MMWeather weather)
    {
        if (fogFileParser == null)
            return;

        int csvIndex = (((int)timeOfDay * 4) + (int)weather);
        fogFileParser.Seek(csvIndex + 1);
        if (fogFileParser.EOF())
        {
            Debug.LogWarning($"CityFog.SetTimeAndWeather failed: No entry for {timeOfDay} + {weather}.");
            return;
        }

        fogFileParser.PrepareLine();

        byte fogR = byte.Parse(fogFileParser.GetToken(0));
        byte fogG = byte.Parse(fogFileParser.GetToken(1));
        byte fogB = byte.Parse(fogFileParser.GetToken(2));
        color = new Color32(fogR, fogG, fogB, 255);

        fogStart = FastFloatParser.Parse(fogFileParser.GetToken(3));
        fogEnd = FastFloatParser.Parse(fogFileParser.GetToken(4));

        Apply();
    }

    public void Init(string city)
    {
        //init parser   
        if (AssetManager.Exists("city", $"{city}_fog.csv"))
        {
            fogFileParser = AssetManager.OpenCSV("city", city + "_fog");
        }
        else
        {
            fogFileParser = AssetManager.OpenCSV("city", "sf_fog");
        }
    }
}
