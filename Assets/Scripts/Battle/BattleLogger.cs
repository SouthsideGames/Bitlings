using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

// ─────────────────────────────────────────────────────────────
// Shared types (kept here so all scripts can "see" them)
// If you already created BattleLogTypes.cs, delete that or
// remove these duplicates.
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
// Color & formatting helpers (as requested)
// ─────────────────────────────────────────────────────────────
public static class BattleLogColors
{
    // Tweak freely; these are readable on light/dark UIs
    public const string Base    = "#FFFFFF"; // white
    public const string Buff    = "#36D674"; // green-ish
    public const string Debuff  = "#FF7A53"; // orange/red
    public const string Crit    = "#FFD94A"; // gold
    public const string Info    = "#7FD7FF"; // cyan (type effectiveness, notes)
    public const string Name    = "#D7B6FF"; // attacker/skill names if you want flair
}

public enum ModKind { Buff, Debuff, Info }

[Serializable]
public struct DamageMod
{
    public ModKind kind;     // Buff/Debuff/Info
    public float amount;     // positive number for display (+X / -Y). For Info you can leave 0.
    public string label;     // e.g., "attack up", "armor", "type"
    
    public DamageMod(ModKind kind, float amount, string label = null)
    {
        this.kind   = kind;
        this.amount = amount;
        this.label  = label;
    }
}

public static class DamageLogFormatter
{
    // Builds:  27 dmg ( <base>18</base> (<buff>+6</buff> <debuff>-2</debuff>) [flags] )
    public static string FormatDamageLine(
        string attackerName,
        string targetName,
        string moveName,
        int totalDamage,
        int baseDamage,
        IReadOnlyList<DamageMod> mods,
        bool crit = false,
        float effectiveness = 1f // 2f = super, 0.5f=weak, 1f = normal
    )
    {
        var sb = new StringBuilder(128);

        // Header story bit
        if (!string.IsNullOrEmpty(attackerName) && !string.IsNullOrEmpty(targetName))
            sb.Append($"{attackerName} ");
        else if (!string.IsNullOrEmpty(attackerName))
            sb.Append($"{attackerName} ");

        if (!string.IsNullOrEmpty(moveName))
            sb.Append($"uses {moveName} ");

        if (!string.IsNullOrEmpty(targetName))
            sb.Append($"on {targetName} ");

        sb.Append("→ ");
        sb.Append($"{totalDamage} dmg ");

        // Breakdown
        sb.Append("(");
        sb.Append(" <color=").Append(BattleLogColors.Base).Append(">")
          .Append(baseDamage)
          .Append("</color>");

        // Parenthesized buff/debuff details
        bool hasBD = HasBuffOrDebuff(mods);
        if (hasBD)
        {
            sb.Append(" (");
            for (int i = 0; i < mods.Count; i++)
            {
                var m = mods[i];
                if (m.kind == ModKind.Info) continue; // keep Info outside the parens

                if (i > 0 && (mods[i - 1].kind != ModKind.Info))
                    sb.Append(" ");

                if (m.kind == ModKind.Buff)
                {
                    sb.Append("<color=").Append(BattleLogColors.Buff).Append(">");
                    sb.Append("+").Append(Mathf.RoundToInt(m.amount));
                    if (!string.IsNullOrEmpty(m.label)) sb.Append($" {m.label}");
                    sb.Append("</color>");
                }
                else if (m.kind == ModKind.Debuff)
                {
                    sb.Append("<color=").Append(BattleLogColors.Debuff).Append(">");
                    sb.Append("-").Append(Mathf.RoundToInt(m.amount));
                    if (!string.IsNullOrEmpty(m.label)) sb.Append($" {m.label}");
                    sb.Append("</color>");
                }
            }
            sb.Append(")");
        }

        // Flags like CRIT or effectiveness live after parens
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

        // Also print any Info-type mods as trailing tags (e.g., “pierce”, “ignores armor”)
        if (mods != null)
        {
            for (int i = 0; i < mods.Count; i++)
            {
                var m = mods[i];
                if (m.kind != ModKind.Info) continue;
                if (!string.IsNullOrEmpty(m.label))
                    sb.Append(" <color=").Append(BattleLogColors.Info).Append(">")
                      .Append(m.label)
                      .Append("</color>");
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
    public static event Action<string>   OnBattleBegan;
    public static event Action<bool>     OnBattleEnded;
    public static event Action<string>   OnEncounterBegan;
    public static event Action<bool>     OnEncounterEnded;

    static readonly List<LogEntry> _entries = new List<LogEntry>(512);
    static string _currentBattleLabel;
    static string _currentEncounterLabel;

    public static IReadOnlyList<LogEntry> Entries => _entries;

    static long NowUnix() => SaveManager.NowUnix(); // assumes your SaveManager exposes this

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
        if (string.IsNullOrEmpty(message)) return;

        var e = new LogEntry
        {
            unix        = NowUnix(),
            scope       = scope,
            text        = message,
            battleLabel = _currentBattleLabel ?? _currentEncounterLabel
        };

        _entries.Add(e);
        OnLogAppended?.Invoke(e);
    }

    // ─────────────────────────────────────────────────────────
    // Narrative damage line with colored base/buffs/debuffs
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
        string line = DamageLogFormatter.FormatDamageLine(
            attackerName, targetName, moveName, totalDamage, baseDamage, mods, crit, effectiveness
        );
        Log(line, scope);
    }

    // Optional: simple wrappers for common beats
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
    // Maintenance
    // ─────────────────────────────────────────────────────────
    public static void ClearAll()
    {
        _entries.Clear();
        OnLogAppended?.Invoke(new LogEntry { unix = NowUnix(), scope = LogScope.System, text = "(log cleared)" });
    }
}
