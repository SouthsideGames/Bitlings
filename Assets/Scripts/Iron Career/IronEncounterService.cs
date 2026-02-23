using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Deterministic wild selection + level scaling for Iron Career.
/// Produces a single wild snapshot used for BOTH the battle enemy and the hire offer.
/// </summary>
public sealed class IronEncounterService
{
    private readonly IronCareerRunState _state;
    private readonly IronRngStream _rng;
    private readonly IronTitleRoller _titleRoller;

    public IronEncounterService(IronCareerRunState state, IronRngStream rng, IronTitleRoller titleRoller)
    {
        _state = state;
        _rng = rng;
        _titleRoller = titleRoller;
    }

    public IronMonster RollNextWild()
    {
        // Cache so battle + hire offer share the same wild.
        if (_state.lastRolledWild != null && _state.lastRolledWild.def != null)
            return _state.lastRolledWild;

        var def = PickWildDefWeighted();
        if (def == null)
        {
            Debug.LogError("[IronEncounterService] No wild candidates available.");
            return null;
        }

        int lvl = ComputeWildLevel(_state.wins, _rng);
        var title = _titleRoller != null ? _titleRoller.RollLockedTitle(def, lvl, _rng, isWild: true) : null;

        var m = new IronMonster(def, lvl, curHp: -1f, locked: title);
        // if curHp < 0 -> auto-fill to max
        m.maxHp = Mathf.Max(1f, BattleCalc.CalcHP(def, lvl));
        m.hp = m.maxHp;

        _state.lastRolledWild = m;
        return m;
    }

    public void ClearWildCache()
    {
        _state.lastRolledWild = null;
    }

    private MonsterDataSO PickWildDefWeighted()
    {
        // Prefer MonsterCatalog list.
        var all = MonsterLibraryLocator.AllMonsters;
        if (all == null || all.Count == 0) return null;

        // Build weighted sum. We keep allocations minimal by two passes.
        int total = 0;
        for (int i = 0; i < all.Count; i++)
        {
            var d = all[i];
            if (d == null) continue;
            if (d.uncatchable) continue; // Iron battles are hire-driven; uncatchable doesn't belong.
            if (d.isBoss) continue;
            int w = (int)Mathf.Max(0, d.spawnWeight);
            total += w;
        }

        if (total <= 0) return null;

        int roll = _rng.NextInt(0, total);
        for (int i = 0; i < all.Count; i++)
        {
            var d = all[i];
            if (d == null) continue;
            if (d.uncatchable) continue;
            if (d.isBoss) continue;
            int w = (int)Mathf.Max(0, d.spawnWeight);
            if (w <= 0) continue;
            roll -= w;
            if (roll < 0)
                return d;
        }

        // Fallback shouldn't happen.
        return all[0];
    }

    public static int ComputeWildLevel(int wins, IronRngStream rng)
    {
        wins = Mathf.Max(0, wins);

        // base = 1 + wins
        int lvl = 1 + wins;

        // variance -1..+1 clamp >= 1
        int var = rng != null ? rng.NextInt(-1, 2) : 0;
        lvl += var;

        // milestone +1 on wins % 3 == 0 (but only after you have some wins)
        if (wins > 0 && (wins % 3) == 0)
            lvl += 1;

        return Mathf.Max(1, lvl);
    }
}
