// Assets/Scripts/Battle/AutoBattleLogArchive.cs
using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Persisted archive of auto-battle logs so players can review what happened while auto-battle was running.
/// Gated by FeatureId.Battle_LogArchive.
/// </summary>
[Serializable]
public class AutoBattleLogEntry
{
    public long unix;
    public string opponentId;
    public string opponentName;
    public int opponentLevel;

    public bool victory;
    public bool escaped;
    public string defeatReason;

    // Flat text lines from BattleLogger (already formatted)
    public List<string> lines = new List<string>();
}

    public static class AutoBattleLogArchive
    {
        // Prevent runaway save growth.
        // (If you want longer history later, bump this and consider compressing the lines.)
        public const int MaxEntries = 100;

        public static void TryArchiveCurrentBattle(bool wasAutoResolved, BattleResult result)
        {
            if (!wasAutoResolved) return;

            if (FeatureUnlockManager.I == null || !FeatureUnlockManager.I.IsUnlocked(FeatureId.IdleBattle_LogArchive))
                return;

            var data = SaveManager.Data;
            if (data == null) return;

            data.EnsureTransientSets();
            data.autoBattleLogArchive ??= new List<AutoBattleLogEntry>();

            bool wildWasPremium = RiftManager.I != null && RiftManager.I.CurrentWildIsPremium;
            string opponentName = result.wildDef
                ? MonsterNameFormatter.Format(result.wildDef, wildWasPremium)
                : "Unknown";

            var entry = new AutoBattleLogEntry
            {
                unix = SaveManager.NowUnix(),
                opponentId = result.wildDef ? result.wildDef.id : "",
                opponentName = opponentName,
                opponentLevel = Mathf.Max(1, result.wildLevel),
                victory = result.victory,
                escaped = result.escaped,
                defeatReason = BuildDefeatReason(result.victory, result.escaped),
                lines = new List<string>(BattleLogger.GetLinesSnapshot())
            };

            data.autoBattleLogArchive.Add(entry);

            // Trim oldest
            int over = data.autoBattleLogArchive.Count - MaxEntries;
            if (over > 0)
                data.autoBattleLogArchive.RemoveRange(0, over);

            SaveManager.Save();
        }

        public static void AddEntry(
        PlayerManager data,
        string opponentId,
        int opponentLevel,
        bool victory,
        bool escaped,
        IReadOnlyList<string> logLines)
    {
        if (data == null) return;

        data.EnsureTransientSets();
        data.autoBattleLogArchive ??= new List<AutoBattleLogEntry>();

        // Copy the read-only log into a persisted List<string>
        var copy = (logLines != null) ? new List<string>(logLines) : new List<string>();

        string opponentName = "Unknown";
        if (!string.IsNullOrEmpty(opponentId))
        {
            try
            {
                var def = MonsterLibraryLocator.GetById(opponentId);
                if (def != null && !string.IsNullOrEmpty(def.displayName))
                    opponentName = def.displayName;
            }
            catch { }
        }

        var entry = new AutoBattleLogEntry
        {
            unix = SaveManager.NowUnix(),
            opponentId = opponentId ?? "",
            opponentName = opponentName,
            opponentLevel = Mathf.Max(1, opponentLevel),
            victory = victory,
            escaped = escaped,
            defeatReason = BuildDefeatReason(victory, escaped),
            lines = copy
        };

        data.autoBattleLogArchive.Add(entry);

        int over = data.autoBattleLogArchive.Count - MaxEntries;
        if (over > 0)
            data.autoBattleLogArchive.RemoveRange(0, over);

        SaveManager.Save();
    }

    private static string BuildDefeatReason(bool victory, bool escaped)
    {
        if (victory) return string.Empty;
        if (escaped) return "On this turn wild monster fled.";
        return string.Empty;
    }
}
