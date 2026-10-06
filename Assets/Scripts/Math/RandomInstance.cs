using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Instance-based replacement for UnityEngine.Random.
///
/// Mirrors Unity's static Random API (value, Range, insideUnitCircle, rotation,
/// ColorHSV, InitState, state) but each instance owns its seed and stream, so one
/// system can draw numbers without perturbing anyone else's sequence.
///
/// Backed by PCG32 rather than System.Random: the algorithm is fixed here, so the
/// same seed gives the same sequence on every platform, Unity version and .NET
/// runtime, forever. State is 16 bytes and trivially serializable for save games.
///
/// NOT thread-safe. Give each thread/job its own instance (see Fork).
/// </summary>
[Serializable]
public sealed class RandomInstance
{
    #region State

    /// <summary>Full generator state. Save/restore this to rewind or resume a stream.</summary>
    [Serializable]
    public struct State
    {
        public ulong s;   // sequence position
        public ulong inc; // stream selector (always odd)

        public override string ToString() => $"State({s:X16}, {inc:X16})";
    }

    private const ulong Multiplier = 6364136223846793005UL;

    // 1 / (2^24 - 1): matches Unity, where value is inclusive of both 0 and 1.
    private const float ToFloat01 = 1f / 16777215f;

    private ulong _state;
    private ulong _inc = 1442695040888963407UL | 1UL;

    #endregion

    #region Construction

    /// <summary>Seeded from the clock. Use a fixed seed if you want reproducible runs.</summary>
    public RandomInstance() : this(Environment.TickCount) { }

    public RandomInstance(int seed) => InitState(seed);

    /// <summary>
    /// Two instances with the same seed but different stream ids produce completely
    /// independent sequences. Handy for "one stream per subsystem, one shared seed".
    /// </summary>
    public RandomInstance(int seed, int stream) => InitState(seed, stream);

    /// <summary>Resets this instance to the start of the sequence for <paramref name="seed"/>.</summary>
    public void InitState(int seed) => InitState(seed, 0);

    public void InitState(int seed, int stream)
    {
        // Avalanche the seed so that 0, 1, 2... give unrelated sequences.
        ulong scrambled = Mix64((ulong)(uint)seed * 0x9E3779B97F4A7C15UL);

        _inc = ((ulong)(uint)stream << 1) | 1UL;
        _state = 0UL;
        NextUInt();
        _state += scrambled;
        NextUInt();
    }

    /// <summary>Snapshot of the generator. Assign it back to resume exactly where you were.</summary>
    public State state
    {
        get => new State { s = _state, inc = _inc };
        set
        {
            _state = value.s;
            _inc = value.inc | 1UL; // increment must stay odd or the period collapses
        }
    }

    /// <summary>
    /// Creates a child generator seeded from this one. Deterministic: forking in the
    /// same order from the same parent always yields the same children. Use this to
    /// hand every agent its own stream from a single master seed.
    /// </summary>
    public RandomInstance Fork()
    {
        var child = new RandomInstance();
        child._inc = (NextULong() << 1) | 1UL;
        child._state = NextULong();
        child.NextUInt();
        return child;
    }

