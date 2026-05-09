using System;
using System.Collections.Generic;
using UnityEngine;

public enum DriftArchetype
{
    None = 0,
    Warrior = 1,
    Guardian = 2,
    Scholar = 3,
    Laborer = 4,
    Wanderer = 5,
    Broker = 6
}

public enum WillType
{
    None = 0,
    Attack = 1,
    Defense = 2,
    Speed = 3,
    Vitality = 4,
    Fortune = 5
}

public enum PronounSet
{
    They = 0,
    He = 1,
    She = 2
}

public enum MentorQuality
{
    Bronze = 0,
    Silver = 1,
    Gold = 2,
    Legend = 3
}

[Serializable]
public struct NarrativePronounSet
{
    public string subject;
    public string @object;
    public string possessive;

    public NarrativePronounSet(string subject, string @object, string possessive)
    {
        this.subject = string.IsNullOrWhiteSpace(subject) ? "they" : subject;
        this.@object = string.IsNullOrWhiteSpace(@object) ? "them" : @object;
        this.possessive = string.IsNullOrWhiteSpace(possessive) ? "their" : possessive;
    }

    public static NarrativePronounSet For(PronounSet set)
    {
        switch (set)
        {
            case PronounSet.He:
                return new NarrativePronounSet("he", "him", "his");
            case PronounSet.She:
                return new NarrativePronounSet("she", "her", "her");
            default:
                return new NarrativePronounSet("they", "them", "their");
        }
    }
}

[Serializable]
public sealed class LifetimeJobHoursEntry
{
    public JobType jobType;
    public float hours;
}

[Serializable]
public sealed class LifetimeMonsterStats
{
    public int lifetimeBattles;
    public int lifetimeWins;
    public int maxWinStreak;
    public int riftsCompleted;
    public int bossesDefeated;
    public int ironCareerWins;
    public float lifetimeJobHours;
    public JobType topJobType;
    public long firstCaptureUnix;
    public long retiredAtUnix;
    public List<string> titlesEquippedAtRetirement = new List<string>();
    public DriftArchetype driftArchetypeAtRetirement;
    public int driftTierAtRetirement;
    public long evolvedAtUnix;
    public string evolvedFromMonsterId;
    public int levelAtEvolution;
    public List<string> titlesEquippedAtEvolution = new List<string>();
    public string willRecipientUID;
    public string willRecipientName;
    public WillType willType;
    public PronounSet pronounSet = PronounSet.They;

    public List<LifetimeJobHoursEntry> perJobHours = new List<LifetimeJobHoursEntry>();

    public void EnsureInitialized(long nowUnix)
    {
        if (firstCaptureUnix <= 0)
            firstCaptureUnix = nowUnix;

        titlesEquippedAtRetirement ??= new List<string>();
        titlesEquippedAtEvolution ??= new List<string>();
        perJobHours ??= new List<LifetimeJobHoursEntry>();
    }

    public void AddJobHours(JobType jobType, float deltaHours)
    {
        if (deltaHours <= 0f || jobType == JobType.None)
            return;

        lifetimeJobHours += deltaHours;

        perJobHours ??= new List<LifetimeJobHoursEntry>();
        LifetimeJobHoursEntry best = null;
        float bestHours = float.MinValue;

        for (int i = 0; i < perJobHours.Count; i++)
        {
            var entry = perJobHours[i];
            if (entry == null) continue;
            if (entry.jobType == jobType)
            {
                entry.hours += deltaHours;
                perJobHours[i] = entry;
            }

            if (entry.hours > bestHours)
            {
                bestHours = entry.hours;
                best = entry;
            }
        }

        if (best == null)
        {
            best = new LifetimeJobHoursEntry { jobType = jobType, hours = deltaHours };
            perJobHours.Add(best);
            bestHours = best.hours;
        }

        topJobType = best != null ? best.jobType : topJobType;

        for (int i = 0; i < perJobHours.Count; i++)
        {
            var entry = perJobHours[i];
            if (entry == null) continue;
            if (entry.hours > bestHours)
            {
                bestHours = entry.hours;
                topJobType = entry.jobType;
            }
        }
    }

    public LifetimeMonsterStats Clone()
    {
        var clone = new LifetimeMonsterStats
        {
            lifetimeBattles = lifetimeBattles,
            lifetimeWins = lifetimeWins,
            maxWinStreak = maxWinStreak,
            riftsCompleted = riftsCompleted,
            bossesDefeated = bossesDefeated,
            ironCareerWins = ironCareerWins,
            lifetimeJobHours = lifetimeJobHours,
            topJobType = topJobType,
            firstCaptureUnix = firstCaptureUnix,
            retiredAtUnix = retiredAtUnix,
            driftArchetypeAtRetirement = driftArchetypeAtRetirement,
            driftTierAtRetirement = driftTierAtRetirement,
            willRecipientUID = willRecipientUID,
            willRecipientName = willRecipientName,
            willType = willType,
            pronounSet = pronounSet,
            titlesEquippedAtRetirement = new List<string>(titlesEquippedAtRetirement ?? new List<string>()),
            evolvedAtUnix = evolvedAtUnix,
            evolvedFromMonsterId = evolvedFromMonsterId,
            levelAtEvolution = levelAtEvolution,
            titlesEquippedAtEvolution = new List<string>(titlesEquippedAtEvolution ?? new List<string>()),
            perJobHours = new List<LifetimeJobHoursEntry>()
        };

        if (perJobHours != null)
        {
            for (int i = 0; i < perJobHours.Count; i++)
            {
                var e = perJobHours[i];
                if (e == null) continue;
                clone.perJobHours.Add(new LifetimeJobHoursEntry
                {
                    jobType = e.jobType,
                    hours = e.hours
                });
            }
        }

        return clone;
    }
}

[Serializable]
public sealed class LifetimeMonsterStatsKV
{
    public string ownedUID;
    public LifetimeMonsterStats stats = new LifetimeMonsterStats();
}

[Serializable]
public sealed class MentorRecord
{
    public string mentorUID;
    public string ownedUID;
    public string monsterId;
    public MonsterType monsterType;
    public string displayName;
    public string epithet;
    public MentorQuality quality;
    public int retiredDay;
    public long retiredAtUnix;
    public DriftArchetype driftArchetype;
    public int driftTier;
    public LifetimeMonsterStats lifetimeStatsSnapshot = new LifetimeMonsterStats();
}

[Serializable]
public sealed class HonorBonusState
{
    public string honoredUID;
    public MonsterType honoredType;
    public long expiresAtUnix;
    public float atkPct;
    public float defPct;
    public float xpMul;
    public float jobMul;
}
