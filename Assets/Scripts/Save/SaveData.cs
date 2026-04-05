using System;
using System.Collections.Generic;

[Serializable]
public sealed class SaveData
{
    public int saveVersion = SaveMigrationManager.CURRENT_SAVE_VERSION;
    public PlayerSaveSection playerData = new PlayerSaveSection();
    public ResourceSaveSection resourceData = new ResourceSaveSection();
    public BitlingCollectionSaveSection bitlingCollectionData = new BitlingCollectionSaveSection();
    public TeamSaveSection teamData = new TeamSaveSection();
    public IdleSaveSection idleData = new IdleSaveSection();
    public AchievementSaveSection achievementData = new AchievementSaveSection();
    public CodexSaveSection codexData = new CodexSaveSection();
    public ExchangeSystemSaveSection exchangeData = new ExchangeSystemSaveSection();
    public WorldEventSystemSaveSection worldEventData = new WorldEventSystemSaveSection();
    public SettingsSaveSection settingsData = new SettingsSaveSection();
    public TutorialSaveSection tutorialData = new TutorialSaveSection();
    public TitleSystemSaveSection titleData = new TitleSystemSaveSection();
    public JobRuntimeSystemSaveSection jobRuntimeData = new JobRuntimeSystemSaveSection();
}

[Serializable]
internal sealed class LegacyCombinedSaveRoot
{
    public int version = 1;
    public PlayerManager player;
    public List<string> tutorialCompleted = new List<string>();
    public JobRuntimeSave jobRuntime;
    public TitleSaveData titles;
    public WorldEventSaveData worldEvents;
    public ExchangeSaveData exchange;
}

[Serializable]
public sealed class PlayerSaveSection
{
    public string playerId;
    public string playerName;
    public int tapLevel;
    public int idleLevel;
    public int battleXPLevel;
    public int critLevel;
    public int autoTapLevel;
    public int creditGainLevel;
    public int offlineLevel;
    public int winStreak;
    public int encounterPoints;
    public int encounterMax = 50;
    public int encounterCost = 1;
    public int lastEncounterResetYMD;
    public int dailyBonusDay = 1;
    public int lastDailyClaimDayIndex = -1;
    public int cheatInvalidAttempts;
    public long cheatLockedUntilUnix;
    public int forcePremiumCapturesRemaining;
    public string trainingMonsterId;
    public int trainingMonsterLevel;
    public int pendingIdleXP;
    public bool hasSeenStory;
    public bool hasChosenStarter;
    public long lastClosedUnix;
    public long lastSavedUnix;
    public long jobsOfflineLastUnix;
    public long energyLastUnix;
    public float energyRemainderSecs;
    public int encountersSinceBoss;
    public int bossEveryN = 10;
    public string lastBossId;
    public int promotionRank = 1;
    public int promotionXP;
    public FieldOpsStats fieldOps = new FieldOpsStats();
    public SeedState seedState = new SeedState();
    public List<FlyerBiasData> activeFlyers = new List<FlyerBiasData>();
    public List<WorkOrderData> activeWorkOrders = new List<WorkOrderData>();
    public List<JobGlobalMod> activeJobMods = new List<JobGlobalMod>();
    public List<LuckBoostData> activeFavorBoosts = new List<LuckBoostData>();
    public List<PremiumBoostData> activePremiumBoosts = new List<PremiumBoostData>();
    public List<JobStorageUpgrade> jobStorageUpgrades = new List<JobStorageUpgrade>();
    public List<JobAssignment> jobAssignments = new List<JobAssignment>();
    public List<JobProgress> jobProgress = new List<JobProgress>();
}

[Serializable]
public sealed class ResourceSaveSection
{
    public int credits;
    public bool creditsMigratedToResourceBank;
    public List<int> resourceCounts = new List<int>();
    public List<int> lifetimeResourceCollected = new List<int>();
}

