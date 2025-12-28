using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

[Serializable]
public struct TitleProcEvent
{
    public string ownerName;
    public string titleName;
    public string summary;

    public TitleProcEvent(string ownerName, string titleName, string summary)
    {
        this.ownerName = ownerName;
        this.titleName = titleName;
        this.summary = summary;
    }
}

// ─────────────────────────────────────────────────────────────
// Shared types
// ─────────────────────────────────────────────────────────────
public enum LogScope { System, Encounter, Battle }

[Serializable]
public struct LogEntry
{
    public long unix;
    public LogScope scope;
    public string text;
    public string battleLabel;
}

// ─────────────────────────────────────────────────────────────
// Color & formatting helpers
// ─────────────────────────────────────────────────────────────
public static class BattleLogColors
{
    public const string Base   = "#FFFFFF"; // white
    public const string Buff   = "#36D674"; // green-ish
    public const string Debuff = "#FF7A53"; // orange/red
    public const string Crit   = "#FFD94A"; // gold
    public const string Info   = "#7FD7FF"; // cyan
    public const string Name   = "#D7B6FF";
    public const string Title  = "#FFB347";
}

public enum ModKind { Buff, Debuff, Info }

[Serializable]
public struct DamageMod
{
    public ModKind kind;   // Buff/Debuff/Info
    public float amount;   // positive number for display
    public string label;   // e.g., "attack up", "armor", "type"

    public DamageMod(ModKind kind, float amount, string label = null)
    {
        this.kind   = kind;
        this.amount = amount;
        this.label  = label;
    }
}

public static class DamageLogFormatter
{
    public static string FormatDamageLine(
        string attackerName,
        string targetName,
        string moveName,
        int totalDamage,
        int baseDamage,
        IReadOnlyList<DamageMod> mods,
        bool crit = false,
        float effectiveness = 1f
    )
    {
        var sb = new StringBuilder(128);

        // Header story bit
        if (!string.IsNullOrEmpty(attackerName))
            sb.Append(attackerName).Append(" ");
        if (!string.IsNullOrEmpty(moveName))
            sb.Append("uses ").Append(moveName).Append(" ");
        if (!string.IsNullOrEmpty(targetName))
            sb.Append("on ").Append(targetName).Append(" ");

        sb.Append("→ ");
        sb.Append(totalDamage).Append(" dmg ");

        // Breakdown
        sb.Append("(");
        sb.Append(" <color=").Append(BattleLogColors.Base).Append(">")
          .Append(baseDamage)
          .Append("</color>");

        // Buff / Debuff breakdown
        bool hasBD = HasBuffOrDebuff(mods);
        if (hasBD)
        {
            sb.Append(" (");
            bool first = true;
            for (int i = 0; i < (mods?.Count ?? 0); i++)
            {
                var m = mods[i];
                if (m.kind == ModKind.Info) continue;

                if (!first) sb.Append(" ");
                first = false;

                if (m.kind == ModKind.Buff)
                {
                    sb.Append("<color=").Append(BattleLogColors.Buff).Append(">");
                    sb.Append("+").Append(Mathf.RoundToInt(m.amount));
                    if (!string.IsNullOrEmpty(m.label)) sb.Append(" ").Append(m.label);
                    sb.Append("</color>");
                }
                else if (m.kind == ModKind.Debuff)
                {
                    sb.Append("<color=").Append(BattleLogColors.Debuff).Append(">");
                    sb.Append("-").Append(Mathf.RoundToInt(m.amount));
                    if (!string.IsNullOrEmpty(m.label)) sb.Append(" ").Append(m.label);
                    sb.Append("</color>");
                }
            }
            sb.Append(")");
        }

        // Crit / effectiveness flags
        if (crit)
        {
            sb.Append(" <color=").Append(BattleLogColors.Crit).Append(">CRIT!</color>");
        }

        if (!Mathf.Approximately(effectiveness, 1f))
        {
            if (effectiveness > 1f)
                sb.Append(" <color=").Append(BattleLogColors.Info).Append(">Super-effective</color>");
            else
                sb.Append(" <color=").Append(BattleLogColors.Info).Append(">Not very effective</color>");
        }

        // Info-type mods (pierce, ignores armor, etc.)
        if (mods != null)
        {
            for (int i = 0; i < mods.Count; i++)
            {
                var m = mods[i];
                if (m.kind != ModKind.Info) continue;
                if (!string.IsNullOrEmpty(m.label))
                {
                    sb.Append(" <color=").Append(BattleLogColors.Info).Append(">")
                      .Append(m.label)
                      .Append("</color>");
                }
            }
        }

        sb.Append(" )");
        return sb.ToString();
    }

