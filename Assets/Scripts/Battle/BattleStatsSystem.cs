using System;
using UnityEngine;

public struct BattleStatBlock
{
    public int maxHP;
    public int atk;
    public int def;
    public int spd;

    public int Get(BattleStatKind k)
    {
        switch (k)
        {
            case BattleStatKind.HP:  return maxHP;
            case BattleStatKind.ATK: return atk;
            case BattleStatKind.DEF: return def;
            case BattleStatKind.SPD: return spd;
            default: return 0;
        }
    }
}

/// <summary>
/// BattleStatsSystem is a single source of truth for all battle-time stat values.
///
/// It is intentionally designed as a pure computation layer sitting next to BattleManager.
/// Over time, other scripts should stop computing stats directly and instead query this.
///
/// Hierarchy per combatant:
///  1) Adjusted baseline (player: level + training + permanent; wild: level + encounter scalar)
///  2) Job modifiers (where applicable)
///  3) Title modifiers (BattleStart/StatBooster/etc.)
///  4) Conditional title modifiers (HP% thresholds, allies alive, win streak, etc.)
///  5) Temporary boosters (BattleTempBuffs, BattleBoosterController)
///
/// Notes
///  - DEF minimum is 0, others minimum is 1.
///  - HP here refers to MAX HP.
/// </summary>
public sealed class BattleStatsSystem
{
    private readonly BattleManager _bm;

    private BattleStatBlock[] _playerAdjusted; // per slot
    private BattleStatBlock _wildAdjusted;

    private bool _dirtyPlayer;
    private bool _dirtyWild;

    public BattleStatsSystem(BattleManager bm)
    {
        _bm = bm;
        _dirtyPlayer = true;
        _dirtyWild = true;
    }

    public void RebuildAdjustedBaselines()
    {
        RebuildPlayerAdjusted();
        RebuildWildAdjusted();
    }

    public void MarkDirtyAll()
    {
        _dirtyPlayer = true;
        _dirtyWild = true;
    }

    public void MarkDirtyPlayer() => _dirtyPlayer = true;
    public void MarkDirtyWild() => _dirtyWild = true;

    private void EnsureAdjustedReady()
    {
        if (_dirtyPlayer) RebuildPlayerAdjusted();
        if (_dirtyWild) RebuildWildAdjusted();
    }

    private void RebuildPlayerAdjusted()
    {
        _dirtyPlayer = false;

        int count = _bm != null ? _bm.TeamCountSafe : 0;
        if (count <= 0)
        {
            _playerAdjusted = null;
            return;
        }

        if (_playerAdjusted == null || _playerAdjusted.Length != count)
            _playerAdjusted = new BattleStatBlock[count];

        for (int i = 0; i < count; i++)
        {
            _bm.GetProgressionTotalsForIndex(i, out int hp, out int atk, out int def, out int spd, out _);
            _playerAdjusted[i] = new BattleStatBlock
            {
                maxHP = Mathf.Max(1, hp),
                atk = Mathf.Max(1, atk),
                def = Mathf.Max(0, def),
                spd = Mathf.Max(1, spd),
            };
        }
    }

    private void RebuildWildAdjusted()
    {
        _dirtyWild = false;

        if (_bm == null || _bm.WildDef == null)
        {
            _wildAdjusted = default;
            return;
        }

        int baseHP = Mathf.RoundToInt(Mathf.Max(1f, _bm.WildBaseMaxHP));
        int baseATK = Mathf.RoundToInt(Mathf.Max(1f, _bm.WildBaseAttackPerTurn));
        int baseDEF = BattleCalc.CalcDefense(_bm.WildDef, _bm.WildLevel);
        int baseSPD = BattleCalc.CalcSpeed(_bm.WildDef, _bm.WildLevel);

        _wildAdjusted = new BattleStatBlock
        {
            maxHP = Mathf.Max(1, baseHP),
            atk = Mathf.Max(1, baseATK),
            def = Mathf.Max(0, baseDEF),
            spd = Mathf.Max(1, baseSPD),
        };
    }

    public BattleStatBlock GetAdjustedPlayer(int idx)
    {
        EnsureAdjustedReady();
        if (_playerAdjusted == null || idx < 0 || idx >= _playerAdjusted.Length)
            return default;
        return _playerAdjusted[idx];
    }

    public BattleStatBlock GetAdjustedWild()
    {
        EnsureAdjustedReady();
        return _wildAdjusted;
    }

