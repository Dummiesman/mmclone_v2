using System;

public static class FastIntParser
{
    public static int Parse(string intText, int startIndex, int endIndex)
    {
        int y = 0;
        bool isNegative = false;
        for (int i = startIndex; i < endIndex; i++)
        {
            char c = intText[i];
            if ((c < '0' || c > '9') && c != '-')
                throw new ArgumentException("Char is out of integer range");
            if (c == '-')
                isNegative = true;
            else
                y = y * 10 + (intText[i] - '0');
        }
        return (isNegative) ? -y : y;
    }

    public static int Parse(string intText)
    {
        return Parse(intText, 0, intText.Length);
    }

    public static bool TryParse(string intText, out int outVal)
    {
        outVal = 0;
        try
        {
            outVal = Parse(intText);
            return true;
        }
        catch
        {
            return false;
            //do nothing
        }
    }
}