    static bool HasBuffOrDebuff(IReadOnlyList<DamageMod> mods)
    {
        if (mods == null) return false;
        for (int i = 0; i < mods.Count; i++)
        {
            if (mods[i].kind == ModKind.Buff || mods[i].kind == ModKind.Debuff)
                return true;
        }
        return false;
    }
}

// ─────────────────────────────────────────────────────────────
// Battle Logger
// ─────────────────────────────────────────────────────────────
public static class BattleLogger
{
    public static event Action<LogEntry> OnLogAppended;

    // NEW: for BattleHistoryModalUI (append-only text stream)
    public static event Action<string> OnLineLogged;

    public static event Action<string> OnBattleBegan;
    public static event Action<bool>   OnBattleEnded;
    public static event Action<string> OnEncounterBegan;
    public static event Action<bool>   OnEncounterEnded;
    public static event Action         OnLogCleared;
    public static event Action<TitleProcEvent> OnTitleProc;

    static readonly List<LogEntry> _entries = new List<LogEntry>(512);
    static string _currentBattleLabel;
    static string _currentEncounterLabel;

    public static IReadOnlyList<LogEntry> Entries => _entries;

    // NEW: snapshot helper for modal rebuild
    public static IReadOnlyList<LogEntry> GetEntriesSnapshot() => _entries;

    // NEW: optional helper if you only want strings
    public static IReadOnlyList<string> GetLinesSnapshot(int max = 0)
    {
        if (max <= 0 || max >= _entries.Count)
        {
            var all = new List<string>(_entries.Count);
            for (int i = 0; i < _entries.Count; i++) all.Add(_entries[i].text);
            return all;
        }

        int start = Mathf.Max(0, _entries.Count - max);
        int count = _entries.Count - start;
        var list = new List<string>(count);
        for (int i = start; i < _entries.Count; i++) list.Add(_entries[i].text);
        return list;
    }

    // Global toggle (use for Manual vs Auto battle)
    public static bool Enabled { get; private set; } = true;

    // Optional: cap stored lines to avoid unbounded growth across many encounters
    public static int MaxEntries { get; private set; } = 800;

    static long NowUnix() => SaveManager.NowUnix();

    // ─────────────────────────────────────────────────────────
    // Enable / Disable
    // ─────────────────────────────────────────────────────────
    public static void SetEnabled(bool on) => Enabled = on;

    public static void SetMaxEntries(int max)
    {
        MaxEntries = Mathf.Clamp(max, 50, 10000);
        TrimToMax();
    }

    // ─────────────────────────────────────────────────────────
    // Battle / Encounter lifecycle
    // ─────────────────────────────────────────────────────────
    public static void BeginBattle(string label)
    {
        _currentBattleLabel = string.IsNullOrEmpty(label) ? "Battle" : label;
        OnBattleBegan?.Invoke(_currentBattleLabel);
    }

    public static void EndBattle(bool victory)
    {
        OnBattleEnded?.Invoke(victory);
        _currentBattleLabel = null;
    }

    public static void BeginEncounter(string label)
    {
        _currentEncounterLabel = string.IsNullOrEmpty(label) ? "Encounter" : label;
        OnEncounterBegan?.Invoke(_currentEncounterLabel);
    }

    public static void EndEncounter(bool victory)
    {
        OnEncounterEnded?.Invoke(victory);
        _currentEncounterLabel = null;
    }

