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
        public float offenseMul;
        public float defenseMul;
        public float earlyEdge;
        public float coinMul;
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
            baseCoins *= Mathf.Max(0.5f, i.coinMul);
            coins = Mathf.RoundToInt(baseCoins);
        }

        return new Output { victory = win, coins = coins };
    }
}
