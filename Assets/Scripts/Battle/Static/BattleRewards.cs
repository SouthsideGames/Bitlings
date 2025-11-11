using UnityEngine;
using System;
using System.Collections.Generic;

public static class BattleRewards
{
    public static int CoinsFor(bool victory, int wildLevel, float secondsSurvived)
    {
        if (victory)
        {
            int baseWin   = Mathf.RoundToInt(6 + wildLevel * 2.5f);
            int timeBonus = Mathf.RoundToInt(secondsSurvived * 0.75f);
            return baseWin + timeBonus;
        }
        else
        {
            return Mathf.RoundToInt(secondsSurvived * 1.0f);
        }
    }

    public static int CalcCoinsForWin(int wildLevel, Rarity rarity)
    {
        float rarityMul = 1f;
        switch (rarity)
        {
            case Rarity.Common:     rarityMul = 1.00f; break;
            case Rarity.Uncommon:   rarityMul = 1.10f; break;
            case Rarity.Rare:       rarityMul = 1.25f; break;
            case Rarity.Epic:       rarityMul = 1.40f; break;
            case Rarity.Legendary:  rarityMul = 1.60f; break;
            case Rarity.Mythic:     rarityMul = 1.80f; break;
            default:                rarityMul = 1.00f; break;
        }

        int baseWin = Mathf.RoundToInt(6 + wildLevel * 2.5f);
        int coins = Mathf.RoundToInt(baseWin * rarityMul);
        return Mathf.Max(1, coins);
    }

    public static void GrantVictoryXPAndEvo(int activeIndex, int wildLevel, MonsterLibrarySO library)
    {
        GrantVictoryXPAndEvo(activeIndex, wildLevel, library, 1f);
    }

    public static void GrantVictoryXPAndEvo(int activeIndex, int wildLevel, MonsterLibrarySO library, float xpMultiplier)
    {
        var data = SaveManager.Data;
        if (data == null || data.team == null) return;
        if (activeIndex < 0 || activeIndex >= data.team.Count) return;

        int baseXp = Mathf.Max(0, 5 + 2 * wildLevel);
        var m = data.team[activeIndex];

        int finalXp = Mathf.RoundToInt(
            baseXp *
            ShinySystems.TrainingXpMult(m) *
            Mathf.Max(0f, xpMultiplier)
        );

        if (m.level >= LevelRules.MaxLevel)
        {
            m.currentXP = 0;
            data.team[activeIndex] = m;
            SaveManager.Save();
            return;
        }

        m.currentXP += finalXp;

        while (m.level < LevelRules.MaxLevel)
        {
            int need = LevelRules.XPToNext(m.level);
            if (m.currentXP < need) break;

            m.currentXP -= need;
            m.level++;

            GameEvents.MonsterLeveled?.Invoke(m.monsterId, m.level);

            var defNow = (library != null) ? library.GetById(m.monsterId) : null;
            if (defNow != null && defNow.evolutionLevel > 0 && defNow.evolutionForm != null &&
                m.level >= defNow.evolutionLevel)
            {
                GameEvents.EvolutionOffered?.Invoke(m.monsterId);
            }
        }

        if (m.level >= LevelRules.MaxLevel) m.currentXP = 0;

        data.team[activeIndex] = m;

        if (!string.IsNullOrEmpty(data.trainingMonsterId) && data.trainingMonsterId == m.monsterId)
            data.trainingMonsterLevel = m.level;

        SaveManager.Save();
    }


}
