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
    // Convenience: ID-less damage calc (matches calls in BattleManager)
    // ─────────────────────────────────────────────────────────────────────────────
    //
    // This does NOT invoke TagRuntime side-effects directly (those are handled
    // around the call site in BattleManager). It still applies:
    //  - Type effectiveness
    //  - Crit chance & multiplier (Rock-type crit immunity)
    //  - Defender defense (with optional flat ignore + flat bonus)
    //
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

        // Rock-type is immune to crits (as per your rules)
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
    // Back-compat overloads (no IDs) — kept intact
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
    // ID-aware Resolve (preferred when you want TagRuntime hooks inside calc)
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

        if (crit && !string.IsNullOrEmpty(defenderMonsterId))
        {
            // one-time negate via tags
            if (TagRuntime.TryConsumeNegateFirstIncomingCrit(defenderMonsterId))
                crit = false;
        }

        float dmg = baseDamage;

        // Attacker-side outgoing scalars (if you use these central hooks)
        if (!string.IsNullOrEmpty(attackerMonsterId))
            dmg = TagRuntime.ApplyOutgoingDamage(attackerMonsterId, atkDef, defDef, dmg);

        float preMit = dmg * Mathf.Max(0.25f, eff) * (crit ? critMultiplier : 1f);

        // Attacker-side: bonus vs boss (OnEnemyBoss)
        if (!string.IsNullOrEmpty(attackerMonsterId) && defDef && defDef.isBoss)
        {
            var bossCtx = new TagRuntime.TagContext { enemyIsBoss = true };
            float bossMul = TagRuntime.EvaluateConditionalMultiplier(
                attackerMonsterId,
                new[] { TagTrigger.OnEnemyBoss },
                bossCtx,
                atkDef,
                defDef
            );
            if (!float.IsNaN(bossMul) && !float.IsInfinity(bossMul) && bossMul != 1f)
                preMit *= Mathf.Max(0f, bossMul);
        }

        int defense = CalcDefense(defDef, defLevel) + Mathf.Max(0, defenderFlatDefenseBonus);

        // Defender-side defense multiplier
        if (!string.IsNullOrEmpty(defenderMonsterId))
        {
            float defMul = TagRuntime.GetDefenseMultiplier(defenderMonsterId, defDef, atkDef);
            if (!float.IsNaN(defMul) && !float.IsInfinity(defMul) && defMul != 1f)
                defense = Mathf.Max(0, Mathf.RoundToInt(defense * Mathf.Max(0f, defMul)));
        }

        // Attacker-side flat defense ignore from tags
        if (!string.IsNullOrEmpty(attackerMonsterId))
        {
            var diCtx = new TagRuntime.TagContext
            {
                enemyIsBoss = (defDef && defDef.isBoss)
            };
            int ignoreFlat = TagRuntime.GetDefenseIgnoreFlat(attackerMonsterId, diCtx, atkDef, defDef);
            if (ignoreFlat > 0) defense = Mathf.Max(0, defense - ignoreFlat);
        }

        float mitFactor = 100f / (100f + Mathf.Max(0, defense));
        float afterDefense = preMit * mitFactor;

        // Defender-side incoming scalars (central hook)
        if (!string.IsNullOrEmpty(defenderMonsterId))
            afterDefense = TagRuntime.ApplyIncomingDamage(defenderMonsterId, defDef, atkDef, afterDefense);

        int dealt = Mathf.Max(1, Mathf.RoundToInt(afterDefense));

        return new DamageResult
        {
            damage = dealt,
            crit = crit,
            effectiveness = eff
        };
    }
}
