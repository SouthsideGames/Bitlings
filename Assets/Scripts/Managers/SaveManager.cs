using UnityEngine;
using System;
using System.IO;
using System.Text;
using System.Collections.Generic;

#region Job runtime sidecar (used by JobManager)

[Serializable]
public class JobRuntimeSite
{
    public JobType job;
    public float[] slotFatigue01;
    public long[]  slotCooldownUntilUnix;
}

[Serializable]
public class MonsterCooldownKV
{
    public string id;
    public long until;
}

[Serializable]
public class JobRuntimeSave
{
    public List<JobRuntimeSite> sites = new();
    public List<MonsterCooldownKV> cooldowns = new();
    public long savedAtUnix;
}

#endregion

public static class SaveManager
{
    public static PlayerManager Data;

    public static string SavePath      => Path.Combine(Application.persistentDataPath, "idle_mon_save.json");
    public static string BackupPath    => Path.Combine(Application.persistentDataPath, "idle_mon_save.bak");
    public static string JobRuntimePath=> Path.Combine(Application.persistentDataPath, "idle_job_runtime.json");

    // ─────────────────────────────────────────────────────────────────────────────
    // Lifecycle
    // ─────────────────────────────────────────────────────────────────────────────

    public static void LoadOrCreate()
    {
        EnsureFolder();

        if (!TryLoad(SavePath, out Data))
        {
            if (!TryLoad(BackupPath, out Data))
            {
                Data = NewFreshPlayer();
                Save();
            }
        }

        EnsureDefaults();
        EnsureTrainingDefaults();

        PruneExpiredCaptureBands(true);
        PruneExpiredLures(true);
        PruneExpiredLuckBoosts(true);
    }

    public static void Save()
    {
        try
        {
            // Keep mirrors in sync for JSON
            if (Data.ownedIds != null)
            {
                Data.ownedIdsList ??= new List<string>();
                Data.ownedIdsList.Clear();
                foreach (var id in Data.ownedIds)
                    if (!string.IsNullOrEmpty(id)) Data.ownedIdsList.Add(id);
                Data.ownedIdsList.Sort(StringComparer.Ordinal);
            }

            if (Data.seenTypes != null)
            {
                Data.seenTypesList ??= new List<MonsterType>();
                Data.seenTypesList.Clear();
                foreach (var t in Data.seenTypes) Data.seenTypesList.Add(t);
            }

            if (Data.unlockedJobSites != null)
            {
                Data.unlockedJobSitesList ??= new List<JobType>();
                Data.unlockedJobSitesList.Clear();
                foreach (var j in Data.unlockedJobSites) Data.unlockedJobSitesList.Add(j);
            }

            // Update last saved time safely (never go backwards far)
            long now = NowUnix();
            if (Data.lastSavedUnix > 0 && now + 300 < Data.lastSavedUnix) now = Data.lastSavedUnix;
            Data.lastSavedUnix = Math.Max(Data.lastSavedUnix, now);

            string json = JsonUtility.ToJson(Data, prettyPrint: true);
            AtomicWrite(SavePath, json);
            try { File.Copy(SavePath, BackupPath, overwrite: true); } catch { }
        }
        catch (Exception e)
        {
            Debug.LogError($"SaveManager.Save failed: {e.Message}");
        }
    }

    public static void ClearAll()
    {
        try { if (File.Exists(SavePath)) File.Delete(SavePath); } catch { }
        try { if (File.Exists(BackupPath)) File.Delete(BackupPath); } catch { }
        try { if (File.Exists(JobRuntimePath)) File.Delete(JobRuntimePath); } catch { }

        Data = NewFreshPlayer();
        Save();

        GameEvents.OnJobsChanged?.Invoke();
        if (JobManager.I)
        {
            JobManager.I.LoadAssignmentsFromSave();
            JobManager.I.ProcessOfflineAllSites();
            JobManager.I.RefreshAllJobSiteViewsInScene();
        }

        Debug.Log("SaveManager: Cleared JSON save and reset state.");
    }

