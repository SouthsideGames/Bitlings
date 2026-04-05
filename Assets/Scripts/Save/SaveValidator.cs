using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public readonly struct SaveValidationResult
{
    public SaveValidationResult(bool repaired, string summary)
    {
        Repaired = repaired;
        Summary = summary ?? string.Empty;
    }

    public bool Repaired { get; }
    public string Summary { get; }
}

public static class SaveValidator
{
    public static SaveValidationResult ValidateAndRepair(SaveData saveData)
    {
        saveData ??= new SaveData();

        int repairs = 0;
        var notes = new List<string>();

        saveData.saveVersion = SaveMigrationManager.CURRENT_SAVE_VERSION;

        repairs += EnsureSection(ref saveData.playerData, () => new PlayerSaveSection(), "playerData", notes);
        repairs += EnsureSection(ref saveData.resourceData, () => new ResourceSaveSection(), "resourceData", notes);
        repairs += EnsureSection(ref saveData.bitlingCollectionData, () => new BitlingCollectionSaveSection(), "bitlingCollectionData", notes);
        repairs += EnsureSection(ref saveData.teamData, () => new TeamSaveSection(), "teamData", notes);
        repairs += EnsureSection(ref saveData.idleData, () => new IdleSaveSection(), "idleData", notes);
        repairs += EnsureSection(ref saveData.achievementData, () => new AchievementSaveSection(), "achievementData", notes);
        repairs += EnsureSection(ref saveData.codexData, () => new CodexSaveSection(), "codexData", notes);
        repairs += EnsureSection(ref saveData.exchangeData, () => new ExchangeSystemSaveSection(), "exchangeData", notes);
        repairs += EnsureSection(ref saveData.worldEventData, () => new WorldEventSystemSaveSection(), "worldEventData", notes);
        repairs += EnsureSection(ref saveData.settingsData, () => new SettingsSaveSection(), "settingsData", notes);
        repairs += EnsureSection(ref saveData.tutorialData, () => new TutorialSaveSection(), "tutorialData", notes);
        repairs += EnsureSection(ref saveData.titleData, () => new TitleSystemSaveSection(), "titleData", notes);
        repairs += EnsureSection(ref saveData.jobRuntimeData, () => new JobRuntimeSystemSaveSection(), "jobRuntimeData", notes);

        repairs += NormalizePlayerSection(saveData.playerData, notes);
        repairs += NormalizeResourceSection(saveData.resourceData, notes);

        repairs += EnsureList(ref saveData.bitlingCollectionData.owned, "bitlingCollectionData.owned", notes);
        repairs += EnsureList(ref saveData.bitlingCollectionData.ownedIds, "bitlingCollectionData.ownedIds", notes);
        repairs += EnsureList(ref saveData.bitlingCollectionData.favoriteMonsterIds, "bitlingCollectionData.favoriteMonsterIds", notes);
        repairs += EnsureList(ref saveData.bitlingCollectionData.unlockedPacks, "bitlingCollectionData.unlockedPacks", notes);
        repairs += EnsureList(ref saveData.bitlingCollectionData.preferredVariants, "bitlingCollectionData.preferredVariants", notes);
        repairs += RemoveBlankStrings(saveData.bitlingCollectionData.ownedIds, "bitlingCollectionData.ownedIds", notes);
        repairs += RemoveDuplicateStrings(saveData.bitlingCollectionData.ownedIds, "bitlingCollectionData.ownedIds", notes);
        repairs += RemoveBlankStrings(saveData.bitlingCollectionData.favoriteMonsterIds, "bitlingCollectionData.favoriteMonsterIds", notes);
        repairs += RemoveDuplicateStrings(saveData.bitlingCollectionData.favoriteMonsterIds, "bitlingCollectionData.favoriteMonsterIds", notes);
        repairs += RemoveBlankStrings(saveData.bitlingCollectionData.unlockedPacks, "bitlingCollectionData.unlockedPacks", notes);
        repairs += RemoveDuplicateStrings(saveData.bitlingCollectionData.unlockedPacks, "bitlingCollectionData.unlockedPacks", notes);
        repairs += NormalizeOwnedMonsters(saveData.bitlingCollectionData.owned, notes);
        var ownedUidSet = BuildOwnedUidSet(saveData.bitlingCollectionData.owned);

        repairs += EnsureList(ref saveData.teamData.activeTeam, "teamData.activeTeam", notes);
        repairs += NormalizeOwnedMonsters(saveData.teamData.activeTeam, notes);
        repairs += ClearDuplicateTeamEntries(saveData.teamData.activeTeam, notes);

        repairs += EnsureList(ref saveData.idleData.idleTeamOwnedUIDs, "idleData.idleTeamOwnedUIDs", notes);
        repairs += EnsureIdleSlotSize(saveData.idleData.idleTeamOwnedUIDs, notes);
        repairs += EnsureList(ref saveData.idleData.autoBattleLogArchive, "idleData.autoBattleLogArchive", notes);
        repairs += RepairIdleReferences(saveData.idleData.idleTeamOwnedUIDs, ownedUidSet, saveData.teamData.activeTeam, notes);

        repairs += EnsureList(ref saveData.achievementData.achievements, "achievementData.achievements", notes);
        repairs += NormalizeAchievements(saveData.achievementData.achievements, notes);

        repairs += EnsureList(ref saveData.codexData.discoveredMonsterIds, "codexData.discoveredMonsterIds", notes);
        repairs += EnsureList(ref saveData.codexData.seenTypes, "codexData.seenTypes", notes);
        repairs += RemoveBlankStrings(saveData.codexData.discoveredMonsterIds, "codexData.discoveredMonsterIds", notes);
        repairs += RemoveDuplicateStrings(saveData.codexData.discoveredMonsterIds, "codexData.discoveredMonsterIds", notes);
        repairs += RemoveDuplicateEnums(saveData.codexData.seenTypes, "codexData.seenTypes", notes);

        repairs += EnsureList(ref saveData.settingsData.unlockedFeatureIds, "settingsData.unlockedFeatureIds", notes);
        repairs += EnsureList(ref saveData.settingsData.unlockedJobSites, "settingsData.unlockedJobSites", notes);
        repairs += RemoveBlankStrings(saveData.settingsData.unlockedFeatureIds, "settingsData.unlockedFeatureIds", notes);
        repairs += RemoveDuplicateStrings(saveData.settingsData.unlockedFeatureIds, "settingsData.unlockedFeatureIds", notes);
        repairs += RemoveDuplicateEnums(saveData.settingsData.unlockedJobSites, "settingsData.unlockedJobSites", notes);
        repairs += EnsureSection(ref saveData.settingsData.settings, () => new SettingsState(), "settingsData.settings", notes);
        repairs += NormalizeSettingsSection(saveData.settingsData, notes);

        repairs += EnsureList(ref saveData.tutorialData.completed, "tutorialData.completed", notes);
        repairs += RemoveBlankStrings(saveData.tutorialData.completed, "tutorialData.completed", notes);
        repairs += RemoveDuplicateStrings(saveData.tutorialData.completed, "tutorialData.completed", notes);

        repairs += EnsureSection(ref saveData.titleData.titles, () => new TitleSaveData(), "titleData.titles", notes);
        repairs += EnsureList(ref saveData.titleData.titles.equips, "titleData.titles.equips", notes);
        repairs += NormalizeTitleData(saveData.titleData.titles, notes);

        repairs += EnsureSection(ref saveData.jobRuntimeData.jobRuntime, () => new JobRuntimeSave(), "jobRuntimeData.jobRuntime", notes);
        repairs += EnsureList(ref saveData.jobRuntimeData.jobRuntime.sites, "jobRuntimeData.jobRuntime.sites", notes);
        repairs += EnsureList(ref saveData.jobRuntimeData.jobRuntime.cooldowns, "jobRuntimeData.jobRuntime.cooldowns", notes);
        repairs += NormalizeJobRuntime(saveData.jobRuntimeData.jobRuntime, notes);

        repairs += EnsureSection(ref saveData.worldEventData.worldEvents, () => new WorldEventSaveData(), "worldEventData.worldEvents", notes);
        repairs += EnsureList(ref saveData.worldEventData.worldEvents.cooldowns, "worldEventData.worldEvents.cooldowns", notes);
        repairs += EnsureList(ref saveData.worldEventData.archivedEventIds, "worldEventData.archivedEventIds", notes);
        repairs += NormalizeWorldEventData(saveData.worldEventData, notes);

        repairs += EnsureSection(ref saveData.exchangeData.exchangeState, () => new ExchangeSaveData(), "exchangeData.exchangeState", notes);
        repairs += EnsureList(ref saveData.exchangeData.exchangeState.speciesStates, "exchangeData.exchangeState.speciesStates", notes);
        repairs += EnsureList(ref saveData.exchangeData.exchangeState.monthlyBattleSentiments, "exchangeData.exchangeState.monthlyBattleSentiments", notes);
        repairs += EnsureList(ref saveData.exchangeData.exchangeState.activeRequests, "exchangeData.exchangeState.activeRequests", notes);
        repairs += EnsureList(ref saveData.exchangeData.exchangeState.demandOverrides, "exchangeData.exchangeState.demandOverrides", notes);
        repairs += EnsureList(ref saveData.exchangeData.exchangeState.bullTokenUsages, "exchangeData.exchangeState.bullTokenUsages", notes);
        repairs += EnsureList(ref saveData.exchangeData.exchangeState.bearTokenUsages, "exchangeData.exchangeState.bearTokenUsages", notes);
        repairs += EnsureList(ref saveData.exchangeData.exchangeState.catchHype, "exchangeData.exchangeState.catchHype", notes);
        repairs += EnsureList(ref saveData.exchangeData.exchangeState.brokerScarcity, "exchangeData.exchangeState.brokerScarcity", notes);
        repairs += EnsureList(ref saveData.exchangeData.exchangeState.surgeAlertSpeciesIds, "exchangeData.exchangeState.surgeAlertSpeciesIds", notes);
        repairs += NormalizeExchangeData(saveData.exchangeData.exchangeState, notes);

        // Future validation rules should be added here by section so new systems stay isolated.

        string summary = repairs > 0
            ? $"SaveValidator repaired {repairs} issue(s). {string.Join(" ", notes.Take(10))}".Trim()
            : "SaveValidator found no structural repairs.";

        Debug.Log($"[SaveValidator] {summary}");
        return new SaveValidationResult(repairs > 0, summary);
    }

