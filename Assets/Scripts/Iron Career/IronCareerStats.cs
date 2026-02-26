using System;
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
    private const string Key = "IronCareerStats_v1";
    private static IronCareerStatsData? _cached;

    public static IronCareerStatsData Load()
    {
        if (_cached.HasValue) return _cached.Value;

        string json = PlayerPrefs.GetString(Key, string.Empty);
        if (string.IsNullOrEmpty(json))
        {
            _cached = default;
            return _cached.Value;
        }

        try
        {
            _cached = JsonUtility.FromJson<IronCareerStatsData>(json);
            return _cached.Value;
        }
        catch
        {
            _cached = default;
            return _cached.Value;
        }
    }

    public static void Save(IronCareerStatsData data)
    {
        _cached = data;
        PlayerPrefs.SetString(Key, JsonUtility.ToJson(data));
        PlayerPrefs.Save();
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
