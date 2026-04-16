// ArenaModule — Bracket building helpers
// Port of the bracket-creation logic from LockAndAssignBrackets.js

namespace ArenaModule.Helpers;

public static class BracketHelper
{
    public const int BracketSize = 32;
    public const int MinRealForMerge = 8;

    public static readonly string[] BandNames = { "Low", "Standard", "High", "Elite" };

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

            if (candidates.Count > 0) break; // found at least one at this distance
        }

        if (candidates.Count == 0) return sourceBand;

        // prefer the larger pool
        candidates.Sort((a, b) => b.size.CompareTo(a.size));
        return candidates[0].band;
    }
}
