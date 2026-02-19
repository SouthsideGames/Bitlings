using System;
using UnityEngine;

public static class OwnedMonsterHP
{
    public enum Reason
    {
        Unknown,
        LoadNormalize,
        BattleWriteback,
        HealButton,
        OfflineRegen,
        OnlineRegen,
        StarterGrant,
        CaptureGrant,
        Debug,
    }

    public static int GetMaxHP(OwnedMonsterData om)
    {
        if (om == null || string.IsNullOrEmpty(om.monsterId)) return 1;
        var def = MonsterLibraryLocator.GetById(om.monsterId);
        if (def == null) return 1;
        return HealingService.CalcMaxHP(def, Mathf.Max(1, om.level), includeTraining: true, includeTitles: false);
    }

    public static bool Normalize(ref OwnedMonsterData om, long nowUnix, Reason reason = Reason.LoadNormalize)
    {
        if (om == null || string.IsNullOrEmpty(om.monsterId)) return false;

        int maxHP = GetMaxHP(om);
        int beforeHP = om.currentHP;
        long beforeLast = om.lastHPUnix;

        if (om.currentHP < 0) om.currentHP = maxHP;
        om.currentHP = Mathf.Clamp(om.currentHP, 0, maxHP);

        if (om.lastHPUnix < 0) om.lastHPUnix = 0;
        if (om.lastHPUnix == 0 && om.currentHP < maxHP)
            om.lastHPUnix = nowUnix;

        return (om.currentHP != beforeHP) || (om.lastHPUnix != beforeLast);
    }

    public static void SetHP(ref OwnedMonsterData om, int newHP, long nowUnix, Reason reason)
    {
        if (om == null || string.IsNullOrEmpty(om.monsterId)) return;

        int maxHP = GetMaxHP(om);
        int clamped = Mathf.Clamp(newHP, 0, maxHP);
        bool changed = clamped != om.currentHP;

        om.currentHP = clamped;

        // When HP changes and the monster isn't full, we want regen timers to start counting.
        if (changed && om.currentHP < maxHP)
            om.lastHPUnix = nowUnix;

        if (om.currentHP >= maxHP)
        {
            // Full HP: keep lastHPUnix stable (avoids UI churn).
            // Do not force it to now.
        }

        // Final safety.
        if (om.currentHP < 0) om.currentHP = 0;
    }

    public static void SetFull(ref OwnedMonsterData om, long nowUnix, Reason reason)
    {
        if (om == null || string.IsNullOrEmpty(om.monsterId)) return;
        int maxHP = GetMaxHP(om);
        SetHP(ref om, maxHP, nowUnix, reason);
    }
}
