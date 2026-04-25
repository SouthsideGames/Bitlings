// Assets/Scripts/Arena/ArenaBotGenerator.cs
// BRN Arena v1 — Bot generation pipeline.
// Creates bot tournament entries from curated templates with limited variation.
// Uses ArenaBotTemplateLibrary for template sourcing and ArenaScoreCalculator
// for final score computation.

using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Static bot generation pipeline.  Generates believable, competitive bot entries
/// from curated <see cref="ArenaBotTeamTemplate"/>s with controlled variation
/// (at most 1 monster swap + 1 title swap per generation).
/// </summary>
public static class ArenaBotGenerator
{
    // ═════════════════════════════════════════════════════════════
    //  Tuning constants
    // ═════════════════════════════════════════════════════════════

    /// <summary>Probability that one monster slot is swapped during variation.</summary>
    private const float MonsterVariationChance = 0.40f;

    /// <summary>Probability that one title slot is swapped during variation.</summary>
    private const float TitleVariationChance = 0.40f;

    /// <summary>Band-dependent probability of a bot having a title on each slot.</summary>
    private static readonly float[] BandTitleChance = { 0f, 0.35f, 0.65f, 0.90f };

    // ═════════════════════════════════════════════════════════════
    //  Public API — template access
    // ═════════════════════════════════════════════════════════════

    /// <summary>
    /// Returns all curated templates for the given score band from the library.
    /// Convenience wrapper over <see cref="ArenaBotTemplateLibrary.GetTemplatesForBand"/>.
    /// </summary>
    public static List<ArenaBotTeamTemplate> GetBotTemplatesForBand(ArenaScoreBand band)
    {
        return ArenaBotTemplateLibrary.GetTemplatesForBand(band);
    }

    // ═════════════════════════════════════════════════════════════
    //  Public API — batch entry generation
    // ═════════════════════════════════════════════════════════════

    /// <summary>
    /// Generates <paramref name="count"/> bot entries for a tournament bracket.
    /// Merges library templates with any additional external templates.
    /// Falls back to random catalog-based generation if no templates are available.
    /// </summary>
    /// <param name="count">Number of bots to generate.</param>
    /// <param name="band">Target score band for the bracket.</param>
    /// <param name="tournamentId">Tournament these bots belong to.</param>
    /// <param name="externalTemplates">Optional additional templates (may be null).</param>
    /// <param name="rng">Seeded RNG for deterministic generation.</param>
    public static List<ArenaTournamentEntry> GenerateBotEntries(
        int count,
        ArenaScoreBand band,
        string tournamentId,
        List<ArenaBotTeamTemplate> externalTemplates,
        System.Random rng)
    {
        // Merge library templates with any external overrides for this band.
        var bandTemplates = new List<ArenaBotTeamTemplate>();

        var libraryTemplates = ArenaBotTemplateLibrary.GetTemplatesForBand(band);
        bandTemplates.AddRange(libraryTemplates);

        if (externalTemplates != null)
        {
            for (int i = 0; i < externalTemplates.Count; i++)
            {
                var t = externalTemplates[i];
                if (t != null && t.scoreBand == band)
                    bandTemplates.Add(t);
            }
        }

        var bots = new List<ArenaTournamentEntry>(count);
        long nowUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        for (int i = 0; i < count; i++)
            bots.Add(CreateBotTournamentEntry(band, tournamentId, i, bandTemplates, nowUtc, rng));

        return bots;
    }

    // ═════════════════════════════════════════════════════════════
    //  Public API — single entry creation
    // ═════════════════════════════════════════════════════════════

