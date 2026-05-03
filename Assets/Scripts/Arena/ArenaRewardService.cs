// Assets/Scripts/Arena/ArenaRewardService.cs
// BRN Arena v1 — Reward distribution for tournament placements.
// Builds an ArenaRewardResult for each placement tier and grants resources
// (credits, featured resource, random bundles, pack vouchers, tickets).
// All values are locked to the design table. Duplicate prevention via wasGranted flag.

using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Static service that builds and grants arena tournament rewards.
/// </summary>
public static class ArenaRewardService
{
    // ═════════════════════════════════════════════════════════════
    //  Reward table (locked design values)
    // ═════════════════════════════════════════════════════════════

    /// <summary>Internal struct holding the fixed reward definition per placement tier.</summary>
    private struct RewardTierDef
    {
        public int credits;
        public int featuredAmount;      // 0 = none
        public int randomBundleCount;   // number of random resource bundles
        public int packVouchers;        // 0 = none
        public int arenaTickets;        // 0 = none
    }

    /// <summary>
    /// Returns the fixed reward definition for a given placement (1–32).
    /// </summary>
    private static RewardTierDef GetTierDef(int placement)
    {
        switch (placement)
        {
            case 1:  return new RewardTierDef { credits = 4000, featuredAmount = 500, randomBundleCount = 2, packVouchers = 1, arenaTickets = 1 };
            case 2:  return new RewardTierDef { credits = 2000, featuredAmount = 250, randomBundleCount = 2, packVouchers = 0, arenaTickets = 1 };
            case 3:  return new RewardTierDef { credits = 1000, featuredAmount = 100, randomBundleCount = 2, packVouchers = 0, arenaTickets = 1 };
            case 4:  return new RewardTierDef { credits = 750,  featuredAmount = 0,   randomBundleCount = 1, packVouchers = 0, arenaTickets = 1 };
            case 5:  return new RewardTierDef { credits = 600,  featuredAmount = 0,   randomBundleCount = 0, packVouchers = 0, arenaTickets = 1 };
            case 6:  return new RewardTierDef { credits = 600,  featuredAmount = 0,   randomBundleCount = 0, packVouchers = 0, arenaTickets = 0 };
            case 7:
            case 8:  return new RewardTierDef { credits = 550,  featuredAmount = 0,   randomBundleCount = 0, packVouchers = 0, arenaTickets = 0 };
            case 9:
            case 10: return new RewardTierDef { credits = 450,  featuredAmount = 0,   randomBundleCount = 0, packVouchers = 0, arenaTickets = 0 };
            case 11:
            case 12: return new RewardTierDef { credits = 425,  featuredAmount = 0,   randomBundleCount = 0, packVouchers = 0, arenaTickets = 0 };
            case 13:
            case 14: return new RewardTierDef { credits = 400,  featuredAmount = 0,   randomBundleCount = 0, packVouchers = 0, arenaTickets = 0 };
            case 15:
            case 16: return new RewardTierDef { credits = 375,  featuredAmount = 0,   randomBundleCount = 0, packVouchers = 0, arenaTickets = 0 };
            case 17:
            case 18: return new RewardTierDef { credits = 300,  featuredAmount = 0,   randomBundleCount = 0, packVouchers = 0, arenaTickets = 0 };
            case 19:
            case 20: return new RewardTierDef { credits = 285,  featuredAmount = 0,   randomBundleCount = 0, packVouchers = 0, arenaTickets = 0 };
            case 21:
            case 22: return new RewardTierDef { credits = 270,  featuredAmount = 0,   randomBundleCount = 0, packVouchers = 0, arenaTickets = 0 };
            case 23:
            case 24: return new RewardTierDef { credits = 255,  featuredAmount = 0,   randomBundleCount = 0, packVouchers = 0, arenaTickets = 0 };
            case 25:
            case 26: return new RewardTierDef { credits = 240,  featuredAmount = 0,   randomBundleCount = 0, packVouchers = 0, arenaTickets = 0 };
            case 27:
            case 28: return new RewardTierDef { credits = 225,  featuredAmount = 0,   randomBundleCount = 0, packVouchers = 0, arenaTickets = 0 };
            case 29:
            case 30: return new RewardTierDef { credits = 210,  featuredAmount = 0,   randomBundleCount = 0, packVouchers = 0, arenaTickets = 0 };
            case 31:
            case 32: return new RewardTierDef { credits = 200,  featuredAmount = 0,   randomBundleCount = 0, packVouchers = 0, arenaTickets = 0 };
            default: return new RewardTierDef { credits = 200,  featuredAmount = 0,   randomBundleCount = 0, packVouchers = 0, arenaTickets = 0 };
        }
    }