    public static void EnsureResourceCountsSized(PlayerManager data)
    {
        if (data == null) return;
        data.resourceCounts ??= new List<int>();
        int need = GetResourceVectorSize();
        while (data.resourceCounts.Count < need)
            data.resourceCounts.Add(0);
    }

    public static void EnsureLifetimeResourceCountsSized(PlayerManager data)
    {
        if (data == null) return;
        data.lifetimeResourceCollected ??= new List<int>();
        int need = GetResourceVectorSize();
        while (data.lifetimeResourceCollected.Count < need)
            data.lifetimeResourceCollected.Add(0);

        int max = Mathf.Min(data.lifetimeResourceCollected.Count, data.resourceCounts?.Count ?? 0);
        for (int i = 0; i < max; i++)
            if (data.lifetimeResourceCollected[i] < data.resourceCounts[i])
                data.lifetimeResourceCollected[i] = Mathf.Max(0, data.resourceCounts[i]);
    }

    public static void NormalizeOwnedEntries(PlayerManager data, List<OwnedMonsterData> list, Func<OwnedMonsterData, int> resolveFullHp)
    {
        if (list == null) return;
        for (int i = 0; i < list.Count; i++)
        {
            var monster = list[i];
            if (monster == null)
            {
                list[i] = new OwnedMonsterData();
                monster = list[i];
            }

            monster.level = Mathf.Clamp(monster.level, 1, LevelRules.MaxLevel);
            monster.currentXP = Mathf.Max(0, monster.currentXP);
            if (monster.currentHP < 0)
                monster.currentHP = resolveFullHp != null ? resolveFullHp(monster) : 1;
            monster.currentHP = Mathf.Max(0, monster.currentHP);
            monster.pendingLevels = Mathf.Max(0, monster.pendingLevels);
            monster.unspentStatPoints = Mathf.Max(0, monster.unspentStatPoints);
            monster.autoApplyTargetLevel = Mathf.Clamp(monster.autoApplyTargetLevel, 0, LevelRules.MaxLevel);
            monster.lastHPUnix = Math.Max(0L, monster.lastHPUnix);
            monster.trainingLastUnix = Math.Max(0L, monster.trainingLastUnix);
            if (monster.lastLevelClaimDay == 0) monster.lastLevelClaimDay = -1;

            if (string.IsNullOrEmpty(monster.ownedUID))
                monster.ownedUID = Guid.NewGuid().ToString("N");

            if (monster.premiumTier > 0 && !monster.isPremium) monster.isPremium = true;
            if (monster.isPremium && monster.premiumTier <= 0) monster.premiumTier = 1;

            if (data != null && ReferenceEquals(list, data.owned) && !string.IsNullOrEmpty(monster.monsterId))
            {
                data.ownedIds ??= new HashSet<string>();
                data.ownedIds.Add(monster.monsterId);
            }
        }
    }

    public static void EnsureTrainingDefaults(PlayerManager data, Func<long> nowUnix)
    {
        if (data?.owned == null) return;
        long now = nowUnix != null ? nowUnix() : 0L;
        foreach (var monster in data.owned)
        {
            if (monster == null) continue;
            if (monster.level <= 0) monster.level = 1;
            if (monster.currentXP < 0) monster.currentXP = 0;
            if (monster.trainingLastUnix == 0) monster.trainingLastUnix = now;
            if (monster.lastLevelClaimDay == 0) monster.lastLevelClaimDay = -1;
            if (monster.pendingLevels < 0) monster.pendingLevels = 0;
            if (monster.premiumTier > 0 && !monster.isPremium) monster.isPremium = true;
            if (monster.isPremium && monster.premiumTier <= 0) monster.premiumTier = 1;
        }
    }

