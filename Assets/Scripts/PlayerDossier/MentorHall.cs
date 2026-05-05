using System;
using System.Collections.Generic;
using UnityEngine;

public static class MentorHall
{
    public static bool RetireMonster(string ownedUID, string displayNameOverride = null)
    {
        if (string.IsNullOrEmpty(ownedUID))
            return false;

        SaveManager.LoadOrCreate();

        var data = SaveManager.Data;
        if (data == null || data.owned == null)
            return false;

        OwnedMonsterData owned = null;
        for (int i = 0; i < data.owned.Count; i++)
        {
            var o = data.owned[i];
            if (o == null) continue;
            if (o.ownedUID == ownedUID)
            {
                owned = o;
                break;
            }
        }

        if (owned == null)
            return false;

        var stats = SaveManager.GetOrCreateStats(ownedUID);
        long now = SaveManager.NowUnix();

        stats.retiredAtUnix = now;

        if (TryGetDriftSnapshot(ownedUID, out var archetype, out var tier))
        {
            stats.driftArchetypeAtRetirement = archetype;
            stats.driftTierAtRetirement = tier;
        }

        var equip = TitleSaveStore.GetOrCreateEquip(ownedUID);
        stats.titlesEquippedAtRetirement = new List<string>();
        if (equip != null && equip.tierSelections != null)
        {
            for (int i = 0; i < equip.tierSelections.Count; i++)
            {
                string id = equip.tierSelections[i];
                if (!string.IsNullOrWhiteSpace(id))
                    stats.titlesEquippedAtRetirement.Add(id);
            }
        }

        var def = !string.IsNullOrEmpty(owned.monsterId) ? MonsterLibraryLocator.GetById(owned.monsterId) : null;
        var mentors = SaveManager.GetMentorHallMutable();

        var record = new MentorRecord
        {
            mentorUID = Guid.NewGuid().ToString("N"),
            ownedUID = ownedUID,
            monsterId = owned.monsterId,
            monsterType = def != null ? def.type : MonsterType.None,
            displayName = string.IsNullOrWhiteSpace(displayNameOverride)
                ? (def != null ? def.displayName : "Veteran")
                : displayNameOverride,
            quality = ComputeQuality(stats),
            retiredAtUnix = now,
            retiredDay = SaveManager.TodayDayIndexUTC(),
            driftArchetype = stats.driftArchetypeAtRetirement,
            driftTier = stats.driftTierAtRetirement,
            lifetimeStatsSnapshot = stats.Clone()
        };

        mentors.Add(record);

        SaveManager.Save();
        GameEvents.MentorRetired?.Invoke(record.mentorUID);
        return true;
    }

    private static bool TryGetDriftSnapshot(string ownedUID, out DriftArchetype archetype, out int tier)
    {
        archetype = DriftArchetype.None;
        tier = 0;

        // DriftTracker is not guaranteed in all branches. Keep this safe fallback.
        return false;
    }

    private static MentorQuality ComputeQuality(LifetimeMonsterStats stats)
    {
        if (stats == null)
            return MentorQuality.Bronze;

        if (stats.lifetimeWins >= 500 || stats.ironCareerWins >= 25)
            return MentorQuality.Legend;

        if (stats.lifetimeWins >= 250 || stats.maxWinStreak >= 25)
            return MentorQuality.Gold;

        if (stats.lifetimeWins >= 100 || stats.riftsCompleted >= 20)
            return MentorQuality.Silver;

        return MentorQuality.Bronze;
    }
}
