using UnityEngine;

public static class LevelUpCalculator
{
    // Distribute N "points" (derived from cores) by weights and return a TrainingBonus delta.
    public static TrainingBonus DistributeByWeights(int points, LevelUpBucketSO bucket, TokenEconomy econ)
    {
        TrainingBonus delta = new TrainingBonus();
        if (points <= 0 || bucket == null || econ == null) return delta;

        float total = bucket.Total;

        for (int p = 0; p < points; p++)
        {
            float roll = Random.value * total;

            if ((roll -= bucket.hp) <= 0f) { delta.hp  += econ.hpPerCore;  continue; }
            if ((roll -= bucket.atk) <= 0f) { delta.atk += econ.atkPerCore; continue; }
            if ((roll -= bucket.def) <= 0f) { delta.def += econ.defPerCore; continue; }
            delta.spd += econ.spdPerCore;
        }
        return delta;
    }
}
