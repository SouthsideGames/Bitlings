using UnityEngine;
using System.Collections.Generic;

public static class LevelUpCalculator
{
    public static TrainingBonus DistributeByWeights(int points, LevelUpBucketSO bucket, TokenEconomySO econ)
    {
        TrainingBonus delta = new TrainingBonus();
        if (points <= 0 || bucket == null || econ == null) return delta;

        int picksPerLevel = Mathf.Max(1, bucket.picksPerLevel);

        for (int i = 0; i < points; i++)
        {
            // Track what we already picked this "level" if duplicates are disallowed
            HashSet<int> pickedThisLevel = bucket.allowDuplicatePicks ? null : new HashSet<int>();

            for (int p = 0; p < picksPerLevel; p++)
            {
                // Build a working weight table (respecting no-duplicates)
                int hpW  = bucket.hpWeight;
                int atkW = bucket.atkWeight;
                int defW = bucket.defWeight;
                int spdW = bucket.spdWeight;

                if (pickedThisLevel != null)
                {
                    if (pickedThisLevel.Contains(0)) hpW  = 0; // HP
                    if (pickedThisLevel.Contains(1)) atkW = 0; // ATK
                    if (pickedThisLevel.Contains(2)) defW = 0; // DEF
                    if (pickedThisLevel.Contains(3)) spdW = 0; // SPD
                }

                int total = hpW + atkW + defW + spdW;
                if (total <= 0) break; // nothing left to pick this level

                // Integer roll in [0, total)
                int roll = Random.Range(0, total);

                // Resolve pick
                int pickIdx; // 0=HP,1=ATK,2=DEF,3=SPD
                if ((roll -= hpW) < 0)       pickIdx = 0;
                else if ((roll -= atkW) < 0) pickIdx = 1;
                else if ((roll -= defW) < 0) pickIdx = 2;
                else                         pickIdx = 3;

                // Apply econ-scaled gain to the TrainingBonus
                switch (pickIdx)
                {
                    case 0: delta.hp  += econ.hpPerCore;  break;
                    case 1: delta.atk += econ.atkPerCore; break;
                    case 2: delta.def += econ.defPerCore; break;
                    default: delta.spd += econ.spdPerCore; break;
                }

                // Mark as picked if duplicates are not allowed
                pickedThisLevel?.Add(pickIdx);
            }
        }

        return delta;
    }
}
