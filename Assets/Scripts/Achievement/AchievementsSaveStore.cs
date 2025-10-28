using UnityEngine;
using System.IO;
using System.Collections.Generic;

[System.Serializable]
public class AchievementsSave
{
    public List<string> completed = new List<string>();
    public List<string> unlockedPacks = new List<string>();
    public List<ProgKV> progress = new List<ProgKV>();
    public int tokens = 0;
    public int tokensEarnedTotal = 0;
}

[System.Serializable]
public struct ProgKV
{
    public string key;
    public int value;
}

public static class AchievementsSaveStore
{
    static string Path => System.IO.Path.Combine(Application.persistentDataPath, "achievements.json");
    static AchievementsSave _cache;

    public static AchievementsSave Data
    {
        get
        {
            if (_cache == null) _cache = Load();
            return _cache;
        }
    }

    public static void Save()
    {
        var json = JsonUtility.ToJson(Data, true);
        File.WriteAllText(Path, json);
    }

    static AchievementsSave Load()
    {
        if (!File.Exists(Path)) return new AchievementsSave();
        var json = File.ReadAllText(Path);
        if (string.IsNullOrEmpty(json)) return new AchievementsSave();
        return JsonUtility.FromJson<AchievementsSave>(json) ?? new AchievementsSave();
    }
}
