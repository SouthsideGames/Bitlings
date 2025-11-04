using UnityEngine;

public static class HeadlessBattle
{
    public struct Input
    {
        public int   avgTeamLevel;
        public int   wildLevel;
        public int   baseCoinPerWin;
        public float rewardMultiplier;
        public int   rngSeed;

        // These already include Jobs/Idle + Titles (caller composes)
        public float offenseMul;
        public float defenseMul;

        public float earlyEdge;  // Jobs/Idle only
        public float coinMul;    // Jobs/Idle only (Title coin mult applied at grant time)
    }

    public struct Output
    {
        public bool victory;
        public int coins;
    }

    public static Output Resolve(in Input i)
    {
        int diff = i.avgTeamLevel - i.wildLevel;
        float p = 0.5f + Mathf.Clamp(diff * 0.08f, -0.35f, 0.40f);

        // Fold multipliers into win probability (bounded later)
        p += (i.offenseMul - 1f) * 0.30f;
        p += (i.defenseMul - 1f) * 0.30f;
        p += i.earlyEdge;

        p = Mathf.Clamp01(p);
        p = Mathf.Clamp(p, 0.05f, 0.95f);

        var rng = new System.Random(i.rngSeed);
        bool win = rng.NextDouble() < p;

        int coins = 0;
        if (win)
        {
            float baseCoins = (i.baseCoinPerWin + Mathf.Max(0, i.wildLevel) * 1.5f) * i.rewardMultiplier;
            baseCoins *= Mathf.Max(0.5f, i.coinMul); // jobs/idle only
            coins = Mathf.RoundToInt(baseCoins);
        }

        return new Output { victory = win, coins = coins };
    }

    // (Kept for parity if you ever want to mirror DamageFilter logic in headless detail sims later)
    private struct DamageFilterView { public bool cannotBeCrit; public float percentReduce; public int flatReduce; }
    private static bool TryUnboxDamageFilter(object boxed, out DamageFilterView view)
    {
        view = default;
        if (boxed == null) return false;
        var t = boxed.GetType();

        var fNoCrit = t.GetField("cannotBeCrit");
        var fPct    = t.GetField("percentReduce");
        var fFlat   = t.GetField("flatReduce");

        bool ok = true;
        bool noCrit = false; float pct = 0f; int flat = 0;

        if (fNoCrit != null && fNoCrit.FieldType == typeof(bool)) noCrit = (bool)fNoCrit.GetValue(boxed); else ok = false;
        if (fPct    != null && fPct.FieldType    == typeof(float)) pct    = (float)fPct.GetValue(boxed);   else ok = false;
        if (fFlat   != null && fFlat.FieldType   == typeof(int))   flat   = (int)fFlat.GetValue(boxed);    else ok = false;

        if (!ok) return false;
        view = new DamageFilterView { cannotBeCrit = noCrit, percentReduce = pct, flatReduce = flat };
        return true;
    }
}
