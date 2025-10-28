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
    // Core stat curves
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
    // Convenience: ID-less damage calc
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
    // Simplified ID-aware Resolve (TagRuntime fully removed)
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

        float eff = BattleTypeChart.GetMultiplier(atkType, defType);
        if (float.IsNaN(eff) || float.IsInfinity(eff)) eff = 1f;

        bool defenderIsRock = defDef && defDef.type == MonsterType.Rock;
        bool crit = !defenderIsRock && (Random.value < critChance);

        float preMit = baseDamage * Mathf.Max(0.25f, eff) * (crit ? critMultiplier : 1f);

        int defense = CalcDefense(defDef, defLevel) + Mathf.Max(0, defenderFlatDefenseBonus);
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
}
