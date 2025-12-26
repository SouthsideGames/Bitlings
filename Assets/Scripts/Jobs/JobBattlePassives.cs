using UnityEngine;

public static class JobBattlePassives
{
    // Tunables
    const float GYM_HP_PER_HOUR = 0.02f, GYM_HP_CAP = 0.10f;
    const float FORGE_FIRST_HIT_BASE = 0.20f, FORGE_PER_HOUR = 0.02f, FORGE_CAP = 0.30f;
    const float POWER_SPEED_FIRST2 = 0.10f;
    const float QUARRY_DEF_PER_HOUR = 0.01f, QUARRY_DEF_CAP = 0.05f;
    const float GROVE_BASE_HEAL = 0.02f, GROVE_PER_HOUR = 0.005f, GROVE_HEAL_CAP = 0.06f;
    const float WORKSHOP_CRIT_FIRST2 = 0.10f;
    const float HARBOR_START_SHIELD = 0.05f, HARBOR_TRICKLE = 0.01f;
    const float CRYO_CRITRES_BASE = 0.05f, CRYO_PER_HOUR = 0.01f, CRYO_CRITRES_CAP = 0.10f;
    const float OBS_CRIT_FLAT = 0.10f, OBS_CRITRES_FIRST2 = 0.10f;
    const float CONT_DMGRED_BASE = 0.05f, CONT_PER_HOUR = 0.01f, CONT_DMGRED_CAP = 0.10f;
    const float WYRM_SURGE_ATK = 0.15f;
    const float SHADOW_FIRST_INCOMING_REDUCE = 0.50f;
    const float SANCTUM_START_SHIELD = 0.08f, SANCTUM_DMGRED_FIRST2 = 0.05f;
    const float MINE_ATK_PER_HOUR = 0.02f, MINE_ATK_CAP = 0.10f;
    
    const float CLINIC_TRIAGE_BASE = 0.10f;
    const float CLINIC_PER_HOUR    = 0.02f;
    const float CLINIC_TRIAGE_CAP  = 0.20f;
    const float CLINIC_THRESHOLD   = 0.40f;

    // Expedition (Scout’s Edge)
    const float EXPEDITION_SPEED_BASE = 0.08f, EXPEDITION_SPEED_PER_HOUR = 0.01f, EXPEDITION_SPEED_CAP = 0.15f;
    const float EXPEDITION_CRIT_BASE  = 0.05f, EXPEDITION_CRIT_PER_HOUR  = 0.005f, EXPEDITION_CRIT_CAP  = 0.10f;


    public class Ctx
    {
        public JobType job;
        public float hours;

        public float maxHpBonusPct, attackBonusPct, defenseBonusPct, baseDamageReducePct;
        public float critChanceFlat, critResistFlat;

        public int speedBuffTurns; public float speedBonusPctFirstTurns;
        public int critBuffTurns; public float critChanceBonusFirstTurns;
        public int critResistBuffTurns; public float critResistBonusFirstTurns;
        public int dmgReduceBuffTurns; public float dmgReduceFirstTurns;

        public int regenTurns; public float endTurnHealPct;
        public float startShieldPctMaxHp;

        public float firstOutgoingBonus; public bool usedFirstOutgoing;
        public float firstIncomingReduce; public bool usedFirstIncoming;

        public float surgeAtkBonusPct; public bool surgeApplied;

        public float rescueHealPct;
        public float rescueThreshold;
        public bool  rescueUsed;
    }

    public static Ctx Build(JobType job, float hours)
    {
        var c = new Ctx { job = job, hours = Mathf.Max(0f, hours) };
        switch (job)
        {
            case JobType.Gym:
                c.maxHpBonusPct = Mathf.Min(GYM_HP_PER_HOUR * c.hours, GYM_HP_CAP); break;

            case JobType.Forge:
                c.firstOutgoingBonus = Mathf.Clamp01(
                    FORGE_FIRST_HIT_BASE + Mathf.Min(FORGE_PER_HOUR * c.hours, FORGE_CAP - FORGE_FIRST_HIT_BASE)
                ); break;

            case JobType.PowerPlant:
                c.speedBuffTurns = 2; c.speedBonusPctFirstTurns = POWER_SPEED_FIRST2; break;

            case JobType.Quarry:
                c.defenseBonusPct = Mathf.Min(QUARRY_DEF_PER_HOUR * c.hours, QUARRY_DEF_CAP); break;

            case JobType.Grove:
                c.regenTurns = int.MaxValue;
                c.endTurnHealPct = Mathf.Clamp(
                    GROVE_BASE_HEAL + Mathf.Min(GROVE_PER_HOUR * c.hours, GROVE_HEAL_CAP - GROVE_BASE_HEAL),
                    0f, GROVE_HEAL_CAP
                ); break;

            case JobType.Workshop:
                c.critBuffTurns = 2; c.critChanceBonusFirstTurns = WORKSHOP_CRIT_FIRST2; break;

            case JobType.Harbor:
                c.startShieldPctMaxHp = HARBOR_START_SHIELD; c.regenTurns = 2; c.endTurnHealPct = HARBOR_TRICKLE; break;

            case JobType.CryoLab:
                c.critResistFlat = Mathf.Clamp(
                    CRYO_CRITRES_BASE + Mathf.Min(CRYO_PER_HOUR * c.hours, CRYO_CRITRES_CAP - CRYO_CRITRES_BASE),
                    0f, CRYO_CRITRES_CAP
                ); break;

            case JobType.Observatory:
                c.critChanceFlat = OBS_CRIT_FLAT; c.critResistBuffTurns = 2; c.critResistBonusFirstTurns = OBS_CRITRES_FIRST2; break;

            case JobType.Containment:
                c.baseDamageReducePct = Mathf.Clamp(
                    CONT_DMGRED_BASE + Mathf.Min(CONT_PER_HOUR * c.hours, CONT_DMGRED_CAP - CONT_DMGRED_BASE),
                    0f, CONT_DMGRED_CAP
                ); break;

            case JobType.Clinic:
                c.rescueThreshold = CLINIC_THRESHOLD;
                c.rescueHealPct = Mathf.Clamp(
                    CLINIC_TRIAGE_BASE + Mathf.Min(CLINIC_PER_HOUR * c.hours, CLINIC_TRIAGE_CAP - CLINIC_TRIAGE_BASE),
                    0f, CLINIC_TRIAGE_CAP
                );
                break;

            case JobType.WyrmDen:
                c.surgeAtkBonusPct = WYRM_SURGE_ATK; break;

            case JobType.ShadowMarket:
                c.firstIncomingReduce = SHADOW_FIRST_INCOMING_REDUCE; break;

            case JobType.Sanctum:
                c.startShieldPctMaxHp = SANCTUM_START_SHIELD; c.dmgReduceBuffTurns = 2; c.dmgReduceFirstTurns = SANCTUM_DMGRED_FIRST2; break;

            case JobType.Mine:
                c.attackBonusPct = Mathf.Min(MINE_ATK_PER_HOUR * c.hours, MINE_ATK_CAP); break;
                
            case JobType.Expedition:
                c.speedBuffTurns = 2;
                c.speedBonusPctFirstTurns = Mathf.Clamp(
                    EXPEDITION_SPEED_BASE + (EXPEDITION_SPEED_PER_HOUR * c.hours),
                    0f, EXPEDITION_SPEED_CAP
                );

                c.critChanceFlat = Mathf.Clamp(
                    EXPEDITION_CRIT_BASE + (EXPEDITION_CRIT_PER_HOUR * c.hours),
                    0f, EXPEDITION_CRIT_CAP
                );
                break;

        }
        return c;
    }
}
