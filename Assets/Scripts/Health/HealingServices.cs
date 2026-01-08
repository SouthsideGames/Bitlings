using UnityEngine;

public static class HealingService
{
    // Backward-compatible default:
    // - includeTraining = true (recommended)
    // - includeTitles = false (opt-in)
    public static int CalcMaxHP(
        MonsterDataSO def,
        int level,
        bool includeTraining = true,
        bool includeTitles = false)
    {
        if (!def) return 1;
        level = Mathf.Max(1, level);

        // Base HP curve (single source of truth)
        float hp = Mathf.Max(1f, BattleCalc.CalcHP(def, level));

        // ─────────────────────────────────────────
        // Training HP
        // ─────────────────────────────────────────
        if (includeTraining)
        {
            int trainingHP = ResolveTrainingHpFor(def.id);
            if (trainingHP > 0)
                hp += trainingHP;
        }

        // ─────────────────────────────────────────
        // Title HP (menu / simulation context)
        // ─────────────────────────────────────────
        if (includeTitles)
        {
            // Menu-safe context (isBattle=false) so battle-only conditionals never leak.
            // For max HP, assume "full health" (selfHp01=1).
            TitleContext ctx = TitleContext.Empty;
            ctx.ownedId = def.id;
            ctx.selfHp01 = 1f;

            hp = TitlesAdapter.GetStatValue(def.id, def, level, StatKind.HP.ToString(), ctx, hp);
        }

        return Mathf.Max(1, Mathf.RoundToInt(hp));
    }

    private static int ResolveTrainingHpFor(string monsterId)
    {
        if (string.IsNullOrEmpty(monsterId)) return 0;

        var data = SaveManager.Data;
        if (data == null) return 0;

        int best = 0;

        // Prefer team instance
        if (data.team != null)
        {
            for (int i = 0; i < data.team.Count; i++)
            {
                var m = data.team[i];
                if (m == null || m.monsterId != monsterId) continue;

                best = Mathf.Max(best, Mathf.Max(0, m.trainingBonus.hp));
            }
        }

        // Fallback to owned
        if (data.owned != null)
        {
            for (int i = 0; i < data.owned.Count; i++)
            {
                var m = data.owned[i];
                if (m == null || m.monsterId != monsterId) continue;

                best = Mathf.Max(best, Mathf.Max(0, m.trainingBonus.hp));
            }
        }

        return best;
    }

    public static int MissingHP(int currentHP, int maxHP)
        => Mathf.Max(0, maxHP - Mathf.Max(0, currentHP));

    public static int MedkitsToHealFull(int missingHP, int hpPerKit)
        => missingHP <= 0
            ? 0
            : Mathf.CeilToInt((float)missingHP / Mathf.Max(1, hpPerKit));

    public static int creditsToHealFull(HealingConfigSO cfg, int level, int missingHP)
    {
        if (missingHP <= 0) return 0;

        float perHP =
            Mathf.Max(
                0f,
                cfg.basecreditsPerHP +
                cfg.creditsPerHPPerLevel * Mathf.Max(1, level));

        int cost = Mathf.CeilToInt(perHP * missingHP);
        return Mathf.Max(cfg.minimumCost, cost);
    }
}