    public static SaveValidationResult ValidateRuntimeAndRepair(PlayerManager data, Func<OwnedMonsterData, int> resolveFullHp, Func<long> nowUnix)
    {
        if (data == null)
            return new SaveValidationResult(false, "Save integrity: data missing.");

        bool changed = false;
        int removedTeamEntries = 0;
        int clampedValues = 0;
        bool fixedJobsOffline = false;

        data.owned ??= new List<OwnedMonsterData>();
        data.team ??= new List<OwnedMonsterData>();

        for (int i = data.team.Count - 1; i >= 0; i--)
        {
            var teamEntry = data.team[i];
            if (teamEntry == null || string.IsNullOrEmpty(teamEntry.monsterId))
            {
                data.team.RemoveAt(i);
                changed = true;
                removedTeamEntries++;
            }
        }

        NormalizeOwnedEntries(data, data.owned, resolveFullHp);
        NormalizeOwnedEntries(data, data.team, resolveFullHp);

        clampedValues += ClampRuntime(ref data.encounterMax, 1);
        clampedValues += ClampRuntime(ref data.encounterCost, 1);
        clampedValues += ClampRuntime(ref data.promotionRank, 1);
        clampedValues += ClampRuntime(ref data.promotionXP, 0);
        clampedValues += ClampRuntime(ref data.winStreak, 0);
        clampedValues += ClampRuntime(ref data.encounterPoints, 0);
        clampedValues += ClampRuntime(ref data.cheatInvalidAttempts, 0);
        clampedValues += ClampRuntime(ref data.forcePremiumCapturesRemaining, 0);
        clampedValues += ClampRuntime(ref data.pendingIdleXP, 0);
        clampedValues += ClampRuntime(ref data.trainingMonsterLevel, 0);
        clampedValues += ClampRuntime(ref data.energyRemainderSecs, 0f);
        clampedValues += ClampRuntime(ref data.lastClosedUnix, 0L);
        clampedValues += ClampRuntime(ref data.lastSavedUnix, 0L);
        clampedValues += ClampRuntime(ref data.energyLastUnix, 0L);

        if (data.settings == null)
        {
            data.settings = new SettingsState();
            changed = true;
        }

        int beforeDifficulty = data.settings.difficultyMode;
        data.settings.difficultyMode = Mathf.Clamp(data.settings.difficultyMode, 0, 2);
        if (data.promotionRank < 15)
            data.settings.difficultyMode = 0;
        if (beforeDifficulty != data.settings.difficultyMode)
            clampedValues++;

        if (data.jobsOfflineLastUnix < 0)
        {
            data.jobsOfflineLastUnix = nowUnix != null ? nowUnix() : 0L;
            changed = true;
            fixedJobsOffline = true;
        }

        if (clampedValues > 0)
            changed = true;

        string summary = changed
            ? $"Repaired save: removedTeamEntries={removedTeamEntries}, fixedJobsOffline={fixedJobsOffline}, clampedValues={clampedValues}"
            : "Save integrity: no repairs needed.";

        if (changed)
            Debug.Log($"[SaveValidator] {summary}");

        return new SaveValidationResult(changed, summary);
    }

    private static int NormalizePlayerSection(PlayerSaveSection player, List<string> notes)
    {
        if (player == null) return 0;

        int repairs = 0;
        repairs += EnsureSection(ref player.fieldOps, () => new FieldOpsStats(), "playerData.fieldOps", notes);
        repairs += EnsureSection(ref player.seedState, () => new SeedState(), "playerData.seedState", notes);
        repairs += EnsureList(ref player.activeFlyers, "playerData.activeFlyers", notes);
        repairs += EnsureList(ref player.activeWorkOrders, "playerData.activeWorkOrders", notes);
        repairs += EnsureList(ref player.activeJobMods, "playerData.activeJobMods", notes);
        repairs += EnsureList(ref player.activeFavorBoosts, "playerData.activeFavorBoosts", notes);
        repairs += EnsureList(ref player.activePremiumBoosts, "playerData.activePremiumBoosts", notes);
        repairs += EnsureList(ref player.jobStorageUpgrades, "playerData.jobStorageUpgrades", notes);
        repairs += EnsureList(ref player.jobAssignments, "playerData.jobAssignments", notes);
        repairs += EnsureList(ref player.jobProgress, "playerData.jobProgress", notes);
        repairs += EnsureList(ref player.fieldOps.recentHighlights, "playerData.fieldOps.recentHighlights", notes);
        repairs += ClampMin(ref player.encounterMax, 1, "playerData.encounterMax", notes);
        repairs += ClampMin(ref player.encounterCost, 1, "playerData.encounterCost", notes);
        repairs += ClampMin(ref player.dailyBonusDay, 1, "playerData.dailyBonusDay", notes);
        repairs += ClampMin(ref player.promotionRank, 1, "playerData.promotionRank", notes);
        repairs += ClampMin(ref player.promotionXP, 0, "playerData.promotionXP", notes);
        repairs += ClampMin(ref player.winStreak, 0, "playerData.winStreak", notes);
        repairs += ClampMin(ref player.encounterPoints, 0, "playerData.encounterPoints", notes);
        repairs += ClampMin(ref player.cheatInvalidAttempts, 0, "playerData.cheatInvalidAttempts", notes);
        repairs += ClampMin(ref player.forcePremiumCapturesRemaining, 0, "playerData.forcePremiumCapturesRemaining", notes);
        repairs += ClampMin(ref player.trainingMonsterLevel, 0, "playerData.trainingMonsterLevel", notes);
        repairs += ClampMin(ref player.pendingIdleXP, 0, "playerData.pendingIdleXP", notes);
        repairs += ClampMin(ref player.encountersSinceBoss, 0, "playerData.encountersSinceBoss", notes);
        repairs += ClampMin(ref player.bossEveryN, 1, "playerData.bossEveryN", notes);
        repairs += ClampMin(ref player.lastClosedUnix, 0L, "playerData.lastClosedUnix", notes);
        repairs += ClampMin(ref player.lastSavedUnix, 0L, "playerData.lastSavedUnix", notes);
        repairs += ClampMin(ref player.jobsOfflineLastUnix, 0L, "playerData.jobsOfflineLastUnix", notes);
        repairs += ClampMin(ref player.energyLastUnix, 0L, "playerData.energyLastUnix", notes);
        repairs += ClampMin(ref player.cheatLockedUntilUnix, 0L, "playerData.cheatLockedUntilUnix", notes);
        repairs += ClampMin(ref player.energyRemainderSecs, 0f, "playerData.energyRemainderSecs", notes);
        repairs += ClampMin(ref player.fieldOps.encountersInitiated, 0, "playerData.fieldOps.encountersInitiated", notes);
        repairs += ClampMin(ref player.fieldOps.captureAttempts, 0, "playerData.fieldOps.captureAttempts", notes);
        repairs += ClampMin(ref player.fieldOps.capturesSuccessful, 0, "playerData.fieldOps.capturesSuccessful", notes);
        repairs += ClampMin(ref player.fieldOps.rareBitlingsFound, 0, "playerData.fieldOps.rareBitlingsFound", notes);
        repairs += ClampMin(ref player.fieldOps.premiumDiscoveries, 0, "playerData.fieldOps.premiumDiscoveries", notes);
        repairs += ClampMin(ref player.fieldOps.riftStabilizations, 0, "playerData.fieldOps.riftStabilizations", notes);
        repairs += ClampMin(ref player.fieldOps.longestCaptureStreak, 0, "playerData.fieldOps.longestCaptureStreak", notes);
        repairs += ClampMin(ref player.fieldOps.currentCaptureStreak, 0, "playerData.fieldOps.currentCaptureStreak", notes);
        repairs += RemoveBlankStrings(player.fieldOps.recentHighlights, "playerData.fieldOps.recentHighlights", notes);
        repairs += NormalizeBoostLists(player, notes);
        repairs += NormalizeJobStorageUpgrades(player.jobStorageUpgrades, notes);
        repairs += NormalizeJobAssignments(player.jobAssignments, notes);
        repairs += NormalizeJobProgress(player.jobProgress, notes);
        return repairs;
    }

