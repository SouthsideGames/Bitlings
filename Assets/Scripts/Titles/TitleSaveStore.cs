using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

[Serializable]
public class MonsterTitleEquip
{
    public string monsterId;                
    public List<string> tierSelections = new List<string>(); 
}

[Serializable]
public class TitleSaveData
{
    public List<MonsterTitleEquip> equips = new List<MonsterTitleEquip>();
}

public static class TitleSaveStore
{
    private static TitleSaveData _cache;
    private const string LegacyFileName = "idle_titles.json";
    public static string SavePath => Path.Combine(Application.persistentDataPath, LegacyFileName);

    public static TitleSaveData Load()
    {
        if (_cache != null) return _cache;

        SaveManager.LoadOrCreate();

        _cache = SaveManager.GetTitlesBlob();
        if (_cache == null) _cache = new TitleSaveData();
        return _cache;
    }

    public static TitleSaveData TryLoadLegacyDirect()
    {
        try
        {
            if (!File.Exists(SavePath)) return null;
            string json = File.ReadAllText(SavePath);
            if (string.IsNullOrWhiteSpace(json)) return null;
            return JsonUtility.FromJson<TitleSaveData>(json);
        }
        catch
        {
            return null;
        }
    }

    public static void Save()
    {
        try
        {
            var data = _cache ?? new TitleSaveData();
            SaveManager.SetTitlesBlob(data);
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

        // Also remove legacy file if present.
        try
        {
            if (File.Exists(SavePath)) File.Delete(SavePath);
        }
        catch { /* swallow */ }
    }

    public static void InvalidateCache()
    {
        _cache = null;
    }
}
