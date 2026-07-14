using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

[Serializable]
public struct ExecutiveTrialMonthlyRecord
{
    public string monthKey; // "yyyy-MM"
    public int runs;
    public int wins;
    public int forfeits;
    public int bestStandardWins;
    public int bestHardcoreWins;
}

[Serializable]
public struct ExecutiveTrialStatsData
{
    // Existing cumulative stats
    public int totalRuns;
    public int totalForfeits;
    public int totalWinsAcrossRuns;
    public int bestStandardWins;
    public int bestHardcoreWins;

    // All-time run records
    public float longestRunSeconds;
    public int mostDamageDealtInRun;
    public int mostDamageTakenInRun;
    public int mostCritsInRun;
    public int mostBattlesInRun;

    // Monthly records (pruned to last 6 months on save)
    public List<ExecutiveTrialMonthlyRecord> monthlyRecords;
}

public static class ExecutiveTrialStats
{
    private const string FileName = "ExecutiveTrialStats.json";
    private const int MaxMonthsKept = 6;
    private static ExecutiveTrialStatsData? _cached;

    private static string FilePath =>
        Path.Combine(Application.persistentDataPath, FileName);
    private static string BackupPath =>
        Path.Combine(Application.persistentDataPath, "ExecutiveTrialStats.bak");

    private static string CurrentMonthKey => DateTime.Now.ToString("yyyy-MM");

    private static ExecutiveTrialStatsData Sanitize(ExecutiveTrialStatsData data)
    {
        data.totalRuns = Mathf.Max(0, data.totalRuns);
        data.totalForfeits = Mathf.Max(0, data.totalForfeits);
        data.totalWinsAcrossRuns = Mathf.Max(0, data.totalWinsAcrossRuns);
        data.bestStandardWins = Mathf.Max(0, data.bestStandardWins);
        data.bestHardcoreWins = Mathf.Max(0, data.bestHardcoreWins);
        data.longestRunSeconds = Mathf.Max(0f, data.longestRunSeconds);
        data.mostDamageDealtInRun = Mathf.Max(0, data.mostDamageDealtInRun);
        data.mostDamageTakenInRun = Mathf.Max(0, data.mostDamageTakenInRun);
        data.mostCritsInRun = Mathf.Max(0, data.mostCritsInRun);
        data.mostBattlesInRun = Mathf.Max(0, data.mostBattlesInRun);
        if (data.monthlyRecords == null) data.monthlyRecords = new List<ExecutiveTrialMonthlyRecord>();
        return data;
    }

    public static ExecutiveTrialStatsData Load()
    {
        if (_cached.HasValue) return _cached.Value;

        try
        {
            if (TryRead(FilePath, out var data) || TryRead(BackupPath, out data))
            {
                _cached = Sanitize(data);
                return _cached.Value;
            }

            _cached = Sanitize(default(ExecutiveTrialStatsData));
            return _cached.Value;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[ExecutiveTrialStats] Failed to load stats. Resetting. Error: {ex.Message}");
            _cached = Sanitize(default(ExecutiveTrialStatsData));
            return _cached.Value;
        }
    }

    public static void Save(ExecutiveTrialStatsData data)
    {
        data = Sanitize(data);
        PruneOldMonths(ref data);
        _cached = data;

        try
        {
            string json = JsonUtility.ToJson(data, true);
            AtomicWrite(FilePath, json);
            TryCopy(FilePath, BackupPath);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[ExecutiveTrialStats] Failed to save stats. Error: {ex.Message}");
        }
    }

    /// <summary>Back-compat overload (no run summary).</summary>
    public static void RecordRunEnd(ExecutiveTrialRunState.ExecutiveTrialMode mode, int wins, bool forfeited)
    {
        RecordRunEnd(mode, wins, forfeited, ExecutiveTrialRunSummary.Empty);
    }

    public static void RecordRunEnd(ExecutiveTrialRunState.ExecutiveTrialMode mode, int wins, bool forfeited,
                                    ExecutiveTrialRunSummary summary)
    {
        var data = Load();

        // ── Cumulative stats ──
        data.totalRuns = Mathf.Max(0, data.totalRuns) + 1;
        data.totalWinsAcrossRuns = Mathf.Max(0, data.totalWinsAcrossRuns) + Mathf.Max(0, wins);

        if (forfeited)
            data.totalForfeits = Mathf.Max(0, data.totalForfeits) + 1;

        int clampedWins = Mathf.Max(0, wins);
        if (mode == ExecutiveTrialRunState.ExecutiveTrialMode.Hardcore)
            data.bestHardcoreWins = Mathf.Max(data.bestHardcoreWins, clampedWins);
        else
            data.bestStandardWins = Mathf.Max(data.bestStandardWins, clampedWins);

        // ── All-time run records ──
        data.longestRunSeconds = Mathf.Max(data.longestRunSeconds, Mathf.Max(0f, summary.totalSecondsSurvived));
        data.mostDamageDealtInRun = Mathf.Max(data.mostDamageDealtInRun, Mathf.Max(0, summary.totalDamageDealt));
        data.mostDamageTakenInRun = Mathf.Max(data.mostDamageTakenInRun, Mathf.Max(0, summary.totalDamageTaken));
        data.mostCritsInRun = Mathf.Max(data.mostCritsInRun, Mathf.Max(0, summary.totalCrits));
        data.mostBattlesInRun = Mathf.Max(data.mostBattlesInRun, Mathf.Max(0, summary.totalBattles));

        // ── Monthly record ──
        UpdateMonthlyRecord(ref data, mode, clampedWins, forfeited);

        Save(data);
    }

