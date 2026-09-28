using UnityEngine;

public class CircuitHud : MonoBehaviour
{
    const float labelWidth = 0.1f;
    const float labelHeight = 0.1f;
    const float lapNumberWidth = 0.05f;
    const int MaxLaps = 8;

    const float ReferenceAspect = 4.0f / 3.0f;

    private MMTextNode placeLabelText;
    private MMTextNode placeValueText;

    private MMTextNode checkpointLabelText;
    private MMTextNode checkpointValueText;

    private MMTextNode lapLabelText;
    private MMTextNode lapValueText;

    private MMTextNode[] lapNumberTexts = new MMTextNode[MaxLaps];
    private MMTextNode[] lapTimeTexts = new MMTextNode[MaxLaps];

    private float placeYPos, checkpointYPos, lapYPos;
    private float lapTimesYPos, lapTimesYSpacing;

    // 1-based, 0 means the race hasn't started yet
    private int currentLap = 0;

    public void UpdatePlaceValues(int place, int totalRacers)
    {
        placeValueText.SetString(0, $"{place}/{totalRacers}");
    }

    public void UpdateCheckpointValues(int checkpoint, int totalCheckpoints)
    {
        checkpointValueText.SetString(0, $"{checkpoint}/{totalCheckpoints}");
    }

    public void UpdateLapValues(int lap, int totalLaps)
    {
        currentLap = lap;
        lapValueText.SetString(0, $"{lap}/{totalLaps}");
    }

    public void UpdateLapTime(int lap, float time)
    {
        int index = lap - 1;
        if (index < 0 || index >= MaxLaps)
            return;

        lapTimeTexts[index].SetString(0, GetLocTime(time));
    }

    private static string GetLocTime(float time)
    {
        time += 0.005f;

        int totalSeconds = (int)time;
        int hundredths = (int)((time - totalSeconds) * 100.0f);

        int minutes = totalSeconds / 60;
        int seconds = totalSeconds % 60;

        return $"{minutes}:{seconds:00}:{hundredths:00}";
    }

    private static Rect ToScreen(Rect normalized)
    {
        float virtualWidth = Screen.height * ReferenceAspect;

        return new Rect(
            normalized.x * virtualWidth,
            normalized.y * Screen.height,
            normalized.width * virtualWidth,
            normalized.height * Screen.height
        );
    }

    private void OnGUI()
    {
        float scale = (Screen.height / UIConstants.ReferenceHeight) * 0.85f; // arbitrary 0.85 constant makes it look closer to how it should

        if (placeLabelText != null)
        {
            placeLabelText.Draw(ToScreen(new Rect(0.0f, placeYPos, labelWidth, labelHeight)), scale);
            placeValueText.Draw(ToScreen(new Rect(labelWidth, placeYPos, labelWidth, labelHeight)), scale);
        }
        if (checkpointLabelText != null)
        {
            checkpointLabelText.Draw(ToScreen(new Rect(0.0f, checkpointYPos, labelWidth, labelHeight)), scale);
            checkpointValueText.Draw(ToScreen(new Rect(labelWidth, checkpointYPos, labelWidth, labelHeight)), scale);
        }
        if (lapLabelText != null)
        {
            lapLabelText.Draw(ToScreen(new Rect(0.0f, lapYPos, labelWidth, labelHeight)), scale);
            lapValueText.Draw(ToScreen(new Rect(labelWidth, lapYPos, labelWidth, labelHeight)), scale);
        }

        for (int i = 0; i < MaxLaps; i++)
        {
            if (lapTimeTexts[i] == null)
                continue;

            int lap = i + 1;
            if (lap > currentLap)
                continue;

            float y = lapTimesYPos + (i * lapTimesYSpacing);
            lapNumberTexts[i].Draw(ToScreen(new Rect(0.0f, y, lapNumberWidth, labelHeight)), scale);
            lapTimeTexts[i].Draw(ToScreen(new Rect(lapNumberWidth, y, labelWidth, labelHeight)), scale);
        }
    }

    public void Init()
    {
        var font = FontLoader.LoadFont(Localization.GetString(LocString.CircuitHudFont));

        Color placeCheckColor = new Color(0.5f, 1.0f, 0.5f, 1.0f);
        placeYPos = 0.035f;
        checkpointYPos = 0.085f;
        lapYPos = 0.135f;
        lapTimesYPos = 0.195f;
        lapTimesYSpacing = 0.045f;

        placeLabelText = new MMTextNode();
        placeLabelText.AddText(font, Localization.GetString(LocString.CircuitHudPlace), TextNodeEffect.Shadow, 0.0f, 0.0f);
        placeLabelText.ForegroundColor = placeCheckColor;

        placeValueText = new MMTextNode();
        placeValueText.AddText(font, "---", TextNodeEffect.Shadow, 0.0f, 0.0f);

        checkpointLabelText = new MMTextNode();
        checkpointLabelText.AddText(font, Localization.GetString(LocString.CircuitHudCheck), TextNodeEffect.Shadow, 0.0f, 0.0f);
        checkpointLabelText.ForegroundColor = placeCheckColor;

        checkpointValueText = new MMTextNode();
        checkpointValueText.AddText(font, "---", TextNodeEffect.Shadow, 0.0f, 0.0f);

        lapLabelText = new MMTextNode();
        lapLabelText.AddText(font, Localization.GetString(LocString.CircuitHudLap), TextNodeEffect.Shadow, 0.0f, 0.0f);
        lapLabelText.ForegroundColor = placeCheckColor;

        lapValueText = new MMTextNode();
        lapValueText.AddText(font, "---", TextNodeEffect.Shadow, 0.0f, 0.0f);

        for (int i = 0; i < MaxLaps; i++)
        {
            lapNumberTexts[i] = new MMTextNode();
            lapNumberTexts[i].AddText(font, $"{Localization.GetNumber(i + 1)}.", TextNodeEffect.Shadow, 0.0f, 0.0f);
            lapNumberTexts[i].ForegroundColor = placeCheckColor;

            lapTimeTexts[i] = new MMTextNode();
            lapTimeTexts[i].AddText(font, GetLocTime(0.0f), TextNodeEffect.Shadow, 0.0f, 0.0f);
        }
    }
}