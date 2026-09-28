using System.Collections.Generic;

public class BMButtonGroup 
{
    public readonly List<BMButton> Buttons = new List<BMButton>();
    private int activeButtonIndex = -1;
    
    public int ActiveButtonIndex
    {
        get => activeButtonIndex;
        set => activeButtonIndex = value;
    }

    public BMButton ActiveButton
    {
        get => (activeButtonIndex < 0) ? null : Buttons[activeButtonIndex];
        set
        {
            var index = Buttons.IndexOf(value);
            activeButtonIndex = index;
        }
    }
}