[Serializable]
public sealed class BitlingCollectionSaveSection
{
    public List<OwnedMonsterData> owned = new List<OwnedMonsterData>();
    public List<string> ownedIds = new List<string>();
    public List<string> favoriteMonsterIds = new List<string>();
    public List<string> unlockedPacks = new List<string>();
    public List<PreferredVariantKV> preferredVariants = new List<PreferredVariantKV>();
}

[Serializable]
public sealed class TeamSaveSection
{
    public List<OwnedMonsterData> activeTeam = new List<OwnedMonsterData>();
}

[Serializable]
public sealed class IdleSaveSection
{
    public List<string> idleTeamOwnedUIDs = new List<string>();
    public List<AutoBattleLogEntry> autoBattleLogArchive = new List<AutoBattleLogEntry>();
}

[Serializable]
public sealed class AchievementSaveSection
{
    public List<AchievementProgressData> achievements = new List<AchievementProgressData>();
}

[Serializable]
public sealed class CodexSaveSection
{
    public List<string> discoveredMonsterIds = new List<string>();
    public List<MonsterType> seenTypes = new List<MonsterType>();
}

[Serializable]
public sealed class ExchangeSystemSaveSection
{
    public ExchangeSaveData exchangeState = new ExchangeSaveData();
}

[Serializable]
public sealed class WorldEventSystemSaveSection
{
    public WorldEventSaveData worldEvents = new WorldEventSaveData();
    public List<string> archivedEventIds = new List<string>();
}

[Serializable]
public sealed class SettingsSaveSection
{
    public SettingsState settings = new SettingsState();
    public List<string> unlockedFeatureIds = new List<string>();
    public List<JobType> unlockedJobSites = new List<JobType>();
    public bool diagnosticsUnlocked;
}

[Serializable]
public sealed class TutorialSaveSection
{
    public List<string> completed = new List<string>();
}

[Serializable]
public sealed class TitleSystemSaveSection
{
    public TitleSaveData titles = new TitleSaveData();
}

[Serializable]
public sealed class JobRuntimeSystemSaveSection
{
    public JobRuntimeSave jobRuntime = new JobRuntimeSave();
}

