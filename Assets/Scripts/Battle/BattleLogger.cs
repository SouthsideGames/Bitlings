using System;
using System.Collections.Generic;
using UnityEngine;

public enum LogScope { System, Encounter, Battle }

[Serializable]
public struct LogEntry
{
    public long unix;
    public LogScope scope;
    public string text;
    public string battleLabel; 
}

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

    static long NowUnix() => SaveManager.NowUnix(); 

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

    public static void Log(string message, LogScope scope = LogScope.Battle)
    {
        if (string.IsNullOrEmpty(message)) return;

        var e = new LogEntry
        {
            unix = NowUnix(),
            scope = scope,
            text = message,
            battleLabel = _currentBattleLabel ?? _currentEncounterLabel
        };

        _entries.Add(e);
        OnLogAppended?.Invoke(e);
    }

    public static void ClearAll()
    {
        _entries.Clear();
        OnLogAppended?.Invoke(new LogEntry { unix = NowUnix(), scope = LogScope.System, text = "(log cleared)" });
    }
}