    private static ulong Mix64(ulong z)
    {
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    #endregion

    #region Core generation

    /// <summary>Raw 32 bits, uniform over the full uint range.</summary>
    public uint NextUInt()
    {
        ulong x = _state;
        _state = x * Multiplier + _inc;

        uint xorshifted = (uint)(((x >> 18) ^ x) >> 27);
        int rot = (int)(x >> 59);
        return (xorshifted >> rot) | (xorshifted << ((-rot) & 31));
    }

    public ulong NextULong() => ((ulong)NextUInt() << 32) | NextUInt();

    /// <summary>Uniform in [0, range). Rejection-sampled, so no modulo bias.</summary>
    private uint NextUIntBelow(uint range)
    {
        uint threshold = (uint)((0x100000000UL - range) % range);
        uint r;
        do { r = NextUInt(); } while (r < threshold);
        return r % range;
    }

    /// <summary>Uniform float in [0, 1], inclusive at both ends (same as Unity's Random.value).</summary>
    public float value => (NextUInt() >> 8) * ToFloat01;

    /// <summary>Uniform double in [0, 1), full 53-bit precision.</summary>
    public double NextDouble()
    {
        ulong hi = (ulong)NextUInt() >> 5; // 27 bits
        ulong lo = (ulong)NextUInt() >> 6; // 26 bits
        return (hi * 67108864.0 + lo) * (1.0 / 9007199254740992.0);
    }

    #endregion

    #region Range

    /// <summary>Uniform float in [min, max]. Max is INCLUSIVE, matching Unity.</summary>
    public float Range(float minInclusive, float maxInclusive)
    {
        return minInclusive + (maxInclusive - minInclusive) * value;
    }

    /// <summary>Uniform int in [min, max). Max is EXCLUSIVE, matching Unity.</summary>
    public int Range(int minInclusive, int maxExclusive)
    {
        if (minInclusive == maxExclusive) return minInclusive;

        if (minInclusive > maxExclusive)
        {
            int tmp = minInclusive;
            minInclusive = maxExclusive;
            maxExclusive = tmp;
        }

        uint span = (uint)((long)maxExclusive - minInclusive);
        return minInclusive + (int)NextUIntBelow(span);
    }

    #endregion

    #region Vectors, rotations, colors

    /// <summary>Uniformly distributed point inside a radius-1 circle.</summary>
    public Vector2 insideUnitCircle
    {
        get
        {
            while (true)
            {
                float x = value * 2f - 1f;
                float y = value * 2f - 1f;
                if (x * x + y * y <= 1f) return new Vector2(x, y);
            }
        }
    }

    /// <summary>Uniformly distributed point inside a radius-1 sphere.</summary>
    public Vector3 insideUnitSphere
    {
        get
        {
            while (true)
            {
                float x = value * 2f - 1f;
                float y = value * 2f - 1f;
                float z = value * 2f - 1f;
                if (x * x + y * y + z * z <= 1f) return new Vector3(x, y, z);
            }
        }
    }

    /// <summary>Uniformly distributed point on the surface of a radius-1 sphere.</summary>
    public Vector3 onUnitSphere
    {
        get
        {
            float z = value * 2f - 1f;
            float theta = value * 2f * Mathf.PI;
            float r = Mathf.Sqrt(Mathf.Max(0f, 1f - z * z));
            return new Vector3(r * Mathf.Cos(theta), r * Mathf.Sin(theta), z);
        }
    }

    /// <summary>Uniformly distributed random rotation (Shoemake's method).</summary>
    public Quaternion rotation
    {
        get
        {
            float u1 = value, u2 = value, u3 = value;
            float s1 = Mathf.Sqrt(1f - u1);
            float s2 = Mathf.Sqrt(u1);
            float t1 = 2f * Mathf.PI * u2;
            float t2 = 2f * Mathf.PI * u3;
            return new Quaternion(
                s1 * Mathf.Sin(t1),
                s1 * Mathf.Cos(t1),
                s2 * Mathf.Sin(t2),
                s2 * Mathf.Cos(t2));
        }
    }

    /// <summary>Alias of <see cref="rotation"/>; this implementation is already uniform.</summary>
    public Quaternion rotationUniform => rotation;

    public Color ColorHSV() => ColorHSV(0f, 1f, 0f, 1f, 0f, 1f, 1f, 1f);

    public Color ColorHSV(float hueMin, float hueMax)
        => ColorHSV(hueMin, hueMax, 0f, 1f, 0f, 1f, 1f, 1f);

    public Color ColorHSV(float hueMin, float hueMax, float satMin, float satMax)
        => ColorHSV(hueMin, hueMax, satMin, satMax, 0f, 1f, 1f, 1f);

    public Color ColorHSV(float hueMin, float hueMax, float satMin, float satMax,
                          float valueMin, float valueMax)
        => ColorHSV(hueMin, hueMax, satMin, satMax, valueMin, valueMax, 1f, 1f);

    public Color ColorHSV(float hueMin, float hueMax, float satMin, float satMax,
                          float valueMin, float valueMax, float alphaMin, float alphaMax)
    {
        float h = Mathf.Lerp(hueMin, hueMax, value);
        float s = Mathf.Lerp(satMin, satMax, value);
        float v = Mathf.Lerp(valueMin, valueMax, value);
        float a = Mathf.Lerp(alphaMin, alphaMax, value);

        Color c = Color.HSVToRGB(h, s, v, true);
        c.a = a;
        return c;
    }

    #endregion

    #region Extras (not in Unity's API, but useful for AI)

    /// <summary>True with the given probability (0 = never, 1 = always).</summary>
    public bool Chance(float probability) => value < probability;

    /// <summary>Coin flip.</summary>
    public bool NextBool() => (NextUInt() & 1u) != 0u;

    /// <summary>+1 or -1.</summary>
    public int Sign() => NextBool() ? 1 : -1;

    /// <summary>Normally distributed value (Box-Muller). Clamp it yourself if you need bounds.</summary>
    public float NextGaussian(float mean = 0f, float standardDeviation = 1f)
    {
        float u1 = Mathf.Max(1e-7f, value);
        float u2 = value;
        float z = Mathf.Sqrt(-2f * Mathf.Log(u1)) * Mathf.Cos(2f * Mathf.PI * u2);
        return mean + z * standardDeviation;
    }

    /// <summary>Random element from a list.</summary>
    public T Pick<T>(IReadOnlyList<T> items)
    {
        if (items == null || items.Count == 0)
            throw new ArgumentException("Cannot pick from an empty collection.", nameof(items));
        return items[Range(0, items.Count)];
    }

    /// <summary>
    /// Index chosen in proportion to the given weights. Negative weights are treated
    /// as zero. Returns -1 if every weight is zero.
    /// </summary>
    public int WeightedIndex(IReadOnlyList<float> weights)
    {
        if (weights == null || weights.Count == 0) return -1;

        float total = 0f;
        for (int i = 0; i < weights.Count; i++)
            if (weights[i] > 0f) total += weights[i];

        if (total <= 0f) return -1;

        float roll = value * total;
        for (int i = 0; i < weights.Count; i++)
        {
            float w = weights[i];
            if (w <= 0f) continue;
            roll -= w;
            if (roll <= 0f) return i;
        }
        return weights.Count - 1; // float drift safety net
    }

    /// <summary>In-place Fisher-Yates shuffle.</summary>
    public void Shuffle<T>(IList<T> items)
    {
        if (items == null) return;
        for (int i = items.Count - 1; i > 0; i--)
        {
            int j = Range(0, i + 1);
            T tmp = items[i];
            items[i] = items[j];
            items[j] = tmp;
        }
    }

    #endregion
}