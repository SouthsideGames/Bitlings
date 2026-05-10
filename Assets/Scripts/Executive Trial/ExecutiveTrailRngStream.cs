using System;


public sealed class ExecutiveTrailRngStream
{
    private System.Random _rng;

    public int Seed { get; private set; }

    public ExecutiveTrailRngStream(int seed)
    {
        Reset(seed);
    }

    public void Reset(int seed)
    {
        Seed = seed;
        _rng = new System.Random(seed);
    }

    public int NextInt(int minInclusive, int maxExclusive)
    {
        if (maxExclusive <= minInclusive) return minInclusive;
        return _rng.Next(minInclusive, maxExclusive);
    }

    public float Next01()
    {
        // [0,1)
        return (float)_rng.NextDouble();
    }

    public bool Chance(float p)
    {
        if (p <= 0f) return false;
        if (p >= 1f) return true;
        return Next01() < p;
    }
}
