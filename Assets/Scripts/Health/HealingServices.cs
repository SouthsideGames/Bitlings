using UnityEngine;

public static class HealingService
{
    public static int CalcMaxHP(MonsterDataSO def, int level) => def.baseHP + (level - 1) * 12;
    public static int MissingHP(int currentHP, int maxHP) => Mathf.Max(0, maxHP - Mathf.Max(0, currentHP));

    // How many Medkits to fully heal; 1 kit = heals fixed HP chunk
    public static int MedkitsToHealFull(int missingHP, int hpPerKit) 
        => missingHP <= 0 ? 0 : Mathf.CeilToInt((float)missingHP / Mathf.Max(1, hpPerKit));

    public static int CoinsToHealFull(HealingConfigSO cfg, int level, int missingHP)
    {
        if (missingHP <= 0) return 0;
        float perHP = Mathf.Max(0f, cfg.baseCoinsPerHP + cfg.coinsPerHPPerLevel * Mathf.Max(1, level));
        int cost = Mathf.CeilToInt(perHP * missingHP);
        return Mathf.Max(cfg.minimumCost, cost);
    }
}
