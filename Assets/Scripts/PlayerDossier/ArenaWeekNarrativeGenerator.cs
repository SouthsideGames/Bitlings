using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Generates a quarterly KPI report narrative for a completed Arena week.
/// Pure data-in, string-out. No MonoBehaviour, no Unity lifecycle, no serialized fields.
/// </summary>
public static class ArenaWeekNarrativeGenerator
{
    public static string Generate(
        ArenaTournamentHistoryEntry entry,
        ArenaLifetimeStats lifetimeStats,
        int weekNumber)
    {
        if (entry == null || lifetimeStats == null)
            return string.Empty;

        var sentences = new List<string>(6);

        // Sentence 1 — Header
        sentences.Add($"Weekly Arena assessment — Period {weekNumber}. Tournament ID: {Truncate(entry.tournamentId, 8)}.");

        // Sentence 2 — Placement
        int placement = entry.finalPlacement;
        int total = Mathf.Max(1, entry.totalEntrants);
        string suffix = PlacementSuffix(placement);
        sentences.Add($"Final placement: {placement}{suffix} of {total} entrants. {PlacementRating(placement, total)}.");

        // Sentence 3 — Score band (omit if default/Low)
        if (entry.scoreBand != default)
            sentences.Add($"Score classification: {entry.scoreBand}. Band recorded.");

        // Sentence 4 — Notable flag (omit if nothing worth flagging)
        if (placement == 1)
            sentences.Add("Notable: championship recorded. Lifetime championships updated.");
        else if (lifetimeStats.podiumFinishes > 0 && placement <= 3)
            sentences.Add($"Notable: {lifetimeStats.podiumFinishes} career podium finish(es) on record.");
        else if (lifetimeStats.bestPlacementAllTime > 0 && placement < lifetimeStats.bestPlacementAllTime)
            sentences.Add($"Notable: personal best placement updated ({placement}{PlacementSuffix(placement)}).");

        // Sentence 5 — Career context
        sentences.Add($"Career record: {lifetimeStats.tournamentsEntered} tournament(s) entered, {lifetimeStats.championshipsWon} championship(s) won.");

        // Sentence 6 — Closing
        if (placement == 1)
            sentences.Add("File closed. Season record updated.");
        else if (placement <= 3)
            sentences.Add("File closed. Performance acknowledged.");
        else
            sentences.Add("File closed. Next assessment period opens on schedule.");

        return string.Join(" ", sentences);
    }

    private static string Truncate(string s, int len) =>
        string.IsNullOrEmpty(s) ? "—" : (s.Length <= len ? s : s.Substring(0, len));

    private static string PlacementSuffix(int n)
    {
        if (n <= 0) return "th";
        int abs = Mathf.Abs(n);
        if (abs % 100 >= 11 && abs % 100 <= 13) return "th";
        switch (abs % 10)
        {
            case 1: return "st";
            case 2: return "nd";
            case 3: return "rd";
            default: return "th";
        }
    }

    private static string PlacementRating(int placement, int total)
    {
        float pct = (float)placement / total;
        if (placement == 1)   return "Championship secured";
        if (placement <= 3)   return "Podium finish confirmed";
        if (pct <= 0.25f)     return "Top quartile. Exceeds projection";
        if (pct <= 0.5f)      return "Upper half. Meets projection";
        if (pct <= 0.75f)     return "Lower half. Below projection";
        return                       "Bottom quartile. Performance review recommended";
    }
}
