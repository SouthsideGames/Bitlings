using System;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;

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
        if (_state == null || _rng == null || _titleRoller == null)
            return null;

        // Grab the MonsterLibrarySO that exists in memory (ScriptableObject)
        var libs = Resources.FindObjectsOfTypeAll<MonsterLibrarySO>();
        MonsterLibrarySO library = (libs != null && libs.Length > 0) ? libs[0] : null;

        // AllAvailable is a PROPERTY IEnumerable<MonsterDataSO> (NOT a method)
        List<MonsterDataSO> all = (library != null && library.AllAvailable != null)
            ? library.AllAvailable.Where(m => m != null).ToList()
            : new List<MonsterDataSO>();

        if (all.Count == 0)
        {
            Debug.LogError("[IronEncounterService] MonsterLibrary missing or AllAvailable is empty.");
            return null;
        }

        // Exclude current party defs so wild isn't the same as your starter unless necessary
        var partyDefs = new HashSet<MonsterDataSO>();
        if (_state.party != null)
        {
            foreach (var m in _state.party)
                if (m != null && m.def != null)
                    partyDefs.Add(m.def);
        }

        var candidates = new List<MonsterDataSO>();
        for (int i = 0; i < all.Count; i++)
        {
            var def = all[i];
            if (def == null) continue;
            if (partyDefs.Contains(def)) continue;
            candidates.Add(def);
        }

        if (candidates.Count == 0)
            candidates = all; // fallback if everything is excluded

        int index = _rng.NextInt(0, candidates.Count);
        var chosen = candidates[index];

        int level = ComputeWildLevel(_state.wins, _rng);

        var title = _titleRoller.RollLockedTitle(chosen, level, _rng, isWild: true);
        var wild = new IronMonster(chosen, level, curHp: -1f, locked: title);

        // ensure full hp snapshot
        wild.maxHp = Mathf.Max(1f, BattleCalc.CalcHP(chosen, level));
        wild.hp = wild.maxHp;

        return wild;
    }

    public void ClearWildCache()
    {
        _state.lastRolledWild = null;
    }

    public static int ComputeWildLevel(int wins, IronRngStream rng)
    {
        wins = Mathf.Max(0, wins);

        int lvl = 1 + wins;
        int var = rng != null ? rng.NextInt(-1, 2) : 0;
        lvl += var;

        if (wins > 0 && (wins % 3) == 0)
            lvl += 1;

        return Mathf.Max(1, lvl);
    }
}