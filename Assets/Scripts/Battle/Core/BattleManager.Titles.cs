using System;
using System.Collections.Generic;
using UnityEngine;

// ─────────────────────────────────────────────────────────────
// BattleManager.Titles
// Battle-start title application and runtime conditional title effects.
// ─────────────────────────────────────────────────────────────

public partial class BattleManager : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────
    // Battle-start Titles application
    // ─────────────────────────────────────────────────────────────
    private void ApplyBattleStartTitles()
    {
        // Player (active slot)
        try
        {
            if (teamIds != null && activeIndex >= 0 && activeIndex < teamIds.Length)
            {
                string ownedId = GetTeamTitleIdSafe(activeIndex);
                if (!string.IsNullOrEmpty(ownedId))
                {
                    TitlesAdapter.OnBattleStart(ownedId, wildDef, wildLevel);

                    // Cache credit multiplier for the active monster at battle start.
                    try
                    {
                        _cachedCreditMult = Mathf.Max(0f, TitlesAdapter.GetCreditMultOnVictory(ownedId, wildDef, wildLevel));
                    }
                    catch (Exception ex)
                    {
                        Debug.Log($"[BattleManager] Failed to cache credit multiplier: {ex.Message}");
                        _cachedCreditMult = 1f;
                    }

                    if (titleShieldHP != null && activeIndex < titleShieldHP.Length)
                        titleShieldHP[activeIndex] = Mathf.Max(0f, TitlesAdapter.GetBattleStartShieldRemaining(ownedId));
                }
            }
        }
        catch (Exception ex)
        {
            BattleLogger.Log($"[Titles] OnBattleStart(player) exception: {ex.Message}", LogScope.Battle);
        }

        // Wild
        try
        {
            if (string.IsNullOrEmpty(_wildCombatIdForTitles) || !_wildCombatIdForTitles.StartsWith("WILD::", StringComparison.OrdinalIgnoreCase))
                _wildCombatIdForTitles = (EncounterManager.I != null) ? EncounterManager.I.WildCombatId : null;

            if (string.IsNullOrEmpty(_wildCombatIdForTitles) || !_wildCombatIdForTitles.StartsWith("WILD::", StringComparison.OrdinalIgnoreCase))
                _wildCombatIdForTitles = BuildFallbackWildCombatId(wildDef);

            if (!string.IsNullOrEmpty(_wildCombatIdForTitles))
            {
                TitlesAdapter.OnBattleStart(_wildCombatIdForTitles, wildDef, wildLevel);
                wildTitleShieldHP = Mathf.Max(0f, TitlesAdapter.GetBattleStartShieldRemaining(_wildCombatIdForTitles));
            }

            RefreshWildEffectiveStatsFromTitles();
        }
        catch (Exception ex)
        {
            BattleLogger.Log($"[Titles] OnBattleStart(wild) exception: {ex.Message}", LogScope.Battle);
        }

        // Unified stat/UI sync contract.
        RequestBattleStatRebuild(BattleStatRebuildReason.BattleStart, forceRebuildAdjusted: true);
    }

    private TitleStatMods GetTitleModsForIndex(int idx)
    {
        if (teamIds != null && idx >= 0 && idx < teamIds.Length && !string.IsNullOrEmpty(teamIds[idx]))
            return TitlesAdapter.GetBattleStatMods(teamTitleIds[idx]);
        return default;
    }

    private TitleStatMods GetConditionalModsForIndex(int idx)
    {
        if (teamIds == null || teamDefs == null || teamLevels == null) return default;
        if (idx < 0 || idx >= teamIds.Length) return default;
        if (string.IsNullOrEmpty(teamIds[idx]) || teamDefs[idx] == null) return default;

        float curMax = GetActiveMaxHP_NoConditionals(teamMaxHP[idx], idx);

        float curHp = (teamHP != null && idx >= 0 && idx < teamHP.Length) ? teamHP[idx] : curMax;
        float hp01 = curMax > 0.01f ? Mathf.Clamp01(curHp / curMax) : 0f;

        int alliesAlive = 0;
        for (int i = 0; i < teamCount; i++)
            if (i != idx && teamHP != null && i < teamHP.Length && teamHP[i] > 0.01f) alliesAlive++;

        int winStreak = (EncounterManager.I != null) ? EncounterManager.I.CurrentWinStreak : 0;

        TitleContext ctx = TitleContext.Empty;
        ctx.selfHp01 = hp01;
        ctx.alliesAlive = alliesAlive;
        ctx.winStreak = winStreak;

        var def = teamDefs[idx];
        int lvl = teamLevels[idx];
        string ownedId = GetTeamTitleIdSafe(idx);

        TitleStatMods mods = default;
        mods.atkFlat = Mathf.RoundToInt(TitlesAdapter.GetStatValue(ownedId, def, lvl, "atkFlat", ctx, 0f));
        mods.atkPct = TitlesAdapter.GetStatValue(ownedId, def, lvl, "atkPct", ctx, 0f);

        mods.defFlat = Mathf.RoundToInt(TitlesAdapter.GetStatValue(ownedId, def, lvl, "defFlat", ctx, 0f));
        mods.defPct = TitlesAdapter.GetStatValue(ownedId, def, lvl, "defPct", ctx, 0f);

        mods.spdFlat = Mathf.RoundToInt(TitlesAdapter.GetStatValue(ownedId, def, lvl, "spdFlat", ctx, 0f));
        mods.spdPct = TitlesAdapter.GetStatValue(ownedId, def, lvl, "spdPct", ctx, 0f);

        mods.hpPct = TitlesAdapter.GetStatValue(ownedId, def, lvl, "hpPct", ctx, 0f);

        return mods;
    }

    private TitleStatMods GetConditionalModsForActive() => GetConditionalModsForIndex(activeIndex);

    public float GetActiveMaxHP(float baseMax, int idx = -1)
    {
        float v = Mathf.Max(1f, baseMax);

        if (idx >= 0)
        {
            var tmods = GetTitleModsForIndex(idx);
            if (tmods.hpPct > 0f) v *= 1f + tmods.hpPct;

            var cmods = GetConditionalModsForIndex(idx);
            if (cmods.hpPct > 0f) v *= 1f + Mathf.Max(0f, cmods.hpPct);
        }

        int hpBuff = (_rules.allowBoosters && BattleTempBuffs.I) ? BattleTempBuffs.I.GetPlayerHPBonus() : 0;
        v = Mathf.Max(1f, v + hpBuff);

        return v;
    }

    private float GetFinalMaxHPForIndex(int idx)
    {
        if (_stats != null)
            return Mathf.Max(1f, _stats.GetEffectivePlayer(idx).maxHP);

        if (teamMaxHP == null || idx < 0 || idx >= teamMaxHP.Length) return 1f;
        return GetActiveMaxHP(teamMaxHP[idx], idx);
    }

    private int GetAlliesAliveNotIncludingActive()
    {
        int alive = 0;
        for (int i = 0; i < teamCount; i++)
            if (i != activeIndex && teamHP[i] > 0.01f) alive++;
        return alive;
    }

    private int GetWinStreakSafe()
    {
        try
        {
            var em = EncounterManager.I;
            if (em == null) return 0;

            var t = em.GetType();
            var p = t.GetProperty("CurrentWinStreak") ?? t.GetProperty("WinStreak");
            if (p != null && p.PropertyType == typeof(int)) return (int)p.GetValue(em);

            var m = t.GetMethod("GetWinStreak", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            if (m != null && m.ReturnType == typeof(int)) return (int)m.Invoke(em, null);
        }
        catch { }
        return 0;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Conditional Title feedback (battle textbox + BattleLogger)
    // ─────────────────────────────────────────────────────────────────────────

    // Tracks the last conditional-mod snapshot so we only notify on changes.
    // We keep this in BattleManager so it survives across partials and avoids per-frame allocations.
    private bool _condModsInit;
    private int _condModsHashLast;
    private TitleStatMods _condModsLast;

    private static int HashTitleStatMods(in TitleStatMods m)
    {
        unchecked
        {
            int h = 17;
            h = (h * 31) ^ BitConverter.SingleToInt32Bits(m.hpPct);
            h = (h * 31) ^ BitConverter.SingleToInt32Bits(m.atkPct);
            h = (h * 31) ^ BitConverter.SingleToInt32Bits(m.defPct);
            h = (h * 31) ^ BitConverter.SingleToInt32Bits(m.spdPct);
            h = (h * 31) ^ m.atkFlat;
            h = (h * 31) ^ m.defFlat;
            h = (h * 31) ^ m.spdFlat;
            return h;
        }
    }

    private static bool HasAnyConditional(in TitleStatMods m)
    {
        const float EPS = 0.0001f;
        return
            Mathf.Abs(m.hpPct) > EPS ||
            Mathf.Abs(m.atkPct) > EPS ||
            Mathf.Abs(m.defPct) > EPS ||
            Mathf.Abs(m.spdPct) > EPS ||
            m.atkFlat != 0 || m.defFlat != 0 || m.spdFlat != 0;
    }

    private static string BuildCondSummaryShort(in TitleStatMods m)
    {
        List<string> parts = null;

        void Add(string s)
        {
            parts ??= new List<string>(4);
            parts.Add(s);
        }

        bool anyUp = false;
        bool anyDown = false;

        if (m.atkPct > 0f || m.atkFlat > 0) { Add("ATK↑"); anyUp = true; }
        else if (m.atkPct < 0f || m.atkFlat < 0) { Add("ATK↓"); anyDown = true; }

        if (m.defPct > 0f || m.defFlat > 0) { Add("DEF↑"); anyUp = true; }
        else if (m.defPct < 0f || m.defFlat < 0) { Add("DEF↓"); anyDown = true; }

        if (m.spdPct > 0f || m.spdFlat > 0) { Add("SPD↑"); anyUp = true; }
        else if (m.spdPct < 0f || m.spdFlat < 0) { Add("SPD↓"); anyDown = true; }

        if (m.hpPct > 0f) { Add("HP↑"); anyUp = true; }
        else if (m.hpPct < 0f) { Add("HP↓"); anyDown = true; }

        if (parts == null || parts.Count == 0) return null;

        string prefix = anyUp && !anyDown ? "Title Boost" : (anyDown && !anyUp ? "Title Drag" : "Title Shift");
        return $"{prefix}: {string.Join(" ", parts)}";
    }

    private static string BuildCondSummaryMathy(in TitleStatMods m)
    {
        return $"COND hpPct={m.hpPct:0.###} atkPct={m.atkPct:0.###} defPct={m.defPct:0.###} spdPct={m.spdPct:0.###} atkFlat={m.atkFlat} defFlat={m.defFlat} spdFlat={m.spdFlat}";
    }

    private bool TryConsumeConditionalTitleFeedback(out TitleStatMods mods, out string battleLine, out string logLine)
    {
        mods = default;
        battleLine = null;
        logLine = null;

        string ownedId = GetTeamTitleIdSafe(activeIndex);
        if (string.IsNullOrEmpty(ownedId)) return false;

        float activeHp = Mathf.Max(0f, GetActivePlayerCurHP());
        float baseMax = (teamMaxHP != null && activeIndex >= 0 && activeIndex < teamMaxHP.Length) ? Mathf.Max(1f, teamMaxHP[activeIndex]) : 1f;
        float maxHp = Mathf.Max(1f, GetActiveMaxHP(baseMax, activeIndex));
        float hpPct = Mathf.Clamp01(activeHp / maxHp);
        int alliesAlive = GetAlliesAliveNotIncludingActive();
        int winStreak = GetWinStreakSafe();

        TitleStatMods cond = TitlesAdapter.GetConditionalBattleMods(ownedId, hpPct, alliesAlive, winStreak);
        mods = cond;
        int hash = HashTitleStatMods(cond);

        if (!_condModsInit)
        {
            _condModsInit = true;
            _condModsLast = cond;
            _condModsHashLast = hash;

            if (HasAnyConditional(cond))
            {
                battleLine = BuildCondSummaryShort(cond);
                logLine = BuildCondSummaryMathy(cond);
                return !string.IsNullOrEmpty(battleLine);
            }

            return false;
        }

        if (hash == _condModsHashLast)
            return false;

        bool had = HasAnyConditional(_condModsLast);
        bool has = HasAnyConditional(cond);

        _condModsLast = cond;
        _condModsHashLast = hash;

        if (!had && !has) return false;

        if (!has)
        {
            battleLine = "Title Boost ended";
            logLine = "COND ended";
            return true;
        }

        battleLine = BuildCondSummaryShort(cond);
        logLine = BuildCondSummaryMathy(cond);
        return !string.IsNullOrEmpty(battleLine);
    }

    private void ResetConditionalTitleFeedbackCache()
    {
        _condModsInit = false;
        _condModsHashLast = 0;
        _condModsLast = default;
    }

    private TitleContext BuildTitleContextForActive()
    {
        float curMax = GetFinalMaxHPForIndex(activeIndex);
        float hpPct = curMax > 0.01f ? Mathf.Clamp01(teamHP[activeIndex] / curMax) : 0f;
        int alliesAlive = GetAlliesAliveNotIncludingActive();
        int streak = GetWinStreakSafe();

        var ctx = new TitleContext
        {
            selfHp01 = hpPct,
            alliesAlive = alliesAlive,
            winStreak = streak,
            isBattle = true,
            ownedId = GetTeamTitleIdSafe(activeIndex)
        };
        return ctx;
    }

    internal TitleContext BuildTitleContextForWild()
    {
        float max = Mathf.Max(1f, wildMaxHP);
        float hp01 = max > 0.01f ? Mathf.Clamp01(wildHP / max) : 0f;

        return new TitleContext
        {
            selfHp01 = hp01,
            alliesAlive = 0,
            winStreak = 0,
            isBattle = true,
            ownedId = _wildCombatIdForTitles
        };
    }

    private void RefreshWildEffectiveStatsFromTitles()
    {
        if (!wildDef) return;
        if (string.IsNullOrEmpty(_wildCombatIdForTitles)) return;

        // Preferred path: use centralized stat pipeline so wild titles affect ALL stats consistently.
        // This also ensures conditional titles that depend on HP% evaluate against the effective max HP.
        if (_stats != null)
        {
            // Max HP can change from titles/conditionals; preserve HP%.
            SyncEffectiveMaxHPFromStats();

            // Keep legacy fields in sync for older code paths that still read them.
            wildAttackPerTurn = Mathf.Max(1f, _stats.GetEffectiveWild().atk);
            return;
        }

        // Fallback: legacy title evaluation (HP/ATK only). Kept for safety when _stats is unavailable.
        float prevMax = Mathf.Max(1f, wildMaxHP);
        float hp01 = prevMax > 0.01f ? Mathf.Clamp01(wildHP / prevMax) : 0f;

        var wCtx = BuildTitleContextForWild();

        float wMaxF = TitlesAdapter.GetStatValue(_wildCombatIdForTitles, wildDef, wildLevel, "HP", wCtx, wildBaseMaxHP);
        if (!float.IsNaN(wMaxF) && !float.IsInfinity(wMaxF))
            wildMaxHP = Mathf.Max(1f, wMaxF);

        wildHP = Mathf.Clamp(wildMaxHP * hp01, 0f, wildMaxHP);

        float wAtkF = TitlesAdapter.GetStatValue(_wildCombatIdForTitles, wildDef, wildLevel, "Attack", wCtx, wildBaseAttackPerTurn);
        if (!float.IsNaN(wAtkF) && !float.IsInfinity(wAtkF))
            wildAttackPerTurn = Mathf.Max(1f, wAtkF);
    }

    private float GetActiveMaxHP_NoConditionals(float baseMax, int idx)
    {
        float v = Mathf.Max(1f, baseMax);

        var tmods = GetTitleModsForIndex(idx);
        if (tmods.hpPct > 0f) v *= (1f + tmods.hpPct);

        int hpBuff = (_rules.allowBoosters && BattleTempBuffs.I) ? BattleTempBuffs.I.GetPlayerHPBonus() : 0;
        v = Mathf.Max(1f, v + hpBuff);

        return v;
    }

    private List<TitleSO> GetTitlesForOwnedIdSafe(string ownedId)
    {
        if (string.IsNullOrEmpty(ownedId)) return null;

        try
        {
            return TitleManager.I?.GetTitlesForMonster(ownedId);
        }
        catch { }

        return null;
    }

    private void Debug_LogActiveTitlesSnapshot(string reason)
    {
        if (!debugTitles) return;
        if (activeIndex < 0) return;
        if (teamDefs == null || teamLevels == null || teamIds == null) return;
        if (activeIndex >= teamDefs.Length || activeIndex >= teamLevels.Length || activeIndex >= teamIds.Length) return;

        string ownedId = GetTeamTitleIdSafe(activeIndex);
        var def = teamDefs[activeIndex];
        int lvl = teamLevels[activeIndex];

        if (string.IsNullOrEmpty(ownedId) || def == null) return;

        var titles = GetTitlesForOwnedIdSafe(ownedId);

        Debug.Log($"[Titles][{reason}] Turn={_turnIndex} OwnedId={ownedId} Monster={def.displayName} Lv={lvl}");

        if (titles == null)
        {
            Debug.Log("[Titles] Title list unavailable (TitleManager.I.GetTitlesForMonster not reachable).");
        }
        else if (titles.Count == 0)
        {
            Debug.Log("[Titles] (No titles found)");
        }
        else
        {
            for (int i = 0; i < titles.Count; i++)
            {
                var t = titles[i];
                if (!t) continue;

                string id = "";
                try { id = t.titleId; } catch { }

                string extra = "";
                if (t is BattleStartFlatTitleSO bsf)
                    extra = $" stat={bsf.stat} flatAmount={bsf.flatAmount} durationTurns={bsf.durationTurns}";

                Debug.Log($"  • [{i}] {id} {t.name} ({t.GetType().Name}){extra}");
            }
        }
    }
}
