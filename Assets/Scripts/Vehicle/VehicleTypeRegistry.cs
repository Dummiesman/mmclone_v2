using System.Collections.Generic;
using UnityEngine;

public class VehicleTypeRegistry
{
    public static bool AlwaysNitro = false;
    private static List<string> semiNames = new List<string>();
    private static List<string> policeNames = new List<string>();
    
    public static bool IsPolice(string basename)
    {
        foreach(var policeName in policeNames)
        {
            if (policeName.Equals(basename, System.StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    public static bool IsSemi(string basename)
    {
        foreach (var semiName in semiNames)
        {
            if (semiName.Equals(basename, System.StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static void AddTokensToList(string listName, List<string> list, string[] tokens)
    {
        foreach (var token in tokens)
        {
            string tokenTrimmed = token.Trim();
            if (tokenTrimmed == "ENDOFDATA")
                break;
            if (tokenTrimmed.Length > 0)
            {
                list.Add(tokenTrimmed);
            }
        }
    }

    public static void Clear()
    {
        AlwaysNitro = false;
        policeNames.Clear();
        semiNames.Clear();
    }

    public static void Load()
    {
        //clear existing
        Clear();

        //load new
        string dataPath = AssetManager.CombinePath("aud", "cardata", "shared", "vehtypes.csv");
        if (!AssetManager.Exists(dataPath))
        {
            Debug.Log("vehtypes file doesn't exist!");
            return;
        }

        var parser = AssetManager.OpenCSV(dataPath);
        bool isHeaderLine = true;
        string lastHeader = string.Empty;

        while (!parser.EOF())
        {
            parser.PrepareLine();
            if (isHeaderLine)
            {
                lastHeader = parser.GetToken(0);
            }
            else
            {
                if(lastHeader.Equals("Semi or bus", System.StringComparison.OrdinalIgnoreCase))
                {
                    var tokens = parser.GetTokens();
                    AddTokensToList(lastHeader, semiNames, tokens);
                }
                else if(lastHeader.Equals("Police Car", System.StringComparison.OrdinalIgnoreCase))
                {
                    var tokens = parser.GetTokens();
                    AddTokensToList(lastHeader, policeNames, tokens);
                }
                else if(lastHeader.Equals("Always nitro", System.StringComparison.OrdinalIgnoreCase)) 
                {
                    string tfToken = parser.GetToken(0);
                    AlwaysNitro = tfToken.Equals("TRUE", System.StringComparison.OrdinalIgnoreCase);
                }
            }
            isHeaderLine = !isHeaderLine;
        }
    }
}
