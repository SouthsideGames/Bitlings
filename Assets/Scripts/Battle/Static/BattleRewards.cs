using UnityEngine;
using System;
using System.Collections.Generic;

public static class BattleRewards
{
    public static int CreditsFor(bool victory, int wildLevel, float secondsSurvived)
    {
        float globalMul = 1f;
        if (GameBalance.TryGet(out var bal))
            globalMul = Mathf.Max(0f, bal.creditGainMultiplier);

        float eventMul = GetWorldEventBattleRewardMultiplier();

        if (victory)
        {
            int baseWin   = Mathf.RoundToInt(6 + wildLevel * 2.5f);
            int timeBonus = Mathf.RoundToInt(secondsSurvived * 0.75f);
            return Mathf.Max(0, Mathf.RoundToInt((baseWin + timeBonus) * globalMul * eventMul));
        }
        else
        {
            return Mathf.Max(0, Mathf.RoundToInt((secondsSurvived * 1.0f) * globalMul * eventMul));
        }
    }

    public static int CalcCreditsForWin(int wildLevel, Rarity rarity)
    {
        float globalMul = 1f;
        if (GameBalance.TryGet(out var bal))
            globalMul = Mathf.Max(0f, bal.creditGainMultiplier);

        float eventMul = GetWorldEventBattleRewardMultiplier();

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
        int credits = Mathf.RoundToInt(baseWin * rarityMul * globalMul * eventMul);
        return Mathf.Max(1, credits);
    }

    public static void GrantVictoryXPAndEvo(int activeIndex, int wildLevel, MonsterLibrarySO library)
    {
        GrantVictoryXPAndEvo(activeIndex, wildLevel, library, 1f);
    }

    public static void GrantVictoryXPAndEvo(int activeIndex, int wildLevel, MonsterLibrarySO library, float xpMultiplier)
    {
        int baseCores = Mathf.Max(0, 5 + 2 * wildLevel);
        float globalMul = 1f;
        if (GameBalance.TryGet(out var bal))
            globalMul = Mathf.Max(0f, bal.xpGainMultiplier);

        int finalCores = Mathf.RoundToInt(baseCores * Mathf.Max(0f, xpMultiplier) * globalMul);

        if (finalCores <= 0) return;

        ResourceManager.I?.Add(ResourceType.GrowthCore, finalCores);
    }

    private static float GetWorldEventBattleRewardMultiplier()
    {
        if (WorldEventSystem.I == null) return 1f;
        return Mathf.Max(0f, WorldEventSystem.I.GetBattleRewardMultiplier());
    }
}