    // ═════════════════════════════════════════════════════════════
    //  Featured resource selection
    // ═════════════════════════════════════════════════════════════

    /// <summary>
    /// Pool of resource types eligible to be the weekly "featured" resource.
    /// Rotated deterministically each tournament week.
    /// </summary>
    private static readonly ResourceType[] FeaturedResourcePool = new ResourceType[]
    {
        ResourceType.GrowthCore,
        ResourceType.Material,
        ResourceType.TrainingVoucher,
        ResourceType.WellnessVoucher,
        ResourceType.EfficiencyVoucher,
    };

    /// <summary>
    /// Selects the featured resource type for a given tournament, deterministically
    /// derived from the tournament id so all players in the same bracket get the
    /// same featured resource.
    /// </summary>
    public static ResourceType SelectFeaturedResource(string tournamentId)
    {
        int hash = StableHash(tournamentId);
        int index = Mathf.Abs(hash) % FeaturedResourcePool.Length;
        return FeaturedResourcePool[index];
    }

    // ═════════════════════════════════════════════════════════════
    //  Random resource bundles
    // ═════════════════════════════════════════════════════════════

    /// <summary>
    /// Pool of resource types eligible for random reward bundles.
    /// </summary>
    private static readonly ResourceType[] RandomResourcePool = new ResourceType[]
    {
        ResourceType.GrowthCore,
        ResourceType.Material,
        ResourceType.TrainingVoucher,
        ResourceType.WellnessVoucher,
        ResourceType.EfficiencyVoucher,
        ResourceType.Favor,
        ResourceType.Coffee,
    };

    /// <summary>Min/max amount per random bundle entry.</summary>
    private const int RandomBundleMinAmount = 5;
    private const int RandomBundleMaxAmount = 20;

    /// <summary>
    /// Generates a list of random resource reward entries using a seeded RNG.
    /// Each bundle picks a type from the random pool and a random amount.
    /// </summary>
    private static List<ArenaResourceRewardEntry> GenerateRandomBundles(int count, System.Random rng)
    {
        var bundles = new List<ArenaResourceRewardEntry>(count);
        for (int i = 0; i < count; i++)
        {
            var type = RandomResourcePool[rng.Next(RandomResourcePool.Length)];
            int amount = rng.Next(RandomBundleMinAmount, RandomBundleMaxAmount + 1);
            bundles.Add(new ArenaResourceRewardEntry { resourceType = type, amount = amount });
        }
        return bundles;
    }

    // ═════════════════════════════════════════════════════════════
    //  Public API — Build reward result
    // ═════════════════════════════════════════════════════════════

