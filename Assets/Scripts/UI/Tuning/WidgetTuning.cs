using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

public class WidgetTuning
{
    private static Dictionary<int, Dictionary<string, Rect>> widgetData = new Dictionary<int, Dictionary<string, Rect>>();

    /// <summary>
    /// Retrieve data for a widget
    /// </summary>
    /// <param name="rect">Rect in pixels</param>
    /// <returns></returns>
    public static bool RetrieveWidgetData(int menuId, string name, out Rect rect)
    {
        if (widgetData.TryGetValue(menuId, out var widgets))
        {
            if (widgets.TryGetValue(name, out rect))
            {
                return true;
            }
        }

        rect = new Rect();
        return false;
    }

    public static void Load()
    {
        using(var stream = AssetManager.Open("tune", "widget.csv"))
        {
            var parser = new CSVParser(stream);
            parser.PrepareHeader();

            while(!parser.EOF())
            {
                parser.PrepareLine();

                int menuId, widgetId;
                if (int.TryParse(parser[1], out menuId) && int.TryParse(parser[3], out widgetId))
                {
                    string widgetName = parser[2];

                    float x, y, w, h;
                    float.TryParse(parser[4], NumberStyles.Float, CultureInfo.InvariantCulture, out x);
                    float.TryParse(parser[5], NumberStyles.Float, CultureInfo.InvariantCulture, out y);
                    float.TryParse(parser[6], NumberStyles.Float, CultureInfo.InvariantCulture, out w);
                    float.TryParse(parser[7], NumberStyles.Float, CultureInfo.InvariantCulture, out h);

                    Dictionary<string, Rect> widgets;
                    if (!widgetData.TryGetValue(menuId, out widgets))
                    {
                        widgets = new Dictionary<string, Rect>();
                        widgetData.Add(menuId, widgets);
                    }
                    widgets[widgetName] = new Rect(x, y, w, h);
                }
            }
        }
    }
}