public static class SaveDataMapper
{
    public static SaveData FromRuntime(
        PlayerManager data,
        ICollection<string> tutorialCompleted,
        JobRuntimeSave jobRuntime,
        TitleSaveData titles,
        WorldEventSaveData worldEvents,
        ExchangeSaveData exchange)
    {
        data ??= new PlayerManager();

        return new SaveData
        {
            saveVersion = SaveMigrationManager.CURRENT_SAVE_VERSION,
            playerData = new PlayerSaveSection
            {
                playerId = data.playerId,
                playerName = data.playerName,
                tapLevel = data.tapLevel,
                idleLevel = data.idleLevel,
                battleXPLevel = data.battleXPLevel,
                critLevel = data.critLevel,
                autoTapLevel = data.autoTapLevel,
                creditGainLevel = data.creditGainLevel,
                offlineLevel = data.offlineLevel,
                winStreak = data.winStreak,
                encounterPoints = data.encounterPoints,
                encounterMax = data.encounterMax,
                encounterCost = data.encounterCost,
                lastEncounterResetYMD = data.lastEncounterResetYMD,
                dailyBonusDay = data.dailyBonusDay,
                lastDailyClaimDayIndex = data.lastDailyClaimDayIndex,
                cheatInvalidAttempts = data.cheatInvalidAttempts,
                cheatLockedUntilUnix = data.cheatLockedUntilUnix,
                forcePremiumCapturesRemaining = data.forcePremiumCapturesRemaining,
                trainingMonsterId = data.trainingMonsterId,
                trainingMonsterLevel = data.trainingMonsterLevel,
                pendingIdleXP = data.pendingIdleXP,
                hasSeenStory = data.hasSeenStory,
                hasChosenStarter = data.hasChosenStarter,
                lastClosedUnix = data.lastClosedUnix,
                lastSavedUnix = data.lastSavedUnix,
                jobsOfflineLastUnix = data.jobsOfflineLastUnix,
                energyLastUnix = data.energyLastUnix,
                energyRemainderSecs = data.energyRemainderSecs,
                encountersSinceBoss = data.encountersSinceBoss,
                bossEveryN = data.bossEveryN,
                lastBossId = data.lastBossId,
                promotionRank = data.promotionRank,
                promotionXP = data.promotionXP,
                fieldOps = data.fieldOps,
                seedState = data.seedState,
                activeFlyers = data.activeFlyers,
                activeWorkOrders = data.activeWorkOrders,
                activeJobMods = data.activeJobMods,
                activeFavorBoosts = data.activeFavorBoosts,
                activePremiumBoosts = data.activePremiumBoosts,
                jobStorageUpgrades = data.jobStorageUpgrades,
                jobAssignments = data.jobAssignments,
                jobProgress = data.jobProgress
            },
            resourceData = new ResourceSaveSection
            {
                credits = data.credits,
                creditsMigratedToResourceBank = data.creditsMigratedToResourceBank,
                resourceCounts = data.resourceCounts,
                lifetimeResourceCollected = data.lifetimeResourceCollected
            },
            bitlingCollectionData = new BitlingCollectionSaveSection
            {
                owned = data.owned,
                ownedIds = data.ownedIdsList,
                favoriteMonsterIds = data.favoriteMonsterIdsList,
                unlockedPacks = data.unlockedPacks,
                preferredVariants = data.preferredVariants
            },
            teamData = new TeamSaveSection
            {
                activeTeam = data.team
            },
            idleData = new IdleSaveSection
            {
                idleTeamOwnedUIDs = data.idleTeamOwnedUIDs,
                autoBattleLogArchive = data.autoBattleLogArchive
            },
            achievementData = new AchievementSaveSection
            {
                achievements = data.achievements
            },
            codexData = new CodexSaveSection
            {
                discoveredMonsterIds = data.discoveredMonsterIdsList,
                seenTypes = data.seenTypesList
            },
            exchangeData = new ExchangeSystemSaveSection
            {
                exchangeState = exchange
            },
            worldEventData = new WorldEventSystemSaveSection
            {
                worldEvents = worldEvents
            },
            settingsData = new SettingsSaveSection
            {
                settings = data.settings,
                unlockedFeatureIds = data.unlockedFeatureIds,
                unlockedJobSites = data.unlockedJobSitesList,
                diagnosticsUnlocked = data.diagnosticsUnlocked
            },
            tutorialData = new TutorialSaveSection
            {
                completed = tutorialCompleted != null ? new List<string>(tutorialCompleted) : new List<string>()
            },
            titleData = new TitleSystemSaveSection
            {
                titles = titles
            },
            jobRuntimeData = new JobRuntimeSystemSaveSection
            {
                jobRuntime = jobRuntime
            }
        };
    }

    internal static SaveData FromLegacyRoot(LegacyCombinedSaveRoot legacy)
    {
        legacy ??= new LegacyCombinedSaveRoot();
        return FromRuntime(
            legacy.player,
            legacy.tutorialCompleted,
            legacy.jobRuntime,
            legacy.titles,
            legacy.worldEvents,
            legacy.exchange);
    }