    /// <summary>
    /// Creates a single bot tournament entry.  Picks a template, generates a team
    /// snapshot with optional variation, and wraps it in a tournament entry.
    /// </summary>
    public static ArenaTournamentEntry CreateBotTournamentEntry(
        ArenaScoreBand band,
        string tournamentId,
        int botIndex,
        List<ArenaBotTeamTemplate> templates,
        long nowUtc,
        System.Random rng)
    {
        string botPlayerId = $"bot_{tournamentId}_{botIndex:D3}";
        string entryId = $"{tournamentId}_E_BOT{botIndex:D3}";

        ArenaTeamSnapshot snapshot;
        string displayName;

        if (templates != null && templates.Count > 0)
        {
            var template = templates[rng.Next(templates.Count)];
            snapshot = GenerateBotFromTemplate(template, botPlayerId, nowUtc, rng);
            ApplyBotVariation(snapshot, template, rng);
            displayName = PickDisplayName(template, rng);
        }
        else
        {
            snapshot = GenerateFallbackBot(band, botPlayerId, nowUtc, rng);
            displayName = GenerateFallbackName(rng);
        }

        snapshot.ownerPlayerId = botPlayerId;
        snapshot.ownerDisplayName = displayName;
        snapshot.isBot = true;
        snapshot.arenaScore = CalculateGeneratedBotScore(snapshot);

        return new ArenaTournamentEntry
        {
            entryId = entryId,
            tournamentId = tournamentId,
            playerId = botPlayerId,
            displayNameSnapshot = displayName,
            isBot = true,
            arenaScore = snapshot.arenaScore,
            teamSnapshot = snapshot
        };
    }

    // ═════════════════════════════════════════════════════════════
    //  Public API — template-based generation
    // ═════════════════════════════════════════════════════════════

    /// <summary>
    /// Generates a bot team snapshot from a curated template.
    /// Picks one monster per slot from the template's pools, resolves titles
    /// (from template pools or harvested from each monster's SO data),
    /// and creates a frozen <see cref="ArenaTeamSnapshot"/>.
    /// </summary>
    public static ArenaTeamSnapshot GenerateBotFromTemplate(
        ArenaBotTeamTemplate template,
        string botPlayerId,
        long nowUtc,
        System.Random rng)
    {
        var slotMonsterPools = new List<string>[]
        {
            template.slot1MonsterOptions,
            template.slot2MonsterOptions,
            template.slot3MonsterOptions
        };
        var slotTitlePools = new List<string>[]
        {
            template.slot1TitleOptions,
            template.slot2TitleOptions,
            template.slot3TitleOptions
        };

        var usedSpecies = new HashSet<string>(StringComparer.Ordinal);
        var slots = new List<ArenaBitlingSnapshot>(ArenaConstants.BattleTeamSize);
        var types = new List<MonsterType>(ArenaConstants.BattleTeamSize);

        float titleChance = GetTitleChanceForBand(template.scoreBand);

        for (int s = 0; s < ArenaConstants.BattleTeamSize; s++)
        {
            // ── Pick monster ──
            MonsterDataSO def = PickMonsterFromPool(
                slotMonsterPools[s], usedSpecies, template.allowDuplicateSpecies, rng);
            if (def == null) def = PickFallbackMonster(rng);
            if (def == null) continue;

            if (!template.allowDuplicateSpecies)
                usedSpecies.Add(def.id);

            // ── Pick title (template pool → monster data → none) ──
            TitleSO titleDef = null;
            if (rng.NextDouble() < titleChance)
            {
                titleDef = PickTitleFromPool(slotTitlePools[s], rng);
                if (titleDef == null)
                    titleDef = PickTitleFromMonsterData(def, rng);
            }

            string titleId = titleDef != null ? titleDef.titleId : "";
            string titleName = titleDef != null ? titleDef.DisplayOrId : "";

            int monsterScore = Mathf.Max(0, def.arenaScore);
            int titleScore = titleDef != null ? Mathf.Max(0, titleDef.arenaScore) : 0;

            slots.Add(new ArenaBitlingSnapshot
            {
                instanceId = $"{botPlayerId}_slot{s}",
                monsterId = def.id,
                monsterName = def.displayName ?? def.id,
                monsterType = def.type,
                titleId = titleId,
                titleName = titleName,
                monsterArenaScore = monsterScore,
                titleArenaScore = titleScore,
                finalArenaContributionScore = monsterScore + titleScore
            });

            types.Add(def.type);
        }

        int baseSum = 0;
        for (int i = 0; i < slots.Count; i++)
            baseSum += slots[i].finalArenaContributionScore;

        int synergy = ArenaScoreCalculator.CalculateTypeSynergyBonus(types);

        var visOptions = template.visibilityModeOptions;
        var vis = (visOptions != null && visOptions.Count > 0)
            ? visOptions[rng.Next(visOptions.Count)]
            : ArenaVisibilityMode.FullReveal;

        return new ArenaTeamSnapshot
        {
            snapshotId = Guid.NewGuid().ToString("N"),
            ownerPlayerId = botPlayerId,
            isBot = true,
            visibilityMode = vis,
            arenaScore = baseSum + synergy,
            createdUtc = nowUtc,
            slotSnapshots = slots
        };
    }

