using System;
using System.IO;
using UnityEngine;

[Serializable]
public struct IronCareerStatsData
{
    public int totalRuns;
    public int totalForfeits;
    public int totalWinsAcrossRuns;
    public int bestStandardWins;
    public int bestHardcoreWins;
}

public static class IronCareerStats
{
    private const string FileName = "IronCareerStats.json";
    private static IronCareerStatsData? _cached;

    private static string FilePath =>
        Path.Combine(Application.persistentDataPath, FileName);

    private static IronCareerStatsData Sanitize(IronCareerStatsData data)
    {
        data.totalRuns = Mathf.Max(0, data.totalRuns);
        data.totalForfeits = Mathf.Max(0, data.totalForfeits);
        data.totalWinsAcrossRuns = Mathf.Max(0, data.totalWinsAcrossRuns);
        data.bestStandardWins = Mathf.Max(0, data.bestStandardWins);
        data.bestHardcoreWins = Mathf.Max(0, data.bestHardcoreWins);
        return data;
    }

    public static IronCareerStatsData Load()
    {
        if (_cached.HasValue) return _cached.Value;

        try
        {
            if (File.Exists(FilePath))
            {
                string json = File.ReadAllText(FilePath);
                if (string.IsNullOrEmpty(json))
                {
                    _cached = default(IronCareerStatsData);
                    return _cached.Value;
                }

                _cached = Sanitize(JsonUtility.FromJson<IronCareerStatsData>(json));
                return _cached.Value;
            }

            _cached = default(IronCareerStatsData);
            return _cached.Value;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[IronCareerStats] Failed to load stats. Resetting. Error: {ex.Message}");
            _cached = default(IronCareerStatsData);
            return _cached.Value;
        }
    }

    public static void Save(IronCareerStatsData data)
    {
        data = Sanitize(data);
        _cached = data;

        try
        {
            string json = JsonUtility.ToJson(data, true);
            File.WriteAllText(FilePath, json);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[IronCareerStats] Failed to save stats. Error: {ex.Message}");
        }
    }

    public static void RecordRunEnd(IronCareerRunState.IronCareerMode mode, int wins, bool forfeited)
    {
        var data = Load();
        data.totalRuns = Mathf.Max(0, data.totalRuns) + 1;
        data.totalWinsAcrossRuns = Mathf.Max(0, data.totalWinsAcrossRuns) + Mathf.Max(0, wins);

        if (forfeited)
            data.totalForfeits = Mathf.Max(0, data.totalForfeits) + 1;

        int clampedWins = Mathf.Max(0, wins);
        if (mode == IronCareerRunState.IronCareerMode.Hardcore)
            data.bestHardcoreWins = Mathf.Max(data.bestHardcoreWins, clampedWins);
        else
            data.bestStandardWins = Mathf.Max(data.bestStandardWins, clampedWins);

        Save(data);
    }
}
