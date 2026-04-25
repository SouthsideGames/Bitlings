using System.Collections.Generic;
using UnityEngine;

public static class BossRiftHelper
{
    // ----- Public API -----

    /// Decide if the next rift should be a boss.
    public static bool ShouldSpawnBoss(int riftsSinceBoss, int bossEveryN)
    {
        if (bossEveryN < 1) bossEveryN = 10;
        return riftsSinceBoss >= (bossEveryN - 1);
    }

    /// Weighted random pick of a boss Bitling from the library, excluding lastBossId.
    /// Falls back to including lastBossId if exclusion empties the pool.
    public static MonsterDataSO PickBossWeighted(MonsterLibrarySO lib, string lastBossId)
    {
        if (!lib || lib.monsters == null || lib.monsters.Length == 0) return null;

        var pool = BuildBossPool(lib, excludeId: lastBossId, allowUncatchableOnly: true);
        if (pool.Count == 0)
        {
            // Fallback: allow lastBossId if exclusion made pool empty
            pool = BuildBossPool(lib, excludeId: null, allowUncatchableOnly: true);
            if (pool.Count == 0) return null;
        }

        return WeightedPick(pool);
    }

    /// Update cadence after a battle.
    /// Returns the new riftsSinceBoss and (optionally) updates lastBossId when a boss spawned.
    public static void AfterBattle(ref int riftsSinceBoss, bool wasBoss, MonsterDataSO bossUsed, ref string lastBossId)
    {
        if (wasBoss)
        {
            riftsSinceBoss = 0;
            lastBossId = bossUsed ? bossUsed.id : null;
        }
        else
        {
            riftsSinceBoss = Mathf.Max(0, riftsSinceBoss + 1);
        }
    }

    // ----- Private helpers -----

    private static List<MonsterDataSO> BuildBossPool(MonsterLibrarySO lib, string excludeId, bool allowUncatchableOnly)
    {
        var list = new List<MonsterDataSO>();
        foreach (var m in lib.monsters)
        {
            if (!m) continue;
            if (!m.isBoss) continue;
            if (allowUncatchableOnly && !m.uncatchable) continue; 
            if (!string.IsNullOrEmpty(excludeId) && m.id == excludeId) continue;
            list.Add(m);
        }
        return list;
    }

    private static MonsterDataSO WeightedPick(List<MonsterDataSO> pool)
    {
        int total = 0;
        for (int i = 0; i < pool.Count; i++)
        {
            int w = Mathf.Max(1, pool[i].bossWeight);
            total += w;
        }
        int r = Random.Range(0, total); // 0..total-1
        int acc = 0;
        for (int i = 0; i < pool.Count; i++)
        {
            acc += Mathf.Max(1, pool[i].bossWeight);
            if (r < acc) return pool[i];
        }
        return pool[pool.Count - 1];
    }
}
