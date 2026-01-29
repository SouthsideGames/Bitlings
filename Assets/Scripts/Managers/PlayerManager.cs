using System;
using System.Collections.Generic;

[Serializable]
public class AchievementProgressData
{
    public string id;
    public int value;
    public bool unlocked;
    public long unlockedUnix;
    public bool seen; 
}


[Serializable]
public class OwnedMonsterData
{
    public string monsterId;
    public int level = 1;
    public int currentXP = 0;
    public int currentHP = -1;
    public long lastHPUnix = 0;
    public int flatAtkBonus = 0;
    public bool isTraining = false;
    public long trainingLastUnix = 0;
    public int pendingLevels = 0;
    public int lastLevelClaimDay = -1;
    public string ownedUID;
    public bool isShiny = false;
    public int shinyTier = 0;
    public TrainingBonus trainingBonus = new TrainingBonus();
    public bool autoApply = false;
    public int autoApplyTargetLevel = 0;
    public string lastBucketId = null;
    public int unspentStatPoints = 0;
    
}



[Serializable]
public class JobAssignment
{
    public JobType job;
    public List<string> workerIds = new List<string>();
}

[Serializable]
public class FlyerBiasData
{
    public MonsterType type;
    public float bonus;
    public long expireUnix;
}

[Serializable]
public class WorkOrderData
{
    public float bonus;
    public long expireUnix;
}

[Serializable]
public class LuckBoostData
{
    public float bonus;
    public long  expireUnix;
}

[Serializable]
public class JobStorageUpgrade
{
    public JobType job;
    public int extra;
}

[Serializable]
public class ShinyBoostData
{
    public float bonus;
    public long expireUnix;
}

[Serializable]
public class FieldOpsStats
{
    public int encountersInitiated;     
    public int captureAttempts;       
    public int capturesSuccessful;       
    public int rareBitlingsFound;      
    public int shinyDiscoveries;       
    public int riftStabilizations;    
    public int longestCaptureStreak;    
    public int currentCaptureStreak;     

    public List<string> recentHighlights =
        new List<string>();
}

[Serializable]
public class PreferredVariantKV
{
    public string monsterId;
    public string preferredOwnedUid; 
}

[Serializable]
public class PlayerManager
{
    public string playerId = null;
    public string playerName = null;


    public List<OwnedMonsterData> team = new List<OwnedMonsterData>();
    public List<OwnedMonsterData> owned = new List<OwnedMonsterData>();
    public List<FlyerBiasData> activeFlyers = new List<FlyerBiasData>();
    public List<WorkOrderData> activeWorkOrders = new List<WorkOrderData>();
    public List<JobGlobalMod> activeJobMods = new List<JobGlobalMod>();
    public List<LuckBoostData> activeFavorBoosts = new List<LuckBoostData>();
    public List<JobStorageUpgrade> jobStorageUpgrades = new List<JobStorageUpgrade>();
    public List<ShinyBoostData> activeShinyBoosts = new List<ShinyBoostData>();
    public List<PreferredVariantKV> preferredVariants = new();
    public FieldOpsStats fieldOps = new FieldOpsStats();

    public List<string> ownedIdsList = new List<string>();
    [NonSerialized] public HashSet<string> ownedIds = new HashSet<string>();

    public List<string> favoriteMonsterIdsList = new List<string>();
    [NonSerialized] public HashSet<string> favoriteMonsterIds = new HashSet<string>();

    public List<string> discoveredMonsterIdsList = new List<string>();
    [NonSerialized] public HashSet<string> discoveredMonsterIds = new HashSet<string>();

    public List<AchievementProgressData> achievements = new List<AchievementProgressData>();
    [NonSerialized] public Dictionary<string, AchievementProgressData> achievementMap
        = new Dictionary<string, AchievementProgressData>(StringComparer.Ordinal);

    public int credits = 0;
    public List<int> resourceCounts = new List<int>();
    public List<string> unlockedPacks = new List<string>();

    public int tapLevel = 0;
    public int idleLevel = 0;
    public int battleXPLevel = 0;
    public int critLevel;
    public int autoTapLevel;
    public int creditGainLevel;
    public int offlineLevel;
    public int winStreak;
    public int encounterPoints = 0;
    public int encounterMax = 50;
    public int encounterCost = 5;
    public int lastEncounterResetYMD = 0;
    public int dailyBonusDay = 1;         
    public int lastDailyClaimDayIndex = -1;
    public int cheatInvalidAttempts;
    public long cheatLockedUntilUnix;
    public int forceShinyCapturesRemaining = 0;
    public string trainingMonsterId = null;
    public int trainingMonsterLevel = 0;
    public int pendingIdleXP = 0;
    public bool hasSeenStory = false;
    public long lastClosedUnix = 0;
    public long lastSavedUnix = 0;
    public long energyLastUnix;
    public float energyRemainderSecs;

    public int encountersSinceBoss = 0;  
    public int bossEveryN = 10;        
    public string lastBossId = null;
    public SettingsState settings;
    public List<JobAssignment> jobAssignments = new List<JobAssignment>();
    public List<JobProgress> jobProgress = new List<JobProgress>();

    public bool hasChosenStarter;
    public bool diagnosticsUnlocked = false;

    public List<MonsterType> seenTypesList = new List<MonsterType>();
    [NonSerialized] public HashSet<MonsterType> seenTypes = new HashSet<MonsterType>();

