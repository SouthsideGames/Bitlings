using System;
using System.Collections.Generic;


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
public class LureBiasData
{
    public MonsterType type;
    public float bonus;
    public long expireUnix;
}

[Serializable]
public class CaptureBandData
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
    public int encountersInitiated;      // how many wild battles were started
    public int captureAttempts;          // how many capture rolls happened
    public int capturesSuccessful;       // how many succeeded
    public int rareBitlingsFound;        // successful captures of Rare/Epic/Legendary/Mythic
    public int shinyDiscoveries;         // shiny captures
    public int riftStabilizations;       // boss defeats (or other rift events)
    public int longestCaptureStreak;     // best streak of consecutive successes
    public int currentCaptureStreak;     // current streak (resets on fail)

    public System.Collections.Generic.List<string> recentHighlights =
        new System.Collections.Generic.List<string>();
}

[Serializable]
public class PlayerManager
{
    public string playerId = null;
    public string playerName = null;


    public List<OwnedMonsterData> team = new List<OwnedMonsterData>();
    public List<OwnedMonsterData> owned = new List<OwnedMonsterData>();
    public List<LureBiasData> activeLures = new List<LureBiasData>();
    public List<CaptureBandData> activeCaptureBands = new List<CaptureBandData>();
    public List<JobGlobalMod> activeJobMods = new List<JobGlobalMod>();
    public List<LuckBoostData> activeLuckBoosts = new List<LuckBoostData>();
    public List<JobStorageUpgrade> jobStorageUpgrades = new List<JobStorageUpgrade>();
    public List<ShinyBoostData> activeShinyBoosts = new List<ShinyBoostData>();
    public FieldOpsStats fieldOps = new FieldOpsStats();

    public List<string> ownedIdsList = new List<string>();
    [NonSerialized] public HashSet<string> ownedIds = new HashSet<string>();

    public List<string> favoriteMonsterIdsList = new List<string>();
    [NonSerialized] public HashSet<string> favoriteMonsterIds = new HashSet<string>();

    public int coins = 0;
    public List<int> resourceCounts = new List<int>();
    public List<string> unlockedPacks = new List<string>();

    public int tapLevel = 0;
    public int idleLevel = 0;
    public int battleXPLevel = 0;
    public int critLevel;
    public int autoTapLevel;
    public int coinGainLevel;
    public int offlineLevel;
    public int winStreak;
    public int encounterPoints = 0;
    public int encounterMax = 50;
    public int encounterCost = 5;
    public int lastEncounterResetYMD = 0;
    public int dailyBonusDay = 1;            // 1-based day in the current cycle
    public int lastDailyClaimDayIndex = -1;

    public string trainingMonsterId = null;
    public int trainingMonsterLevel = 0;
    public int pendingIdleXP = 0;
    public bool hasSeenStory = false;
    public long lastClosedUnix = 0;
    public long lastSavedUnix = 0;

    public int encountersSinceBoss = 0;  
    public int bossEveryN = 10;        
    public string lastBossId = null;
    public SettingsState settings;
    public List<JobAssignment> jobAssignments = new List<JobAssignment>();
    public List<JobProgress> jobProgress = new List<JobProgress>();

    public bool hasChosenStarter;

    public List<MonsterType> seenTypesList = new List<MonsterType>();
    [NonSerialized] public HashSet<MonsterType> seenTypes = new HashSet<MonsterType>();

    public List<JobType> unlockedJobSitesList = new List<JobType>();
    [NonSerialized] public HashSet<JobType> unlockedJobSites = new HashSet<JobType>();

    public void EnsureTransientSets()
    {
        if (ownedIds == null)
            ownedIds = new HashSet<string>(ownedIdsList ?? new List<string>());

        if (favoriteMonsterIds == null)
            favoriteMonsterIds = new HashSet<string>(favoriteMonsterIdsList ?? new List<string>());

        activeLures         ??= new List<LureBiasData>();
        activeCaptureBands  ??= new List<CaptureBandData>();
        activeLuckBoosts    ??= new List<LuckBoostData>();
        activeShinyBoosts   ??= new List<ShinyBoostData>();
        jobStorageUpgrades  ??= new List<JobStorageUpgrade>();
        team                ??= new List<OwnedMonsterData>();
        owned               ??= new List<OwnedMonsterData>();

        if (seenTypes == null)
            seenTypes = new HashSet<MonsterType>(seenTypesList ?? new List<MonsterType>());
        if (unlockedJobSites == null)
            unlockedJobSites = new HashSet<JobType>(unlockedJobSitesList ?? new List<JobType>());

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