    /// <summary>
    /// Builds an <see cref="ArenaRewardResult"/> for a given placement within a tournament.
    /// Does NOT grant resources — call <see cref="TryGrantRewards"/> to apply.
    /// </summary>
    /// <param name="placement">1-based final placement (1 = champion).</param>
    /// <param name="tournamentId">Used to deterministically select featured resource and seed random bundles.</param>
    /// <param name="entryId">Used to seed random bundle generation per-entry.</param>
    public static ArenaRewardResult BuildRewardResult(int placement, string tournamentId, string entryId)
    {
        if (placement < 1 || placement > ArenaConstants.BracketSize)
            return null;

        var tier = GetTierDef(placement);
        var featured = SelectFeaturedResource(tournamentId);

        // Seed random bundles deterministically from tournament + entry + placement.
        int bundleSeed = StableHash(tournamentId + entryId + placement.ToString());
        var rng = new System.Random(bundleSeed);
        var randomBundles = GenerateRandomBundles(tier.randomBundleCount, rng);

        return new ArenaRewardResult
        {
            placement = placement,
            creditsAwarded = tier.credits,
            featuredResourceType = featured,
            featuredResourceAmount = tier.featuredAmount,
            randomResourceRewards = randomBundles,
            packVoucherAmount = tier.packVouchers,
            arenaTicketAmount = tier.arenaTickets,
            wasGranted = false
        };
    }

    // ═════════════════════════════════════════════════════════════
    //  Public API — Build all rewards for a tournament
    // ═════════════════════════════════════════════════════════════

    /// <summary>
    /// Builds and attaches <see cref="ArenaRewardResult"/> to every entry in the
    /// tournament record. Entries must already have <c>finalPlacement</c> set.
    /// Only builds rewards for entries that don't already have a reward result.
    /// </summary>
    public static void BuildAllRewards(ArenaTournamentRecord record)
    {
        if (record == null || record.entries == null) return;

        for (int i = 0; i < record.entries.Count; i++)
        {
            var entry = record.entries[i];
            if (entry == null) continue;
            if (entry.finalPlacement <= 0) continue;
            if (entry.rewardResult != null) continue; // already built

            entry.rewardResult = BuildRewardResult(
                entry.finalPlacement,
                record.tournamentId,
                entry.entryId);
        }
    }

    // ═════════════════════════════════════════════════════════════
    //  Public API — Grant rewards to the real player
    // ═════════════════════════════════════════════════════════════

    /// <summary>
    /// Grants the resources described in the reward result to the local player.
    /// Skips bots. Prevents double-granting via the <c>wasGranted</c> flag.
    /// Returns <c>true</c> if resources were actually granted this call.
    /// </summary>
    /// <param name="entry">The player's tournament entry (must not be a bot).</param>
    public static bool TryGrantRewards(ArenaTournamentEntry entry)
    {
        if (entry == null) return false;
        if (entry.isBot) return false;
        if (entry.rewardResult == null) return false;
        if (entry.rewardResult.wasGranted) return false;

        var reward = entry.rewardResult;

        // ── Credits ──
        if (reward.creditsAwarded > 0)
            ResourceBank.Add(ResourceType.Credits, reward.creditsAwarded);

        // ── Featured resource ──
        if (reward.featuredResourceAmount > 0 && reward.featuredResourceType != ResourceType.None)
            ResourceBank.Add(reward.featuredResourceType, reward.featuredResourceAmount);

        // ── Random bundles ──
        if (reward.randomResourceRewards != null)
        {
            for (int i = 0; i < reward.randomResourceRewards.Count; i++)
            {
                var bundle = reward.randomResourceRewards[i];
                if (bundle != null && bundle.amount > 0 && bundle.resourceType != ResourceType.None)
                    ResourceBank.Add(bundle.resourceType, bundle.amount);
            }
        }

        // ── Pack vouchers ──
        if (reward.packVoucherAmount > 0)
            ResourceBank.Add(ResourceType.PackVoucher, reward.packVoucherAmount);

        // ── Arena ticket ──
        if (reward.arenaTicketAmount > 0)
            ArenaTicketManager.TryGrantArenaTicket(entry.finalPlacement);

        // ── Mark granted ──
        reward.wasGranted = true;

        SaveManager.Save();
        GameEvents.OnResourcesChanged?.Invoke();
        return true;
    }

