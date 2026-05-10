using System;
using System.Collections.Generic;
using UnityEngine;

public static class CareerNarrativeGenerator
{
    public static readonly Dictionary<string, string[]> ClosingLinePool = new Dictionary<string, string[]>
    {
        { "Warrior", new[] { "The roster misses the fire. The numbers remember the fighter." } },
        { "Guardian", new[] { "Some monsters don't win the most battles. They win the ones that matter." } },
        { "Scholar", new[] { "Quiet dedication leaves the loudest echoes." } },
        { "Laborer", new[] { "The harvest doesn't remember who planted the seed. But you do." } },
        { "Wanderer", new[] { "They went everywhere. They changed everything." } },
        { "Broker", new[] { "Every credit in this treasury passed through their hands at least once." } },
        { "HighWinRate", new[] { "The numbers remember everything." } },
        { "HighBattles", new[] { "Some careers are built in moments. This one was built in battles." } },
        { "IronVet", new[] { "Iron doesn't break what it forges." } },
        { "ShortCareer", new[] { "Not every retired monster needs a long chapter to matter." } },
        { "Default", new[] { "The roster is quieter now." } }
    };

    public static string GenerateNarrative(MentorRecord record, LifetimeMonsterStats stats)
    {
        if (record == null && stats == null)
            return string.Empty;

        record ??= new MentorRecord();
        stats ??= new LifetimeMonsterStats();

        var pronouns = NarrativePronounSet.For(stats.pronounSet);
        string name = string.IsNullOrWhiteSpace(record.displayName) ? "This Bitling" : record.displayName;

        long startUnix = stats.firstCaptureUnix;
        long endUnix = stats.retiredAtUnix > 0 ? stats.retiredAtUnix : SaveManager.NowUnix();
        int days = Mathf.Max(0, (int)((endUnix - startUnix) / 86400L));

        var sentences = new List<string>(8)
        {
            $"{name} served for {days.ToString("N0")} days."
        };

        int winPct = 0;
        bool hasBattleRecord = stats.lifetimeBattles > 0;
        if (hasBattleRecord)
        {
            winPct = (stats.lifetimeWins * 100) / Mathf.Max(1, stats.lifetimeBattles);
            sentences.Add(
                $"In that time, {pronouns.subject} fought {stats.lifetimeBattles.ToString("N0")} battles, winning {winPct}% of them.");
        }

        if (stats.maxWinStreak >= 5)
            sentences.Add($"{Cap(pronouns.possessive)} longest win streak was {stats.maxWinStreak.ToString("N0")} consecutive victories.");

        if (stats.riftsCompleted >= 10)
        {
            sentences.Add(
                $"{Cap(pronouns.subject)} completed {stats.riftsCompleted.ToString("N0")} Rifts, including {stats.bossesDefeated.ToString("N0")} boss encounters.");
        }

        if (stats.ironCareerWins > 0)
            sentences.Add($"{Cap(pronouns.subject)} survived {stats.ironCareerWins.ToString("N0")} battles in Executive Trial.");

        bool hasDriftLine = false;
        if (stats.driftTierAtRetirement >= 2)
        {
            string activity = ActivityFor(stats.driftArchetypeAtRetirement);
            string archetype = stats.driftArchetypeAtRetirement.ToString();
            sentences.Add($"A career of {activity} shaped {pronouns.@object} into a true {archetype}.");
            hasDriftLine = true;
        }

        if (stats.lifetimeJobHours >= 20f)
        {
            int roundedHours = Mathf.RoundToInt(stats.lifetimeJobHours);
            sentences.Add($"{Cap(pronouns.subject)} spent {roundedHours.ToString("N0")} hrs at the {stats.topJobType}." );
        }

        if (!string.IsNullOrEmpty(stats.willRecipientUID))
        {
            string heir = string.IsNullOrWhiteSpace(stats.willRecipientName) ? "an heir" : stats.willRecipientName;
            sentences.Add($"{Cap(pronouns.subject)} chose {heir} as {pronouns.possessive} heir, passing on {stats.willType}." );
        }
        else
        {
            sentences.Add($"{Cap(pronouns.subject)} left no heir - some legacies burn alone.");
        }

        if (sentences.Count < 4)
        {
            if (!hasBattleRecord)
            {
                sentences.Add(
                    $"In that time, {pronouns.subject} fought {stats.lifetimeBattles.ToString("N0")} battles, winning {winPct}% of them.");
            }

            if (!hasDriftLine)
            {
                string activity = ActivityFor(stats.driftArchetypeAtRetirement);
                string archetype = stats.driftArchetypeAtRetirement == DriftArchetype.None ? "Wanderer" : stats.driftArchetypeAtRetirement.ToString();
                sentences.Add($"A career of {activity} shaped {pronouns.@object} into a true {archetype}.");
            }
        }

        string closing = PickClosing(stats, winPct);
        sentences.Add(closing);

        // Keep it concise and in target 4-6 sentence range.
        if (sentences.Count > 6)
            sentences = sentences.GetRange(0, 6);

        return string.Join(" ", sentences);
    }

    private static string ActivityFor(DriftArchetype archetype)
    {
        switch (archetype)
        {
            case DriftArchetype.Warrior: return "battle";
            case DriftArchetype.Guardian: return "sacrifice";
            case DriftArchetype.Scholar: return "study";
            case DriftArchetype.Laborer: return "work";
            case DriftArchetype.Wanderer: return "exploration";
            case DriftArchetype.Broker: return "trade";
            default: return "exploration";
        }
    }

    private static string PickClosing(LifetimeMonsterStats stats, int winPct)
    {
        string key = null;

        if (stats.driftArchetypeAtRetirement != DriftArchetype.None)
        {
            key = stats.driftArchetypeAtRetirement.ToString();
        }
        else if (winPct > 85 && stats.lifetimeBattles > 0)
        {
            key = "HighWinRate";
        }
        else if (stats.lifetimeBattles > 300)
        {
            key = "HighBattles";
        }
        else if (stats.ironCareerWins > 0)
        {
            key = "IronVet";
        }
        else if (stats.lifetimeBattles > 0 && stats.lifetimeBattles < 50)
        {
            key = "ShortCareer";
        }
        else
        {
            key = "Default";
        }

        if (!ClosingLinePool.TryGetValue(key, out var lines) || lines == null || lines.Length == 0)
            lines = ClosingLinePool["Default"];

        return lines[0];
    }

    private static string Cap(string v)
    {
        if (string.IsNullOrEmpty(v)) return v;
        if (v.Length == 1) return v.ToUpperInvariant();
        return char.ToUpperInvariant(v[0]) + v.Substring(1);
    }
}