    /// <summary>Returns the monthly record for the current month, or a zeroed struct if none exists.</summary>
    public static ExecutiveTrialMonthlyRecord GetCurrentMonthRecord()
    {
        var data = Load();
        string key = CurrentMonthKey;

        if (data.monthlyRecords != null)
        {
            for (int i = 0; i < data.monthlyRecords.Count; i++)
            {
                if (data.monthlyRecords[i].monthKey == key)
                    return data.monthlyRecords[i];
            }
        }

        return new ExecutiveTrialMonthlyRecord { monthKey = key };
    }

    // ─────────────────────────────────────────────────────────────
    // Internal helpers
    // ─────────────────────────────────────────────────────────────

    private static void UpdateMonthlyRecord(ref ExecutiveTrialStatsData data,
                                            ExecutiveTrialRunState.ExecutiveTrialMode mode, int wins, bool forfeited)
    {
        if (data.monthlyRecords == null) data.monthlyRecords = new List<ExecutiveTrialMonthlyRecord>();

        string key = CurrentMonthKey;
        int idx = -1;
        for (int i = 0; i < data.monthlyRecords.Count; i++)
        {
            if (data.monthlyRecords[i].monthKey == key) { idx = i; break; }
        }

        ExecutiveTrialMonthlyRecord rec;
        if (idx >= 0)
            rec = data.monthlyRecords[idx];
        else
            rec = new ExecutiveTrialMonthlyRecord { monthKey = key };

        rec.runs++;
        rec.wins += wins;
        if (forfeited) rec.forfeits++;

        if (mode == ExecutiveTrialRunState.ExecutiveTrialMode.Hardcore)
            rec.bestHardcoreWins = Mathf.Max(rec.bestHardcoreWins, wins);
        else
            rec.bestStandardWins = Mathf.Max(rec.bestStandardWins, wins);

        if (idx >= 0)
            data.monthlyRecords[idx] = rec;
        else
            data.monthlyRecords.Add(rec);
    }

    private static void PruneOldMonths(ref ExecutiveTrialStatsData data)
    {
        if (data.monthlyRecords == null || data.monthlyRecords.Count <= MaxMonthsKept) return;

        // Sort descending by monthKey (lexicographic works for yyyy-MM)
        data.monthlyRecords.Sort((a, b) => string.Compare(b.monthKey, a.monthKey, StringComparison.Ordinal));

        if (data.monthlyRecords.Count > MaxMonthsKept)
            data.monthlyRecords.RemoveRange(MaxMonthsKept, data.monthlyRecords.Count - MaxMonthsKept);
    }

    public static void ClearCache()
    {
        _cached = null;
    }

    private static bool TryRead(string path, out ExecutiveTrialStatsData data)
    {
        data = default;
        try
        {
            if (!File.Exists(path)) return false;
            string json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json)) return false;
            data = JsonUtility.FromJson<ExecutiveTrialStatsData>(json);
            return true;
        }
        catch
        {
            data = default;
            return false;
        }
    }

    private static void AtomicWrite(string path, string contents)
    {
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, contents ?? string.Empty);

        // Prefer File.Replace: no window where the destination is missing.
        if (File.Exists(path))
        {
            try { File.Replace(tmp, path, null); return; }
            catch { /* unsupported on some platforms; fall through */ }
        }

        try
        {
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }
        catch (Exception e)
        {
            Debug.LogError($"[ExecutiveTrialStats] AtomicWrite failed for '{path}': {e.GetType().Name} – {e.Message}");
            try { if (!File.Exists(path)) File.Copy(tmp, path); }
            catch (Exception e2) { Debug.LogError($"[ExecutiveTrialStats] AtomicWrite fallback copy failed: {e2.Message}"); }
            try { File.Delete(tmp); } catch { }
        }
    }

    private static void TryCopy(string src, string dst)
    {
        try
        {
            if (File.Exists(src)) File.Copy(src, dst, overwrite: true);
        }
        catch { }
    }
}