    public BattleStatBlock GetEffectivePlayer(int idx)
    {
        EnsureAdjustedReady();
        var adj = GetAdjustedPlayer(idx);
        if (_bm == null) return adj;

        // Start from adjusted.
        float hp = adj.maxHP;
        float atk = adj.atk;
        float def = adj.def;
        float spd = adj.spd;

        // 2) Job (max HP already baked into teamMaxHP elsewhere, but keep defensive).
        var jctx = _bm.GetJobCtxSafe(idx);
        if (jctx != null && jctx.maxHpBonusPct > 0f)
            hp = hp * (1f + jctx.maxHpBonusPct);

        // Build Title context.
        // IMPORTANT: Use the non-recursive context builder so effective stat evaluation
        // does not call back into GetFinalMaxHPForIndex (which itself uses GetEffectivePlayer).
        var ctx = _bm.BuildTitleContextForIndexUsingMaxSafe(idx, hp);

        // 3) Titles (non-conditional). These keys match TitlesAdapter conventions in your project.
        string ownedId = _bm.GetTeamIdSafe(idx);
        var defSO = _bm.GetTeamDefSafe(idx);
        int lvl = _bm.GetTeamLevelSafe(idx);

        if (!string.IsNullOrEmpty(ownedId) && defSO != null)
        {
            hp = TitlesAdapter.GetStatValue(ownedId, defSO, lvl, "HP", ctx, hp);
            atk = TitlesAdapter.GetStatValue(ownedId, defSO, lvl, "Attack", ctx, atk);
            def = TitlesAdapter.GetStatValue(ownedId, defSO, lvl, "Defense", ctx, def);
            spd = TitlesAdapter.GetStatValue(ownedId, defSO, lvl, "Speed", ctx, spd);
        }

        // 4) Conditional title mods
        var cmods = _bm.GetConditionalModsForIndexSafe(idx);
        atk = (atk + Mathf.Max(0, cmods.atkFlat)) * (1f + Mathf.Max(0f, cmods.atkPct));
        def = (def + Mathf.Max(0, cmods.defFlat));
        // NOTE: cmods.defPct is treated as incoming damage reduction in BattleManager (not a DEF stat multiplier).
        spd = (spd + Mathf.Max(0, cmods.spdFlat)) * (1f + Mathf.Max(0f, cmods.spdPct));
        hp = hp * (1f + Mathf.Max(0f, cmods.hpPct));

        // 5) Temp boosters (flat)
        if (BattleTempBuffs.I != null)
        {
            hp += Mathf.Max(0, BattleTempBuffs.I.GetPlayerHPBonus());
            atk += Mathf.Max(0, BattleTempBuffs.I.GetPlayerAtkBonus());
            def += Mathf.Max(0, BattleTempBuffs.I.GetPlayerDefenseBonus());
            spd += Mathf.Max(0, BattleTempBuffs.I.GetPlayerSpeedFlatBonus());
        }

        var booster = BattleBoosterController.I;
        if (booster != null)
        {
            atk += Mathf.Max(0, booster.GetAttackBonus());
        }

        // First-turn job speed bonuses (existing design).
        if (jctx != null && jctx.speedBuffTurns > 0 && jctx.speedBonusPctFirstTurns != 0f)
            spd = spd * (1f + jctx.speedBonusPctFirstTurns);

        return new BattleStatBlock
        {
            maxHP = Mathf.Max(1, Mathf.RoundToInt(hp)),
            atk = Mathf.Max(1, Mathf.RoundToInt(atk)),
            def = Mathf.Max(0, Mathf.RoundToInt(def)),
            spd = Mathf.Max(1, Mathf.RoundToInt(spd)),
        };
    }

    public BattleStatBlock GetEffectiveWild()
    {
        EnsureAdjustedReady();
        if (_bm == null || _bm.WildDef == null)
            return _wildAdjusted;

        float hp = _wildAdjusted.maxHP;
        float atk = _wildAdjusted.atk;
        float def = _wildAdjusted.def;
        float spd = _wildAdjusted.spd;

        string wildId = _bm.WildCombatIdForTitles;
        if (!string.IsNullOrEmpty(wildId))
        {
            var ctx = _bm.BuildTitleContextForWild();
            hp = TitlesAdapter.GetStatValue(wildId, _bm.WildDef, _bm.WildLevel, "HP", ctx, hp);
            atk = TitlesAdapter.GetStatValue(wildId, _bm.WildDef, _bm.WildLevel, "Attack", ctx, atk);
            def = TitlesAdapter.GetStatValue(wildId, _bm.WildDef, _bm.WildLevel, "Defense", ctx, def);
            spd = TitlesAdapter.GetStatValue(wildId, _bm.WildDef, _bm.WildLevel, "Speed", ctx, spd);
        }

        return new BattleStatBlock
        {
            maxHP = Mathf.Max(1, Mathf.RoundToInt(hp)),
            atk = Mathf.Max(1, Mathf.RoundToInt(atk)),
            def = Mathf.Max(0, Mathf.RoundToInt(def)),
            spd = Mathf.Max(1, Mathf.RoundToInt(spd)),
        };
    }
}