    private static int NormalizeResourceSection(ResourceSaveSection resources, List<string> notes)
    {
        if (resources == null) return 0;
        int repairs = 0;
        repairs += EnsureList(ref resources.resourceCounts, "resourceData.resourceCounts", notes);
        repairs += EnsureList(ref resources.lifetimeResourceCollected, "resourceData.lifetimeResourceCollected", notes);
        repairs += EnsureResourceVectorSize(resources.resourceCounts, "resourceData.resourceCounts", notes);
        repairs += EnsureResourceVectorSize(resources.lifetimeResourceCollected, "resourceData.lifetimeResourceCollected", notes);
        repairs += ClampNonNegative(resources.resourceCounts, "resourceData.resourceCounts", notes);
        repairs += ClampNonNegative(resources.lifetimeResourceCollected, "resourceData.lifetimeResourceCollected", notes);
        repairs += EnsureLifetimeNotBelowCurrent(resources, notes);
        repairs += ClampMin(ref resources.credits, 0, "resourceData.credits", notes);
        return repairs;
    }

    private static int NormalizeSettingsSection(SettingsSaveSection settings, List<string> notes)
    {
        if (settings?.settings == null) return 0;
        int repairs = 0;
        int clamped = Mathf.Clamp(settings.settings.difficultyMode, 0, 2);
        if (clamped != settings.settings.difficultyMode)
        {
            settings.settings.difficultyMode = clamped;
            repairs++;
            notes.Add("Clamped settingsData.settings.difficultyMode.");
        }
        return repairs;
    }

    private static int NormalizeOwnedMonsters(List<OwnedMonsterData> monsters, List<string> notes)
    {
        if (monsters == null) return 0;
        int repairs = 0;
        var usedUids = new HashSet<string>(StringComparer.Ordinal);
        for (int i = monsters.Count - 1; i >= 0; i--)
        {
            var monster = monsters[i];
            if (monster == null || string.IsNullOrWhiteSpace(monster.monsterId))
            {
                monsters.RemoveAt(i);
                repairs++;
                notes.Add("Removed null or blank Bitling entry.");
                continue;
            }

            repairs += ClampMin(ref monster.level, 1, $"OwnedMonster({monster.monsterId}).level", notes);
            repairs += ClampMax(ref monster.level, LevelRules.MaxLevel, $"OwnedMonster({monster.monsterId}).level", notes);
            repairs += ClampMin(ref monster.currentXP, 0, $"OwnedMonster({monster.monsterId}).currentXP", notes);
            repairs += ClampMin(ref monster.currentHP, 0, $"OwnedMonster({monster.monsterId}).currentHP", notes);
            repairs += ClampMin(ref monster.lastHPUnix, 0L, $"OwnedMonster({monster.monsterId}).lastHPUnix", notes);
            repairs += ClampMin(ref monster.flatAtkBonus, 0, $"OwnedMonster({monster.monsterId}).flatAtkBonus", notes);
            repairs += ClampMin(ref monster.trainingLastUnix, 0L, $"OwnedMonster({monster.monsterId}).trainingLastUnix", notes);
            repairs += ClampMin(ref monster.pendingLevels, 0, $"OwnedMonster({monster.monsterId}).pendingLevels", notes);
            repairs += ClampMin(ref monster.premiumTier, 0, $"OwnedMonster({monster.monsterId}).premiumTier", notes);
            repairs += ClampMin(ref monster.autoApplyTargetLevel, 0, $"OwnedMonster({monster.monsterId}).autoApplyTargetLevel", notes);
            repairs += ClampMax(ref monster.autoApplyTargetLevel, LevelRules.MaxLevel, $"OwnedMonster({monster.monsterId}).autoApplyTargetLevel", notes);
            repairs += ClampMin(ref monster.unspentStatPoints, 0, $"OwnedMonster({monster.monsterId}).unspentStatPoints", notes);

            if (monster.lastLevelClaimDay == 0)
            {
                monster.lastLevelClaimDay = -1;
                repairs++;
                notes.Add($"Normalized {monster.monsterId} lastLevelClaimDay.");
            }

            if (monster.isPremium && monster.premiumTier <= 0)
            {
                monster.premiumTier = 1;
                repairs++;
                notes.Add($"Filled missing premium tier for {monster.monsterId}.");
            }
            if (monster.premiumTier > 0 && !monster.isPremium)
            {
                monster.isPremium = true;
                repairs++;
                notes.Add($"Normalized premium flag for {monster.monsterId}.");
            }

            if (string.IsNullOrWhiteSpace(monster.ownedUID) || !usedUids.Add(monster.ownedUID))
            {
                monster.ownedUID = Guid.NewGuid().ToString("N");
                usedUids.Add(monster.ownedUID);
                repairs++;
                notes.Add($"Repaired duplicate or blank ownedUID for {monster.monsterId}.");
            }
        }

        return repairs;
    }