    // ─────────────────────────────────────────────────────────
    // Plain text log
    // ─────────────────────────────────────────────────────────
    public static void Log(string message, LogScope scope = LogScope.Battle)
    {
        if (!Enabled) return;
        if (string.IsNullOrEmpty(message)) return;

        var e = new LogEntry
        {
            unix        = NowUnix(),
            scope       = scope,
            text        = message,
            battleLabel = _currentBattleLabel ?? _currentEncounterLabel
        };

        _entries.Add(e);
        TrimToMax();

        OnLogAppended?.Invoke(e);

        // NEW: battle history modal can subscribe to this
        OnLineLogged?.Invoke(message);
    }

    // ─────────────────────────────────────────────────────────
    // Damage + combat helpers
    // ─────────────────────────────────────────────────────────
    public static void LogDamage(
        string attackerName,
        string targetName,
        string moveName,
        int totalDamage,
        int baseDamage,
        IReadOnlyList<DamageMod> mods,
        bool crit = false,
        float effectiveness = 1f,
        LogScope scope = LogScope.Battle)
    {
        if (!Enabled) return;

        string line = DamageLogFormatter.FormatDamageLine(
            attackerName, targetName, moveName, totalDamage, baseDamage, mods, crit, effectiveness
        );
        Log(line, scope);
    }

    public static void LogMiss(string attackerName, string targetName, string moveName, LogScope scope = LogScope.Battle)
    {
        Log($"{attackerName} uses {moveName} on {targetName} → <color={BattleLogColors.Info}>Missed</color>", scope);
    }

    public static void LogDodge(string targetName, LogScope scope = LogScope.Battle)
    {
        Log($"{targetName} <color={BattleLogColors.Info}>dodged</color>!", scope);
    }

    public static void LogKO(string targetName, LogScope scope = LogScope.Battle)
    {
        Log($"{targetName} is <color={BattleLogColors.Info}>KO'd</color>!", scope);
    }

    // ─────────────────────────────────────────────────────────
    // Turn / Title helpers for “what happened this turn?”
    // ─────────────────────────────────────────────────────────
    public static void LogTurnStart(int turnIndex)
    {
        Log($"— Turn {turnIndex} begins —", LogScope.Battle);
    }

    public static void LogTitleActivation(string ownerName, string titleName, string summary)
    {
        // Fire proc event FIRST so UI can react even if logging is disabled later.
        OnTitleProc?.Invoke(new TitleProcEvent(ownerName, titleName, summary));

        Log(
            $"<color={BattleLogColors.Title}>[TITLE]</color> " +
            $"<color={BattleLogColors.Name}>{ownerName}'s</color> {titleName} " +
            $"<color={BattleLogColors.Buff}>activates</color>: {summary}",
            LogScope.Battle
        );
    }


    public static void LogChoice(string actorName, string choiceSummary, bool isPlayer)
    {
        // e.g. "Player: Umbra-01 chooses ATTACK (Shadow Claw)."
        string side = isPlayer ? "Player" : "Enemy";
        Log($"{side}: {actorName} chooses {choiceSummary}.", LogScope.Battle);
    }

    // ─────────────────────────────────────────────────────────
    // Maintenance
    // ─────────────────────────────────────────────────────────
    /// <summary>
    /// Clears all stored entries and notifies listeners that the log was cleared.
    /// Use emitSystemLine = true if you want a "(log cleared)" message added after clearing.
    /// </summary>
    public static void ClearAll(bool emitSystemLine = false)
    {
        _entries.Clear();
        OnLogCleared?.Invoke();

        if (emitSystemLine && Enabled)
        {
            var e = new LogEntry
            {
                unix       = NowUnix(),
                scope      = LogScope.System,
                text       = "(log cleared)",
                battleLabel = null
            };

            _entries.Add(e);
            OnLogAppended?.Invoke(e);
            OnLineLogged?.Invoke(e.text);
        }
    }

    static void TrimToMax()
    {
        if (MaxEntries <= 0) return;
        int overflow = _entries.Count - MaxEntries;
        if (overflow <= 0) return;

        // Remove oldest
        _entries.RemoveRange(0, overflow);
    }
}
