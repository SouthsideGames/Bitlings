using System;
using System.Collections.Generic;
using UnityEngine;

public static class MonsterJobProgress
{
    private const float BaseToNext = 100f;     // XP from Lv1->2
    private const float StepPerLevel = 50f;    // Added each level
    public const int HardMaxLevel = 15;
    private const int MaxEquipped = 5;

    public static float XpToNext(int level, int maxLevel = HardMaxLevel)
    {
        if (level >= maxLevel) return Mathf.Infinity;
        return BaseToNext + StepPerLevel * Mathf.Max(0, level - 1);
    }

    public static void AddJobXp(string monsterId, float xp, MonsterDataSO def)
    {
        var rec = TagSave.GetOrCreate(monsterId);
        if (rec.jobLevel < 1) rec.jobLevel = 1;

        rec.jobXp += Mathf.Max(0f, xp);

        int maxLevel = def != null && def.tagTrack != null ? Mathf.Max(1, def.tagTrack.maxLevel) : HardMaxLevel;
        bool leveled = false;
        while (rec.jobLevel < maxLevel && rec.jobXp >= XpToNext(rec.jobLevel, maxLevel))
        {
            rec.jobXp -= XpToNext(rec.jobLevel, maxLevel);
            rec.jobLevel++;
            leveled = true;
            TryUnlockNewTagsOnLevel(rec, def);
        }

        if (leveled) TagSave.Save();
    }

    static void TryUnlockNewTagsOnLevel(MonsterTagRecord rec, MonsterDataSO def)
    {
        if (def == null || def.tagTrack == null) return;
        var track = def.tagTrack;
        var tags = track.tags;
        var levels = track.unlockLevels;
        if (tags == null || levels == null) return;

        for (int i = 0; i < Mathf.Min(tags.Length, levels.Length); i++)
        {
            var tag = tags[i];
            int unlockAt = levels[i];
            if (tag == null || unlockAt != rec.jobLevel) continue;

            if (!rec.unlockedTagIds.Contains(tag.id))
            {
                rec.unlockedTagIds.Add(tag.id);
                TagEvents.MasteryTagUnlocked?.Invoke(rec.monsterId, default, tag.id, rec.jobLevel);
            }
        }
    }

    public static bool EquipTag(string monsterId, string tagId)
    {
        var rec = TagSave.GetOrCreate(monsterId);
        if (!rec.unlockedTagIds.Contains(tagId)) return false;
        if (!rec.equippedTagIds.Contains(tagId))
        {
            if (rec.equippedTagIds.Count >= MaxEquipped) return false;
            rec.equippedTagIds.Add(tagId);
            TagSave.Save();
        }
        return true;
    }

    public static void UnequipTag(string monsterId, string tagId)
    {
        var rec = TagSave.GetOrCreate(monsterId);
        if (rec.equippedTagIds.Remove(tagId)) TagSave.Save();
    }

    public static IReadOnlyList<string> GetEquippedIds(string monsterId)
    {
        return TagSave.GetOrCreate(monsterId).equippedTagIds;
    }

    public static int LevelFromXp(float currentXp, int startLevel, int maxLevel = HardMaxLevel)
    {
        int lvl = Mathf.Max(1, startLevel);
        float xp = Mathf.Max(0f, currentXp);

        while (lvl < maxLevel)
        {
            float need = XpToNext(lvl, maxLevel);
            if (xp < need) break;
            xp -= need;
            lvl++;
        }
        return lvl;
    }

}
