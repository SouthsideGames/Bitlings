using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Lightweight in-memory trace buffer for on-device debugging.
/// - Gated by SaveManager.Data.diagnosticsUnlocked (so players never see it unless you unlock via cheat)
/// - Stores a rolling list of human-readable lines plus a few typed "last" records.
/// </summary>
public static class DiagnosticsTrace
{
    // Rolling buffer of recent lines
    const int MAX_LINES = 200;
    static readonly List<string> _lines = new List<string>(MAX_LINES);

    // Typed "last" records (quick access in snapshot)
    public struct EncounterStart
    {
        public string monsterId;
        public int level;
        public bool isBoss;
        public bool isShiny;
        public string seedString;
        public long unix;
    }

    public struct ShinyRoll
    {
        public string monsterId;
        public float chance;
        public float roll;
        public bool success;
        public long unix;
    }

    public struct HireDecision
    {
        public string monsterId;
        public bool accepted;
        public bool success;
        public string notes;
        public long unix;
    }

    static EncounterStart _lastEncounter;
    static ShinyRoll _lastShinyRoll;
    static HireDecision _lastHire;

    public static EncounterStart LastEncounter => _lastEncounter;
    public static ShinyRoll LastShinyRoll => _lastShinyRoll;
    public static HireDecision LastHire => _lastHire;

    static bool IsEnabled()
    {
        try
        {
            return SaveManager.Data != null && SaveManager.Data.diagnosticsUnlocked;
        }
        catch { return false; }
    }

    static void Add(string type, string msg)
    {
        if (!IsEnabled()) return;

        long now = SaveManager.NowUnix();
        string line = $"[{UnixToIso(now)}] {type}: {msg}";

        _lines.Add(line);
        if (_lines.Count > MAX_LINES)
            _lines.RemoveRange(0, Mathf.Max(1, _lines.Count - MAX_LINES));
    }

    public static string PeekRecent(string typeContains = "", int max = 8)
    {
        if (!IsEnabled()) return "(locked)";
        if (max <= 0) max = 1;

        typeContains = typeContains ?? string.Empty;

        int found = 0;
        var sb = new System.Text.StringBuilder(512);

        for (int i = _lines.Count - 1; i >= 0; i--)
        {
            var line = _lines[i];
            if (string.IsNullOrEmpty(typeContains) || line.IndexOf(typeContains, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                sb.AppendLine(line);
                found++;
                if (found >= max) break;
            }
        }

        if (found == 0) return "(none)";
        return sb.ToString();
    }

    public static void RecordEncounterStart(string monsterId, int level, bool isBoss, bool isShiny, string seedString)
    {
        if (!IsEnabled()) return;

        long now = SaveManager.NowUnix();
        monsterId = string.IsNullOrWhiteSpace(monsterId) ? "?" : monsterId;

        _lastEncounter = new EncounterStart
        {
            monsterId = monsterId,
            level = level,
            isBoss = isBoss,
            isShiny = isShiny,
            seedString = seedString ?? "",
            unix = now
        };

        Add("Encounter Start", $"{monsterId} L{level} | boss={isBoss} shiny={isShiny} | seed={seedString}");
    }

    public static void RecordShinyRoll(string monsterId, float chance, float roll, bool success)
    {
        if (!IsEnabled()) return;

        long now = SaveManager.NowUnix();
        monsterId = string.IsNullOrWhiteSpace(monsterId) ? "?" : monsterId;

        _lastShinyRoll = new ShinyRoll
        {
            monsterId = monsterId,
            chance = chance,
            roll = roll,
            success = success,
            unix = now
        };

        Add("Shiny Roll", $"{monsterId} | chance={chance:0.0000} roll={roll:0.0000} => {(success ? "SHINY" : "normal")}");
    }

    public static void RecordHireDecision(string monsterId, bool accepted, bool success, string notes = "")
    {
        if (!IsEnabled()) return;

        long now = SaveManager.NowUnix();
        monsterId = string.IsNullOrWhiteSpace(monsterId) ? "?" : monsterId;

        _lastHire = new HireDecision
        {
            monsterId = monsterId,
            accepted = accepted,
            success = success,
            notes = notes ?? "",
            unix = now
        };

        Add("Hire Decision", $"{monsterId} | accepted={accepted} success={success}" + (string.IsNullOrEmpty(notes) ? "" : $" | {notes}"));
    }

    static string UnixToIso(long unix)
    {
        if (unix <= 0) return "—";
        try
        {
            return DateTimeOffset.FromUnixTimeSeconds(unix).UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss");
        }
        catch { return "—"; }
    }
}
