using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Utility for parsing separated strings
/// </summary>
public static class StringArrayParsingUtil
{
    private static int GetStartArrayParseIndex(string arrayContent)
    {
        return arrayContent.TakeWhile(c => char.IsWhiteSpace(c)).Count();
    }

    private static bool ShouldSkipChar(char c)
    {
        return c <= 0x20;
    }

    private static int FindNextNonWhitespace(string arrayContent, int startIndex, int maxIndex = -1)
    {
        if (maxIndex < 0)
            maxIndex = arrayContent.Length;
        for (int i = startIndex; i < maxIndex; i++)
        {
            if (!ShouldSkipChar(arrayContent[i]))
                return i;
        }
        return -1;
    }

    private static int FindNextWhitespace(string arrayContent, int startIndex, int maxIndex = -1)
    {
        if (maxIndex < 0)
            maxIndex = arrayContent.Length;
        for (int i = startIndex; i < maxIndex; i++)
        {
            if (ShouldSkipChar(arrayContent[i]))
                return i;
        }
        return -1;
    }

    private static int GetEndArrayParseIndex(string arrayContent)
    {
        for (int i = arrayContent.Length - 1; i >= 0; i--)
        {
            if (!ShouldSkipChar(arrayContent[i]))
            {
                return i + 1;
            }
        }
        return -1;
    }

    public static IEnumerable<StringSlice> GetCommaSeparatedSlices(string arrayContent)
    {
        int lastIndex = -1;
        int index = -1;
        do
        {
            index = arrayContent.IndexOf(',', index + 1);
            if (index == -1)
            {
                yield return StringSlice.FromStartEndIndex(arrayContent, lastIndex + 1, arrayContent.Length); 
            }
            else
            {
                yield return StringSlice.FromStartEndIndex(arrayContent, lastIndex + 1, index);
            }
            lastIndex = index;
        } 
        while (index >= 0);
    }

    public static IEnumerable<StringSlice> GetSpaceSeparatedSlices(string arrayContent)
    {
        int startIndex = GetStartArrayParseIndex(arrayContent);
        int endIndex = GetEndArrayParseIndex(arrayContent);

        for (int i = startIndex; i < endIndex; i++)
        {
            bool atLastElement = false;

            int nextWhitespaceIdx = FindNextWhitespace(arrayContent, i, endIndex);
            if (nextWhitespaceIdx < 0) // reached end
            {
                nextWhitespaceIdx = endIndex;
                atLastElement = true;
            }

            yield return StringSlice.FromStartEndIndex(arrayContent, i, nextWhitespaceIdx);

            //set i to the start of the next string
            if (!atLastElement)
            {
                int nextNonWhitespace = FindNextNonWhitespace(arrayContent, nextWhitespaceIdx, endIndex);
                if (nextNonWhitespace >= 0)
                    i = nextNonWhitespace - 1;
                else
                    break;
            }
            else
            {
                break;
            }
        }
    }
}