    public List<JobType> unlockedJobSitesList = new List<JobType>();
    [NonSerialized] public HashSet<JobType> unlockedJobSites = new HashSet<JobType>();

    public void EnsureTransientSets()
    {
        // Ensure list mirrors exist (these are what JsonUtility actually persists)
        ownedIdsList ??= new List<string>();
        favoriteMonsterIdsList ??= new List<string>();
        discoveredMonsterIdsList ??= new List<string>();
        seenTypesList ??= new List<MonsterType>();
        unlockedJobSitesList ??= new List<JobType>();

        // Ensure transient sets exist (runtime only)
        ownedIds ??= new HashSet<string>();
        favoriteMonsterIds ??= new HashSet<string>();
        discoveredMonsterIds ??= new HashSet<string>();
        seenTypes ??= new HashSet<MonsterType>();
        unlockedJobSites ??= new HashSet<JobType>();

        // ALWAYS resync sets from lists (authoritative after load)
        ownedIds.Clear();
        for (int i = 0; i < ownedIdsList.Count; i++)
        {
            var id = ownedIdsList[i];
            if (!string.IsNullOrEmpty(id)) ownedIds.Add(id);
        }

        favoriteMonsterIds.Clear();
        for (int i = 0; i < favoriteMonsterIdsList.Count; i++)
        {
            var id = favoriteMonsterIdsList[i];
            if (!string.IsNullOrEmpty(id)) favoriteMonsterIds.Add(id);
        }

        discoveredMonsterIds.Clear();
        for (int i = 0; i < discoveredMonsterIdsList.Count; i++)
        {
            var id = discoveredMonsterIdsList[i];
            if (!string.IsNullOrEmpty(id)) discoveredMonsterIds.Add(id);
        }

        seenTypes.Clear();
        for (int i = 0; i < seenTypesList.Count; i++)
            seenTypes.Add(seenTypesList[i]);

        unlockedJobSites.Clear();
        for (int i = 0; i < unlockedJobSitesList.Count; i++)
            unlockedJobSites.Add(unlockedJobSitesList[i]);

        // Ensure other collections exist
        activeFlyers ??= new List<FlyerBiasData>();
        activeWorkOrders ??= new List<WorkOrderData>();
        activeFavorBoosts ??= new List<LuckBoostData>();
        activeShinyBoosts ??= new List<ShinyBoostData>();
        jobStorageUpgrades ??= new List<JobStorageUpgrade>();
        team ??= new List<OwnedMonsterData>();
        owned ??= new List<OwnedMonsterData>();
        jobAssignments ??= new List<JobAssignment>();
        jobProgress ??= new List<JobProgress>();
        activeJobMods ??= new List<JobGlobalMod>();

        fieldOps ??= new FieldOpsStats();

        settings ??= new SettingsState();

        // Ensure ownedUIDs exist
        if (owned != null)
        {
            for (int i = 0; i < owned.Count; i++)
                if (owned[i] != null && string.IsNullOrEmpty(owned[i].ownedUID))
                    owned[i].ownedUID = Guid.NewGuid().ToString("N");
        }

        if (team != null)
        {
            for (int i = 0; i < team.Count; i++)
                if (team[i] != null && string.IsNullOrEmpty(team[i].ownedUID))
                    team[i].ownedUID = Guid.NewGuid().ToString("N");
        }

        // Expired shiny boosts cleanup (keeps existing behavior)
        if (activeShinyBoosts.Count > 0 && activeShinyBoosts[0] != null &&
            activeShinyBoosts[0].expireUnix <= SaveManager.NowUnix())
        {
            activeShinyBoosts.Clear();
        }
    }


    public int GetJobStorageExtra(JobType j)
    {
        if (jobStorageUpgrades == null) return 0;
        for (int i = 0; i < jobStorageUpgrades.Count; i++)
        {
            var e = jobStorageUpgrades[i];
            if (e != null && e.job == j) return e.extra;
        }
        return 0;
    }

    public void AddJobStorageExtra(JobType j, int amount)
    {
        if (amount <= 0) return;
        if (jobStorageUpgrades == null) jobStorageUpgrades = new List<JobStorageUpgrade>();
        for (int i = 0; i < jobStorageUpgrades.Count; i++)
        {
            var e = jobStorageUpgrades[i];
            if (e != null && e.job == j) { e.extra += amount; return; }
        }
        jobStorageUpgrades.Add(new JobStorageUpgrade { job = j, extra = amount });
    }

    public List<OwnedMonsterData> GetAllOwnedMonsters(bool includeTeam = true)
    {
        team  ??= new List<OwnedMonsterData>();
        owned ??= new List<OwnedMonsterData>();

        var result = new List<OwnedMonsterData>();
        var seen   = new HashSet<string>();

        // Owned first
        for (int i = 0; i < owned.Count; i++)
        {
            var m = owned[i];
            if (m == null || string.IsNullOrEmpty(m.monsterId))
                continue;

            if (!string.IsNullOrEmpty(m.ownedUID))
            {
                if (!seen.Add(m.ownedUID))
                    continue;
            }

            result.Add(m);
        }

        if (includeTeam)
        {
            for (int i = 0; i < team.Count; i++)
            {
                var t = team[i];
                if (t == null || string.IsNullOrEmpty(t.monsterId))
                    continue;

                if (!string.IsNullOrEmpty(t.ownedUID))
                {
                    if (!seen.Add(t.ownedUID))
                        continue;
                }

                result.Add(t);
            }
        }

        return result;
    }


}
