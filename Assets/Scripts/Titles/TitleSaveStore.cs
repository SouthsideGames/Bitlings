using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

[Serializable]
public class MonsterTitleEquip
{
    public string monsterId;                 // Owned ID (preferred) or base ID
    public List<string> tierSelections = new List<string>(); // index = tier, value = TitleSO.titleId (or "")
}

[Serializable]
public class TitleSaveData
{
    public List<MonsterTitleEquip> equips = new List<MonsterTitleEquip>();
}

public static class TitleSaveStore
{
    private static TitleSaveData _cache;
    private const string FileName = "idle_titles.json";
    public static string SavePath => Path.Combine(Application.persistentDataPath, FileName);

    public static TitleSaveData Load()
    {
        if (_cache != null) return _cache;

        try
        {
            if (File.Exists(SavePath))
            {
                string json = File.ReadAllText(SavePath);
                _cache = JsonUtility.FromJson<TitleSaveData>(json);
            }
        }
        catch { /* swallow */ }

        if (_cache == null) _cache = new TitleSaveData();
        return _cache;
    }

    public static void Save()
    {
        try
        {
            var data = _cache ?? new TitleSaveData();
            string json = JsonUtility.ToJson(data, prettyPrint: true);
            File.WriteAllText(SavePath, json);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[TitleSaveStore] Failed to save: {e}");
        }
    }

    public static MonsterTitleEquip GetOrCreateEquip(string monsterId)
    {
        var s = Load();
        var e = s.equips.Find(x => x.monsterId == monsterId);
        if (e == null)
        {
            e = new MonsterTitleEquip { monsterId = monsterId };
            s.equips.Add(e);
            Save();
        }
        return e;
    }

    public static void ClearAll()
    {
        _cache = new TitleSaveData();
        Save();
    }
}
