using UnityEngine;

public static class EvolutionHelper
{
    
    public static void RebuildStatsAdditive(OwnedMonsterData m, MonsterDataSO newForm, float carryScale = 1f)
    {
        if (m == null || newForm == null) return;
        var tb = m.trainingBonus;

        int maxHP = Mathf.RoundToInt(BattleCalc.CalcHP(newForm, Mathf.Max(1, m.level)));
        maxHP += Mathf.RoundToInt(tb.hp * carryScale);
        m.currentHP = Mathf.Max(1, maxHP);

        // Bake attack increase
        m.flatAtkBonus += Mathf.RoundToInt(tb.atk * (carryScale - 1f));
    }
}
