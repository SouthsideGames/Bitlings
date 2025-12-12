using UnityEngine;

public static class HeadlessBattle
{
    /// <summary>
    /// Compact headless input for win/credit simulation.
    /// Compose upstream (Jobs/Idle/Title systems) and pass the final multipliers here.
    /// </summary>
    public struct Input
    {
        public int   avgTeamLevel;
        public int   wildLevel;
        public int   basecreditPerWin;
        public float rewardMultiplier;
        public int   rngSeed;

        /// <summary>Offensive pressure scalar (>=0). Compose jobs, titles, etc.</summary>
        public float offenseMul;

        /// <summary>Defensive durability scalar (>=0). Compose jobs, titles, etc.</summary>
        public float defenseMul;

        /// <summary>
        /// NEW: Defensive per-type resist scalar (>=0). 
        /// Example: 0.75 for “-25% Fire damage taken” when fighting Fire.
        /// If unset/zero, treated as 1f (neutral).
        /// </summary>
        public float incomingTypeResistMul;

        /// <summary>Early momentum edge from idle/job systems only (additive bias to p).</summary>
        public float earlyEdge;

        /// <summary>Jobs/Idle credit multiplier only (title credit multipliers are applied at grant time).</summary>
        public float creditMul;
    }

    public struct Output
    {
        public bool victory;
        public int  credits;
    }

    public static Output Resolve(in Input i)
    {
        // Base win probability from level diff
        int   diff = i.avgTeamLevel - i.wildLevel;
        float p    = 0.5f + Mathf.Clamp(diff * 0.08f, -0.35f, 0.40f);

        // Normalize multipliers to safe domain (0 or negative -> 1)
        float offMul = SafeMul(i.offenseMul);
        float defMul = SafeMul(i.defenseMul) * SafeMul(i.incomingTypeResistMul); // ← include type resist here

        // Fold composed multipliers into probability
        // Tuning weights are intentionally conservative; adjust to taste.
        p += (offMul - 1f) * 0.30f;
        p += (defMul - 1f) * 0.30f;

        // Early momentum/edge (jobs/idle)
        p += i.earlyEdge;

        // Boundaries
        p = Mathf.Clamp01(p);
        p = Mathf.Clamp(p, 0.05f, 0.95f);

        var  rng = new System.Random(i.rngSeed);
        bool win = rng.NextDouble() < p;

        // Rewards
        int credits = 0;
        if (win)
        {
            float basecredits = (i.basecreditPerWin + Mathf.Max(0, i.wildLevel) * 1.5f) * Mathf.Max(0f, i.rewardMultiplier);
            basecredits *= Mathf.Max(0.5f, i.creditMul); // jobs/idle only; title credit mult applied later at grant
            credits = Mathf.RoundToInt(basecredits);
        }

        return new Output { victory = win, credits = credits };
    }

    /// <summary>Coerces invalid/zero multipliers to neutral (1f).</summary>
    private static float SafeMul(float m) => (m > 0f) ? m : 1f;

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

        if (fNoCrit != null && fNoCrit.FieldType == typeof(bool))  noCrit = (bool)fNoCrit.GetValue(boxed); else ok = false;
        if (fPct    != null && fPct.FieldType    == typeof(float)) pct    = (float)fPct.GetValue(boxed);    else ok = false;
        if (fFlat   != null && fFlat.FieldType   == typeof(int))   flat   = (int)fFlat.GetValue(boxed);     else ok = false;

        if (!ok) return false;
        view = new DamageFilterView { cannotBeCrit = noCrit, percentReduce = pct, flatReduce = flat };
        return true;
    }
}
