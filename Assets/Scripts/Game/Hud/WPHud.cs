using UnityEngine;

public enum WPHudType
{
    Checkpoint,
    PositionAndCheckpoint
}

public class WPHud : MonoBehaviour
{
    const float labelWidth = 0.1f;
    const float labelHeight = 0.1f;

    const float ReferenceAspect = 4.0f / 3.0f;

    private MMTextNode placeLabelText;
    private MMTextNode placeValueText;

    private MMTextNode checkpointLabelText;
    private MMTextNode checkpointValueText;

    private float checkpointYPos, placeYPos;

    public void UpdatePlaceValues(int place, int totalRacers)
    {
        placeValueText.SetString(0, $"{place}/{totalRacers}");
    }   
    
    public void UpdateCheckpointValues(int checkpoint, int totalCheckpoints)
    {
        checkpointValueText.SetString(0, $"{checkpoint}/{totalCheckpoints}");
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
    }

    public void Init(WPHudType type)
    {
        var font = FontLoader.LoadFont(Localization.GetString(LocString.WpHudFont));

        Color placeCheckColor = new Color(0.5f, 1.0f, 0.5f, 1.0f);
        placeYPos = 0.035f;
        checkpointYPos = 0.085f;

        if (type == WPHudType.PositionAndCheckpoint)
        {
            placeLabelText = new MMTextNode();
            placeLabelText.AddText(font, Localization.GetString(LocString.WpHudPlace), TextNodeEffect.Shadow, 0.0f, 0.0f);
            placeLabelText.ForegroundColor = placeCheckColor;

            placeValueText = new MMTextNode();
            placeValueText.AddText(font, "---", TextNodeEffect.Shadow, 0.0f, 0.0f);
        }

        checkpointLabelText = new MMTextNode();
        checkpointLabelText.AddText(font, Localization.GetString(LocString.WpHudCheck), TextNodeEffect.Shadow, 0.0f, 0.0f);
        checkpointLabelText.ForegroundColor = placeCheckColor;

        checkpointValueText = new MMTextNode();
        checkpointValueText.AddText(font, "---", TextNodeEffect.Shadow, 0.0f, 0.0f);
    }
}
