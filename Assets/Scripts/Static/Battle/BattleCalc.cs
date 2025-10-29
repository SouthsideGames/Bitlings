using UnityEngine;
using Random = UnityEngine.Random;

public struct DamageResult
{
    public int damage;
    public bool crit;
    public float effectiveness;
}

public static class BattleCalc
{
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
        int scaled = (def ? def.baseSpeed : 1) + Mathf.Max(0, (level - 1) / 5);
        return Mathf.Max(1, scaled);
    }

    public static int CalcDefense(MonsterDataSO def, int level)
    {
        level = Mathf.Max(1, level);
        int baseDef = def ? def.baseDefense : 0;
        int scaled = baseDef + Mathf.Max(0, (level - 1) / 4);
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
            if (t.atkPct != 0f)  atk *= (1f + t.atkPct);
        }
        return Mathf.Max(1f, atk);
    }

    public static int CalcSpeed(MonsterDataSO def, int level, string ownedId)
    {
        int spd = CalcSpeed(def, level);
        if (!string.IsNullOrEmpty(ownedId))
        {
            var t = TitlesAdapter.GetBattleStatMods(ownedId);
            if (t.spdFlat != 0) spd += t.spdFlat;
            if (t.spdPct != 0f)  spd = Mathf.RoundToInt(spd * (1f + t.spdPct));
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
        return Mathf.Max(0, d);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Convenience: ID-less damage calc (unchanged)
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
        bool crit = !defenderIsRock && (Random.value < critChance);

        float preMit = baseDamage * Mathf.Max(0.25f, eff) * (crit ? critMultiplier : 1f);

        int defense = CalcDefense(defDef, defLevel) + Mathf.Max(0, defenderFlatDefenseBonus);
        defense = Mathf.Max(0, defense - Mathf.Max(0, defenseIgnoreFlat));

        float mitFactor = 100f / (100f + Mathf.Max(0, defense));
        float afterDefense = preMit * mitFactor;

        int dealt = Mathf.Max(1, Mathf.RoundToInt(afterDefense));

        return new DamageResult
        {
            damage = dealt,
            crit = crit,
            effectiveness = eff
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
            defenderFlatDefenseBonus: 0);
    }

    public static DamageResult ResolveHit(
        MonsterDataSO atkDef, int atkLevel, MonsterDataSO defDef, int defLevel,
        float baseDamage, float critChance, float critMultiplier, int defenderFlatDefenseBonus = 0)
    {
        return ResolveHit(
            attackerMonsterId: null, atkDef: atkDef, atkLevel: atkLevel,
            defenderMonsterId: null, defDef: defDef, defLevel: defLevel,
            baseDamage: baseDamage, critChance: critChance, critMultiplier: critMultiplier,
            defenderFlatDefenseBonus: defenderFlatDefenseBonus);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // ID-aware Resolve with Titles integration
    // ─────────────────────────────────────────────────────────────────────────────
    public static DamageResult ResolveHit(
        string attackerMonsterId, MonsterDataSO atkDef, int atkLevel,
        string defenderMonsterId, MonsterDataSO defDef, int defLevel,
        float baseDamage, float critChance, float critMultiplier, int defenderFlatDefenseBonus = 0)
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

        // Titles can multiply effectiveness for the attacker
        if (!string.IsNullOrEmpty(attackerMonsterId))
        {
            float titleEffMul = TitlesAdapter.GetEffectivenessMult(attackerMonsterId, atkDef, atkLevel);
            if (!float.IsNaN(titleEffMul) && !float.IsInfinity(titleEffMul) && titleEffMul > 0f)
                eff *= titleEffMul;
        }

        // Defender filter may block crit / add DR
        bool blockCrit = false;
        float percentDR = 0f; // 0.10 => 10% less after-defense damage
        float flatDR    = 0f; // subtract after percent

        if (!string.IsNullOrEmpty(defenderMonsterId))
        {
            TryReadDamageFilter(TitlesAdapter.GetDamageFilter(defenderMonsterId, defDef, defLevel), out flatDR, out percentDR, out blockCrit);
        }

        bool defenderIsRock = defDef && defDef.type == MonsterType.Rock;
        bool crit = !defenderIsRock && !blockCrit && (Random.value < critChance);

        float preMit = baseDamage * Mathf.Max(0.25f, eff) * (crit ? critMultiplier : 1f);

        // Title-aware defense value if defender ID is present
        int defense = string.IsNullOrEmpty(defenderMonsterId)
            ? CalcDefense(defDef, defLevel)
            : CalcDefense(defDef, defLevel, defenderMonsterId);

        defense += Mathf.Max(0, defenderFlatDefenseBonus);
        float mitFactor = 100f / (100f + Mathf.Max(0, defense));
        float afterDefense = preMit * mitFactor;

        // Apply defender title DR (percent then flat)
        if (percentDR > 0f) afterDefense *= Mathf.Max(0f, 1f - percentDR);
        if (flatDR    > 0f) afterDefense -= flatDR;

        int dealt = Mathf.Max(1, Mathf.RoundToInt(afterDefense));

        return new DamageResult
        {
            damage = dealt,
            crit = crit,
            effectiveness = eff
        };
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────────

    private static void TryReadDamageFilter(object boxed,
                                            out float flatDR,
                                            out float percentDR,
                                            out bool blockCrit)
    {
        flatDR = 0f; percentDR = 0f; blockCrit = false;
        if (boxed == null) return;

        var t = boxed.GetType();
        try
        {
            // Try common field/property names flexibly
            flatDR    = ReadFloat(t, boxed, "flatDR", "FlatDR", "flat", "Flat", "flatReduction");
            percentDR = ReadFloat(t, boxed, "percentDR", "PercentDR", "pct", "Pct", "reductionPct", "Percent");
            blockCrit = ReadBool (t, boxed, "blockCrit", "NoCrit", "noCrit", "BlockCrit");
        }
        catch { /* safe no-op */ }
        flatDR    = Mathf.Max(0f, flatDR);
        percentDR = Mathf.Clamp01(percentDR);
    }

    private static float ReadFloat(System.Type t, object o, params string[] names)
    {
        for (int i = 0; i < names.Length; i++)
        {
            var f = t.GetField(names[i]);
            if (f != null && f.FieldType == typeof(float)) return (float)f.GetValue(o);
            var p = t.GetProperty(names[i]);
            if (p != null && p.PropertyType == typeof(float)) return (float)p.GetValue(o, null);
        }
        return 0f;
    }

    private static bool ReadBool(System.Type t, object o, params string[] names)
    {
        for (int i = 0; i < names.Length; i++)
        {
            var f = t.GetField(names[i]);
            if (f != null && f.FieldType == typeof(bool)) return (bool)f.GetValue(o);
            var p = t.GetProperty(names[i]);
            if (p != null && p.PropertyType == typeof(bool)) return (bool)p.GetValue(o, null);
        }
        return false;
    }
}
