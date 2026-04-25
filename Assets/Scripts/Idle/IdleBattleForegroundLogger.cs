using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Option 1 bridge:
/// - During foreground auto-battles we suppress PostBattleSummary popups.
/// - We log each auto-battle result into IdleBattleStore (merged), and set a
///   pending summary flag so IdleBattleManager can open IdleBattleRewardPanelUI.
/// </summary>
public static class IdleBattleForegroundLogger
{
    public static void LogBattle(BattleResult result, int energySpentGuess = 0)
    {
        if (result.wildDef == null) return;

        var s = IdleBattleStore.Load();
        if (s == null) return;

        s.log ??= new List<IdleRiftLogEntry>();

        // Merge by monsterId.
        AddToLogMerged(s.log, result.wildDef.id, Mathf.Max(0, result.creditsGained), premium: false);

        if (energySpentGuess > 0)
            s.totalEnergySpent += Mathf.Max(0, energySpentGuess);

        // Match IdleBattleManager behavior: set an optional hasPendingSummary flag if present.
        TrySetBoolFieldIfPresent(s, "hasPendingSummary", true);

        IdleBattleStore.Save(s);
    }

    public static void MarkPendingIfLogExists()
    {
        var s = IdleBattleStore.Load();
        if (s == null) return;

        bool hasLog = (s.log != null && s.log.Count > 0);
        if (!hasLog) return;

        TrySetBoolFieldIfPresent(s, "hasPendingSummary", true);
        IdleBattleStore.Save(s);
    }

    private static void AddToLogMerged(List<IdleRiftLogEntry> log, string monsterId, int credits, bool premium)
    {
        if (log == null || string.IsNullOrEmpty(monsterId)) return;

        IdleRiftLogEntry e = null;
        for (int i = 0; i < log.Count; i++)
        {
            if (log[i] != null && log[i].monsterId == monsterId) { e = log[i]; break; }
        }
        if (e == null)
        {
            e = new IdleRiftLogEntry
            {
                monsterId = monsterId,
                count = 0,
                credits = 0,
                premiumSeen = false
            };
            log.Add(e);
        }

        e.count += 1;
        e.credits += Mathf.Max(0, credits);
        e.premiumSeen |= premium;
    }

    private static void TrySetBoolFieldIfPresent(object obj, string fieldName, bool value)
    {
        if (obj == null) return;

        var t = obj.GetType();
        var f = t.GetField(fieldName);
        if (f == null || f.FieldType != typeof(bool)) return;
        f.SetValue(obj, value);
    }
}
