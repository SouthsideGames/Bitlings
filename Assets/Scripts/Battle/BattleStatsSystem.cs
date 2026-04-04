using System;
using System.Collections.Generic;
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

        bool allowJobs = _bm.Rules.allowJobPassives;
        bool allowBoosters = _bm.Rules.allowBoosters;

        // 2) Job (optional)
        var jctx = allowJobs ? _bm.GetJobCtxSafe(idx) : null;
        if (jctx != null && jctx.maxHpBonusPct > 0f)
            hp = hp * (1f + jctx.maxHpBonusPct);

        // Build Title context.
        // IMPORTANT: Use the non-recursive context builder so effective stat evaluation
        // does not call back into GetFinalMaxHPForIndex (which itself uses GetEffectivePlayer).
        var ctx = _bm.BuildTitleContextForIndexUsingMaxSafe(idx, hp);

        // 3) Titles (non-conditional). These keys match TitlesAdapter conventions in your project.
        string ownedId = _bm.GetTeamTitleIdSafe(idx);
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

        // 5) Temp boosters (flat) (optional)
        if (allowBoosters && BattleTempBuffs.I != null)
        {
            hp += Mathf.Max(0, BattleTempBuffs.I.GetPlayerHPBonus());
            atk += Mathf.Max(0, BattleTempBuffs.I.GetPlayerAtkBonus());
            def += Mathf.Max(0, BattleTempBuffs.I.GetPlayerDefenseBonus());
            spd += Mathf.Max(0, BattleTempBuffs.I.GetPlayerSpeedFlatBonus());
        }

        var booster = allowBoosters ? BattleBoosterController.I : null;
        if (booster != null)
        {
            atk += Mathf.Max(0, booster.GetAttackBonus());
            spd += Mathf.Max(0, booster.GetSpeedBonus());
        }

        // First-turn job speed bonuses (existing design).
        if (jctx != null && jctx.speedBuffTurns > 0 && jctx.speedBonusPctFirstTurns != 0f)
            spd = spd * (1f + jctx.speedBonusPctFirstTurns);

        // 6) Status-based stat presentation modifiers.
        // We use this layer so status effects show red/green deltas in the same UI pipeline as Titles.
        try
        {
            StatusType st = _bm.GetTeamStatusTypeSafe(idx);
            float mag = _bm.GetTeamStatusMagnitudeSafe(idx);

            // Soaked: speed reduced (magnitude = pct). Default = 25%.
            if (st == StatusType.Soaked)
            {
                float pct = (mag > 0f) ? mag : 0.25f;
                pct = Mathf.Clamp01(pct);
                spd = spd * (1f - pct);
            }

            // Rally: allies gain minor ATK boost (aura). Default = +10%.
            float rallyPct = _bm.GetPlayerTeamRallyBonusPctSafe();
            if (rallyPct > 0f)
                atk = atk * (1f + rallyPct);
        }
        catch { /* don't break stats UI if status arrays are mis-sized */ }

        return new BattleStatBlock
        {
            maxHP = Mathf.Max(1, Mathf.RoundToInt(hp)),
            atk = Mathf.Max(1, Mathf.RoundToInt(atk)),
            def = Mathf.Max(0, Mathf.RoundToInt(def)),
            spd = Mathf.Max(1, Mathf.RoundToInt(spd)),
        };
    }

    // ─────────────────────────────────────────────────────────
    // Stat breakdown helpers (for tooltips / explainability)
    // ─────────────────────────────────────────────────────────

    public struct StatBreakdownStages
    {
        public BattleStatBlock adjusted;
        public BattleStatBlock afterJob;
        public BattleStatBlock afterTitles;
        public BattleStatBlock afterConditionals;
        public BattleStatBlock afterTemp;
        public BattleStatBlock afterBoosters;
        public BattleStatBlock final;
    }

    /// <summary>
    /// Builds intermediate stage values for the player's active slot.
    /// Intended for UI explainability (tooltips). Does not change RNG.
    /// </summary>
    public StatBreakdownStages GetPlayerBreakdownStages(int idx)
    {
        EnsureAdjustedReady();

        var stages = new StatBreakdownStages();
        stages.adjusted = GetAdjustedPlayer(idx);

        if (_bm == null)
        {
            stages.afterJob = stages.afterTitles = stages.afterConditionals = stages.afterTemp = stages.afterBoosters = stages.final = stages.adjusted;
            return stages;
        }

        // Start from adjusted.
        float hp = stages.adjusted.maxHP;
        float atk = stages.adjusted.atk;
        float def = stages.adjusted.def;
        float spd = stages.adjusted.spd;

        bool allowJobs = _bm.Rules.allowJobPassives;
        bool allowBoosters = _bm.Rules.allowBoosters;

        // 2) Job
        var jctx = allowJobs ? _bm.GetJobCtxSafe(idx) : null;
        if (jctx != null && jctx.maxHpBonusPct > 0f)
            hp = hp * (1f + jctx.maxHpBonusPct);

        // First-turn job speed bonuses
        if (jctx != null && jctx.speedBuffTurns > 0 && jctx.speedBonusPctFirstTurns != 0f)
            spd = spd * (1f + jctx.speedBonusPctFirstTurns);

        stages.afterJob = new BattleStatBlock
        {
            maxHP = Mathf.Max(1, Mathf.RoundToInt(hp)),
            atk = Mathf.Max(1, Mathf.RoundToInt(atk)),
            def = Mathf.Max(0, Mathf.RoundToInt(def)),
            spd = Mathf.Max(1, Mathf.RoundToInt(spd)),
        };

        // Build Title context.
        var ctx = _bm.BuildTitleContextForIndexUsingMaxSafe(idx, hp);

        string ownedId = _bm.GetTeamTitleIdSafe(idx);
        var defSO = _bm.GetTeamDefSafe(idx);
        int lvl = _bm.GetTeamLevelSafe(idx);

        // 3) Titles (non-conditional)
        if (!string.IsNullOrEmpty(ownedId) && defSO != null)
        {
            hp = TitlesAdapter.GetStatValue(ownedId, defSO, lvl, "HP", ctx, hp);
            atk = TitlesAdapter.GetStatValue(ownedId, defSO, lvl, "Attack", ctx, atk);
            def = TitlesAdapter.GetStatValue(ownedId, defSO, lvl, "Defense", ctx, def);
            spd = TitlesAdapter.GetStatValue(ownedId, defSO, lvl, "Speed", ctx, spd);
        }

        stages.afterTitles = new BattleStatBlock
        {
            maxHP = Mathf.Max(1, Mathf.RoundToInt(hp)),
            atk = Mathf.Max(1, Mathf.RoundToInt(atk)),
            def = Mathf.Max(0, Mathf.RoundToInt(def)),
            spd = Mathf.Max(1, Mathf.RoundToInt(spd)),
        };

        // 4) Conditional title mods
        var cmods = _bm.GetConditionalModsForIndexSafe(idx);
        atk = (atk + Mathf.Max(0, cmods.atkFlat)) * (1f + Mathf.Max(0f, cmods.atkPct));
        def = (def + Mathf.Max(0, cmods.defFlat));
        spd = (spd + Mathf.Max(0, cmods.spdFlat)) * (1f + Mathf.Max(0f, cmods.spdPct));
        hp = hp * (1f + Mathf.Max(0f, cmods.hpPct));

        stages.afterConditionals = new BattleStatBlock
        {
            maxHP = Mathf.Max(1, Mathf.RoundToInt(hp)),
            atk = Mathf.Max(1, Mathf.RoundToInt(atk)),
            def = Mathf.Max(0, Mathf.RoundToInt(def)),
            spd = Mathf.Max(1, Mathf.RoundToInt(spd)),
        };

        // 5) Temp boosters
        if (allowBoosters && BattleTempBuffs.I != null)
        {
            hp += Mathf.Max(0, BattleTempBuffs.I.GetPlayerHPBonus());
            atk += Mathf.Max(0, BattleTempBuffs.I.GetPlayerAtkBonus());
            def += Mathf.Max(0, BattleTempBuffs.I.GetPlayerDefenseBonus());
            spd += Mathf.Max(0, BattleTempBuffs.I.GetPlayerSpeedFlatBonus());
        }

        stages.afterTemp = new BattleStatBlock
        {
            maxHP = Mathf.Max(1, Mathf.RoundToInt(hp)),
            atk = Mathf.Max(1, Mathf.RoundToInt(atk)),
            def = Mathf.Max(0, Mathf.RoundToInt(def)),
            spd = Mathf.Max(1, Mathf.RoundToInt(spd)),
        };

        // 6) Booster controller (currently only ATK flat, per existing design)
        var booster = allowBoosters ? BattleBoosterController.I : null;
        if (booster != null)
        {
            atk += Mathf.Max(0, booster.GetAttackBonus());
            spd += Mathf.Max(0, booster.GetSpeedBonus());
        }

        stages.afterBoosters = new BattleStatBlock
        {
            maxHP = Mathf.Max(1, Mathf.RoundToInt(hp)),
            atk = Mathf.Max(1, Mathf.RoundToInt(atk)),
            def = Mathf.Max(0, Mathf.RoundToInt(def)),
            spd = Mathf.Max(1, Mathf.RoundToInt(spd)),
        };

        // Status-based stat presentation modifiers (so tooltips match UI colors).
        float atkF = stages.afterBoosters.atk;
        float spdF = stages.afterBoosters.spd;
        try
        {
            StatusType st = _bm.GetTeamStatusTypeSafe(idx);
            float mag = _bm.GetTeamStatusMagnitudeSafe(idx);
            if (st == StatusType.Soaked)
            {
                float pct = (mag > 0f) ? mag : 0.25f;
                pct = Mathf.Clamp01(pct);
                spdF = spdF * (1f - pct);
            }

            float rallyPct = _bm.GetPlayerTeamRallyBonusPctSafe();
            if (rallyPct > 0f)
                atkF = atkF * (1f + rallyPct);
        }
        catch { }

        stages.final = new BattleStatBlock
        {
            maxHP = stages.afterBoosters.maxHP,
            atk = Mathf.Max(1, Mathf.RoundToInt(atkF)),
            def = stages.afterBoosters.def,
            spd = Mathf.Max(1, Mathf.RoundToInt(spdF)),
        };
        return stages;
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

        string wildId = _bm.Rules.allowTitles ? _bm.WildCombatIdForTitles : null;
        if (!string.IsNullOrEmpty(wildId))
        {
            // Build context against the caller's current working max HP to avoid
            // HP% drift when titles modify wild max HP.
            var ctx = _bm.BuildTitleContextForWildUsingMaxSafe(hp);

            // 3) Titles (non-conditional)
            hp = TitlesAdapter.GetStatValue(wildId, _bm.WildDef, _bm.WildLevel, "HP", ctx, hp);
            atk = TitlesAdapter.GetStatValue(wildId, _bm.WildDef, _bm.WildLevel, "Attack", ctx, atk);
            def = TitlesAdapter.GetStatValue(wildId, _bm.WildDef, _bm.WildLevel, "Defense", ctx, def);
            spd = TitlesAdapter.GetStatValue(wildId, _bm.WildDef, _bm.WildLevel, "Speed", ctx, spd);

            // 4) Conditional title mods (Clutch Booster, AllyCount conditions, etc.)
            // Player side already applies these in GetEffectivePlayer; wild must do the same.
            // Use HP% based on *effective* max HP after the non-conditional title pass.
            float hp01 = _bm.GetWildHp01UsingMaxSafe(hp);
            var wmods = TitlesAdapter.GetConditionalBattleMods(wildId, hp01, alliesAlive: 0, winStreak: 0);

            atk = (atk + Mathf.Max(0, wmods.atkFlat)) * (1f + Mathf.Max(0f, wmods.atkPct));
            def = (def + Mathf.Max(0, wmods.defFlat));
            spd = (spd + Mathf.Max(0, wmods.spdFlat)) * (1f + Mathf.Max(0f, wmods.spdPct));
            hp = hp * (1f + Mathf.Max(0f, wmods.hpPct));
        }

        // Status-based stat presentation modifiers (for red/green stat rows).
        try
        {
            StatusType st = _bm.GetWildStatusTypeSafe();
            float mag = _bm.GetWildStatusMagnitudeSafe();

            if (st == StatusType.Soaked)
            {
                float pct = (mag > 0f) ? mag : 0.25f;
                pct = Mathf.Clamp01(pct);
                spd = spd * (1f - pct);
            }

            float rallyPct = _bm.GetWildRallyBonusPctSafe();
            if (rallyPct > 0f)
                atk = atk * (1f + rallyPct);
        }
        catch { }

        return new BattleStatBlock
        {
            maxHP = Mathf.Max(1, Mathf.RoundToInt(hp)),
            atk = Mathf.Max(1, Mathf.RoundToInt(atk)),
            def = Mathf.Max(0, Mathf.RoundToInt(def)),
            spd = Mathf.Max(1, Mathf.RoundToInt(spd)),
        };
    }

    /// <summary>
    /// Wild stat breakdown stages for UI explainability.
    /// </summary>
    public StatBreakdownStages GetWildBreakdownStages()
    {
        EnsureAdjustedReady();

        var stages = new StatBreakdownStages();
        stages.adjusted = _wildAdjusted;

        if (_bm == null || _bm.WildDef == null)
        {
            stages.afterJob = stages.afterTitles = stages.afterConditionals = stages.afterTemp = stages.afterBoosters = stages.final = stages.adjusted;
            return stages;
        }

        // Wild currently has no job modifiers and no temp boosters (design).
        float hp = stages.adjusted.maxHP;
        float atk = stages.adjusted.atk;
        float def = stages.adjusted.def;
        float spd = stages.adjusted.spd;

        stages.afterJob = stages.adjusted;

        string wildId = _bm.Rules.allowTitles ? _bm.WildCombatIdForTitles : null;
        if (!string.IsNullOrEmpty(wildId))
        {
            var ctx = _bm.BuildTitleContextForWildUsingMaxSafe(hp);

            // Titles (non-conditional)
            hp = TitlesAdapter.GetStatValue(wildId, _bm.WildDef, _bm.WildLevel, "HP", ctx, hp);
            atk = TitlesAdapter.GetStatValue(wildId, _bm.WildDef, _bm.WildLevel, "Attack", ctx, atk);
            def = TitlesAdapter.GetStatValue(wildId, _bm.WildDef, _bm.WildLevel, "Defense", ctx, def);
            spd = TitlesAdapter.GetStatValue(wildId, _bm.WildDef, _bm.WildLevel, "Speed", ctx, spd);
        }

        stages.afterTitles = new BattleStatBlock
        {
            maxHP = Mathf.Max(1, Mathf.RoundToInt(hp)),
            atk = Mathf.Max(1, Mathf.RoundToInt(atk)),
            def = Mathf.Max(0, Mathf.RoundToInt(def)),
            spd = Mathf.Max(1, Mathf.RoundToInt(spd)),
        };

        if (!string.IsNullOrEmpty(wildId))
        {
            float hp01 = _bm.GetWildHp01UsingMaxSafe(hp);
            var wmods = TitlesAdapter.GetConditionalBattleMods(wildId, hp01, alliesAlive: 0, winStreak: 0);

            atk = (atk + Mathf.Max(0, wmods.atkFlat)) * (1f + Mathf.Max(0f, wmods.atkPct));
            def = (def + Mathf.Max(0, wmods.defFlat));
            spd = (spd + Mathf.Max(0, wmods.spdFlat)) * (1f + Mathf.Max(0f, wmods.spdPct));
            hp = hp * (1f + Mathf.Max(0f, wmods.hpPct));
        }

        stages.afterConditionals = new BattleStatBlock
        {
            maxHP = Mathf.Max(1, Mathf.RoundToInt(hp)),
            atk = Mathf.Max(1, Mathf.RoundToInt(atk)),
            def = Mathf.Max(0, Mathf.RoundToInt(def)),
            spd = Mathf.Max(1, Mathf.RoundToInt(spd)),
        };

        stages.afterTemp = stages.afterConditionals;
        stages.afterBoosters = stages.afterConditionals;

        // Status-based stat presentation modifiers.
        float atkF = stages.afterBoosters.atk;
        float spdF = stages.afterBoosters.spd;
        try
        {
            StatusType st = _bm.GetWildStatusTypeSafe();
            float mag = _bm.GetWildStatusMagnitudeSafe();
            if (st == StatusType.Soaked)
            {
                float pct = (mag > 0f) ? mag : 0.25f;
                pct = Mathf.Clamp01(pct);
                spdF = spdF * (1f - pct);
            }

            float rallyPct = _bm.GetWildRallyBonusPctSafe();
            if (rallyPct > 0f)
                atkF = atkF * (1f + rallyPct);
        }
        catch { }

        stages.final = new BattleStatBlock
        {
            maxHP = stages.afterBoosters.maxHP,
            atk = Mathf.Max(1, Mathf.RoundToInt(atkF)),
            def = stages.afterBoosters.def,
            spd = Mathf.Max(1, Mathf.RoundToInt(spdF)),
        };
        return stages;
    }

    // ─────────────────────────────────────────────────────────
    // Stat breakdown lines (for stat breakdown panel)
    // ─────────────────────────────────────────────────────────

    public struct StatBreakdownLine
    {
        public string source;
        public BattleStatKind stat;
        public int delta;
    }

    public List<StatBreakdownLine> GetPlayerStatLines(int idx)
    {
        var s = GetPlayerBreakdownStages(idx);
        var lines = new List<StatBreakdownLine>(16);
        AddDeltas(lines, s.adjusted, s.afterJob, "Job");
        AddDeltas(lines, s.afterJob, s.afterTitles, "Titles");
        AddDeltas(lines, s.afterTitles, s.afterConditionals, "Conditional");
        AddDeltas(lines, s.afterConditionals, s.afterTemp, "Temp Buff");
        AddDeltas(lines, s.afterTemp, s.afterBoosters, "Booster");
        AddDeltas(lines, s.afterBoosters, s.final, "Status Effect");
        return lines;
    }

    public List<StatBreakdownLine> GetWildStatLines()
    {
        var s = GetWildBreakdownStages();
        var lines = new List<StatBreakdownLine>(16);
        AddDeltas(lines, s.adjusted, s.afterJob, "Job");
        AddDeltas(lines, s.afterJob, s.afterTitles, "Titles");
        AddDeltas(lines, s.afterTitles, s.afterConditionals, "Conditional");
        AddDeltas(lines, s.afterConditionals, s.afterTemp, "Temp Buff");
        AddDeltas(lines, s.afterTemp, s.afterBoosters, "Booster");
        AddDeltas(lines, s.afterBoosters, s.final, "Status Effect");
        return lines;
    }

    private static void AddDeltas(List<StatBreakdownLine> list, BattleStatBlock from, BattleStatBlock to, string source)
    {
        TryAdd(list, source, BattleStatKind.HP,  to.maxHP - from.maxHP);
        TryAdd(list, source, BattleStatKind.ATK, to.atk   - from.atk);
        TryAdd(list, source, BattleStatKind.DEF, to.def   - from.def);
        TryAdd(list, source, BattleStatKind.SPD, to.spd   - from.spd);
    }

    private static void TryAdd(List<StatBreakdownLine> list, string source, BattleStatKind stat, int delta)
    {
        if (delta == 0) return;
        list.Add(new StatBreakdownLine { source = source, stat = stat, delta = delta });
    }
}
