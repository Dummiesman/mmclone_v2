using System;

public static class StringExtensions 
{
    public static string Clean(this string str)
    {
        string rstr = str.Replace('\t', ' ');
        while (rstr.Contains("  "))
            rstr = rstr.Replace("  ", " ");
        return rstr.Trim();
    }

    public static int IndexOfNth(this string str, string value, int nth = 0)
    {
        if (nth < 0) throw new ArgumentException("nth");

        int offset = str.IndexOf(value);
        for (int i = 0; i < nth; i++)
        {
            if (offset == -1) return -1;
            offset = str.IndexOf(value, offset + 1);
        }

        return offset;
    }

    public static int IndexOfNth(this string str, char value, int nth = 0)
    {
        if (nth < 0) throw new ArgumentException("nth");

        int offset = str.IndexOf(value);
        for (int i = 0; i < nth; i++)
        {
            if (offset == -1) return -1;
            offset = str.IndexOf(value, offset + 1);
        }

        return offset;
    }
}