    // ═════════════════════════════════════════════════════════════
    //  Public API — variation
    // ═════════════════════════════════════════════════════════════

    /// <summary>
    /// Applies controlled variation to a bot snapshot.
    /// At most 1 monster slot may be swapped (same type, band-appropriate rarity).
    /// At most 1 title slot may be swapped (different title or title removed).
    /// Duplicate species are only created if the template explicitly allows it.
    /// Slot scores are recalculated after each swap.
    /// </summary>
    public static void ApplyBotVariation(
        ArenaTeamSnapshot snapshot,
        ArenaBotTeamTemplate template,
        System.Random rng)
    {
        if (snapshot == null || snapshot.slotSnapshots == null || snapshot.slotSnapshots.Count == 0)
            return;

        int slotCount = snapshot.slotSnapshots.Count;

        // ── Monster variation (at most 1 slot) ──
        if (rng.NextDouble() < MonsterVariationChance)
            TryApplyMonsterVariation(snapshot, template, rng);

        // ── Title variation (at most 1 slot) ──
        if (rng.NextDouble() < TitleVariationChance)
            TryApplyTitleVariation(snapshot, template, rng);
    }

    // ═════════════════════════════════════════════════════════════
    //  Public API — score calculation
    // ═════════════════════════════════════════════════════════════

    /// <summary>
    /// Recalculates the full arena team score for a generated bot snapshot.
    /// Delegates to <see cref="ArenaScoreCalculator.CalculateArenaTeamScore(ArenaTeamSnapshot)"/>.
    /// </summary>
    public static int CalculateGeneratedBotScore(ArenaTeamSnapshot snapshot)
    {
        return ArenaScoreCalculator.CalculateArenaTeamScore(snapshot);
    }

    // ═════════════════════════════════════════════════════════════
    //  Monster variation
    // ═════════════════════════════════════════════════════════════

    private static void TryApplyMonsterVariation(
        ArenaTeamSnapshot snapshot,
        ArenaBotTeamTemplate template,
        System.Random rng)
    {
        int slotCount = snapshot.slotSnapshots.Count;
        int targetSlot = rng.Next(slotCount);
        var slot = snapshot.slotSnapshots[targetSlot];

        // Collect species already on the team.
        var usedSpecies = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < slotCount; i++)
            if (!string.IsNullOrEmpty(snapshot.slotSnapshots[i].monsterId))
                usedSpecies.Add(snapshot.slotSnapshots[i].monsterId);

        // Get the original template pool for this slot to avoid picking monsters
        // already in the pool (variation should pick something *different*).
        var originalPool = GetTemplateSlotPool(template, targetSlot);

        // Find a replacement monster of the same type.
        MonsterDataSO replacement = FindVariationMonster(
            slot.monsterType, usedSpecies, originalPool,
            template.allowDuplicateSpecies, template.scoreBand, rng);

        if (replacement == null) return;

