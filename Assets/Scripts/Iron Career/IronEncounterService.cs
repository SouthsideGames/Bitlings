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
    private string _lastWildMonsterId;

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

        if (_state.lastRolledWild != null && _state.lastRolledWild.def != null)
            return _state.lastRolledWild;

        var library = MonsterLibraryLocator.Lib;
        var catalog = MonsterCatalog.All;

        List<MonsterDataSO> all = new List<MonsterDataSO>();

        try
        {
            if (catalog != null)
            {
                for (int i = 0; i < catalog.Count; i++)
                {
                    var def = catalog[i];
                    if (def == null) continue;
                    if (library != null && !library.IsAvailable(def)) continue;
                    all.Add(def);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[IronEncounterService] Failed to build wild pool from MonsterCatalog. Error: {ex.Message}");
        }

        // Fallback to the raw monsters list if catalog-backed pool is empty/missing.
        if ((all == null || all.Count == 0) && library != null && library.monsters != null)
        {
            all = new List<MonsterDataSO>(library.monsters.Length);
            foreach (var m in library.monsters)
            {
                if (m != null && library.IsAvailable(m))
                    all.Add(m);
            }
        }

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
            if (def.uncatchable) continue;
            if (partyDefs.Contains(def)) continue;
            candidates.Add(def);
        }

        if (candidates.Count == 0)
            candidates = all; // fallback if everything is excluded

        if (!string.IsNullOrEmpty(_lastWildMonsterId) && candidates.Count > 1)
        {
            var noRepeat = new List<MonsterDataSO>(candidates.Count);
            for (int i = 0; i < candidates.Count; i++)
            {
                var c = candidates[i];
                if (c == null) continue;
                if (string.Equals(c.id, _lastWildMonsterId, StringComparison.Ordinal)) continue;
                noRepeat.Add(c);
            }

            if (noRepeat.Count > 0)
                candidates = noRepeat;
        }

        int index = _rng.NextInt(0, candidates.Count);
        var chosen = candidates[index];
        _lastWildMonsterId = chosen != null ? chosen.id : null;

        int level = ComputeWildLevel(_state.wins, _rng);

        // Wild enemies intentionally use normal track-based title rolls.
        // Curated ironTitles are reserved for player-side starter/hire generation.
        var title = _titleRoller.RollLockedTitle(chosen, level, _rng, isWild: true);

        // Roll shiny: same base chance as normal encounters. Iron species must have shiny art.
        bool shiny = false;
        if (chosen.shinyIcon != null)
        {
            float mul = (WorldEventSystem.I != null) ? WorldEventSystem.I.GetWildShinyChanceMultiplier() : 1f;
            float chance = Mathf.Clamp01(0.01f * Mathf.Max(0f, mul));
            shiny = _rng != null && _rng.Chance(chance);
        }

        var wild = new IronMonster(chosen, level, curHp: -1f, locked: title, shiny: shiny);

        // ensure full hp snapshot
        wild.maxHp = Mathf.Max(1f, BattleCalc.CalcHP(chosen, level));
        wild.hp = wild.maxHp;

        _state.lastRolledWild = wild;

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