    public static PlayerManager ToPlayerManager(SaveData saveData)
    {
        saveData ??= new SaveData();
        var player = saveData.playerData ?? new PlayerSaveSection();
        var resources = saveData.resourceData ?? new ResourceSaveSection();
        var collection = saveData.bitlingCollectionData ?? new BitlingCollectionSaveSection();
        var team = saveData.teamData ?? new TeamSaveSection();
        var idle = saveData.idleData ?? new IdleSaveSection();
        var achievements = saveData.achievementData ?? new AchievementSaveSection();
        var codex = saveData.codexData ?? new CodexSaveSection();
        var settings = saveData.settingsData ?? new SettingsSaveSection();

        return new PlayerManager
        {
            playerId = player.playerId,
            playerName = player.playerName,
            tapLevel = player.tapLevel,
            idleLevel = player.idleLevel,
            battleXPLevel = player.battleXPLevel,
            critLevel = player.critLevel,
            autoTapLevel = player.autoTapLevel,
            creditGainLevel = player.creditGainLevel,
            offlineLevel = player.offlineLevel,
            winStreak = player.winStreak,
            encounterPoints = player.encounterPoints,
            encounterMax = player.encounterMax,
            encounterCost = player.encounterCost,
            lastEncounterResetYMD = player.lastEncounterResetYMD,
            dailyBonusDay = player.dailyBonusDay,
            lastDailyClaimDayIndex = player.lastDailyClaimDayIndex,
            cheatInvalidAttempts = player.cheatInvalidAttempts,
            cheatLockedUntilUnix = player.cheatLockedUntilUnix,
            forcePremiumCapturesRemaining = player.forcePremiumCapturesRemaining,
            trainingMonsterId = player.trainingMonsterId,
            trainingMonsterLevel = player.trainingMonsterLevel,
            pendingIdleXP = player.pendingIdleXP,
            hasSeenStory = player.hasSeenStory,
            hasChosenStarter = player.hasChosenStarter,
            lastClosedUnix = player.lastClosedUnix,
            lastSavedUnix = player.lastSavedUnix,
            jobsOfflineLastUnix = player.jobsOfflineLastUnix,
            energyLastUnix = player.energyLastUnix,
            energyRemainderSecs = player.energyRemainderSecs,
            encountersSinceBoss = player.encountersSinceBoss,
            bossEveryN = player.bossEveryN,
            lastBossId = player.lastBossId,
            promotionRank = player.promotionRank,
            promotionXP = player.promotionXP,
            fieldOps = player.fieldOps,
            seedState = player.seedState,
            activeFlyers = player.activeFlyers,
            activeWorkOrders = player.activeWorkOrders,
            activeJobMods = player.activeJobMods,
            activeFavorBoosts = player.activeFavorBoosts,
            activePremiumBoosts = player.activePremiumBoosts,
            jobStorageUpgrades = player.jobStorageUpgrades,
            jobAssignments = player.jobAssignments,
            jobProgress = player.jobProgress,
            credits = resources.credits,
            creditsMigratedToResourceBank = resources.creditsMigratedToResourceBank,
            resourceCounts = resources.resourceCounts,
            lifetimeResourceCollected = resources.lifetimeResourceCollected,
            owned = collection.owned,
            ownedIdsList = collection.ownedIds,
            favoriteMonsterIdsList = collection.favoriteMonsterIds,
            unlockedPacks = collection.unlockedPacks,
            preferredVariants = collection.preferredVariants,
            team = team.activeTeam,
            idleTeamOwnedUIDs = idle.idleTeamOwnedUIDs,
            autoBattleLogArchive = idle.autoBattleLogArchive,
            achievements = achievements.achievements,
            discoveredMonsterIdsList = codex.discoveredMonsterIds,
            seenTypesList = codex.seenTypes,
            settings = settings.settings,
            unlockedFeatureIds = settings.unlockedFeatureIds,
            unlockedJobSitesList = settings.unlockedJobSites,
            diagnosticsUnlocked = settings.diagnosticsUnlocked
        };
    }

    public static List<string> GetTutorialFlags(SaveData saveData)
    {
        return saveData?.tutorialData?.completed ?? new List<string>();
    }

    public static JobRuntimeSave GetJobRuntime(SaveData saveData)
    {
        return saveData?.jobRuntimeData?.jobRuntime;
    }

    public static TitleSaveData GetTitles(SaveData saveData)
    {
        return saveData?.titleData?.titles;
    }

    public static WorldEventSaveData GetWorldEvents(SaveData saveData)
    {
        return saveData?.worldEventData?.worldEvents;
    }

    public static ExchangeSaveData GetExchange(SaveData saveData)
    {
        return saveData?.exchangeData?.exchangeState;
    }
}