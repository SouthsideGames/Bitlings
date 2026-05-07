using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
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
public enum LogScope { System, Rift, Battle }

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
    public const string Player = Base;      // player-side actor tint
    public const string Enemy  = "#E19C54"; // enemy-side actor tint
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

// ─────────────────────────────────────────────────────────────
// Math-first damage formatter
//
// The BattleTextBoxUI already handles narrative ("Super effective!",
// "Critical hit!").  This formatter turns the battle LOG into a
// damage-math breakdown the player can actually verify.
//
// Summary line (always):
//   Flamox → Glaceon  Ember  18 × … = 81
//
// Sub-lines (one per active factor, indented with tree glyphs):
//   ├ ×1.5  +9   atk↑    [buff]
//   ├ ×2.0  +36  type    [super-eff]
//   └ ×1.5  +20  crit    ★
//
// If there are no modifiers the sub-lines are omitted and the
// summary collapses to just:  Flamox → Glaceon  Ember  18
// ─────────────────────────────────────────────────────────────
public static class DamageLogFormatter
{
    // Indent prefix for sub-lines (two non-breaking spaces)
    const string Indent = "  ";

    public static string FormatDamageLine(
        string attackerName,
        string targetName,
        string moveName,
        int    totalDamage,
        int    baseDamage,
        IReadOnlyList<DamageMod> mods,
        bool  crit          = false,
        float effectiveness = 1f
    )
    {
        // ── Collect active factors ─────────────────────────────────────
        // Each factor: (multiplier, flat-delta, label, color, badge)
        // We compute running totals so each multiplier reflects its real
        // share of the final damage rather than just a raw ratio.
        var factors = new System.Collections.Generic.List<(float mult, int delta, string label, string color, string badge)>(8);

        float running = baseDamage;

        // Flat buff/debuff mods
        if (mods != null)
        {
            for (int i = 0; i < mods.Count; i++)
            {
                var m = mods[i];
                if (m.kind == ModKind.Info) continue;
                if (Mathf.Approximately(m.amount, 0f)) continue;

                float sign   = m.kind == ModKind.Buff ? 1f : -1f;
                float newVal = running + sign * m.amount;
                float mult   = running > 0f ? newVal / running : 1f;
                int   delta  = Mathf.RoundToInt(newVal) - Mathf.RoundToInt(running);
                running = newVal;

                string col   = m.kind == ModKind.Buff ? BattleLogColors.Buff : BattleLogColors.Debuff;
                string tag   = m.kind == ModKind.Buff ? "[buff]" : "[debuff]";
                factors.Add((mult, delta, m.label ?? "", col, tag));
            }
        }

        // Type effectiveness
        if (!Mathf.Approximately(effectiveness, 1f))
        {
            float newVal = running * effectiveness;
            int   delta  = Mathf.RoundToInt(newVal) - Mathf.RoundToInt(running);
            string col   = effectiveness > 1f ? BattleLogColors.Buff : BattleLogColors.Debuff;
            string tag   = effectiveness > 1f ? "[super-eff]" : "[not-eff]";
            factors.Add((effectiveness, delta, "type", col, tag));
            running = newVal;
        }

        // Crit (standard 1.5×)
        if (crit)
        {
            float newVal = running * 1.5f;
            int   delta  = Mathf.RoundToInt(newVal) - Mathf.RoundToInt(running);
            factors.Add((1.5f, delta, "crit", BattleLogColors.Crit, "★"));
            running = newVal;
        }

        bool hasFactors = factors.Count > 0;

        var sb = new StringBuilder(256);

        // ── Summary line ───────────────────────────────────────────────
        // Attacker → Target
        bool hasAttacker = !string.IsNullOrEmpty(attackerName);
        bool hasTarget   = !string.IsNullOrEmpty(targetName);

        if (hasAttacker)
            sb.Append("<color=").Append(BattleLogColors.Name).Append(">").Append(attackerName).Append("</color>");

        if (hasAttacker && hasTarget)
            sb.Append(" <color=").Append(BattleLogColors.Dim).Append(">→</color> ");

        if (hasTarget)
            sb.Append("<color=").Append(BattleLogColors.Name).Append(">").Append(targetName).Append("</color>");

        // Move name
        if (!string.IsNullOrEmpty(moveName))
            sb.Append("  <color=").Append(BattleLogColors.Info).Append(">").Append(moveName).Append("</color>");

        sb.Append("  ");

        // Formula: base [× factor × factor …] = total
        sb.Append("<color=").Append(BattleLogColors.Base).Append(">").Append(baseDamage).Append("</color>");

        if (hasFactors)
        {
            foreach (var f in factors)
            {
                sb.Append(" <color=").Append(BattleLogColors.Dim).Append(">×</color>");
                sb.Append("<color=").Append(f.color).Append(">").Append(f.mult.ToString("F2")).Append("</color>");
            }

            sb.Append("  <color=").Append(BattleLogColors.Dim).Append(">=</color>  ");
            sb.Append("<color=").Append(BattleLogColors.Base).Append(">").Append(totalDamage).Append("</color>");
        }

        // ── Sub-lines: one per factor ──────────────────────────────────
        if (hasFactors)
        {
            for (int i = 0; i < factors.Count; i++)
            {
                var f        = factors[i];
                bool isLast  = i == factors.Count - 1;
                string glyph = isLast ? "└" : "├";

                sb.Append("\n").Append(Indent);
                sb.Append("<color=").Append(BattleLogColors.Dim).Append(">").Append(glyph).Append("</color> ");

                // Multiplier
                sb.Append("<color=").Append(f.color).Append(">×").Append(f.mult.ToString("F2")).Append("</color>");

                // Flat delta  (+27 or -9)
                string sign = f.delta >= 0 ? "+" : "";
                sb.Append("  <color=").Append(f.color).Append(">").Append(sign).Append(f.delta).Append("</color>");

                // Label (e.g. "atk↑", "type", "crit")
                if (!string.IsNullOrEmpty(f.label))
                    sb.Append("  <color=").Append(BattleLogColors.Dim).Append(">").Append(f.label).Append("</color>");

                // Badge / tag  ([buff], ★, etc.)
                if (!string.IsNullOrEmpty(f.badge))
                    sb.Append("  <color=").Append(f.color).Append(">").Append(f.badge).Append("</color>");
            }
        }

        // ── Trailing Info-kind labels (e.g. "shield absorbed") ─────────
        if (mods != null)
        {
            for (int i = 0; i < mods.Count; i++)
            {
                var m = mods[i];
                if (m.kind != ModKind.Info || string.IsNullOrEmpty(m.label)) continue;
                sb.Append("\n").Append(Indent)
                  .Append("<color=").Append(BattleLogColors.Dim).Append(">└ ")
                  .Append(m.label)
                  .Append("</color>");
            }
        }

        return sb.ToString();
    }

