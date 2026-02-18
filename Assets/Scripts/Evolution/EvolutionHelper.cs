using UnityEngine;

public static class EvolutionHelper
{

    public static string GetOwnedKey(OwnedMonsterData m)
    {
        if (m == null) return null;
        if (!string.IsNullOrEmpty(m.ownedUID)) return m.ownedUID;
        if (!string.IsNullOrEmpty(m.monsterId)) return m.monsterId;
        return null;
    }

    public static int CalcMaxHP(OwnedMonsterData m, MonsterDataSO def)
    {
        if (m == null || def == null) return 0;

        int lvl = Mathf.Max(1, m.level);
        int hp = 0;

        // Prefer the ownedId overload (lets titles apply, if you use that overload)
        string ownedKey = GetOwnedKey(m);
        try
        {
            if (!string.IsNullOrEmpty(ownedKey))
                hp = Mathf.RoundToInt(BattleCalc.CalcHP(def, lvl, ownedKey));
            else
                hp = Mathf.RoundToInt(BattleCalc.CalcHP(def, lvl));
        }
        catch
        {
            // fall back to base if calc blows up
            hp = Mathf.RoundToInt(def.baseHP);
        }

        hp += Mathf.Max(0, m.trainingBonus.hp);
        return Mathf.Max(1, hp);
    }

    public static void FullHealToMax(OwnedMonsterData m, MonsterDataSO def)
    {
        if (m == null || def == null) return;
        // Centralized HP contract (do not stamp timers during evolution heal unless you explicitly want to)
        SaveManager.SetMonsterHP(m, CalcMaxHP(m, def), stampLastHpUnix: false, save: false, fireEvents: false);
    }

    public static void RebuildStatsAdditive(OwnedMonsterData m, MonsterDataSO newForm, float carryScale = 1f)
    {
        if (m == null || newForm == null) return;

        FullHealToMax(m, newForm);
    }

    public static bool CanEvolve(OwnedMonsterData m, MonsterDataSO def)
    {
        if (m == null || def == null) return false;
        if (!def.evolutionForm || def.evolutionLevel <= 0) return false;
        return m.level >= def.evolutionLevel;
    }
}