    /// <summary>
    /// Convenience method: finds the real player's entry in a tournament record
    /// and grants their rewards. Returns <c>true</c> if rewards were granted.
    /// </summary>
    /// <param name="record">Completed tournament record.</param>
    /// <param name="playerEntryId">The local player's entry id in this tournament.</param>
    public static bool TryGrantPlayerRewards(ArenaTournamentRecord record, string playerEntryId)
    {
        if (record == null || string.IsNullOrEmpty(playerEntryId)) return false;

        for (int i = 0; i < record.entries.Count; i++)
        {
            var entry = record.entries[i];
            if (entry != null && string.Equals(entry.entryId, playerEntryId, StringComparison.Ordinal))
                return TryGrantRewards(entry);
        }
        return false;
    }

    /// <summary>
    /// Repairs missing or malformed reward payloads in saved tournament history
    /// and grants any ungranted rewards that should already have been delivered.
    /// Returns <c>true</c> when any history entry was repaired or granted.
    /// </summary>
    public static bool TryReconcileHistoryRewards()
    {
        var arena = SaveManager.GetArenaSaveData();
        if (arena?.recentTournamentHistory == null || arena.recentTournamentHistory.Count == 0)
            return false;

        bool changed = false;
        string stableEntryKey = !string.IsNullOrEmpty(arena.arenaPlayerId)
            ? arena.arenaPlayerId
            : "local_player";

        for (int i = 0; i < arena.recentTournamentHistory.Count; i++)
        {
            var hist = arena.recentTournamentHistory[i];
            if (hist == null) continue;
            if (hist.finalPlacement <= 0 || hist.finalPlacement > ArenaConstants.BracketSize) continue;

            var expectedTier = GetTierDef(hist.finalPlacement);
            bool shouldHaveRewards = expectedTier.credits > 0
                                     || expectedTier.featuredAmount > 0
                                     || expectedTier.randomBundleCount > 0
                                     || expectedTier.packVouchers > 0
                                     || expectedTier.arenaTickets > 0;
            if (!shouldHaveRewards) continue;

            var rw = hist.rewardResult;
            bool rewardMissing = rw == null;
            bool rewardMalformed = rw != null && rw.wasGranted && !HasAnyRewardContent(rw);

            if (rewardMissing || rewardMalformed)
            {
                // Rebuild deterministically from placement/tournament to recover from legacy or partial payloads.
                hist.rewardResult = BuildRewardResult(hist.finalPlacement, hist.tournamentId, stableEntryKey + "_" + i);
                rw = hist.rewardResult;
                changed = true;
            }

            if (rw != null && !rw.wasGranted)
            {
                var tempEntry = new ArenaTournamentEntry
                {
                    isBot = false,
                    finalPlacement = hist.finalPlacement,
                    rewardResult = rw
                };

                if (TryGrantRewards(tempEntry))
                    changed = true;
            }
        }

        if (changed)
            SaveManager.Save();

        return changed;
    }

    // ═════════════════════════════════════════════════════════════
    //  Helpers
    // ═════════════════════════════════════════════════════════════

    /// <summary>
    /// Produces a stable, deterministic hash from a string.
    /// Identical across app domains (unlike <see cref="string.GetHashCode"/>).
    /// </summary>
    private static int StableHash(string s)
    {
        if (string.IsNullOrEmpty(s)) return 0;
        unchecked
        {
            int hash = 17;
            for (int i = 0; i < s.Length; i++)
                hash = hash * 31 + s[i];
            return hash;
        }
    }

    private static bool HasAnyRewardContent(ArenaRewardResult reward)
    {
        if (reward == null) return false;
        if (reward.creditsAwarded > 0) return true;
        if (reward.featuredResourceAmount > 0 && reward.featuredResourceType != ResourceType.None) return true;
        if (reward.packVoucherAmount > 0) return true;
        if (reward.arenaTicketAmount > 0) return true;

        if (reward.randomResourceRewards != null)
        {
            for (int i = 0; i < reward.randomResourceRewards.Count; i++)
            {
                var b = reward.randomResourceRewards[i];
                if (b != null && b.amount > 0 && b.resourceType != ResourceType.None)
                    return true;
            }
        }

        return false;
    }
}