    public static void OnResume()
    {
        PruneExpiredCaptureBands(true);
        PruneExpiredLures(true);
        PruneExpiredLuckBoosts(true);

        TrainingManager.I?.ProcessOfflineTrainingAll();
        JobManager.I?.ProcessOfflineAllSites();
        HealthRegenSystem.I?.TryApplyOfflineRegen();
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Defaults / Normalization
    // ─────────────────────────────────────────────────────────────────────────────

    static PlayerManager NewFreshPlayer()
    {
        var p = new PlayerManager
        {
            encounterMax = 50,
            encounterCost = 5,
            encounterPoints = 50,
            lastEncounterResetYMD = TodayYMD(),
            lastSavedUnix = NowUnix(),

            // Collections (ensure non-null)
            ownedIds = new HashSet<string>(),
            ownedIdsList = new List<string>(),

            seenTypes = new HashSet<MonsterType>(),
            seenTypesList = new List<MonsterType>(),

            unlockedJobSites = new HashSet<JobType>(),
            unlockedJobSitesList = new List<JobType>(),

            team = new List<OwnedMonsterData>(),
            owned = new List<OwnedMonsterData>(),

            activeLures = new List<LureBiasData>(),
            activeCaptureBands = new List<CaptureBandData>(),
            activeLuckBoosts = new List<LuckBoostData>(),

            jobStorageUpgrades = new List<JobStorageUpgrade>(),
            jobAssignments = new List<JobAssignment>(),
            jobProgress = new List<JobProgress>(),

            settings = new SettingsState()
        };

        return p;
    }

    static void EnsureDefaults()
    {
        // Base collections
        Data.owned ??= new List<OwnedMonsterData>();
        Data.team ??= new List<OwnedMonsterData>();
        Data.activeLures ??= new List<LureBiasData>();
        Data.activeCaptureBands ??= new List<CaptureBandData>();
        Data.activeLuckBoosts ??= new List<LuckBoostData>();
        Data.jobAssignments ??= new List<JobAssignment>();
        Data.jobProgress ??= new List<JobProgress>();
        Data.jobStorageUpgrades ??= new List<JobStorageUpgrade>();

        // Identity
        if (string.IsNullOrEmpty(Data.playerId)) Data.playerId = Guid.NewGuid().ToString("N");

        // Encounter economy
        if (Data.encounterMax <= 0) Data.encounterMax = 50;
        if (Data.encounterCost <= 0) Data.encounterCost = 5;
        if (Data.encounterPoints < 0) Data.encounterPoints = 0;
        if (Data.lastEncounterResetYMD == 0) Data.lastEncounterResetYMD = TodayYMD();

        // Resource counts sized to enum
        int need = Enum.GetValues(typeof(ResourceType)).Length;
        Data.resourceCounts ??= new List<int>(new int[Mathf.Max(1, need)]);
        while (Data.resourceCounts.Count < need) Data.resourceCounts.Add(0);

        // Mirrors for hashsets/lists
        Data.ownedIds ??= new HashSet<string>();
        Data.ownedIdsList ??= new List<string>();
        Data.seenTypes ??= new HashSet<MonsterType>();
        Data.seenTypesList ??= new List<MonsterType>();
        Data.unlockedJobSites ??= new HashSet<JobType>();
        Data.unlockedJobSitesList ??= new List<JobType>();

        // Rebuild sets from their list mirrors (authoritative for JSON)
        Data.ownedIds.Clear();
        foreach (var id in Data.ownedIdsList)
            if (!string.IsNullOrEmpty(id))
                Data.ownedIds.Add(id);

        Data.seenTypes.Clear();
        for (int i = 0; i < Data.seenTypesList.Count; i++)
            Data.seenTypes.Add(Data.seenTypesList[i]);

        Data.unlockedJobSites.Clear();
        for (int i = 0; i < Data.unlockedJobSitesList.Count; i++)
            Data.unlockedJobSites.Add(Data.unlockedJobSitesList[i]);

        // Settings
        Data.settings ??= new SettingsState();
        // (Your SettingsState should contain fields like autoBenchEnabled, autoClinicReliefEnabled, etc.)

        // Normalize team & owned and ensure cross-consistency
        NormalizeOwnedEntries(Data.owned);
        NormalizeOwnedEntries(Data.team);

        // Ensure any team member exists in owned list (at least one copy)
        for (int i = 0; i < Data.team.Count; i++)
        {
            var t = Data.team[i];
            if (t == null || string.IsNullOrEmpty(t.monsterId)) continue;

            // Track species in ownedIds
            Data.ownedIds.Add(t.monsterId);

            bool found = false;
            for (int k = 0; k < Data.owned.Count; k++)
            {
                var o = Data.owned[k];
                if (o != null && o.monsterId == t.monsterId) { found = true; break; }
            }

            if (!found)
            {
                // Insert a minimal owned copy for consistency
                Data.owned.Add(new OwnedMonsterData
                {
                    monsterId = t.monsterId,
                    level = Mathf.Max(1, t.level),
                    currentHP = -1,
                    currentXP = Mathf.Max(0, t.currentXP),
                    ownedUID = string.IsNullOrEmpty(t.ownedUID) ? Guid.NewGuid().ToString("N") : t.ownedUID
                });
            }
        }

        // Training pointer default
        if (string.IsNullOrEmpty(Data.trainingMonsterId) && Data.team.Count > 0)
        {
            Data.trainingMonsterId = Data.team[0].monsterId;
            var om = Data.owned.Find(o => o != null && o.monsterId == Data.trainingMonsterId);
            if (om != null) om.isTraining = true;
        }

        if (Data.lastSavedUnix <= 0) Data.lastSavedUnix = NowUnix();
    }

    static void NormalizeOwnedEntries(List<OwnedMonsterData> list)
    {
        if (list == null) return;

        for (int i = 0; i < list.Count; i++)
        {
            var om = list[i];
            if (om == null)
            {
                list[i] = new OwnedMonsterData();
                om = list[i];
            }

            // Enforce minimums and consistent sentinels
            if (om.level <= 0) om.level = 1;
            if (om.currentXP < 0) om.currentXP = 0;

            // ✅ Keep 0 HP as "downed" so the player must heal or bench them.
            //    Only coerce values less than -1 up to the -1 sentinel.
            if (om.currentHP < -1) om.currentHP = -1;

            // Ensure a stable UID for references
            if (string.IsNullOrEmpty(om.ownedUID))
                om.ownedUID = Guid.NewGuid().ToString("N");

            // Track species in ownedIds if this belongs to the owned list
            if (ReferenceEquals(list, Data.owned) && !string.IsNullOrEmpty(om.monsterId))
                Data.ownedIds.Add(om.monsterId);
        }
    }
    static void EnsureTrainingDefaults()
    {
        if (Data?.owned == null) return;

        long now = NowUnix();
        foreach (var om in Data.owned)
        {
            if (om == null) continue;

            if (om.level <= 0) om.level = 1;
            if (om.currentXP < 0) om.currentXP = 0;
            if (om.trainingLastUnix == 0) om.trainingLastUnix = now;

            if (om.lastLevelClaimDay == 0) om.lastLevelClaimDay = -1;
            if (om.pendingLevels < 0) om.pendingLevels = 0;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Buff/Lure Expiration
    // ─────────────────────────────────────────────────────────────────────────────

    static bool PruneExpiredLures(bool saveIfChanged)
    {
        if (Data?.activeLures == null || Data.activeLures.Count == 0) return false;

        long now = NowUnix();
        int before = Data.activeLures.Count;
        Data.activeLures.RemoveAll(l =>
        {
            long exp = (l != null) ? l.expireUnix : 0L;
            return exp > 0 && exp <= now;
        });
        bool changed = Data.activeLures.Count != before;

        if (saveIfChanged && changed) Save();
        return changed;
    }

    static bool PruneExpiredCaptureBands(bool saveIfChanged)
    {
        if (Data?.activeCaptureBands == null || Data.activeCaptureBands.Count == 0) return false;

        long now = NowUnix();
        int before = Data.activeCaptureBands.Count;
        Data.activeCaptureBands.RemoveAll(b => b != null && b.expireUnix <= now);
        bool changed = Data.activeCaptureBands.Count != before;

        if (saveIfChanged && changed) Save();
        return changed;
    }

    static bool PruneExpiredLuckBoosts(bool saveIfChanged)
    {
        if (Data?.activeLuckBoosts == null || Data.activeLuckBoosts.Count == 0) return false;

        long now = NowUnix();
        int before = Data.activeLuckBoosts.Count;
        Data.activeLuckBoosts.RemoveAll(b => b != null && b.expireUnix <= now);
        bool changed = Data.activeLuckBoosts.Count != before;

        if (saveIfChanged && changed) Save();
        return changed;
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Utilities
    // ─────────────────────────────────────────────────────────────────────────────

    public static int TodayYMD()
    {
        var d = DateTime.UtcNow;
        return d.Year * 10000 + d.Month * 100 + d.Day;
    }

    public static long NowUnix() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    public static int TodayDayIndexUTC() => (int)(NowUnix() / 86400L);

    static void EnsureFolder()
    {
        try
        {
            var dir = Path.GetDirectoryName(SavePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
        }
        catch { }
    }

    static bool TryLoad(string path, out PlayerManager data)
    {
        data = null;
        try
        {
            if (!File.Exists(path)) return false;
            string json = File.ReadAllText(path, Encoding.UTF8);
            if (string.IsNullOrWhiteSpace(json)) return false;
            data = JsonUtility.FromJson<PlayerManager>(json);
            return data != null;
        }
        catch
        {
            data = null;
            return false;
        }
    }

    static void AtomicWrite(string path, string contents)
    {
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, contents, Encoding.UTF8);

        try
        {
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }
        catch
        {
            try { if (!File.Exists(path)) File.Copy(tmp, path); } catch { }
            try { File.Delete(tmp); } catch { }
        }
    }

    public static bool HasStarter() => Data != null && Data.hasChosenStarter;

    public static void GrantStarter(string monsterId, int level = 1)
    {
        if (Data == null || string.IsNullOrEmpty(monsterId)) return;
        EnsureDefaults();

        // Ensure an owned copy exists
        var existing = Data.owned.Find(o => o != null && o.monsterId == monsterId);
        if (existing == null)
        {
            var om = new OwnedMonsterData
            {
                monsterId = monsterId,
                level = Mathf.Max(1, level),
                currentHP = -1,
                currentXP = 0,
                ownedUID = Guid.NewGuid().ToString("N")
            };
            Data.owned.Add(om);
        }
        else
        {
            // Normalize the existing one
            if (existing.level <= 0) existing.level = Mathf.Max(1, level);
            if (existing.currentHP == 0) existing.currentHP = -1;
            if (string.IsNullOrEmpty(existing.ownedUID)) existing.ownedUID = Guid.NewGuid().ToString("N");
        }

        // Track species
        Data.ownedIds ??= new HashSet<string>();
        Data.ownedIds.Add(monsterId);

        // Ensure on team at least once
        bool onTeam = Data.team.Exists(t => t != null && t.monsterId == monsterId);
        if (!onTeam)
        {
            Data.team.Add(new OwnedMonsterData
            {
                monsterId = monsterId,
                level = Mathf.Max(1, level),
                currentHP = -1,
                currentXP = 0,
                ownedUID = Guid.NewGuid().ToString("N")
            });
            if (string.IsNullOrEmpty(Data.trainingMonsterId)) Data.trainingMonsterId = monsterId;
        }

        Data.hasChosenStarter = true;

        try
        {
            var def = MonsterLibraryLocator.GetById(monsterId);
            if (def != null)
            {
                Data.seenTypes ??= new HashSet<MonsterType>();
                Data.seenTypes.Add(def.type);
                GameEvents.StarterChosen?.Invoke(def.type);
            }
        }
        catch { }

        Save();
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Job runtime sidecar I/O (slot fatigue + cooldown)
    // ─────────────────────────────────────────────────────────────────────────────

    public static void SaveJobRuntime(JobRuntimeSave blob)
    {
        try
        {
            var json = JsonUtility.ToJson(blob ?? new JobRuntimeSave(), prettyPrint: true);
            AtomicWrite(JobRuntimePath, json);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"SaveJobRuntime failed: {e.Message}");
        }
    }

    public static JobRuntimeSave LoadJobRuntime()
    {
        try
        {
            if (!File.Exists(JobRuntimePath)) return null;
            var json = File.ReadAllText(JobRuntimePath, Encoding.UTF8);
            if (string.IsNullOrWhiteSpace(json)) return null;
            return JsonUtility.FromJson<JobRuntimeSave>(json);
        }
        catch
        {
            return null;
        }
    }
}