        // Remove old species from used set; add new one.
        usedSpecies.Remove(slot.monsterId);
        usedSpecies.Add(replacement.id);

        // Apply the swap.
        int monsterScore = Mathf.Max(0, replacement.arenaScore);
        slot.monsterId = replacement.id;
        slot.monsterName = replacement.displayName ?? replacement.id;
        slot.monsterType = replacement.type;
        slot.monsterArenaScore = monsterScore;
        slot.finalArenaContributionScore = monsterScore + slot.titleArenaScore;
    }

    /// <summary>
    /// Finds a monster of the given type that is NOT in the original template pool
    /// and NOT already on the team (unless duplicates are allowed).
    /// Prefers band-appropriate rarity.
    /// </summary>
    private static MonsterDataSO FindVariationMonster(
        MonsterType type,
        HashSet<string> usedSpecies,
        List<string> originalPool,
        bool allowDuplicates,
        ArenaScoreBand band,
        System.Random rng)
    {
        var all = MonsterCatalog.All;
        if (all == null || all.Count == 0) return null;

        var candidates = new List<MonsterDataSO>();
        var originalSet = new HashSet<string>(originalPool ?? new List<string>(), StringComparer.Ordinal);

        for (int i = 0; i < all.Count; i++)
        {
            var m = all[i];
            if (m == null || m.type != type) continue;
            if (m.isBoss || m.uncatchable) continue;
            if (originalSet.Contains(m.id)) continue;
            if (!allowDuplicates && usedSpecies.Contains(m.id)) continue;
            candidates.Add(m);
        }

        if (candidates.Count == 0) return null;

        // Prefer band-appropriate rarity monsters first.
        var preferred = FilterByBandRarity(candidates, band);
        if (preferred.Count > 0)
            return preferred[rng.Next(preferred.Count)];

        return candidates[rng.Next(candidates.Count)];
    }

    /// <summary>
    /// Filters a list of monsters to only those whose rarity matches the band tier.
    /// Returns the original list if no matches pass the filter.
    /// </summary>
    private static List<MonsterDataSO> FilterByBandRarity(
        List<MonsterDataSO> candidates, ArenaScoreBand band)
    {
        int bandIndex = Mathf.Clamp((int)band, 0, BandRarityTiers.Length - 1);
        var acceptedRarities = BandRarityTiers[bandIndex];
        var raritySet = new HashSet<Rarity>();
        for (int i = 0; i < acceptedRarities.Length; i++)
            raritySet.Add(acceptedRarities[i]);

        var filtered = new List<MonsterDataSO>();
        for (int i = 0; i < candidates.Count; i++)
            if (raritySet.Contains(candidates[i].rarity))
                filtered.Add(candidates[i]);

        return filtered;
    }

    /// <summary>Rarity tiers per band, shared with <see cref="ArenaBotTemplateLibrary"/>.</summary>
    private static readonly Rarity[][] BandRarityTiers =
    {
        /* Low      */ new[] { Rarity.Common, Rarity.Uncommon },
        /* Standard */ new[] { Rarity.Uncommon, Rarity.Common, Rarity.Rare },
        /* High     */ new[] { Rarity.Rare, Rarity.Uncommon, Rarity.Epic },
        /* Elite    */ new[] { Rarity.Epic, Rarity.Legendary, Rarity.Mythic, Rarity.Rare },
    };

    // ═════════════════════════════════════════════════════════════
    //  Title variation
    // ═════════════════════════════════════════════════════════════

    private static void TryApplyTitleVariation(
        ArenaTeamSnapshot snapshot,
        ArenaBotTeamTemplate template,
        System.Random rng)
    {
        int slotCount = snapshot.slotSnapshots.Count;
        int targetSlot = rng.Next(slotCount);
        var slot = snapshot.slotSnapshots[targetSlot];

        // Harvest title alternatives from the picked monster's SO data.
        var alternatives = new List<string>();
        var def = MonsterCatalog.GetById(slot.monsterId);
        if (def != null)
            HarvestTitlesFromMonster(def, alternatives);

        // Also include template pool titles for this slot.
        var templatePool = GetTemplateTitlePool(template, targetSlot);
        if (templatePool != null)
            for (int i = 0; i < templatePool.Count; i++)
                if (!string.IsNullOrEmpty(templatePool[i]) && !alternatives.Contains(templatePool[i]))
                    alternatives.Add(templatePool[i]);

        // Remove the current title from options (we want a *different* one).
        alternatives.Remove(slot.titleId);

        // 30% chance to just remove the title entirely.
        if (alternatives.Count == 0 || rng.NextDouble() < 0.30)
        {
            // Remove title.
            slot.titleId = "";
            slot.titleName = "";
            slot.titleArenaScore = 0;
            slot.finalArenaContributionScore = slot.monsterArenaScore;
            return;
        }

        // Swap to a different title.
        string newTitleId = alternatives[rng.Next(alternatives.Count)];
        TitleSO newTitle = TitleManager.I != null ? TitleManager.I.GetTitleById(newTitleId) : null;

        if (newTitle != null)
        {
            int titleScore = Mathf.Max(0, newTitle.arenaScore);
            slot.titleId = newTitle.titleId;
            slot.titleName = newTitle.DisplayOrId;
            slot.titleArenaScore = titleScore;
            slot.finalArenaContributionScore = slot.monsterArenaScore + titleScore;
        }
        else
        {
            // Title id was invalid — remove title.
            slot.titleId = "";
            slot.titleName = "";
            slot.titleArenaScore = 0;
            slot.finalArenaContributionScore = slot.monsterArenaScore;
        }
    }

    // ═════════════════════════════════════════════════════════════
    //  Title resolution helpers
    // ═════════════════════════════════════════════════════════════

    /// <summary>
    /// Tries to pick a title from a curated pool list.
    /// Returns null if the pool is empty or no valid title could be resolved.
    /// </summary>
    private static TitleSO PickTitleFromPool(List<string> titleIds, System.Random rng)
    {
        if (titleIds == null || titleIds.Count == 0) return null;
        if (TitleManager.I == null) return null;

        // Shuffle to avoid always picking the same entry when pool has invalid ids.
        var shuffled = new List<string>(titleIds);
        Shuffle(shuffled, rng);

        for (int i = 0; i < shuffled.Count; i++)
        {
            var id = shuffled[i];
            if (string.IsNullOrEmpty(id)) continue;
            var title = TitleManager.I.GetTitleById(id);
            if (title != null) return title;
        }

        return null;
    }

    /// <summary>
    /// Picks a title from the monster's own SO data (defaultAlwaysOnTitles / ironTitles).
    /// Used as a fallback when the template's title pool is empty.
    /// </summary>
    private static TitleSO PickTitleFromMonsterData(MonsterDataSO def, System.Random rng)
    {
        if (def == null) return null;

        var candidates = new List<TitleSO>();
        if (def.defaultAlwaysOnTitles != null)
            for (int i = 0; i < def.defaultAlwaysOnTitles.Length; i++)
                if (def.defaultAlwaysOnTitles[i] != null)
                    candidates.Add(def.defaultAlwaysOnTitles[i]);

        if (def.ironTitles != null)
            for (int i = 0; i < def.ironTitles.Length; i++)
                if (def.ironTitles[i] != null)
                    candidates.Add(def.ironTitles[i]);

        if (candidates.Count == 0) return null;
        return candidates[rng.Next(candidates.Count)];
    }

    /// <summary>
    /// Collects all title ids from a monster's defaultAlwaysOnTitles and ironTitles.
    /// </summary>
    private static void HarvestTitlesFromMonster(MonsterDataSO def, List<string> output)
    {
        if (def.defaultAlwaysOnTitles != null)
        {
            for (int i = 0; i < def.defaultAlwaysOnTitles.Length; i++)
            {
                var t = def.defaultAlwaysOnTitles[i];
                if (t != null && !string.IsNullOrEmpty(t.titleId) && !output.Contains(t.titleId))
                    output.Add(t.titleId);
            }
        }

        if (def.ironTitles != null)
        {
            for (int i = 0; i < def.ironTitles.Length; i++)
            {
                var t = def.ironTitles[i];
                if (t != null && !string.IsNullOrEmpty(t.titleId) && !output.Contains(t.titleId))
                    output.Add(t.titleId);
            }
        }
    }

    // ═════════════════════════════════════════════════════════════
    //  Monster pool helpers
    // ═════════════════════════════════════════════════════════════

    /// <summary>
    /// Picks one monster from a curated id pool, respecting species uniqueness.
    /// Shuffles the pool to avoid always picking the same species.
    /// Falls back to <see cref="PickFallbackMonster"/> if the pool yields nothing.
    /// </summary>
    private static MonsterDataSO PickMonsterFromPool(
        List<string> monsterIds,
        HashSet<string> usedSpecies,
        bool allowDuplicates,
        System.Random rng)
    {
        if (monsterIds == null || monsterIds.Count == 0) return null;

        var shuffled = new List<string>(monsterIds);
        Shuffle(shuffled, rng);

        for (int i = 0; i < shuffled.Count; i++)
        {
            var id = shuffled[i];
            if (string.IsNullOrEmpty(id)) continue;
            if (!allowDuplicates && usedSpecies.Contains(id)) continue;
            var def = MonsterCatalog.GetById(id);
            if (def != null) return def;
        }

        return null;
    }

    private static MonsterDataSO PickFallbackMonster(System.Random rng)
    {
        var all = MonsterCatalog.All;
        if (all == null || all.Count == 0) return null;
        return all[rng.Next(all.Count)];
    }

    // ═════════════════════════════════════════════════════════════
    //  Fallback bot (no templates available)
    // ═════════════════════════════════════════════════════════════

    /// <summary>
    /// Generates a bot team from the full monster catalog when no templates are
    /// available for the target band.  Picks 3 random distinct non-boss species.
    /// </summary>
    private static ArenaTeamSnapshot GenerateFallbackBot(
        ArenaScoreBand band,
        string botPlayerId,
        long nowUtc,
        System.Random rng)
    {
        var allMonsters = MonsterCatalog.All;
        if (allMonsters == null || allMonsters.Count == 0)
        {
            Debug.LogWarning("[ArenaBotGenerator] MonsterCatalog is empty — cannot generate bot.");
            return new ArenaTeamSnapshot
            {
                snapshotId = Guid.NewGuid().ToString("N"),
                ownerPlayerId = botPlayerId,
                isBot = true,
                createdUtc = nowUtc
            };
        }

        var usedSpecies = new HashSet<string>(StringComparer.Ordinal);
        var slots = new List<ArenaBitlingSnapshot>(ArenaConstants.BattleTeamSize);
        var types = new List<MonsterType>(ArenaConstants.BattleTeamSize);

        for (int s = 0; s < ArenaConstants.BattleTeamSize; s++)
        {
            MonsterDataSO def = null;
            int attempts = 0;
            while (attempts < 50)
            {
                var candidate = allMonsters[rng.Next(allMonsters.Count)];
                if (candidate != null && !string.IsNullOrEmpty(candidate.id)
                    && !candidate.isBoss && !candidate.uncatchable
                    && !usedSpecies.Contains(candidate.id))
                {
                    def = candidate;
                    break;
                }
                attempts++;
            }

            if (def == null)
                def = allMonsters[rng.Next(allMonsters.Count)];
            if (def == null) continue;

            usedSpecies.Add(def.id);
            int monsterScore = Mathf.Max(0, def.arenaScore);

            slots.Add(new ArenaBitlingSnapshot
            {
                instanceId = $"{botPlayerId}_slot{s}",
                monsterId = def.id,
                monsterName = def.displayName ?? def.id,
                monsterType = def.type,
                monsterArenaScore = monsterScore,
                titleArenaScore = 0,
                finalArenaContributionScore = monsterScore
            });

            types.Add(def.type);
        }

        int baseSum = 0;
        for (int i = 0; i < slots.Count; i++)
            baseSum += slots[i].finalArenaContributionScore;

        int synergy = ArenaScoreCalculator.CalculateTypeSynergyBonus(types);

        return new ArenaTeamSnapshot
        {
            snapshotId = Guid.NewGuid().ToString("N"),
            ownerPlayerId = botPlayerId,
            isBot = true,
            visibilityMode = ArenaVisibilityMode.FullReveal,
            arenaScore = baseSum + synergy,
            createdUtc = nowUtc,
            slotSnapshots = slots
        };
    }

    // ═════════════════════════════════════════════════════════════
    //  Template slot accessors
    // ═════════════════════════════════════════════════════════════

    private static List<string> GetTemplateSlotPool(ArenaBotTeamTemplate t, int slotIndex)
    {
        if (t == null) return new List<string>();
        switch (slotIndex)
        {
            case 0: return t.slot1MonsterOptions ?? new List<string>();
            case 1: return t.slot2MonsterOptions ?? new List<string>();
            case 2: return t.slot3MonsterOptions ?? new List<string>();
            default: return new List<string>();
        }
    }

    private static List<string> GetTemplateTitlePool(ArenaBotTeamTemplate t, int slotIndex)
    {
        if (t == null) return new List<string>();
        switch (slotIndex)
        {
            case 0: return t.slot1TitleOptions ?? new List<string>();
            case 1: return t.slot2TitleOptions ?? new List<string>();
            case 2: return t.slot3TitleOptions ?? new List<string>();
            default: return new List<string>();
        }
    }

    // ═════════════════════════════════════════════════════════════
    //  Band helpers
    // ═════════════════════════════════════════════════════════════

    private static float GetTitleChanceForBand(ArenaScoreBand band)
    {
        int idx = Mathf.Clamp((int)band, 0, BandTitleChance.Length - 1);
        return BandTitleChance[idx];
    }

    // ═════════════════════════════════════════════════════════════
    //  Display name helpers
    // ═════════════════════════════════════════════════════════════

    private static string PickDisplayName(ArenaBotTeamTemplate template, System.Random rng)
    {
        if (template.displayNamePool != null && template.displayNamePool.Count > 0)
        {
            string name = template.displayNamePool[rng.Next(template.displayNamePool.Count)];
            // Append a 2-digit tag to reduce collision between bots using the same template.
            return $"{name}{rng.Next(10, 100)}";
        }

        return GenerateFallbackName(rng);
    }

    private static readonly string[] FallbackPrefixes =
    {
        "Shadow", "Neon", "Pixel", "Volt", "Grit",
        "Blaze", "Frost", "Iron", "Storm", "Drift",
        "Hex", "Cipher", "Nova", "Onyx", "Cloud"
    };

    private static readonly string[] FallbackSuffixes =
    {
        "Trainer", "Master", "Hunter", "Scout", "Warden",
        "Ace", "Hero", "Knight", "Alpha", "Striker",
        "Sage", "Cadet", "Elite", "Rival", "Runner"
    };

    private static string GenerateFallbackName(System.Random rng)
    {
        var prefix = FallbackPrefixes[rng.Next(FallbackPrefixes.Length)];
        var suffix = FallbackSuffixes[rng.Next(FallbackSuffixes.Length)];
        int tag = rng.Next(10, 100);
        return $"{prefix}{suffix}{tag}";
    }

    // ═════════════════════════════════════════════════════════════
    //  Fisher-Yates shuffle
    // ═════════════════════════════════════════════════════════════

    private static void Shuffle<T>(List<T> list, System.Random rng)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
