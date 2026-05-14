// ArenaModule — Bracket building helpers
// Port of the bracket-creation logic from LockAndAssignBrackets.js

namespace ArenaModule.Helpers;

public static class BracketHelper
{
    public const int BracketSize = 32;
    public const int MinRealForMerge = 8;

    public static readonly string[] BandNames = { "Low", "Standard", "High", "Elite" };

    // Mirror of ArenaConstants.cs thresholds — must stay in sync with client.
    private const int ScoreBandStandardThreshold = 50;
    private const int ScoreBandHighThreshold = 100;
    private const int ScoreBandEliteThreshold = 175;

    /// <summary>Computes the score band (0–3) from a validated arena score.</summary>
    public static int ScoreToBand(int score)
    {
        if (score >= ScoreBandEliteThreshold) return 3;
        if (score >= ScoreBandHighThreshold)  return 2;
        if (score >= ScoreBandStandardThreshold) return 1;
        return 0;
    }

    /// <summary>
    /// Find the best adjacent band to merge a small pool into.
    /// Returns the target band index, or <paramref name="sourceBand"/> if no target found.
    /// </summary>
    public static int FindMergeTarget(List<List<RegistrationData>> pools, int sourceBand)
    {
        var candidates = new List<(int band, int size)>();

        for (int d = 1; d <= 3; d++)
        {
            int lo = sourceBand - d;
            int hi = sourceBand + d;

            if (lo >= 0 && pools[lo].Count > 0)
                candidates.Add((lo, pools[lo].Count));
            if (hi <= 3 && pools[hi].Count > 0)
                candidates.Add((hi, pools[hi].Count));

            if (candidates.Count > 0) break;
        }

        if (candidates.Count == 0) return sourceBand;

        candidates.Sort((a, b) => b.size.CompareTo(a.size));
        return candidates[0].band;
    }

    /// <summary>
    /// Splits <paramref name="pool"/> into evenly-sized chunks, each no larger than
    /// <paramref name="maxSize"/>. Players are distributed as evenly as possible so
    /// the last chunk is never tiny (e.g. 33 players → [17, 16], not [32, 1]).
    /// </summary>
    public static List<List<T>> SplitEvenly<T>(List<T> pool, int maxSize)
    {
        int n = pool.Count;
        if (n == 0) return new List<List<T>>();

        int k = (n + maxSize - 1) / maxSize; // ceil(n / maxSize)
        int baseSize = n / k;
        int extra = n % k;                   // first `extra` chunks get baseSize+1

        var chunks = new List<List<T>>(k);
        int offset = 0;
        for (int i = 0; i < k; i++)
        {
            int size = baseSize + (i < extra ? 1 : 0);
            chunks.Add(pool.GetRange(offset, size));
            offset += size;
        }
        return chunks;
    }
}
