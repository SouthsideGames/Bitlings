using UnityEngine;
using System.Collections.Generic;

// ─────────────────────────────────────────────────────────────
// EncounterManager.BossCadence
// Boss spawn cadence, weighted boss selection, and cadence persistence updates.
// ─────────────────────────────────────────────────────────────

public partial class EncounterManager
{
    // ------------- BOSS HELPERS -------------

    private bool ShouldSpawnBoss(int encountersSinceBoss, int bossEveryN)
    {
        if (bossEveryN < 1) bossEveryN = 10;
        return encountersSinceBoss >= (bossEveryN - 1);
    }

    private MonsterDataSO PickBossWeighted(MonsterLibrarySO lib, string lastBossId)
    {
        if (!lib || lib.monsters == null || lib.monsters.Length == 0) return null;

        var pool = BuildBossPool(lib, lastBossId, allowUncatchableOnly: true);
        if (pool.Count == 0)
        {
            pool = BuildBossPool(lib, excludeId: null, allowUncatchableOnly: true);
            if (pool.Count == 0) return null;
        }

        int total = 0;
        for (int i = 0; i < pool.Count; i++)
            total += Mathf.Max(1, pool[i].bossWeight);

        int r = UnityEngine.Random.Range(0, total);
        int acc = 0;
        for (int i = 0; i < pool.Count; i++)
        {
            acc += Mathf.Max(1, pool[i].bossWeight);
            if (r < acc) return pool[i];
        }
        return pool[pool.Count - 1];
    }

    private List<MonsterDataSO> BuildBossPool(MonsterLibrarySO lib, string excludeId, bool allowUncatchableOnly)
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

    private void AfterBattleCadenceUpdate(
        ref int encountersSinceBoss,
        bool wasBoss,
        MonsterDataSO bossUsed,
        ref string lastBossId)
    {
        if (wasBoss)
        {
            encountersSinceBoss = 0;
            lastBossId = bossUsed ? bossUsed.id : null;
        }
        else
        {
            encountersSinceBoss = Mathf.Max(0, encountersSinceBoss + 1);
        }
    }
}
