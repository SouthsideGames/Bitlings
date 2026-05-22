using System;
using UnityEngine;
using Random = UnityEngine.Random;

public struct DamageResult
{
    public int damage;
    public bool crit;
    public float effectiveness;
    public float incomingTypeResistMul;
}

public static class BattleCalc
{

    private static Func<float> _rng01;

    public static void SetRng(Func<float> rng01) => _rng01 = rng01;

    public static void ResetRng() => _rng01 = null;

    private static float Rng01()
    {
        try
        {
            if (_rng01 != null)
                return Mathf.Clamp(_rng01(), 0f, 0.999999f);
        }
        catch { /* fall back to Unity RNG */ }

    #if UNITY_EDITOR || DEVELOPMENT_BUILD
        Debug.LogWarning("[BattleCalc] Falling back to Unity.Random — SetRng() was not called before battle. Seeded battle contexts will produce non-deterministic results."); // FIXED: makes unseeded fallback visible during development
    #endif

        return Random.value;
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Core stat curves (back-compat)
    // ─────────────────────────────────────────────────────────────────────────────

    public static float CalcHP(MonsterDataSO def, int level)
    {
        level = Mathf.Max(1, level);
        float baseHP = def ? def.baseHP : 0f;
        return Mathf.Max(1f, baseHP + (level - 1) * 12f);
    }

    public static float CalcBaseAttack(MonsterDataSO def, int level, int flatBonus, int tempBonus)
    {
        level = Mathf.Max(1, level);
        float baseAtk = (def ? def.baseAttack : 0f) + (level - 1) * 2f;
        return Mathf.Max(1f, baseAtk + Mathf.Max(0, flatBonus) + Mathf.Max(0, tempBonus));
    }

    public static int CalcSpeed(MonsterDataSO def, int level)
    {
        level = Mathf.Max(1, level);
        // Design rule: Speed increases every 3 levels (turn priority stat).
        // +0 at L1–L2, +1 at L3–L5, +2 at L6–L8, etc.
        int scaled = (def ? def.baseSpeed : 1) + Mathf.Max(0, level / 3);
        return Mathf.Max(1, scaled);
    }

    public static int CalcDefense(MonsterDataSO def, int level)
    {
        level = Mathf.Max(1, level);
        int baseDef = def ? def.baseDefense : 0;
        // Design rule: Defense increases every 2 levels (damage mitigation stat).
        // +0 at L1, +1 at L2–L3, +2 at L4–L5, etc.
        int scaled = baseDef + Mathf.Max(0, level / 2);
        return Mathf.Max(0, scaled);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Title-aware stat overloads (apply TitleStatMods when ownedId is provided)
    // ─────────────────────────────────────────────────────────────────────────────

    public static float CalcHP(MonsterDataSO def, int level, string ownedId)
    {
        float hp = CalcHP(def, level);
        if (!string.IsNullOrEmpty(ownedId))
        {
            var t = TitlesAdapter.GetBattleStatMods(ownedId);
            if (t.hpPct != 0f) hp *= (1f + t.hpPct);
        }
        return Mathf.Max(1f, hp);
    }

    public static float CalcBaseAttack(MonsterDataSO def, int level, int flatBonus, int tempBonus, string ownedId)
    {
        float atk = CalcBaseAttack(def, level, flatBonus, tempBonus);
        if (!string.IsNullOrEmpty(ownedId))
        {
            var t = TitlesAdapter.GetBattleStatMods(ownedId);
            if (t.atkFlat != 0) atk += t.atkFlat;
            if (t.atkPct  != 0f) atk *= (1f + t.atkPct);
        }

        if (def != null)
            atk *= HonorService.GetHonorAttackMultiplier(def.type);

        return Mathf.Max(1f, atk);
    }

    public static int CalcSpeed(MonsterDataSO def, int level, string ownedId)
    {
        int spd = CalcSpeed(def, level);
        if (!string.IsNullOrEmpty(ownedId))
        {
            var t = TitlesAdapter.GetBattleStatMods(ownedId);
            if (t.spdFlat != 0) spd += t.spdFlat;
            if (t.spdPct  != 0f)  spd = Mathf.RoundToInt(spd * (1f + t.spdPct));
        }
        return Mathf.Max(1, spd);
    }

    public static int CalcDefense(MonsterDataSO def, int level, string ownedId)
    {
        int d = CalcDefense(def, level);
        if (!string.IsNullOrEmpty(ownedId))
        {
            var t = TitlesAdapter.GetBattleStatMods(ownedId);
            if (t.defFlat != 0) d += t.defFlat;
            if (t.defPct  != 0f) d = Mathf.RoundToInt(d * (1f + t.defPct));
        }

        if (def != null)
            d = Mathf.RoundToInt(d * HonorService.GetHonorDefenseMultiplier(def.type));

        return Mathf.Max(0, d);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Convenience: ID-less damage calc (legacy, unchanged)
    // (No defender title hooks here; keep it simple for legacy callers)
    // ─────────────────────────────────────────────────────────────────────────────

    public static DamageResult CalcDamage(
        MonsterDataSO atkDef, int atkLevel,
        MonsterDataSO defDef, int defLevel,
        float baseDamage, float critChance, float critMultiplier,
        int defenseIgnoreFlat = 0, int defenderFlatDefenseBonus = 0)
    {
        atkLevel = Mathf.Max(1, atkLevel);
        defLevel = Mathf.Max(1, defLevel);
        baseDamage = Mathf.Max(0f, baseDamage);
        critChance = Mathf.Clamp01(critChance);
        critMultiplier = Mathf.Max(1f, critMultiplier);

        var atkType = atkDef ? atkDef.type : default;
        var defType = defDef ? defDef.type : default;

        float eff = BattleTypeChart.GetMultiplier(atkType, defType);
        if (float.IsNaN(eff) || float.IsInfinity(eff)) eff = 1f;

        bool defenderIsRock = defDef && defDef.type == MonsterType.Rock;
        bool crit = !defenderIsRock && (Rng01() < critChance);

        float preMit = baseDamage * Mathf.Max(0.10f, eff) * (crit ? critMultiplier : 1f);

        int defense = CalcDefense(defDef, defLevel) + Mathf.Max(0, defenderFlatDefenseBonus);
        defense = Mathf.Max(0, defense - Mathf.Max(0, defenseIgnoreFlat));

        // Defense curve: K/(K+defense) where K=65. At defense=0 → 100% through; at defense=65 → 50% mitigated.
        // Lower K vs the old 100 makes each defense point roughly 50% more impactful.
        float mitFactor = 65f / (65f + Mathf.Max(0, defense));
        float afterDefense = preMit * mitFactor;

        int dealt = Mathf.Max(1, Mathf.RoundToInt(afterDefense));

        return new DamageResult
        {
            damage = dealt,
            crit = crit,
            effectiveness = eff,
            incomingTypeResistMul = 1f
        };
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Back-compat overloads (no IDs)
    // ─────────────────────────────────────────────────────────────────────────────

    public static DamageResult ResolveHit(
        MonsterDataSO atkDef, int atkLevel, MonsterDataSO defDef,
        float baseDamage, float critChance, float critMultiplier)
    {
        return ResolveHit(
            attackerMonsterId: null, atkDef: atkDef, atkLevel: atkLevel,
            defenderMonsterId: null, defDef: defDef, defLevel: 1,
            baseDamage: baseDamage, critChance: critChance, critMultiplier: critMultiplier,
            defenderFlatDefenseBonus: 0,
            defenderEffectiveDefenseStat: null
        );
    }

    public static DamageResult ResolveHit(
        MonsterDataSO atkDef, int atkLevel, MonsterDataSO defDef, int defLevel,
        float baseDamage, float critChance, float critMultiplier, int defenderFlatDefenseBonus = 0)
    {
        return ResolveHit(
            attackerMonsterId: null, atkDef: atkDef, atkLevel: atkLevel,
            defenderMonsterId: null, defDef: defDef, defLevel: defLevel,
            baseDamage: baseDamage, critChance: critChance, critMultiplier: critMultiplier,
            defenderFlatDefenseBonus: defenderFlatDefenseBonus,
            defenderEffectiveDefenseStat: null
        );
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // ID-aware Resolve with Titles integration
    //
    // NEW: defenderEffectiveDefenseStat
    // If provided, this is treated as the defender's *effective defense STAT* for this hit
    // (i.e., baseline totals + boosters + conditional flats already applied).
    //
    // Titles-based DEF mods are applied *on top* of this value via TitlesAdapter.GetStatValue(...).
    // If you want a "final defense already includes titles" behavior, pass the fully titled value
    // and set applyTitleDefenseMods=false (not exposed currently; see note below).
    // ─────────────────────────────────────────────────────────────────────────────
    public static DamageResult ResolveHit(
        string attackerMonsterId, MonsterDataSO atkDef, int atkLevel,
        string defenderMonsterId, MonsterDataSO defDef, int defLevel,
        float baseDamage, float critChance, float critMultiplier,
        int defenderFlatDefenseBonus = 0,
        int? defenderEffectiveDefenseStat = null)
    {
        atkLevel = Mathf.Max(1, atkLevel);
        defLevel = Mathf.Max(1, defLevel);
        baseDamage = Mathf.Max(0f, baseDamage);
        critChance = Mathf.Clamp01(critChance);
        critMultiplier = Mathf.Max(1f, critMultiplier);

        var atkType = atkDef ? atkDef.type : default;
        var defType = defDef ? defDef.type : default;

        // Base effectiveness
        float eff = BattleTypeChart.GetMultiplier(atkType, defType);
        if (float.IsNaN(eff) || float.IsInfinity(eff)) eff = 1f;

        // Attacker-side title effectiveness add/multiplier
        if (!string.IsNullOrEmpty(attackerMonsterId))
        {
            try
            {
                // Add is applied on top of the base chart BEFORE multipliers.
                float add = TitlesAdapter.GetEffectivenessAdd(attackerMonsterId, atkDef, atkLevel);
                if (!float.IsNaN(add) && !float.IsInfinity(add))
                    eff = Mathf.Max(0f, eff + add);

                float outMul = TitlesAdapter.GetEffectivenessMult(attackerMonsterId, atkDef, atkLevel);
                if (!float.IsNaN(outMul) && !float.IsInfinity(outMul) && outMul > 0f) eff *= outMul;
            }
            catch { /* keep resilient */ }
        }

        // Defender-side incoming effectiveness multiplier (e.g., nullify or weaken type)
        float incomingMul = 1f;
        if (!string.IsNullOrEmpty(defenderMonsterId))
        {
            try
            {
                // Defender-side incoming effectiveness (Titles). Prefer type-aware path so TypeResist titles work.
                float inMul = TitlesAdapter.GetIncomingEffectivenessMult(defenderMonsterId, defDef, defLevel, atkType);
                if (!float.IsNaN(inMul) && !float.IsInfinity(inMul) && inMul >= 0f)
                {
                    incomingMul = inMul;
                    eff *= inMul;
                }
            }
            catch { /* default to 1f if not implemented */ }
        }

        // Read defender damage filter (cannotBeCrit / %DR / flat DR)
        bool blockCrit = false;
        float percentDR = 0f;
        int flatDR = 0;

        if (!string.IsNullOrEmpty(defenderMonsterId))
        {
            try
            {
                var df = TitlesAdapter.GetDamageFilter(defenderMonsterId, defDef, defLevel);
                blockCrit = df.cannotBeCrit;
                if (defDef && defDef.type == MonsterType.Rock)
                    blockCrit = true;
                percentDR = Mathf.Clamp01(df.percentReduce);
                flatDR = Mathf.Max(0, df.flatReduce);
            }
            catch { /* safe no-op */ }
        }

        bool crit = !blockCrit && (Rng01() < critChance);

        float preMit = baseDamage * Mathf.Max(0.10f, eff) * (crit ? critMultiplier : 1f);

        // Weekly world event type bonus: if the attacker's type matches the boosted type,
        // apply its damage multiplier. Defaults to 1f when no event is active.
        if (WorldEventSystem.I != null && atkDef != null &&
            atkDef.type != MonsterType.None &&
            atkDef.type == WorldEventSystem.I.GetBoostedMonsterType())
        {
            preMit *= WorldEventSystem.I.GetTypeDamageMultiplier();
        }

        // ─────────────────────────────────────────────────────────────────────
        // Defense STAT
        //
        // If caller supplies defenderEffectiveDefenseStat: use it as the base defense stat.
        // Otherwise derive from (MonsterDataSO + level), with title-aware defense if we have defenderMonsterId.
        // Then add any extra "flat defense bonus" argument.
        // ─────────────────────────────────────────────────────────────────────
        int defenseStat;

        if (defenderEffectiveDefenseStat.HasValue)
        {
            defenseStat = Mathf.Max(0, defenderEffectiveDefenseStat.Value);
        }
        else
        {
            defenseStat = string.IsNullOrEmpty(defenderMonsterId)
                ? CalcDefense(defDef, defLevel)
                : CalcDefense(defDef, defLevel, defenderMonsterId); // includes battle stat mods
        }

        // Legacy extra flat defense bonus hook
        if (defenderFlatDefenseBonus != 0)
            defenseStat = Mathf.Max(0, defenseStat + Mathf.Max(0, defenderFlatDefenseBonus));

        // Apply Titles/conditionals to defense stat, if we have defender ID AND the caller
        // did not already supply a titled effective defense stat.
        //
        // If caller supplies defenderEffectiveDefenseStat we treat it as the FINAL defense stat
        // for this hit (it may already include conditional title context like hp%).
        if (!defenderEffectiveDefenseStat.HasValue && !string.IsNullOrEmpty(defenderMonsterId))
        {
            try
            {
                // Build a minimal context; if you have richer context in BattleManager, keep that there.
                // Here we just pass empty to avoid recursion; most defense titles are static mods anyway.
                var ctx = TitleContext.Empty;
                float dFinalF = TitlesAdapter.GetStatValue(defenderMonsterId, defDef, defLevel, "Defense", ctx, defenseStat);
                if (!float.IsNaN(dFinalF) && !float.IsInfinity(dFinalF))
                    defenseStat = Mathf.Max(0, Mathf.RoundToInt(dFinalF));
            }
            catch { /* safe no-op */ }
        }

        float mitFactor = 65f / (65f + Mathf.Max(0, defenseStat));
        float afterDefense = preMit * mitFactor;

        // Apply defender title DR: percent first, then flat
        if (percentDR > 0f) afterDefense *= (1f - percentDR);
        if (flatDR > 0) afterDefense -= flatDR;

        int dealt = Mathf.Max(1, Mathf.RoundToInt(afterDefense));

        return new DamageResult
        {
            damage = dealt,
            crit = crit,
            effectiveness = eff,
            incomingTypeResistMul = incomingMul
        };
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Damage Log helpers: Build properly sourced modifier labels for DamageMod entries
    // (Used when constructing DamageMod lists for BattleLogger.LogDamage)
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Build a DamageMod label for a Title bonus applied to attack.
    /// Returns: "Title: {titleDisplayName} atk↑"
    /// </summary>
    public static string MakeTitleAttackModLabel(string titleDisplayName)
    {
        if (string.IsNullOrEmpty(titleDisplayName))
            return "Title atk↑";
        return $"Title: {titleDisplayName} atk↑";
    }

    /// <summary>
    /// Build a DamageMod label for a Title bonus applied to defense.
    /// Returns: "Title: {titleDisplayName} def↑"
    /// </summary>
    public static string MakeTitleDefenseModLabel(string titleDisplayName)
    {
        if (string.IsNullOrEmpty(titleDisplayName))
            return "Title def↑";
        return $"Title: {titleDisplayName} def↑";
    }

    /// <summary>
    /// Build a DamageMod label for a Job passive bonus applied to attack.
    /// Returns: "{JobStrings.SiteName(job)} atk↑"
    /// </summary>
    public static string MakeJobAttackModLabel(JobType job)
    {
        if (job == JobType.None)
            return "atk↑";
        return $"{JobStrings.SiteName(job)} atk↑";
    }

    /// <summary>
    /// Build a DamageMod label for a Job passive bonus applied to defense.
    /// Returns: "{JobStrings.SiteName(job)} def↑"
    /// </summary>
    public static string MakeJobDefenseModLabel(JobType job)
    {
        if (job == JobType.None)
            return "def↑";
        return $"{JobStrings.SiteName(job)} def↑";
    }

    /// <summary>
    /// Build a DamageMod label for an Honor/Mentor bonus applied to attack.
    /// Returns: "Mentor atk↑"
    /// </summary>
    public static string MakeMentorAttackModLabel()
    {
        return "Mentor atk↑";
    }

    /// <summary>
    /// Build a DamageMod label for an Honor/Mentor bonus applied to defense.
    /// Returns: "Mentor def↑"
    /// </summary>
    public static string MakeMentorDefenseModLabel()
    {
        return "Mentor def↑";
    }
}
