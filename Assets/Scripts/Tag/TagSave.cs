using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class MonsterTagRecord
{
    public string monsterId;
    public int jobLevel;
    public float jobXp;
    public List<string> unlockedTagIds = new List<string>();
    public List<string> equippedTagIds = new List<string>();
}

public static class TagSave
{
    private static Dictionary<string, MonsterTagRecord> _records =
        new Dictionary<string, MonsterTagRecord>(StringComparer.Ordinal);

    public static IReadOnlyDictionary<string, MonsterTagRecord> Records => _records;

    public static MonsterTagRecord GetOrCreate(string monsterId)
    {
        if (string.IsNullOrEmpty(monsterId)) return null;
        if (!_records.TryGetValue(monsterId, out var rec))
        {
            rec = new MonsterTagRecord { monsterId = monsterId, jobLevel = 1, jobXp = 0f };
            _records[monsterId] = rec;
        }
        return rec;
    }

    public static void AddXp(string monsterId, JobType job, float amount)
    {
        if (string.IsNullOrEmpty(monsterId) || amount <= 0f) return;
        var rec = GetOrCreate(monsterId);
        rec.jobXp += amount;
        int newLevel = MonsterJobProgress.LevelFromXp(rec.jobXp, rec.jobLevel);
        if (newLevel > rec.jobLevel)
        {
            rec.jobLevel = newLevel;
            TryUnlockTags(monsterId, job, rec);
        }
    }

    private static void TryUnlockTags(string monsterId, JobType job, MonsterTagRecord rec)
    {
        var def = MonsterLibraryLocator.GetById(monsterId);
        if (!def || def.tagTrack == null) return;
        var track = def.tagTrack;

        for (int i = 0; i < track.tags.Length; i++)
        {
            var tag = track.tags[i];
            if (!tag) continue;

            int need = (track.unlockLevels != null && i < track.unlockLevels.Length)
                ? track.unlockLevels[i] : int.MaxValue;

            if (rec.jobLevel >= need && !rec.unlockedTagIds.Contains(tag.id))
            {
                rec.unlockedTagIds.Add(tag.id);
                TagEvents.MasteryTagUnlocked?.Invoke(monsterId, job, tag.id, rec.jobLevel);
            }
        }
    }

    public static bool TryEquip(string monsterId, string tagId)
    {
        if (string.IsNullOrEmpty(monsterId) || string.IsNullOrEmpty(tagId)) return false;
        var rec = GetOrCreate(monsterId);
        if (!rec.unlockedTagIds.Contains(tagId)) return false;
        if (rec.equippedTagIds.Count >= 5) return false;
        if (!rec.equippedTagIds.Contains(tagId))
        {
            rec.equippedTagIds.Add(tagId);
            return true;
        }
        return false;
    }

    public static void Unequip(string monsterId, string tagId)
    {
        if (string.IsNullOrEmpty(monsterId) || string.IsNullOrEmpty(tagId)) return;
        var rec = GetOrCreate(monsterId);
        rec.equippedTagIds.Remove(tagId);
    }

    public static bool IsEquipped(string monsterId, string tagId)
    {
        if (string.IsNullOrEmpty(monsterId) || string.IsNullOrEmpty(tagId)) return false;
        var rec = GetOrCreate(monsterId);
        return rec.equippedTagIds.Contains(tagId);
    }

    public static IEnumerable<string> GetEquippedIds(string monsterId)
    {
        if (string.IsNullOrEmpty(monsterId)) yield break;
        if (!_records.TryGetValue(monsterId, out var rec)) yield break;
        if (rec.equippedTagIds == null) yield break;
        foreach (var id in rec.equippedTagIds) yield return id;
    }

    public static void LoadFromSave(List<MonsterTagRecord> list)
    {
        _records.Clear();
        if (list == null) return;
        foreach (var rec in list)
        {
            if (rec == null || string.IsNullOrEmpty(rec.monsterId)) continue;
            _records[rec.monsterId] = rec;
        }
    }

    public static List<MonsterTagRecord> SaveToList()
    {
        var list = new List<MonsterTagRecord>(_records.Count);
        foreach (var kv in _records) list.Add(kv.Value);
        return list;
    }

    public static void ClearAll()
    {
        _records.Clear();
    }

    public static void Save()
    {
        if (SaveManager.Data == null) return;
        SaveManager.Data.tagProgress = SaveToList();
        SaveManager.Save();
    }
}
