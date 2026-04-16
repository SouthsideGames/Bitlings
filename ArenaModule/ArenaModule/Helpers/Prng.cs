// ArenaModule — Mulberry32 seeded PRNG (deterministic, matches the JS version exactly)

namespace ArenaModule.Helpers;

public sealed class Prng
{
    private int _state;

    public Prng(int seed)
    {
        _state = seed;
    }

    /// <summary>Returns a value in [0, 1).</summary>
    public double Next()
    {
        unchecked
        {
            _state += 0x6D2B79F5;
            int t = _state;
            t = Math.Imul(t ^ (int)((uint)t >> 15), 1 | t);
            t = (t + Math.Imul(t ^ (int)((uint)t >> 7), 61 | t)) ^ t;
            return ((uint)(t ^ (int)((uint)t >> 14))) / 4294967296.0;
        }
    }

    /// <summary>Returns an int in [0, maxExclusive).</summary>
    public int NextInt(int maxExclusive)
    {
        return (int)(Next() * maxExclusive);
    }

    /// <summary>Fisher-Yates shuffle (matches the JS version).</summary>
    public void Shuffle<T>(IList<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = (int)(Next() * (i + 1));
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}

/// <summary>Polyfill for Math.Imul (JS-compatible 32-bit integer multiply).</summary>
internal static class Math
{
    /// <summary>32-bit integer multiply matching JavaScript's Math.imul.</summary>
    public static int Imul(int a, int b)
    {
        // Exactly matches JS: truncates to 32-bit signed int
        return (int)((long)a * b);
    }
}
