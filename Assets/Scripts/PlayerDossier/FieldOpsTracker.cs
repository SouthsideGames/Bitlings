using System.Collections.Generic;
using UnityEngine;

public static class FieldOpsTracker
{
    private const int MAX_HIGHLIGHTS = 5;

    private static FieldOpsStats Stats
    {
        get
        {
            var data = SaveManager.Data;
            if (data == null) return null;

            data.fieldOps ??= new FieldOpsStats();
            if (data.fieldOps.recentHighlights == null)
                data.fieldOps.recentHighlights = new List<string>();

            return data.fieldOps;
        }
    }

    public static void RecordRift(MonsterDataSO wild)
    {
        var stats = Stats;
        if (stats == null) return;

        stats.riftsInitiated = Mathf.Max(0, stats.riftsInitiated + 1);
        SaveManager.Save();
    }

    /// <summary>
    /// Called whenever a capture is attempted (success or fail).
    /// </summary>
    public static void RecordCaptureAttempt(MonsterDataSO def, bool success, bool isPremium)
    {
        if (!def) return;
        var stats = Stats;
        if (stats == null) return;

        stats.captureAttempts = Mathf.Max(0, stats.captureAttempts + 1);

        if (success)
        {
            stats.capturesSuccessful = Mathf.Max(0, stats.capturesSuccessful + 1);

            // Streak handling
            stats.currentCaptureStreak = Mathf.Max(0, stats.currentCaptureStreak + 1);
            if (stats.currentCaptureStreak > stats.longestCaptureStreak)
                stats.longestCaptureStreak = stats.currentCaptureStreak;

            // Rare captured? (Rare+)
            if (def.rarity == Rarity.Rare ||
                def.rarity == Rarity.Epic ||
                def.rarity == Rarity.Legendary ||
                def.rarity == Rarity.Mythic)
            {
                stats.rareBitlingsFound = Mathf.Max(0, stats.rareBitlingsFound + 1);
            }

            // Premium captured?
            if (isPremium)
            {
                stats.premiumDiscoveries = Mathf.Max(0, stats.premiumDiscoveries + 1);
                AddHighlight($"Captured {MonsterNameFormatter.Format(def, true)}!");
            }
            else
            {
                AddHighlight($"Captured {MonsterNameFormatter.Format(def, false)}.");
            }
        }
        else
        {
            // Reset streak on failure
            stats.currentCaptureStreak = 0;
        }

        SaveManager.Save();
    }

    /// <summary>
    /// Called when a Rift (boss) is stabilized.
    /// </summary>
    public static void RecordRiftStabilization(MonsterDataSO bossDef)
    {
        var stats = Stats;
        if (stats == null) return;

        stats.riftStabilizations = Mathf.Max(0, stats.riftStabilizations + 1);

        string name = bossDef ? bossDef.displayName : "Rift";
        AddHighlight($"Stabilized a Rift ({name}).");

        SaveManager.Save();
    }

    private static void AddHighlight(string msg)
    {
        var stats = Stats;
        if (stats == null) return;

        stats.recentHighlights.Insert(0, msg);
        if (stats.recentHighlights.Count > MAX_HIGHLIGHTS)
            stats.recentHighlights.RemoveAt(stats.recentHighlights.Count - 1);
    }
}
