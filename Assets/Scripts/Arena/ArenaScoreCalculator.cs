// Assets/Scripts/Arena/ArenaScoreCalculator.cs
// BRN Arena v1 — Centralised scoring: team score, per-Bitling score, type-synergy grading, score bands.
// Reusable for both real player teams and bot-generated teams.

using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Pure-logic static utility that calculates arena team scores, individual Bitling scores,
/// type synergy bonuses, and maps totals to hidden score bands.
/// </summary>
public static class ArenaScoreCalculator
{
    // ═════════════════════════════════════════════════════════════
    //  Per-Bitling score
    // ═════════════════════════════════════════════════════════════

    /// <summary>
    /// Returns <c>MonsterDataSO.arenaScore + TitleSO.arenaScore</c> for a single Bitling.
    /// Accepts nullable monster and title SOs so callers can pass whatever they have resolved.
    /// </summary>
    public static int CalculateBitlingArenaScore(MonsterDataSO monsterDef, TitleSO titleDef)
    {
        int score = 0;
        if (monsterDef != null) score += Mathf.Max(0, monsterDef.arenaScore);
        if (titleDef != null) score += Mathf.Max(0, titleDef.arenaScore);
        return score;
    }

    // ═════════════════════════════════════════════════════════════
    //  Full team score (live battle team from save data)
    // ═════════════════════════════════════════════════════════════

    /// <summary>
    /// Calculates the full arena team score for the player's current battle team.
    /// Reads the three slots from <see cref="ArenaLoadoutManager"/> and resolves
    /// MonsterDataSO + equipped TitleSO for each.
    /// </summary>
    public static int CalculateArenaTeamScore()
    {
        var members = ArenaLoadoutManager.GetArenaTeamMembers();
        if (members == null || members.Count == 0)
            return 0;

        var types = new List<MonsterType>();
        int baseSum = 0;

        for (int i = 0; i < members.Count; i++)
        {
            var m = members[i];
            if (m == null || string.IsNullOrEmpty(m.monsterId))
                continue;

            var def = MonsterLibraryLocator.GetById(m.monsterId);
            if (def == null) continue;

            TitleSO titleDef = ResolveTitleForMonster(m.monsterId);

            baseSum += CalculateBitlingArenaScore(def, titleDef);
            types.Add(def.type);
        }

        int synergy = CalculateTypeSynergyBonus(types);
        return baseSum + synergy;
    }

    /// <summary>
    /// Calculates the full arena team score from pre-resolved data.
    /// Useful for bot teams and snapshot scoring where definitions are already in hand.
    /// Each entry is (MonsterDataSO, TitleSO — nullable) and the MonsterType is read from the def.
    /// </summary>
    public static int CalculateArenaTeamScore(List<(MonsterDataSO def, TitleSO title)> slots)
    {
        if (slots == null || slots.Count == 0)
            return 0;

        var types = new List<MonsterType>();
        int baseSum = 0;

        for (int i = 0; i < slots.Count; i++)
        {
            var (def, title) = slots[i];
            if (def == null) continue;

            baseSum += CalculateBitlingArenaScore(def, title);
            types.Add(def.type);
        }

        int synergy = CalculateTypeSynergyBonus(types);
        return baseSum + synergy;
    }

    /// <summary>
    /// Calculates team score from an <see cref="ArenaTeamSnapshot"/> (frozen tournament data).
    /// Uses snapshot-stored monster/title arena scores plus re-computed synergy from types.
    /// </summary>
    public static int CalculateArenaTeamScore(ArenaTeamSnapshot snapshot)
    {
        if (snapshot == null || snapshot.slotSnapshots == null || snapshot.slotSnapshots.Count == 0)
            return 0;

        var types = new List<MonsterType>();
        int baseSum = 0;

        for (int i = 0; i < snapshot.slotSnapshots.Count; i++)
        {
            var slot = snapshot.slotSnapshots[i];
            if (slot == null || string.IsNullOrEmpty(slot.monsterId))
                continue;

            baseSum += Mathf.Max(0, slot.monsterArenaScore) + Mathf.Max(0, slot.titleArenaScore);
            types.Add(slot.monsterType);
        }

        int synergy = CalculateTypeSynergyBonus(types);
        return baseSum + synergy;
    }

    // ═════════════════════════════════════════════════════════════
    //  Type synergy bonus
    // ═════════════════════════════════════════════════════════════

