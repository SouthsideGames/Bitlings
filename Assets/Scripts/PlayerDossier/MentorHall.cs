using System;
using System.Collections.Generic;
using UnityEngine;

public static class MentorHall
{
    public static bool TryGetMentorRecord(string ownedUID, out MentorRecord record)
    {
        record = null;
        if (string.IsNullOrEmpty(ownedUID))
            return false;

        var mentors = SaveManager.GetMentorHallSnapshot();
        if (mentors == null)
            return false;

        for (int i = mentors.Count - 1; i >= 0; i--)
        {
            var mentor = mentors[i];
            if (mentor == null)
                continue;

            if (string.Equals(mentor.ownedUID, ownedUID, StringComparison.Ordinal))
            {
                record = mentor;
                return true;
            }
        }

        return false;
    }

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

        RemoveOwnershipReferences(data, ownedUID, owned.monsterId);

        SaveManager.Save();
        GameEvents.OnOwnedMonstersChanged?.Invoke();
        GameEvents.OnTeamChanged?.Invoke();
        GameEvents.MentorRetired?.Invoke(record.mentorUID);
        return true;
    }

    private static void RemoveOwnershipReferences(PlayerManager data, string ownedUID, string monsterId)
    {
        if (data == null || string.IsNullOrEmpty(ownedUID))
            return;

        if (data.owned != null)
        {
            for (int i = data.owned.Count - 1; i >= 0; i--)
            {
                var o = data.owned[i];
                if (o == null) continue;
                if (!string.Equals(o.ownedUID, ownedUID, StringComparison.Ordinal)) continue;
                data.owned.RemoveAt(i);
            }
        }

        if (data.team != null)
        {
            for (int i = 0; i < data.team.Count; i++)
            {
                var t = data.team[i];
                if (t == null) continue;
                if (!string.Equals(t.ownedUID, ownedUID, StringComparison.Ordinal)) continue;
                data.team[i] = new OwnedMonsterData();
            }
        }

        IdleLoadoutManager.RemoveFromIdleByOwnedUid(ownedUID);
        ArenaLoadoutManager.RemoveFromArenaByOwnedUid(ownedUID);

        if (JobManager.I != null)
            JobManager.I.RemoveFromAnyJob(ownedUID);

        if (!string.IsNullOrEmpty(monsterId) && data.ownedIds != null)
        {
            bool speciesStillOwned = false;

            if (data.owned != null)
            {
                for (int i = 0; i < data.owned.Count; i++)
                {
                    var o = data.owned[i];
                    if (o == null || string.IsNullOrEmpty(o.monsterId)) continue;
                    if (string.Equals(o.monsterId, monsterId, StringComparison.Ordinal))
                    {
                        speciesStillOwned = true;
                        break;
                    }
                }
            }

            if (!speciesStillOwned && data.team != null)
            {
                for (int i = 0; i < data.team.Count; i++)
                {
                    var t = data.team[i];
                    if (t == null || string.IsNullOrEmpty(t.monsterId)) continue;
                    if (string.Equals(t.monsterId, monsterId, StringComparison.Ordinal))
                    {
                        speciesStillOwned = true;
                        break;
                    }
                }
            }

            if (!speciesStillOwned)
                data.ownedIds.Remove(monsterId);
        }
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