    private static int ClearDuplicateTeamEntries(List<OwnedMonsterData> team, List<string> notes)
    {
        if (team == null) return 0;
        int repairs = 0;
        var seenUids = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < team.Count; i++)
        {
            var entry = team[i];
            if (entry == null || string.IsNullOrWhiteSpace(entry.ownedUID)) continue;
            if (seenUids.Add(entry.ownedUID)) continue;
            team[i] = new OwnedMonsterData();
            repairs++;
            notes.Add("Cleared duplicate active team reference.");
        }
        return repairs;
    }

    private static HashSet<string> BuildOwnedUidSet(List<OwnedMonsterData> owned)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        if (owned == null) return set;
        for (int i = 0; i < owned.Count; i++)
            if (owned[i] != null && !string.IsNullOrWhiteSpace(owned[i].ownedUID))
                set.Add(owned[i].ownedUID);
        return set;
    }

    private static int EnsureIdleSlotSize(List<string> idleUids, List<string> notes)
    {
        if (idleUids == null) return 0;
        int repairs = 0;
        while (idleUids.Count < IdleLoadoutManager.TeamSize)
        {
            idleUids.Add(null);
            repairs++;
            notes.Add("Initialized missing idle loadout slot.");
        }
        while (idleUids.Count > IdleLoadoutManager.TeamSize)
        {
            idleUids.RemoveAt(idleUids.Count - 1);
            repairs++;
            notes.Add("Removed extra idle loadout slot.");
        }
        return repairs;
    }

    private static int RepairIdleReferences(List<string> idleUids, HashSet<string> ownedUids, List<OwnedMonsterData> activeTeam, List<string> notes)
    {
        if (idleUids == null) return 0;
        int repairs = 0;
        var activeUids = new HashSet<string>(StringComparer.Ordinal);
        if (activeTeam != null)
        {
            for (int i = 0; i < Mathf.Min(activeTeam.Count, IdleLoadoutManager.TeamSize); i++)
            {
                var teamEntry = activeTeam[i];
                if (teamEntry != null && !string.IsNullOrWhiteSpace(teamEntry.ownedUID))
                    activeUids.Add(teamEntry.ownedUID);
            }
        }

        var seenIdle = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < idleUids.Count; i++)
        {
            string uid = idleUids[i];
            if (string.IsNullOrWhiteSpace(uid)) continue;
            if (!ownedUids.Contains(uid) || !seenIdle.Add(uid) || activeUids.Contains(uid))
            {
                idleUids[i] = null;
                repairs++;
                notes.Add("Cleared invalid or conflicting idle loadout reference.");
            }
        }

        return repairs;
    }

    private static int NormalizeAchievements(List<AchievementProgressData> achievements, List<string> notes)
    {
        if (achievements == null) return 0;
        int repairs = 0;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int i = achievements.Count - 1; i >= 0; i--)
        {
            var entry = achievements[i];
            if (entry == null || string.IsNullOrWhiteSpace(entry.id))
            {
                achievements.RemoveAt(i);
                repairs++;
                notes.Add("Removed null or blank achievement entry.");
                continue;
            }

            if (!seen.Add(entry.id))
            {
                achievements.RemoveAt(i);
                repairs++;
                notes.Add($"Removed duplicate achievement entry for {entry.id}.");
                continue;
            }

            repairs += ClampMin(ref entry.value, 0, $"Achievement({entry.id}).value", notes);
            repairs += ClampMin(ref entry.unlockedUnix, 0L, $"Achievement({entry.id}).unlockedUnix", notes);
        }
        return repairs;
    }

    private static int NormalizeTitleData(TitleSaveData titleData, List<string> notes)
    {
        if (titleData?.equips == null) return 0;
        int repairs = 0;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int i = titleData.equips.Count - 1; i >= 0; i--)
        {
            var equip = titleData.equips[i];
            if (equip == null || string.IsNullOrWhiteSpace(equip.monsterId))
            {
                titleData.equips.RemoveAt(i);
                repairs++;
                notes.Add("Removed invalid title equip entry.");
                continue;
            }

            if (!seen.Add(equip.monsterId))
            {
                titleData.equips.RemoveAt(i);
                repairs++;
                notes.Add($"Removed duplicate title equip for {equip.monsterId}.");
                continue;
            }

            repairs += EnsureList(ref equip.tierSelections, $"TitleEquip({equip.monsterId}).tierSelections", notes);
            repairs += RemoveBlankStrings(equip.tierSelections, $"TitleEquip({equip.monsterId}).tierSelections", notes);
        }
        return repairs;
    }

    private static int NormalizeJobRuntime(JobRuntimeSave runtime, List<string> notes)
    {
        if (runtime == null) return 0;
        int repairs = 0;
        repairs += ClampMin(ref runtime.savedAtUnix, 0L, "jobRuntime.savedAtUnix", notes);
        var seenJobs = new HashSet<JobType>();
        for (int i = runtime.sites.Count - 1; i >= 0; i--)
        {
            var site = runtime.sites[i];
            if (site == null || !seenJobs.Add(site.job))
            {
                runtime.sites.RemoveAt(i);
                repairs++;
                notes.Add("Removed invalid or duplicate job runtime site.");
                continue;
            }

            repairs += EnsureArraySize(ref site.slotFatigue01, IdleLoadoutManager.TeamSize, $"jobRuntime[{site.job}].slotFatigue01", notes);
            repairs += EnsureArraySize(ref site.slotCooldownUntilUnix, IdleLoadoutManager.TeamSize, $"jobRuntime[{site.job}].slotCooldownUntilUnix", notes);
            repairs += Clamp01(site.slotFatigue01, $"jobRuntime[{site.job}].slotFatigue01", notes);
            repairs += ClampNonNegative(site.slotCooldownUntilUnix, $"jobRuntime[{site.job}].slotCooldownUntilUnix", notes);
            repairs += ClampMin(ref site.storedUnits, 0, $"jobRuntime[{site.job}].storedUnits", notes);
            repairs += ClampMin(ref site.storedRemainder, 0f, $"jobRuntime[{site.job}].storedRemainder", notes);
        }

        var seenCooldowns = new HashSet<string>(StringComparer.Ordinal);
        for (int i = runtime.cooldowns.Count - 1; i >= 0; i--)
        {
            var cooldown = runtime.cooldowns[i];
            if (cooldown == null || string.IsNullOrWhiteSpace(cooldown.id) || !seenCooldowns.Add(cooldown.id))
            {
                runtime.cooldowns.RemoveAt(i);
                repairs++;
                notes.Add("Removed invalid or duplicate job cooldown entry.");
                continue;
            }

            repairs += ClampMin(ref cooldown.until, 0L, $"jobRuntime.cooldown[{cooldown.id}]", notes);
        }
        return repairs;
    }

    private static int NormalizeWorldEventData(WorldEventSystemSaveSection worldEvents, List<string> notes)
    {
        if (worldEvents == null) return 0;
        int repairs = 0;
        repairs += RemoveBlankStrings(worldEvents.archivedEventIds, "worldEventData.archivedEventIds", notes);
        repairs += RemoveDuplicateStrings(worldEvents.archivedEventIds, "worldEventData.archivedEventIds", notes);
        repairs += ClampMin(ref worldEvents.worldEvents.rotationUntilUnix, 0L, "worldEventData.worldEvents.rotationUntilUnix", notes);
        repairs += ClampMin(ref worldEvents.worldEvents.nextRotationRollUnix, 0L, "worldEventData.worldEvents.nextRotationRollUnix", notes);
        repairs += ClampMin(ref worldEvents.worldEvents.weeklyWeekStartUnix, 0L, "worldEventData.worldEvents.weeklyWeekStartUnix", notes);

        var seenCooldowns = new HashSet<string>(StringComparer.Ordinal);
        for (int i = worldEvents.worldEvents.cooldowns.Count - 1; i >= 0; i--)
        {
            var cooldown = worldEvents.worldEvents.cooldowns[i];
            if (cooldown == null || string.IsNullOrWhiteSpace(cooldown.id) || !seenCooldowns.Add(cooldown.id))
            {
                worldEvents.worldEvents.cooldowns.RemoveAt(i);
                repairs++;
                notes.Add("Removed invalid or duplicate world event cooldown entry.");
                continue;
            }

            repairs += ClampMin(ref cooldown.lastRolledUnix, 0L, $"worldEventData.cooldown[{cooldown.id}]", notes);
        }

        return repairs;
    }

    private static int NormalizeExchangeData(ExchangeSaveData exchange, List<string> notes)
    {
        if (exchange == null) return 0;
        int repairs = 0;
        repairs += ClampMin(ref exchange.totalBrokered, 0, "exchange.totalBrokered", notes);
        repairs += ClampMin(ref exchange.totalCreditsBrokered, 0, "exchange.totalCreditsBrokered", notes);
        repairs += ClampMin(ref exchange.totalRequestsFulfilled, 0, "exchange.totalRequestsFulfilled", notes);
        repairs += ClampOptionalIndex(ref exchange.lastDayIndex, "exchange.lastDayIndex", notes);
        repairs += ClampOptionalIndex(ref exchange.lastRequestRotationDayIndex, "exchange.lastRequestRotationDayIndex", notes);
        repairs += ClampOptionalIndex(ref exchange.battleSentimentMonthKey, "exchange.battleSentimentMonthKey", notes);
        repairs += ClampOptionalIndex(ref exchange.lastDividendDayIndex, "exchange.lastDividendDayIndex", notes);
        repairs += ClampOptionalIndex(ref exchange.pendingDividendToastDayIndex, "exchange.pendingDividendToastDayIndex", notes);
        repairs += ClampOptionalIndex(ref exchange.lastWeekIndex, "exchange.lastWeekIndex", notes);
        repairs += ClampMin(ref exchange.pendingDividendToastAmount, 0, "exchange.pendingDividendToastAmount", notes);
        repairs += ClampMin(ref exchange.lastRecalcUnix, 0L, "exchange.lastRecalcUnix", notes);
        repairs += NormalizeSpeciesStates(exchange.speciesStates, notes);
        repairs += NormalizeSentiments(exchange.monthlyBattleSentiments, notes);
        repairs += NormalizeActiveRequests(exchange.activeRequests, notes);
        repairs += NormalizeDemandOverrides(exchange.demandOverrides, notes);
        repairs += NormalizeTokenUsages(exchange.bullTokenUsages, "bull", notes);
        repairs += NormalizeTokenUsages(exchange.bearTokenUsages, "bear", notes);
        repairs += NormalizeCatchHype(exchange.catchHype, notes);
        repairs += NormalizeBrokerScarcity(exchange.brokerScarcity, notes);
        repairs += RemoveBlankStrings(exchange.surgeAlertSpeciesIds, "exchange.surgeAlertSpeciesIds", notes);
        repairs += RemoveDuplicateStrings(exchange.surgeAlertSpeciesIds, "exchange.surgeAlertSpeciesIds", notes);
        return repairs;
    }

    private static int NormalizeSpeciesStates(List<MarketSpeciesState> states, List<string> notes)
    {
        if (states == null) return 0;
        int repairs = 0;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int i = states.Count - 1; i >= 0; i--)
        {
            var state = states[i];
            if (state == null || string.IsNullOrWhiteSpace(state.speciesId) || !seen.Add(state.speciesId))
            {
                states.RemoveAt(i);
                repairs++;
                notes.Add("Removed invalid or duplicate exchange species state.");
                continue;
            }

            repairs += ClampMin(ref state.currentValue, 0, $"exchange.speciesState[{state.speciesId}].currentValue", notes);
            repairs += ClampMin(ref state.previousValue, 0, $"exchange.speciesState[{state.speciesId}].previousValue", notes);
            repairs += ClampMin(ref state.lastUpdateUnix, 0L, $"exchange.speciesState[{state.speciesId}].lastUpdateUnix", notes);
            if (!Enum.IsDefined(typeof(DemandLevel), state.demandLevel))
            {
                state.demandLevel = DemandLevel.Medium;
                repairs++;
                notes.Add($"Reset invalid demand level for {state.speciesId}.");
            }
            if (!Enum.IsDefined(typeof(TrendDirection), state.trend))
            {
                state.trend = TrendDirection.Stable;
                repairs++;
                notes.Add($"Reset invalid trend for {state.speciesId}.");
            }
        }
        return repairs;
    }

    private static int NormalizeSentiments(List<SpeciesBattleSentimentData> sentiments, List<string> notes)
    {
        if (sentiments == null) return 0;
        int repairs = 0;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int i = sentiments.Count - 1; i >= 0; i--)
        {
            var entry = sentiments[i];
            if (entry == null || string.IsNullOrWhiteSpace(entry.speciesId) || !seen.Add(entry.speciesId))
            {
                sentiments.RemoveAt(i);
                repairs++;
                notes.Add("Removed invalid or duplicate exchange sentiment entry.");
                continue;
            }
            repairs += ClampMin(ref entry.monthlyWinsAgainst, 0, $"exchange.sentiment[{entry.speciesId}].monthlyWinsAgainst", notes);
            repairs += ClampMin(ref entry.monthlyLossesAgainst, 0, $"exchange.sentiment[{entry.speciesId}].monthlyLossesAgainst", notes);
            repairs += ClampMin(ref entry.monthlyHoursWorked, 0f, $"exchange.sentiment[{entry.speciesId}].monthlyHoursWorked", notes);
        }
        return repairs;
    }

    private static int NormalizeActiveRequests(List<ActiveRequest> requests, List<string> notes)
    {
        if (requests == null) return 0;
        int repairs = 0;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int i = requests.Count - 1; i >= 0; i--)
        {
            var request = requests[i];
            if (request == null || string.IsNullOrWhiteSpace(request.requestId) || !seen.Add(request.requestId))
            {
                requests.RemoveAt(i);
                repairs++;
                notes.Add("Removed invalid or duplicate exchange request.");
                continue;
            }
            repairs += ClampMin(ref request.creditReward, 0, $"exchange.request[{request.requestId}].creditReward", notes);
            repairs += ClampMin(ref request.bonusResourceAmount, 0, $"exchange.request[{request.requestId}].bonusResourceAmount", notes);
            repairs += ClampMin(ref request.expiresUnix, 0L, $"exchange.request[{request.requestId}].expiresUnix", notes);
        }
        return repairs;
    }

    private static int NormalizeDemandOverrides(List<DemandOverride> overrides, List<string> notes)
    {
        if (overrides == null) return 0;
        int repairs = 0;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int i = overrides.Count - 1; i >= 0; i--)
        {
            var entry = overrides[i];
            if (entry == null || string.IsNullOrWhiteSpace(entry.speciesId) || !seen.Add(entry.speciesId))
            {
                overrides.RemoveAt(i);
                repairs++;
                notes.Add("Removed invalid or duplicate exchange demand override.");
                continue;
            }
            repairs += ClampMin(ref entry.expiresDay, 0, $"exchange.demandOverride[{entry.speciesId}].expiresDay", notes);
            if (!Enum.IsDefined(typeof(DemandLevel), entry.forcedDemand))
            {
                entry.forcedDemand = DemandLevel.Medium;
                repairs++;
                notes.Add($"Reset invalid demand override for {entry.speciesId}.");
            }
        }
        return repairs;
    }

    private static int NormalizeTokenUsages(List<SpeciesTokenUsage> usages, string label, List<string> notes)
    {
        if (usages == null) return 0;
        int repairs = 0;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int i = usages.Count - 1; i >= 0; i--)
        {
            var usage = usages[i];
            if (usage == null || string.IsNullOrWhiteSpace(usage.speciesId) || !seen.Add(usage.speciesId))
            {
                usages.RemoveAt(i);
                repairs++;
                notes.Add($"Removed invalid or duplicate {label} token usage entry.");
                continue;
            }
            repairs += ClampMin(ref usage.expiresDay, 0, $"exchange.{label}TokenUsage[{usage.speciesId}].expiresDay", notes);
        }
        return repairs;
    }

    private static int NormalizeCatchHype(List<CatchHypeEntry> entries, List<string> notes)
    {
        if (entries == null) return 0;
        int repairs = 0;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int i = entries.Count - 1; i >= 0; i--)
        {
            var entry = entries[i];
            if (entry == null || string.IsNullOrWhiteSpace(entry.speciesId) || !seen.Add(entry.speciesId))
            {
                entries.RemoveAt(i);
                repairs++;
                notes.Add("Removed invalid or duplicate catch hype entry.");
                continue;
            }
            repairs += ClampMin(ref entry.capturedUnix, 0L, $"exchange.catchHype[{entry.speciesId}].capturedUnix", notes);
        }
        return repairs;
    }

    private static int NormalizeBrokerScarcity(List<BrokerScarcityEntry> entries, List<string> notes)
    {
        if (entries == null) return 0;
        int repairs = 0;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int i = entries.Count - 1; i >= 0; i--)
        {
            var entry = entries[i];
            if (entry == null || string.IsNullOrWhiteSpace(entry.speciesId) || !seen.Add(entry.speciesId))
            {
                entries.RemoveAt(i);
                repairs++;
                notes.Add("Removed invalid or duplicate broker scarcity entry.");
                continue;
            }
            repairs += ClampMin(ref entry.timesBrokered, 0, $"exchange.brokerScarcity[{entry.speciesId}].timesBrokered", notes);
        }
        return repairs;
    }

    private static int NormalizeBoostLists(PlayerSaveSection player, List<string> notes)
    {
        int repairs = 0;
        repairs += NormalizeFlyers(player.activeFlyers, notes);
        repairs += NormalizeWorkOrders(player.activeWorkOrders, notes);
        repairs += NormalizeLuckBoosts(player.activeFavorBoosts, notes);
        repairs += NormalizePremiumBoosts(player.activePremiumBoosts, notes);
        repairs += NormalizeJobMods(player.activeJobMods, notes);
        return repairs;
    }

    private static int NormalizeFlyers(List<FlyerBiasData> items, List<string> notes)
    {
        if (items == null) return 0;
        int repairs = 0;
        for (int i = items.Count - 1; i >= 0; i--)
        {
            var entry = items[i];
            if (entry == null)
            {
                items.RemoveAt(i);
                repairs++;
                notes.Add("Removed null active flyer entry.");
                continue;
            }
            repairs += ClampMin(ref entry.bonus, 0f, "playerData.activeFlyers.bonus", notes);
            repairs += ClampMin(ref entry.expireUnix, 0L, "playerData.activeFlyers.expireUnix", notes);
        }
        return repairs;
    }

    private static int NormalizeWorkOrders(List<WorkOrderData> items, List<string> notes)
    {
        if (items == null) return 0;
        int repairs = 0;
        for (int i = items.Count - 1; i >= 0; i--)
        {
            var entry = items[i];
            if (entry == null)
            {
                items.RemoveAt(i);
                repairs++;
                notes.Add("Removed null active work order entry.");
                continue;
            }
            repairs += ClampMin(ref entry.bonus, 0f, "playerData.activeWorkOrders.bonus", notes);
            repairs += ClampMin(ref entry.expireUnix, 0L, "playerData.activeWorkOrders.expireUnix", notes);
        }
        return repairs;
    }

    private static int NormalizeLuckBoosts(List<LuckBoostData> items, List<string> notes)
    {
        if (items == null) return 0;
        int repairs = 0;
        for (int i = items.Count - 1; i >= 0; i--)
        {
            var entry = items[i];
            if (entry == null)
            {
                items.RemoveAt(i);
                repairs++;
                notes.Add("Removed null active favor boost entry.");
                continue;
            }
            repairs += ClampMin(ref entry.bonus, 0f, "playerData.activeFavorBoosts.bonus", notes);
            repairs += ClampMin(ref entry.expireUnix, 0L, "playerData.activeFavorBoosts.expireUnix", notes);
        }
        return repairs;
    }

    private static int NormalizePremiumBoosts(List<PremiumBoostData> items, List<string> notes)
    {
        if (items == null) return 0;
        int repairs = 0;
        for (int i = items.Count - 1; i >= 0; i--)
        {
            var entry = items[i];
            if (entry == null)
            {
                items.RemoveAt(i);
                repairs++;
                notes.Add("Removed null active premium boost entry.");
                continue;
            }
            repairs += ClampMin(ref entry.bonus, 0f, "playerData.activePremiumBoosts.bonus", notes);
            repairs += ClampMin(ref entry.expireUnix, 0L, "playerData.activePremiumBoosts.expireUnix", notes);
        }
        return repairs;
    }

    private static int NormalizeJobMods(List<JobGlobalMod> items, List<string> notes)
    {
        if (items == null) return 0;
        int repairs = 0;
        for (int i = items.Count - 1; i >= 0; i--)
        {
            var entry = items[i];
            if (entry == null)
            {
                items.RemoveAt(i);
                repairs++;
                notes.Add("Removed null active job modifier entry.");
                continue;
            }
            repairs += ClampMin(ref entry.multiplier, 0f, "playerData.activeJobMods.multiplier", notes);
            repairs += ClampMin(ref entry.expiresUnix, 0L, "playerData.activeJobMods.expiresUnix", notes);
        }
        return repairs;
    }

    private static int NormalizeJobStorageUpgrades(List<JobStorageUpgrade> upgrades, List<string> notes)
    {
        if (upgrades == null) return 0;
        int repairs = 0;
        var seen = new HashSet<JobType>();
        for (int i = upgrades.Count - 1; i >= 0; i--)
        {
            var entry = upgrades[i];
            if (entry == null || !seen.Add(entry.job))
            {
                upgrades.RemoveAt(i);
                repairs++;
                notes.Add("Removed invalid or duplicate job storage upgrade entry.");
                continue;
            }
            repairs += ClampMin(ref entry.extra, 0, $"playerData.jobStorageUpgrades[{entry.job}].extra", notes);
        }
        return repairs;
    }

    private static int NormalizeJobAssignments(List<JobAssignment> assignments, List<string> notes)
    {
        if (assignments == null) return 0;
        int repairs = 0;
        var seen = new HashSet<JobType>();
        for (int i = assignments.Count - 1; i >= 0; i--)
        {
            var entry = assignments[i];
            if (entry == null || !seen.Add(entry.job))
            {
                assignments.RemoveAt(i);
                repairs++;
                notes.Add("Removed invalid or duplicate job assignment entry.");
                continue;
            }
            repairs += EnsureList(ref entry.workerIds, $"playerData.jobAssignments[{entry.job}].workerIds", notes);
            repairs += RemoveBlankStrings(entry.workerIds, $"playerData.jobAssignments[{entry.job}].workerIds", notes);
            repairs += RemoveDuplicateStrings(entry.workerIds, $"playerData.jobAssignments[{entry.job}].workerIds", notes);
        }
        return repairs;
    }

    private static int NormalizeJobProgress(List<JobProgress> progressEntries, List<string> notes)
    {
        if (progressEntries == null) return 0;
        int repairs = 0;
        var seen = new HashSet<JobType>();
        for (int i = progressEntries.Count - 1; i >= 0; i--)
        {
            var entry = progressEntries[i];
            if (entry == null || !seen.Add(entry.job))
            {
                progressEntries.RemoveAt(i);
                repairs++;
                notes.Add("Removed invalid or duplicate job progress entry.");
                continue;
            }
            repairs += ClampMin(ref entry.level, 1, $"playerData.jobProgress[{entry.job}].level", notes);
            repairs += ClampMin(ref entry.currentXP, 0, $"playerData.jobProgress[{entry.job}].currentXP", notes);
            repairs += ClampMin(ref entry.maxXPForLevel, 1, $"playerData.jobProgress[{entry.job}].maxXPForLevel", notes);
        }
        return repairs;
    }

    private static int GetResourceVectorSize()
    {
        int need = 0;
        foreach (ResourceType t in Enum.GetValues(typeof(ResourceType)))
            need = Mathf.Max(need, (int)t + 1);
        return need;
    }

    private static int EnsureResourceVectorSize(List<int> values, string path, List<string> notes)
    {
        if (values == null) return 0;
        int repairs = 0;
        int need = GetResourceVectorSize();
        while (values.Count < need)
        {
            values.Add(0);
            repairs++;
            notes.Add($"Expanded {path} to resource enum size.");
        }
        return repairs;
    }

    private static int EnsureLifetimeNotBelowCurrent(ResourceSaveSection resources, List<string> notes)
    {
        if (resources?.resourceCounts == null || resources.lifetimeResourceCollected == null) return 0;
        int repairs = 0;
        int max = Mathf.Min(resources.resourceCounts.Count, resources.lifetimeResourceCollected.Count);
        for (int i = 0; i < max; i++)
        {
            if (resources.lifetimeResourceCollected[i] < resources.resourceCounts[i])
            {
                resources.lifetimeResourceCollected[i] = resources.resourceCounts[i];
                repairs++;
                notes.Add("Raised lifetime resource count to match current resource total.");
            }
        }
        return repairs;
    }

    private static int EnsureSection<T>(ref T value, Func<T> create, string path, List<string> notes) where T : class
    {
        if (value != null) return 0;
        value = create();
        notes.Add($"Initialized missing section: {path}.");
        return 1;
    }

    private static int EnsureList<T>(ref List<T> list, string path, List<string> notes)
    {
        if (list != null) return 0;
        list = new List<T>();
        notes.Add($"Initialized missing list: {path}.");
        return 1;
    }

    private static int EnsureArraySize(ref float[] values, int size, string path, List<string> notes)
    {
        if (values != null && values.Length == size) return 0;
        var resized = new float[size];
        if (values != null) Array.Copy(values, resized, Mathf.Min(values.Length, size));
        values = resized;
        notes.Add($"Resized array {path} to {size}.");
        return 1;
    }

    private static int EnsureArraySize(ref long[] values, int size, string path, List<string> notes)
    {
        if (values != null && values.Length == size) return 0;
        var resized = new long[size];
        if (values != null) Array.Copy(values, resized, Mathf.Min(values.Length, size));
        values = resized;
        notes.Add($"Resized array {path} to {size}.");
        return 1;
    }

    private static int RemoveBlankStrings(List<string> values, string path, List<string> notes)
    {
        if (values == null) return 0;
        int repairs = 0;
        for (int i = values.Count - 1; i >= 0; i--)
        {
            if (!string.IsNullOrWhiteSpace(values[i])) continue;
            values.RemoveAt(i);
            repairs++;
            notes.Add($"Removed blank string entry from {path}.");
        }
        return repairs;
    }

    private static int RemoveDuplicateStrings(List<string> values, string path, List<string> notes)
    {
        if (values == null) return 0;
        int repairs = 0;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int i = values.Count - 1; i >= 0; i--)
        {
            if (seen.Add(values[i])) continue;
            values.RemoveAt(i);
            repairs++;
            notes.Add($"Removed duplicate string entry from {path}.");
        }
        return repairs;
    }

    private static int RemoveDuplicateEnums<T>(List<T> values, string path, List<string> notes)
    {
        if (values == null) return 0;
        int repairs = 0;
        var seen = new HashSet<T>();
        for (int i = values.Count - 1; i >= 0; i--)
        {
            if (seen.Add(values[i])) continue;
            values.RemoveAt(i);
            repairs++;
            notes.Add($"Removed duplicate enum entry from {path}.");
        }
        return repairs;
    }

    private static int ClampNonNegative(List<int> values, string path, List<string> notes)
    {
        if (values == null) return 0;
        int repairs = 0;
        for (int i = 0; i < values.Count; i++)
        {
            if (values[i] >= 0) continue;
            values[i] = 0;
            repairs++;
            notes.Add($"Clamped negative value in {path}[{i}].");
        }
        return repairs;
    }

    private static int ClampNonNegative(long[] values, string path, List<string> notes)
    {
        if (values == null) return 0;
        int repairs = 0;
        for (int i = 0; i < values.Length; i++)
        {
            if (values[i] >= 0) continue;
            values[i] = 0;
            repairs++;
            notes.Add($"Clamped negative value in {path}[{i}].");
        }
        return repairs;
    }

    private static int Clamp01(float[] values, string path, List<string> notes)
    {
        if (values == null) return 0;
        int repairs = 0;
        for (int i = 0; i < values.Length; i++)
        {
            float clamped = Mathf.Clamp01(values[i]);
            if (Mathf.Approximately(clamped, values[i])) continue;
            values[i] = clamped;
            repairs++;
            notes.Add($"Clamped out-of-range percentage in {path}[{i}].");
        }
        return repairs;
    }

    private static int ClampMin(ref int value, int min, string path, List<string> notes)
    {
        if (value >= min) return 0;
        value = min;
        notes.Add($"Clamped {path} to minimum {min}.");
        return 1;
    }

    private static int ClampMax(ref int value, int max, string path, List<string> notes)
    {
        if (value <= max) return 0;
        value = max;
        notes.Add($"Clamped {path} to maximum {max}.");
        return 1;
    }

    private static int ClampMin(ref long value, long min, string path, List<string> notes)
    {
        if (value >= min) return 0;
        value = min;
        notes.Add($"Clamped {path} to minimum {min}.");
        return 1;
    }

    private static int ClampMin(ref float value, float min, string path, List<string> notes)
    {
        if (value >= min) return 0;
        value = min;
        notes.Add($"Clamped {path} to minimum {min}.");
        return 1;
    }

    private static int ClampOptionalIndex(ref int value, string path, List<string> notes)
    {
        if (value >= -1) return 0;
        value = -1;
        notes.Add($"Clamped {path} to minimum -1.");
        return 1;
    }

    private static int ClampRuntime(ref int value, int min)
    {
        if (value >= min) return 0;
        value = min;
        return 1;
    }

    private static int ClampRuntime(ref long value, long min)
    {
        if (value >= min) return 0;
        value = min;
        return 1;
    }

    private static int ClampRuntime(ref float value, float min)
    {
        if (value >= min) return 0;
        value = min;
        return 1;
    }
}