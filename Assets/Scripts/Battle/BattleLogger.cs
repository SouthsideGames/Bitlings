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
        this.summary   = summary;
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
    public const string Name   = "#D7B6FF"; // name tint
    public const string Title  = "#FFB347"; // title tint
    public const string Dim    = "#A9A9A9"; // grey
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
        var sb = new StringBuilder(160);

        if (!string.IsNullOrEmpty(attackerName))
            sb.Append(attackerName).Append(" ");
        if (!string.IsNullOrEmpty(moveName))
            sb.Append("uses ").Append(moveName).Append(" ");
        if (!string.IsNullOrEmpty(targetName))
            sb.Append("on ").Append(targetName).Append(" ");

        sb.Append("→ ");
        sb.Append(totalDamage).Append(" dmg ");

        sb.Append("(");
        sb.Append(" <color=").Append(BattleLogColors.Base).Append(">")
          .Append(baseDamage)
          .Append("</color>");

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

        if (crit)
            sb.Append(" <color=").Append(BattleLogColors.Crit).Append(">CRIT!</color>");

        if (!Mathf.Approximately(effectiveness, 1f))
        {
            if (effectiveness > 1f)
                sb.Append(" <color=").Append(BattleLogColors.Info).Append(">Super-effective</color>");
            else
                sb.Append(" <color=").Append(BattleLogColors.Info).Append(">Not very effective</color>");
        }

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

    // For BattleHistoryModalUI (append-only text stream)
    public static event Action<string> OnLineLogged;

    public static event Action<string> OnBattleBegan;
    public static event Action<bool>   OnBattleEnded;
    public static event Action<string> OnEncounterBegan;
    public static event Action<bool>   OnEncounterEnded;
    public static event Action         OnLogCleared;

    // Fired whenever a Title proc happens (UI toast / side panel / etc.)
    public static event Action<TitleProcEvent> OnTitleProc;

    static readonly List<LogEntry> _entries = new List<LogEntry>(512);
    static string _currentBattleLabel;
    static string _currentEncounterLabel;

    // ─────────────────────────────────────────────────────────
    // Combat snapshot (last N key moments)
    // ─────────────────────────────────────────────────────────
    static readonly List<string> _keyMoments = new List<string>(32);
    static int _keyMomentsCap = 20;

    public static int CurrentBattleSeed { get; private set; }
    public static string CurrentBattleSeedLabel { get; private set; }

    static bool _subscribedToCrash;
    static bool _dumping;

    static BattleLogger()
    {
        TrySubscribeCrashDump();
    }

    public static IReadOnlyList<LogEntry> Entries => _entries;

    // Snapshot helper for modal rebuild
    public static IReadOnlyList<LogEntry> GetEntriesSnapshot() => _entries;

    // Optional helper if you only want strings
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

    // EXACT string you said you don't want shown unless Auto is unlocked
    public const string HoldForAutoLine = "Hold for Auto";

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

    public static void SetKeyMomentsCap(int cap)
    {
        _keyMomentsCap = Mathf.Clamp(cap, 5, 200);
        TrimKeyMoments();
    }

    public static IReadOnlyList<string> GetKeyMomentsSnapshot(int max = 20)
    {
        if (max <= 0) max = _keyMomentsCap;
        int count = Mathf.Min(max, _keyMoments.Count);
        if (count <= 0) return Array.Empty<string>();

        int start = Mathf.Max(0, _keyMoments.Count - count);
        var list = new List<string>(count);
        for (int i = start; i < _keyMoments.Count; i++)
            list.Add(_keyMoments[i]);
        return list;
    }

    public static void AddKeyMoment(string line)
    {
        if (!Enabled) return;
        if (string.IsNullOrWhiteSpace(line)) return;

        _keyMoments.Add(line);
        TrimKeyMoments();
    }

    public static void ClearKeyMoments()
    {
        _keyMoments.Clear();
    }

    static void TrimKeyMoments()
    {
        if (_keyMomentsCap <= 0) return;
        int over = _keyMoments.Count - _keyMomentsCap;
        if (over <= 0) return;
        _keyMoments.RemoveRange(0, over);
    }

    // ─────────────────────────────────────────────────────────
    // Battle / Encounter lifecycle
    // ─────────────────────────────────────────────────────────
    public static void BeginBattle(string label)
    {
        _currentBattleLabel = string.IsNullOrEmpty(label) ? "Battle" : label;
        OnBattleBegan?.Invoke(_currentBattleLabel);
    }

    // New overload: include deterministic battle seed (debug reports)
    public static void BeginBattle(string label, int seed, string seedLabel)
    {
        CurrentBattleSeed = seed;
        CurrentBattleSeedLabel = seedLabel;
        BeginBattle(label);
    }

    public static void EndBattle(bool victory)
    {
        OnBattleEnded?.Invoke(victory);
        _currentBattleLabel = null;
        // Keep seed values for diagnostics even after battle ends.
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

        // do not show "Hold for Auto" unless Auto is unlocked
        if (ShouldSuppressAutoHint(message))
            return;

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
        OnLineLogged?.Invoke(message);
    }

    static bool ShouldSuppressAutoHint(string message)
    {
        if (!string.Equals(message, HoldForAutoLine, StringComparison.Ordinal))
            return false;

        var p = SaveManager.Data;
        if (p == null) return true;

        // infer auto-unlocked from autoTapLevel > 0
        return p.autoTapLevel <= 0;
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
    // Title helpers (NEW/IMPROVED)
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// Short single-line header to show which Title is currently applied.
    /// Call this at battle start (or right after you assign a title).
    /// </summary>
    public static void LogTitleHeader(string ownerName, string titleName, LogScope scope = LogScope.Battle)
    {
        if (string.IsNullOrEmpty(ownerName)) ownerName = "Unknown";
        if (string.IsNullOrEmpty(titleName)) titleName = "None";

        Log(
            $"<color={BattleLogColors.Title}>[TITLE]</color> " +
            $"<color={BattleLogColors.Name}>{ownerName}</color>: " +
            $"<color={BattleLogColors.Title}>{titleName}</color>",
            scope
        );
    }

    /// <summary>
    /// For roll outcomes (wild rolled / no roll / forced).
    /// </summary>
    public static void LogTitleRollResult(
        string ownerName,
        string rolledTitleName,
        bool forced,
        bool didRoll,
        LogScope scope = LogScope.Encounter)
    {
        if (string.IsNullOrEmpty(ownerName)) ownerName = "Wild";

        if (!didRoll)
        {
            Log(
                $"<color={BattleLogColors.Title}>[TITLE]</color> " +
                $"<color={BattleLogColors.Name}>{ownerName}</color>: " +
                $"<color={BattleLogColors.Dim}>No title rolled</color>",
                scope
            );
            return;
        }

        string forcedTag = forced
            ? $" <color={BattleLogColors.Info}>(forced)</color>"
            : "";

        Log(
            $"<color={BattleLogColors.Title}>[TITLE]</color> " +
            $"<color={BattleLogColors.Name}>{ownerName}</color> rolled " +
            $"<color={BattleLogColors.Title}>{(string.IsNullOrEmpty(rolledTitleName) ? "Unknown" : rolledTitleName)}</color>{forcedTag}",
            scope
        );
    }

    /// <summary>
    /// Logs a title activation/proc line and emits the TitleProcEvent for UI toasts.
    /// Use this when an effect actually triggers (not just passively exists).
    /// </summary>
    public static void LogTitleActivation(string ownerName, string titleName, string summary)
    {
        // Fire proc event FIRST so UI can react
        OnTitleProc?.Invoke(new TitleProcEvent(ownerName, titleName, summary));

        Log(
            $"<color={BattleLogColors.Title}>[TITLE PROC]</color> " +
            $"<color={BattleLogColors.Name}>{ownerName}</color> — " +
            $"<color={BattleLogColors.Title}>{titleName}</color>: {summary}",
            LogScope.Battle
        );
    }

    /// <summary>
    /// Optional: one-liner summary of title stat mods (useful for debugging).
    /// Example: ATK +10% | DEF +2 | SPD -1
    /// Pass only what you want to display.
    /// </summary>
    public static void LogTitleStatSummary(
        string ownerName,
        string titleName,
        int atkFlat = 0, float atkPct = 0f,
        int defFlat = 0, float defPct = 0f,
        int spdFlat = 0, float spdPct = 0f,
        float hpPct = 0f,
        LogScope scope = LogScope.Battle)
    {
        var sb = new StringBuilder(128);

        void AppendMod(string label, int flat, float pct)
        {
            bool hasFlat = flat != 0;
            bool hasPct = !Mathf.Approximately(pct, 0f);
            if (!hasFlat && !hasPct) return;

            if (sb.Length > 0) sb.Append(" <color=").Append(BattleLogColors.Dim).Append(">|</color> ");

            sb.Append(label).Append(" ");

            if (hasFlat)
            {
                string col = flat > 0 ? BattleLogColors.Buff : BattleLogColors.Debuff;
                sb.Append("<color=").Append(col).Append(">")
                  .Append(flat > 0 ? "+" : "").Append(flat)
                  .Append("</color>");
                if (hasPct) sb.Append(" ");
            }

            if (hasPct)
            {
                int pctI = Mathf.RoundToInt(pct * 100f);
                string col = pctI > 0 ? BattleLogColors.Buff : BattleLogColors.Debuff;
                sb.Append("<color=").Append(col).Append(">")
                  .Append(pctI > 0 ? "+" : "").Append(pctI).Append("%")
                  .Append("</color>");
            }
        }

        AppendMod("ATK", atkFlat, atkPct);
        AppendMod("DEF", defFlat, defPct);
        AppendMod("SPD", spdFlat, spdPct);

        if (!Mathf.Approximately(hpPct, 0f))
        {
            if (sb.Length > 0) sb.Append(" <color=").Append(BattleLogColors.Dim).Append(">|</color> ");
            int pctI = Mathf.RoundToInt(hpPct * 100f);
            string col = pctI > 0 ? BattleLogColors.Buff : BattleLogColors.Debuff;
            sb.Append("HP ")
              .Append("<color=").Append(col).Append(">")
              .Append(pctI > 0 ? "+" : "").Append(pctI).Append("%")
              .Append("</color>");
        }

        if (sb.Length == 0) return;

        Log(
            $"<color={BattleLogColors.Title}>[TITLE]</color> " +
            $"<color={BattleLogColors.Name}>{(string.IsNullOrEmpty(ownerName) ? "Unknown" : ownerName)}</color> — " +
            $"<color={BattleLogColors.Title}>{(string.IsNullOrEmpty(titleName) ? "None" : titleName)}</color> " +
            $"<color={BattleLogColors.Dim}>[{sb}]</color>",
            scope
        );
    }

    // ─────────────────────────────────────────────────────────
    // Turn helpers
    // ─────────────────────────────────────────────────────────
    public static void LogTurnStart(int turnIndex)
    {
        Log($"— Turn {turnIndex} begins —", LogScope.Battle);
    }

    public static void LogChoice(string actorName, string choiceSummary, bool isPlayer)
    {
        string side = isPlayer ? "Player" : "Enemy";
        Log($"{side}: {actorName} chooses {choiceSummary}.", LogScope.Battle);
    }

    // ─────────────────────────────────────────────────────────
    // Maintenance
    // ─────────────────────────────────────────────────────────
    public static void ClearAll(bool emitSystemLine = false)
    {
        _entries.Clear();
        OnLogCleared?.Invoke();

        _keyMoments.Clear();

        if (emitSystemLine && Enabled)
        {
            var e = new LogEntry
            {
                unix        = NowUnix(),
                scope       = LogScope.System,
                text        = "(log cleared)",
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

        _entries.RemoveRange(0, overflow);
    }

    // ─────────────────────────────────────────────────────────
    // Crash/abort dump (Exceptions/Errors)
    // ─────────────────────────────────────────────────────────
    static void TrySubscribeCrashDump()
    {
        if (_subscribedToCrash) return;
        _subscribedToCrash = true;

        Application.logMessageReceived += HandleUnityLog;
    }

    static void HandleUnityLog(string condition, string stackTrace, LogType type)
    {
        if (type != LogType.Exception && type != LogType.Error && type != LogType.Assert)
            return;

        // Only dump if a battle or encounter is active.
        if (string.IsNullOrEmpty(_currentBattleLabel) && string.IsNullOrEmpty(_currentEncounterLabel))
            return;

        if (_dumping) return;
        _dumping = true;
        try
        {
            DumpSnapshotToConsole(context: type.ToString());
        }
        finally
        {
            _dumping = false;
        }
    }

    public static void DumpSnapshotToConsole(string context = "")
    {
        var sb = new StringBuilder(2048);

        sb.AppendLine("=== BITLINGS COMBAT SNAPSHOT ===");
        if (!string.IsNullOrEmpty(context)) sb.AppendLine($"Context: {context}");
        if (!string.IsNullOrEmpty(_currentBattleLabel)) sb.AppendLine($"Battle: {_currentBattleLabel}");
        if (!string.IsNullOrEmpty(_currentEncounterLabel)) sb.AppendLine($"Encounter: {_currentEncounterLabel}");
        if (CurrentBattleSeed != 0)
        {
            sb.AppendLine($"BattleSeed: {CurrentBattleSeed}" + (string.IsNullOrEmpty(CurrentBattleSeedLabel) ? "" : $" ({CurrentBattleSeedLabel})"));
        }

        var km = GetKeyMomentsSnapshot(_keyMomentsCap);
        sb.AppendLine("— Key Moments —");
        if (km.Count == 0) sb.AppendLine("(none)");
        else
        {
            for (int i = 0; i < km.Count; i++)
                sb.AppendLine($"  - {km[i]}");
        }

        // Last 20 log lines (plain)
        sb.AppendLine("— Last Lines —");
        var lastLines = GetLinesSnapshot(20);
        if (lastLines.Count == 0) sb.AppendLine("(none)");
        else
        {
            for (int i = 0; i < lastLines.Count; i++)
                sb.AppendLine($"  {lastLines[i]}");
        }

        Debug.Log(sb.ToString());
    }
}
