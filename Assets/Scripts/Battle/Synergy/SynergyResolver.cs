using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public static class SynergyResolver
{
    // BattleManager already references this nested type name + fields.
    public sealed class ApplyCommand
    {
        public MonsterType sourceType;          // the synergy element type (Fire/Water/etc.)
        public SynergyTier tier;                // Tier1/Tier2
        public StatusType status;               // what status is applied
        public SynergyTargetScope scope;        // enemy single / ally single / ally team

        public int turns;                       // timed duration (ignored if persistent)
        public bool persistent;                 // true for battle-long
        public float magnitude;                 // tier1Value/tier2Value from StatusLibrary
    }

    /// <summary>
    /// Resolve player synergies from team monster types.
    /// 2 of same type => Tier1; 3+ => Tier2.
    /// Deterministic: Tier2 first, then MonsterType order. Max N synergies.
    /// Produces ApplyCommand(s) by looking up mappings in SynergyLibrarySO + StatusLibrarySO.
    /// </summary>
    public static void ResolvePlayer(
        MonsterType[] teamTypes,
        int maxSynergies,
        SynergyLibrarySO synergyLibrary,
        StatusLibrarySO statusLibrary,
        List<ApplyCommand> outCommands)
    {
        if (outCommands == null) return;
        outCommands.Clear();

        if (teamTypes == null || teamTypes.Length == 0) return;
        if (maxSynergies <= 0) return;
        if (synergyLibrary == null || statusLibrary == null) return;

        // Count types
        var counts = new Dictionary<MonsterType, int>();
        for (int i = 0; i < teamTypes.Length; i++)
        {
            var t = teamTypes[i];
            if (!counts.ContainsKey(t)) counts[t] = 0;
            counts[t]++;
        }

        // Build candidate tiers
        var candidates = new List<(MonsterType type, SynergyTier tier)>();
        foreach (var kv in counts)
        {
            if (kv.Value >= 3) candidates.Add((kv.Key, SynergyTier.Tier2));
            else if (kv.Value >= 2) candidates.Add((kv.Key, SynergyTier.Tier1));
        }

        if (candidates.Count == 0) return;

        // Deterministic ordering
        candidates = candidates
            .OrderByDescending(c => (int)c.tier)
            .ThenBy(c => c.type)
            .Take(maxSynergies)
            .ToList();

        for (int i = 0; i < candidates.Count; i++)
        {
            if (TryBuildCommand(candidates[i].type, candidates[i].tier, synergyLibrary, statusLibrary, out var cmd))
                outCommands.Add(cmd);
        }
    }

    /// <summary>
    /// Resolve a single wild synergy from wildType + tier (driven by difficulty).
    /// </summary>
    public static bool ResolveWild(
        MonsterType wildType,
        SynergyTier tier,
        SynergyLibrarySO synergyLibrary,
        StatusLibrarySO statusLibrary,
        out ApplyCommand cmd)
    {
        return TryBuildCommand(wildType, tier, synergyLibrary, statusLibrary, out cmd);
    }

    private static bool TryBuildCommand(
        MonsterType type,
        SynergyTier tier,
        SynergyLibrarySO synergyLibrary,
        StatusLibrarySO statusLibrary,
        out ApplyCommand cmd)
    {
        cmd = null;
        if (synergyLibrary == null || statusLibrary == null) return false;

        var entry = synergyLibrary.Get(type, tier);
        if (entry == null) return false;
        if (entry.status == StatusType.None) return false;

        var se = statusLibrary.Get(entry.status);
        if (se == null) return false;

        float mag = 0f;
        if (tier == SynergyTier.Tier1) mag = se.tier1Value;
        else if (tier == SynergyTier.Tier2) mag = se.tier2Value;

        int turns = se.persistent ? 0 : Mathf.Max(0, se.defaultTurns);

        cmd = new ApplyCommand
        {
            sourceType = type,
            tier = tier,
            status = entry.status,
            scope = entry.scope,
            persistent = se.persistent,
            turns = turns,
            magnitude = mag
        };

        return true;
    }
}