using System;
using System.Collections.Generic;
using UnityEngine;

public static class CareerNarrativeGenerator
{
    public static readonly Dictionary<string, string[]> ClosingLinePool = new Dictionary<string, string[]>
    {
        { "Warrior",     new[] { "Recommendation: retire with distinction. Field performance record speaks without comment." } },
        { "Guardian",    new[] { "Recommendation: retire with distinction. Some metrics resist quantification. This is noted." } },
        { "Scholar",     new[] { "Recommendation: retire with distinction. Quiet records often outperform loud ones." } },
        { "Laborer",     new[] { "Recommendation: retire with distinction. Consistent output maintained across the full assessment period." } },
        { "Wanderer",    new[] { "Recommendation: retire with distinction. Broad operational footprint. Contributions distributed." } },
        { "Broker",      new[] { "Recommendation: retire with distinction. Economic throughput above baseline. Treasury impact: noted." } },
        { "HighWinRate", new[] { "Recommendation: retire with distinction. Win rate above threshold. No further annotation required." } },
        { "HighBattles", new[] { "Recommendation: retire with distinction. Volume of service noted in permanent file." } },
        { "IronVet",     new[] { "Recommendation: retire with distinction. Iron Career performance entered into permanent record." } },
        { "ShortCareer", new[] { "Recommendation: retire. Assessment period brief. Contribution acknowledged and filed." } },
        { "Default",     new[] { "Recommendation: retire. File closed." } }
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
            $"Performance review: {name}. Assessment period: {days:N0} days."
        };

        int winPct = 0;
        bool hasBattleRecord = stats.lifetimeBattles > 0;
        if (hasBattleRecord)
        {
            winPct = (stats.lifetimeWins * 100) / Mathf.Max(1, stats.lifetimeBattles);
            sentences.Add(
                $"Battle record: {stats.lifetimeBattles:N0} engagements, {winPct}% success rate. {RatingFor(winPct)} field expectations.");
        }

        if (stats.maxWinStreak >= 5)
            sentences.Add($"Notable: {stats.maxWinStreak:N0} consecutive victories logged. Performance spike on record.");

        if (stats.riftsCompleted >= 10)
        {
            sentences.Add(
                $"Rift clearance: {stats.riftsCompleted:N0} completed, {stats.bossesDefeated:N0} boss encounters. Hazard rating: satisfactory.");
        }

        if (stats.ironCareerWins > 0)
            sentences.Add($"Iron Career file: {stats.ironCareerWins:N0} floors cleared. Hardship clause acknowledged.");

        bool hasDriftLine = false;
        if (stats.driftTierAtRetirement >= 2)
        {
            string activity = ActivityFor(stats.driftArchetypeAtRetirement);
            string archetype = stats.driftArchetypeAtRetirement.ToString();
            sentences.Add($"Drift classification: {stats.driftArchetypeAtRetirement} (Tier {stats.driftTierAtRetirement}). Archetype consistent with activity log.");
            hasDriftLine = true;
        }

        if (stats.lifetimeJobHours >= 20f)
        {
            int roundedHours = Mathf.RoundToInt(stats.lifetimeJobHours);
            sentences.Add($"Primary assignment: {stats.topJobType}. {roundedHours:N0} hours logged. Output: satisfactory.");
        }

        var trialStats = ExecutiveTrialStats.Load();
        if (trialStats.totalRuns > 0)
        {
            string bestStr = trialStats.bestHardcoreWins > 0
                ? $"Best: {trialStats.bestStandardWins} standard, {trialStats.bestHardcoreWins} hardcore."
                : $"Best: {trialStats.bestStandardWins} standard.";
            sentences.Add($"Executive Trial record (all-time): {trialStats.totalRuns:N0} runs, {trialStats.totalWinsAcrossRuns:N0} wins. {bestStr}");
        }

        var monthRecord = ExecutiveTrialStats.GetCurrentMonthRecord();
        if (monthRecord.runs > 0)
        {
            string monthBestStr = monthRecord.bestHardcoreWins > 0
                ? $"Best: {monthRecord.bestStandardWins} standard, {monthRecord.bestHardcoreWins} hardcore."
                : $"Best: {monthRecord.bestStandardWins} standard.";
            sentences.Add($"Trial activity this period: {monthRecord.runs:N0} runs, {monthRecord.wins:N0} wins. {monthBestStr}");
        }

        if (!string.IsNullOrEmpty(stats.willRecipientUID))
        {
            string heir = string.IsNullOrWhiteSpace(stats.willRecipientName) ? "an heir" : stats.willRecipientName;
            sentences.Add($"Legacy directive filed. Beneficiary: {heir}. Transfer type: {stats.willType}. Directive valid.");
        }
        else
        {
            sentences.Add("Legacy directive: none filed. File closed without succession.");
        }

        if (sentences.Count < 4)
        {
            if (!hasBattleRecord)
            {
                sentences.Add(
                    $"Battle record: {stats.lifetimeBattles:N0} engagements, {winPct}% success rate. {RatingFor(winPct)} field expectations.");
            }

            if (!hasDriftLine)
            {
                string activity = ActivityFor(stats.driftArchetypeAtRetirement);
                string archetype = stats.driftArchetypeAtRetirement == DriftArchetype.None ? "Wanderer" : stats.driftArchetypeAtRetirement.ToString();
                sentences.Add($"Drift classification: {stats.driftArchetypeAtRetirement} (Tier {stats.driftTierAtRetirement}). Archetype consistent with activity log.");
            }
        }

        string closing = PickClosing(stats, winPct);
        sentences.Add(closing);

        if (sentences.Count > 8)
            sentences = sentences.GetRange(0, 8);

        return string.Join(" ", sentences);
    }

    private static string RatingFor(int winPct)
    {
        if (winPct >= 80) return "Exceeds";
        if (winPct >= 55) return "Meets";
        if (winPct >= 35) return "Approaches";
        return "Below";
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
