public static class FastFloatParser
{
    private const int powCacheLength = 39;
    private static readonly float[] powCache = { 1f, 10f, 100f, 1000f, 10000f, 100000f, 1000000f, 1E+07f, 1E+08f, 1E+09f, 1E+10f, 1E+11f, 1E+12f, 1E+13f, 1E+14f, 1E+15f, 1E+16f, 1E+17f, 1E+18f, 1E+19f, 1E+20f, 1E+21f, 1E+22f, 1E+23f, 1E+24f, 1E+25f, 1E+26f, 1E+27f, 1E+28f, 1E+29f, 1E+30f, 1E+31f, 1E+32f, 1E+33f, 1E+34f, 1E+35f, 1E+36f, 1E+37f, 1E+38f };
    private static readonly float[] invPowCache = { 1f, 0.1f, 0.01f, 0.001f, 0.0001f, 1E-05f, 1E-06f, 1E-07f, 1E-08f, 1E-09f, 1E-10f, 1E-11f, 1E-12f, 1E-13f, 1E-14f, 1E-15f, 1E-16f, 1E-17f, 1E-18f, 1E-19f, 1E-20f, 1E-21f, 1E-22f, 1E-23f, 1E-24f, 1E-25f, 1E-26f, 1E-27f, 1E-28f, 1E-29f, 1E-30f, 1E-31f, 1E-32f, 1E-33f, 1E-34f, 1E-35f, 1E-36f, 1E-37f, 1E-38f };

    // whole * 10 + 9 must stay <= int.MaxValue
    private const int wholeSafeLimit = 214748363;
    // exp can't grow past this, so exp * 10 + 9 can never wrap
    private const int expSafeLimit = 9999;

    public static bool TryParse(string s, out float f)
    {
        if (s == null) { f = 0f; return false; }
        return TryParse(s, 0, s.Length, out f);
    }

    public static bool TryParse(string s, int begin, int end, out float f)
    {
        if (s == null || begin < 0 || end > s.Length || end <= begin)
        {
            f = 0f;
            return false;
        }

        f = Parse(s, begin, end);
        if (float.IsNaN(f) || float.IsInfinity(f))
        {
            f = 0f;   // atof-style silent failure
            return false;
        }
        return true;
    }

    public static float Parse(string s)
    {
        if (s == null) return float.NaN;
        return Parse(s, 0, s.Length);
    }

    public static float Parse(string s, int begin, int end)
    {
        int len = end - begin;
        if (len <= 0) return float.NaN;

        int pos = 0;
        int whole = 0;
        int extraExp = 0;          // integer digits we couldn't fit in `whole`
        int exp = 0;
        float fractional = 0f;
        bool expIsNegative = false;
        bool hasDigits = false;

        float sign = 1f;
        char c = s[begin];
        if (c == '-') { sign = -1f; pos++; }
        else if (c == '+') { pos++; }
        if (pos >= len) return float.NaN;

        // ---- integer part ----
        while (pos < len)
        {
            c = s[begin + pos];
            if (c < '0' || c > '9') break;
            pos++;
            hasDigits = true;

            if (whole <= wholeSafeLimit)
                whole = (whole * 10) + (c - '0');
            else
                extraExp++;        // overflow guard: keep magnitude, drop precision
        }

        // ---- fractional part ----
        if (pos < len && (s[begin + pos] == '.' || s[begin + pos] == ','))
        {
            pos++;
            float dec = 0.1f;
            while (pos < len)
            {
                c = s[begin + pos];
                if (c < '0' || c > '9') break;
                pos++;
                hasDigits = true;
                fractional += (c - '0') * dec;
                dec *= 0.1f;       // safely flushes to 0 on long inputs
            }
        }

        if (!hasDigits) return float.NaN;

        // ---- exponent part ----
        if (pos < len)
        {
            c = s[begin + pos];
            if (c != 'e' && c != 'E') return float.NaN;   // trailing garbage
            pos++;
            if (pos >= len) return float.NaN;            // bare "1E"

            c = s[begin + pos];
            if (c == '-') { expIsNegative = true; pos++; }
            else if (c == '+') { pos++; }
            if (pos >= len) return float.NaN;            // bare "1E-"

            while (pos < len)
            {
                c = s[begin + pos++];
                if (c < '0' || c > '9') return float.NaN;
                if (exp <= expSafeLimit)
                    exp = (exp * 10) + (c - '0');        // clamped, can't wrap
            }
        }

        int totalExp = (expIsNegative ? -exp : exp) + extraExp;
        float value = sign * (whole + fractional);

        if (totalExp == 0) return value;

        if (totalExp > 0)
        {
            if (totalExp >= powCacheLength)
                return value == 0f ? value : (sign * float.PositiveInfinity);
            return value * powCache[totalExp];
        }

        int n = -totalExp;
        if (n >= powCacheLength) return sign * 0f;       // underflow, not -Infinity
        return value * invPowCache[n];
    }
}