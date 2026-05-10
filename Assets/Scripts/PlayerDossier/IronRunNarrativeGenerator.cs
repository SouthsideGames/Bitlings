using System.Collections.Generic;

/// <summary>
/// Generates a field incident report narrative for a completed Executive Trial (Iron Career) run.
/// Pure data-in, string-out. No MonoBehaviour, no Unity lifecycle, no serialized fields.
/// </summary>
public static class IronRunNarrativeGenerator
{
    public static string Generate(
        ExecutiveTrialRunSummary summary,
        int wins,
        bool forfeited,
        bool isHardcore,
        ExecutiveTrialStatsData allTimeStats)
    {
        var sentences = new List<string>(6);

        // Sentence 1 — Header
        string mode = isHardcore ? "Hardcore" : "Standard";
        sentences.Add($"Field incident report — Iron Career ({mode}). Engagement count: {summary.totalBattles:N0}.");

        // Sentence 2 — Outcome
        if (forfeited)
            sentences.Add("Run status: voluntarily terminated. Forfeit logged.");
        else if (wins == 0)
            sentences.Add("Run status: terminated at floor 1. No floors cleared.");
        else
            sentences.Add($"Run status: terminated. Floors cleared: {wins}.");

        // Sentence 3 — Survival threshold comparison (omitted on forfeit)
        if (!forfeited && wins > 0)
        {
            int best = isHardcore ? allTimeStats.bestHardcoreWins : allTimeStats.bestStandardWins;
            if (best > 0)
            {
                if (wins >= best)
                    sentences.Add($"Assessment: performance met or exceeded personal record ({best} floors). Noted in file.");
                else if (wins >= best - 2)
                    sentences.Add($"Assessment: performance within {best - wins} floor(s) of personal record. Projected threshold approached.");
                else
                    sentences.Add($"Assessment: performance below personal record ({best} floors). Variance within expected range.");
            }
            else
            {
                sentences.Add("Assessment: first run on record. Baseline established.");
            }
        }

        // Sentence 4 — Combat metrics
        sentences.Add($"Combat metrics — dealt: {summary.totalDamageDealt:N0}, sustained: {summary.totalDamageTaken:N0}, crits: {summary.totalCrits:N0}.");

        // Sentence 5 — Casualties
        if (summary.totalDeaths == 0)
            sentences.Add("Casualties: none. Party integrity maintained throughout engagement.");
        else if (summary.totalDeaths == 1)
            sentences.Add("Casualties: 1. Single unit lost during engagement.");
        else
            sentences.Add($"Casualties: {summary.totalDeaths}. {(summary.totalDeaths >= 3 ? "Significant losses noted." : "Losses within field tolerance.")}");

        // Sentence 6 — Closing
        int bestForClose = isHardcore ? allTimeStats.bestHardcoreWins : allTimeStats.bestStandardWins;
        if (forfeited)
            sentences.Add("File closed. Voluntary termination recorded.");
        else if (wins == 0)
            sentences.Add("File closed.");
        else if (wins >= bestForClose && bestForClose > 0)
            sentences.Add("File closed. Personal record updated.");
        else
            sentences.Add("File closed. Run data archived.");

        return string.Join(" ", sentences);
    }
}
