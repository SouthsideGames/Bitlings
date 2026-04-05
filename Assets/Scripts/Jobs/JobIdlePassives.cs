using System.Collections.Generic;
using UnityEngine;

public static class JobIdlePassives
{
    public struct TeamPassives
    {
        public float offenseMul;
        public float defenseMul;
        public float earlyEdge;
        public float creditMul;
        public float energyCostMul;
    }

    const float CRIT_TO_OFFENSE     = 0.40f;
    const float FIRST_HIT_TO_EDGE   = 0.04f;
    const float SPD_TO_EDGE         = 0.02f;
    const float SHIELD_TO_DEF       = 0.60f;
    const float REGEN_TURNS         = 5f;
    const float DR_TO_DEF           = 1.00f;

    public static TeamPassives ComputeForActiveTeam()
    {
        var team = SaveManager.Data?.team;
        return ComputeForTeam(team);
    }

    public static TeamPassives ComputeForTeam(List<OwnedMonsterData> team)
    {
        int count = (team == null) ? 0 : Mathf.Min(3, team.Count);
        if (count <= 0 || JobManager.I == null) return new TeamPassives { offenseMul = 1f, defenseMul = 1f, creditMul = 1f, energyCostMul = 1f };

        float offAccum = 0f;
        float defAccum = 0f;
        float edgeAccum = 0f;
        float bestPowerPlantHours = 0f;

        for (int i = 0; i < count; i++)
        {
            var owned = team[i];
            var (job, hours) = JobManager.I.GetCurrentJobAndHours(owned.monsterId);
            var c = JobBattlePassives.Build(job, hours);

            float offensePct = 0f;
            offensePct += c.attackBonusPct;
            offensePct += c.critChanceFlat * CRIT_TO_OFFENSE;
            if (c.critBuffTurns > 0) offensePct += c.critChanceBonusFirstTurns * CRIT_TO_OFFENSE * 0.6f;
            if (c.firstOutgoingBonus > 0f)
                edgeAccum += Mathf.Clamp01(c.firstOutgoingBonus / 0.30f) * FIRST_HIT_TO_EDGE;
            if (c.surgeAtkBonusPct > 0f) offensePct += c.surgeAtkBonusPct * 0.35f;

            offAccum += Mathf.Max(0f, offensePct);

            float defPct = 0f;
            defPct += c.maxHpBonusPct;
            defPct += c.baseDamageReducePct * DR_TO_DEF;
            defPct += c.defenseBonusPct * DR_TO_DEF;
            defPct += c.dmgReduceFirstTurns * 0.6f;
            defPct += c.firstIncomingReduce * 0.5f;
            defPct += c.critResistFlat * 0.45f;
            defPct += c.rescueHealPct * 0.60f;
            if (c.critResistBuffTurns > 0) defPct += c.critResistBonusFirstTurns * 0.25f;
            defPct += c.startShieldPctMaxHp * SHIELD_TO_DEF;
            if (c.endTurnHealPct > 0f)
            {
                float regenTurns = (c.regenTurns == int.MaxValue) ? REGEN_TURNS : Mathf.Min(REGEN_TURNS, c.regenTurns);
                defPct += c.endTurnHealPct * (regenTurns * 0.20f);
            }

            defAccum += Mathf.Max(0f, defPct);

            if (job == JobType.Power_Plant)
                bestPowerPlantHours = Mathf.Max(bestPowerPlantHours, hours);
            if (c.speedBuffTurns > 0 && c.speedBonusPctFirstTurns > 0f)
                edgeAccum += SPD_TO_EDGE;
        }

        float avgOff = offAccum / Mathf.Max(1f, count);
        float avgDef = defAccum / Mathf.Max(1f, count);

        float offenseMul = 1f + Mathf.Clamp(avgOff, 0f, 0.50f);
        float defenseMul = 1f + Mathf.Clamp(avgDef, 0f, 0.50f);
        float energyCostMul = 1f - Mathf.Min(0.02f * bestPowerPlantHours, 0.10f);

        return new TeamPassives
        {
            offenseMul = offenseMul,
            defenseMul = defenseMul,
            earlyEdge  = Mathf.Clamp(edgeAccum, 0f, 0.08f),
            creditMul    = 1f,
            energyCostMul = Mathf.Clamp(energyCostMul, 0.5f, 1f)
        };
    }
}
