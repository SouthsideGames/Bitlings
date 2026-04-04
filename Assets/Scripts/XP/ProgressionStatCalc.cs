using UnityEngine;

public struct ProgressionStats
{
    public int basePlusLevelHP;
    public int basePlusLevelATK;
    public int basePlusLevelDEF;
    public int basePlusLevelSPD;

    public int trainingHP;
    public int trainingATK;
    public int trainingDEF;
    public int trainingSPD;

    public int totalHP;
    public int totalATK;
    public int totalDEF;
    public int totalSPD;
}

public static class ProgressionStatCalc
{
    public static ProgressionStats Get(OwnedMonsterData m)
    {
        ProgressionStats s = new ProgressionStats();

        if (m == null || string.IsNullOrEmpty(m.monsterId))
            return s;

        int lvl = Mathf.Max(1, m.level);

        var def = MonsterLibraryLocator.GetById(m.monsterId);
        if (def == null)
            return s;

        // IMPORTANT:
        // This "basePlusLevel" layer should NOT include TrainingBonus.
        // We derive it from your existing deterministic stat math.
        // TrainingBonus is added explicitly below.
        s.basePlusLevelHP  = Mathf.RoundToInt(BattleCalc.CalcHP(def, lvl));
        s.basePlusLevelATK = Mathf.RoundToInt(BattleCalc.CalcBaseAttack(def, lvl, 0, 0));
        s.basePlusLevelDEF = BattleCalc.CalcDefense(def, lvl);
        s.basePlusLevelSPD = BattleCalc.CalcSpeed(def, lvl);

        // TrainingBonus is your EV-like layer and must persist through evolution.
        // Stored on OwnedMonsterData.trainingBonus. :contentReference[oaicite:0]{index=0}
        s.trainingHP  = Mathf.Max(0, m.trainingBonus.hp);
        s.trainingATK = Mathf.Max(0, m.trainingBonus.atk);
        s.trainingDEF = Mathf.Max(0, m.trainingBonus.def);
        s.trainingSPD = Mathf.Max(0, m.trainingBonus.spd);

        s.totalHP  = s.basePlusLevelHP  + s.trainingHP;
        s.totalATK = s.basePlusLevelATK + s.trainingATK;
        s.totalDEF = s.basePlusLevelDEF + s.trainingDEF;
        s.totalSPD = s.basePlusLevelSPD + s.trainingSPD;

        return s;
    }

    public static int GetTotalMaxHP(OwnedMonsterData m)
    {
        var s = Get(m);
        return Mathf.Max(1, s.totalHP);
    }
}