    static bool HasBuffOrDebuff(IReadOnlyList<DamageMod> mods)
    {
        if (mods == null) return false;
        for (int i = 0; i < mods.Count; i++)
            if (mods[i].kind == ModKind.Buff || mods[i].kind == ModKind.Debuff) return true;
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
    public static event Action<string> OnRiftBegan;
    public static event Action<bool>   OnRiftEnded;
    public static event Action         OnLogCleared;

    // Fired whenever a Title proc happens (UI toast / side panel / etc.)
    public static event Action<TitleProcEvent> OnTitleProc;

    // Fired whenever a conditional title toggles ON/OFF (battle UI pulses / event feed)
    public static event Action<string, string, bool> OnTitleConditionChanged;

    static readonly List<LogEntry> _entries = new List<LogEntry>(512);
    static string _currentBattleLabel;
    static string _currentRiftLabel;
    static readonly List<string> _playerCombatantNames = new List<string>(8);
    static readonly List<string> _enemyCombatantNames = new List<string>(8);

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

    // Optional: cap stored lines to avoid unbounded growth across many rifts
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
    // Battle / Rift lifecycle
    // ─────────────────────────────────────────────────────────
    public static void BeginBattle(string label)
    {
        ClearAll(emitSystemLine: false);
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
        ClearCombatants();
        // Keep seed values for diagnostics even after battle ends.
    }

    public static void BeginRift(string label)
    {
        _currentRiftLabel = string.IsNullOrEmpty(label) ? "Rift" : label;
        OnRiftBegan?.Invoke(_currentRiftLabel);
    }

    public static void EndRift(bool victory)
    {
        OnRiftEnded?.Invoke(victory);
        _currentRiftLabel = null;
    }

    public static void SetCombatants(IReadOnlyList<string> playerNames, IReadOnlyList<string> enemyNames)
    {
        _playerCombatantNames.Clear();
        _enemyCombatantNames.Clear();

        AddNames(_playerCombatantNames, playerNames);
        AddNames(_enemyCombatantNames, enemyNames);
    }

    public static void ClearCombatants()
    {
        _playerCombatantNames.Clear();
        _enemyCombatantNames.Clear();
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

        string rendered = ApplyCombatantTint(message, scope);

        var e = new LogEntry
        {
            unix        = NowUnix(),
            scope       = scope,
            text        = rendered,
            battleLabel = _currentBattleLabel ?? _currentRiftLabel
        };

        _entries.Add(e);
        TrimToMax();

        OnLogAppended?.Invoke(e);
        OnLineLogged?.Invoke(rendered);
    }

    static void AddNames(List<string> target, IReadOnlyList<string> source)
    {
        if (target == null || source == null) return;

        for (int i = 0; i < source.Count; i++)
        {
            string name = source[i];
            if (string.IsNullOrWhiteSpace(name)) continue;

            bool exists = false;
            for (int j = 0; j < target.Count; j++)
            {
                if (string.Equals(target[j], name, StringComparison.OrdinalIgnoreCase))
                {
                    exists = true;
                    break;
                }
            }

            if (!exists)
                target.Add(name);
        }

        target.Sort((a, b) => b.Length.CompareTo(a.Length));
    }

    static string ApplyCombatantTint(string message, LogScope scope)
    {
        if (scope != LogScope.Battle) return message;
        if (string.IsNullOrEmpty(message)) return message;
        if (message.IndexOf("<color=", StringComparison.OrdinalIgnoreCase) >= 0) return message;

        string tinted = TintNames(message, _playerCombatantNames, BattleLogColors.Player);
        tinted = TintNames(tinted, _enemyCombatantNames, BattleLogColors.Enemy);
        return tinted;
    }

    static string TintNames(string input, List<string> names, string colorHex)
    {
        if (string.IsNullOrEmpty(input) || names == null || names.Count == 0)
            return input;

        string output = input;
        for (int i = 0; i < names.Count; i++)
        {
            string name = names[i];
            if (string.IsNullOrEmpty(name)) continue;

            string pattern = $@"(?<![A-Za-z0-9]){Regex.Escape(name)}(?![A-Za-z0-9])";
            output = Regex.Replace(
                output,
                pattern,
                $"<color={colorHex}>{name}</color>",
                RegexOptions.CultureInvariant
            );
        }

        return output;
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
        LogScope scope = LogScope.Rift)
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
    
    /// <summary>
    /// Logs when a conditional title's condition becomes active/inactive (edge-triggered by BattleManager).
    /// This is meant for debugging "why did stats change?" without spamming every turn.
    /// </summary>
    public static void LogTitleConditionState(
        string ownerName,
        string titleName,
        string conditionText,
        bool isActive,
        bool isInitial = false,
        LogScope scope = LogScope.Battle)
    {
        if (string.IsNullOrEmpty(ownerName)) ownerName = "Unknown";
        if (string.IsNullOrEmpty(titleName)) titleName = "None";
        if (string.IsNullOrEmpty(conditionText)) conditionText = "Unknown condition";

        string state = isActive ? "ON" : "OFF";
        string startTag = isInitial ? " (Start)" : "";

        string col = isActive ? BattleLogColors.Buff : BattleLogColors.Debuff;

        Log(
            $"<color={BattleLogColors.Title}>[TITLE {state}]</color> " +
            $"<color={BattleLogColors.Name}>{ownerName}</color> — " +
            $"<color={BattleLogColors.Title}>{titleName}</color>{startTag}: " +
            $"<color={BattleLogColors.Dim}>({conditionText})</color> " +
            $"<color={col}>{state}</color>",
            scope
        );
    }

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
    /// Back-compat helper used by some battle-loop callsites.
    /// Treats <paramref name="summary"/> as a proc line under a generic "Title" bucket.
    /// </summary>
    public static void LogTitleProc(string ownerNameOrId, string summary)
    {
        if (string.IsNullOrEmpty(summary)) return;
        LogTitleActivation(string.IsNullOrEmpty(ownerNameOrId) ? "Unknown" : ownerNameOrId, "Title", summary);
    }

public static void LogTitleConditionChanged(string ownerName, string titleName, bool isActive)
{
    if (string.IsNullOrEmpty(ownerName)) ownerName = "Unknown";
    if (string.IsNullOrEmpty(titleName)) titleName = "Title";

    string state = isActive ? "Activated" : "Deactivated";
    string col = isActive ? BattleLogColors.Buff : BattleLogColors.Debuff;

    Log(
        $"<color={BattleLogColors.Title}>◆ [{titleName}]</color> " +
        $"<color={BattleLogColors.Name}>{ownerName}</color>  " +
        $"<color={col}>{state}</color>",
        LogScope.Battle
    );

    OnTitleConditionChanged?.Invoke(ownerName, titleName, isActive);
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

        // Only dump if a battle or rift is active.
        if (string.IsNullOrEmpty(_currentBattleLabel) && string.IsNullOrEmpty(_currentRiftLabel))
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
        if (!string.IsNullOrEmpty(_currentRiftLabel)) sb.AppendLine($"Rift: {_currentRiftLabel}");
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

        DevLog.Log(sb.ToString());
    }
}