    /// <summary>
    /// Computes the type synergy bonus for a set of team member types.
    /// <para>Algorithm:</para>
    /// <list type="number">
    ///   <item>For each member, gather all threat types (attackers with multiplier &gt; 1).</item>
    ///   <item>For each threat, other teammates provide:
    ///     full cover (1 pt) if they resist that threat,
    ///     partial cover (0.5 pt) if they pressure that threat without resisting.</item>
    ///   <item>CoveragePercent = total covered points / total threat count.</item>
    ///   <item>Map to tier: &lt;40% → 0, 40–64% → 5, 65–84% → 10, 85%+ → 15.</item>
    ///   <item>Shared exposed weakness: if 2+ members share the same uncovered threat, cap at 5.</item>
    /// </list>
    /// </summary>
    public static int CalculateTypeSynergyBonus(List<MonsterType> teamTypes)
    {
        if (teamTypes == null || teamTypes.Count < 2)
            return 0;

        int totalThreats = 0;
        float coveredPoints = 0f;

        // Track uncovered threats across all members for the shared-weakness penalty.
        // Key = threat type, Value = count of members exposed to that uncovered threat.
        var uncoveredCounts = new Dictionary<MonsterType, int>();

        for (int m = 0; m < teamTypes.Count; m++)
        {
            var memberType = teamTypes[m];
            var threats = BattleTypeChart.GetThreatTypes(memberType);

            for (int t = 0; t < threats.Count; t++)
            {
                var threat = threats[t];
                totalThreats++;

                bool fullCover = false;
                bool partialCover = false;

                for (int a = 0; a < teamTypes.Count; a++)
                {
                    if (a == m) continue;
                    var allyType = teamTypes[a];

                    if (BattleTypeChart.DoesTypeResistThreat(allyType, threat))
                    {
                        fullCover = true;
                        break; // best possible cover
                    }

                    if (!partialCover && BattleTypeChart.DoesTypePressureThreat(allyType, threat))
                        partialCover = true;
                }

                if (fullCover)
                {
                    coveredPoints += 1f;
                }
                else if (partialCover)
                {
                    coveredPoints += 0.5f;
                }
                else
                {
                    // Completely uncovered — track for shared-weakness check.
                    if (uncoveredCounts.ContainsKey(threat))
                        uncoveredCounts[threat]++;
                    else
                        uncoveredCounts[threat] = 1;
                }
            }
        }

        if (totalThreats == 0)
            return SynergyTierFromPercent(1f); // no threats = perfect synergy

        float coveragePercent = coveredPoints / totalThreats;
        int tier = SynergyTierFromPercent(coveragePercent);

        // Shared exposed weakness penalty: if any uncovered threat hits 2+ members, cap at 5.
        foreach (var kv in uncoveredCounts)
        {
            if (kv.Value >= 2)
                return Mathf.Min(tier, 5);
        }

        return tier;
    }

    /// <summary>Maps a 0–1 coverage fraction to one of the four synergy bonus tiers.</summary>
    private static int SynergyTierFromPercent(float coverage)
    {
        if (coverage >= 0.85f) return 15;
        if (coverage >= 0.65f) return 10;
        if (coverage >= 0.40f) return 5;
        return 0;
    }

    // ═════════════════════════════════════════════════════════════
    //  Score bands
    // ═════════════════════════════════════════════════════════════

    /// <summary>
    /// Maps a total team arena score to a hidden score band used for bracket seeding.
    /// Bands are never shown to players.
    /// </summary>
    public static ArenaScoreBand GetBattleTeamScoreBand(int teamScore)
    {
        if (teamScore >= ArenaConstants.ScoreBandEliteThreshold) return ArenaScoreBand.Elite;
        if (teamScore >= ArenaConstants.ScoreBandHighThreshold) return ArenaScoreBand.High;
        if (teamScore >= ArenaConstants.ScoreBandStandardThreshold) return ArenaScoreBand.Standard;
        return ArenaScoreBand.Low;
    }

    /// <summary>Convenience: calculates the live team score and returns its band.</summary>
    public static ArenaScoreBand GetCurrentBattleTeamScoreBand()
    {
        return GetBattleTeamScoreBand(CalculateArenaTeamScore());
    }

    // ═════════════════════════════════════════════════════════════
    //  Title resolution helper
    // ═════════════════════════════════════════════════════════════

    /// <summary>
    /// Resolves the equipped <see cref="TitleSO"/> for a monster species id.
    /// Returns <c>null</c> if no title is equipped or TitleManager is unavailable.
    /// </summary>
    private static TitleSO ResolveTitleForMonster(string monsterId)
    {
        if (TitleManager.I == null) return null;

        string titleId = TitleManager.I.GetEquippedTitleId(monsterId);
        if (string.IsNullOrEmpty(titleId)) return null;

        return TitleManager.I.GetTitleById(titleId);
    }
}
