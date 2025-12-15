using UnityEngine;
using System;
using System.IO;
using System.Text;
using System.Collections.Generic;


[Serializable]
public class JobRuntimeSite
{
    public JobType job;
    public float[] slotFatigue01;
    public long[] slotCooldownUntilUnix;
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


public static class SaveManager
{
    public static PlayerManager Data;

    public static string SavePath => Path.Combine(Application.persistentDataPath, "idle_mon_save.json");
    public static string BackupPath => Path.Combine(Application.persistentDataPath, "idle_mon_save.bak");
    public static string JobRuntimePath => Path.Combine(Application.persistentDataPath, "idle_job_runtime.json");

    // ─────────────────────────────────────────────
    // Auto-generated handler names
    // ─────────────────────────────────────────────

    private static readonly string[] namePrefixes = new string[]
    {
        "Handler", "Operator", "Agent", "Keeper", "Caretaker",
        "Riftwatcher", "Observer", "Archivist", "Custodian",
        "Tech", "Warden", "Cipher", "Bitmaster"
    };

    private static readonly string[] nameStems = new string[]
    {
        "Flux", "Byte", "Voxel", "Data", "Prism", "Spark", "Root", "Fracture",
        "Shard", "Node", "Pulse", "Shift", "Core", "Patch", "Signal",
        "Ripple", "Trace", "Flow"
    };

    private static string GeneratePlayerName()
    {
        // 1% chance to get a rare “Ω” title
        if (UnityEngine.Random.value <= 0.01f)
        {
            int rareNum = UnityEngine.Random.Range(1, 99);
            return $"Prime Overseer-Ω{rareNum:00}";
        }

        string prefix = namePrefixes[UnityEngine.Random.Range(0, namePrefixes.Length)];
        string stem = nameStems[UnityEngine.Random.Range(0, nameStems.Length)];

        // 3 random hex characters
        string hex = UnityEngine.Random.Range(0, 4095).ToString("X3");

        return $"{prefix} {stem}-{hex}";
    }

    // ─────────────────────────────────────────────
    // Lifecycle
    // ─────────────────────────────────────────────

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
        Data?.EnsureTransientSets();

        EnsureTrainingDefaults();

        PruneExpiredCaptureBands(true);
        PruneExpiredLures(true);
        PruneExpiredLuckBoosts(true);
    }

    public static void Save()
    {
        if (Data == null)
        {
            Debug.LogWarning("SaveManager.Save called with Data == null. Creating fresh player.");
            Data = NewFreshPlayer();
        }

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

            // ✅ Discovery mirror: Set -> List for JSON
            if (Data.discoveredMonsterIds != null)
            {
                Data.discoveredMonsterIdsList ??= new List<string>();
                Data.discoveredMonsterIdsList.Clear();
                foreach (var id in Data.discoveredMonsterIds)
                    if (!string.IsNullOrEmpty(id)) Data.discoveredMonsterIdsList.Add(id);
                Data.discoveredMonsterIdsList.Sort(StringComparer.Ordinal);
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

    // ─────────────────────────────────────────────
    // Hard reset (new account)
    // ─────────────────────────────────────────────
    public static void ClearAll()
    {
        // Delete persisted save files
        try { if (File.Exists(SavePath)) File.Delete(SavePath); } catch { }
        try { if (File.Exists(BackupPath)) File.Delete(BackupPath); } catch { }
        try { if (File.Exists(JobRuntimePath)) File.Delete(JobRuntimePath); } catch { }

        Data = NewFreshPlayer();

        ResourceBank.EnsureSize();

        foreach (ResourceType t in Enum.GetValues(typeof(ResourceType)))
            ResourceBank.Set(t, 0);

        // Starting resources (adjust as desired)
        ResourceBank.Set(ResourceType.Energy, 50);
        ResourceBank.Set(ResourceType.Credits, 0);
        ResourceBank.Set(ResourceType.Medkit, 0);
        ResourceBank.Set(ResourceType.PackVoucher, 0);

        // Reset encounter config + JSON regen timers
        Data.encounterMax = 50;
        Data.encounterCost = 5;
        Data.lastEncounterResetYMD = 0;
        Data.energyLastUnix = NowUnix();
        Data.energyRemainderSecs = 0f;

        // Persist immediately
        Save();

        // Notify dependent systems
        GameEvents.OnJobsChanged?.Invoke();
        GameEvents.OnResourcesChanged?.Invoke();
        GameEvents.EnergyChanged?.Invoke();

        // Rebuild job state cleanly
        if (JobManager.I)
        {
            JobManager.I.LoadAssignmentsFromSave();
            JobManager.I.ProcessOfflineAllSites();
            JobManager.I.RefreshAllJobSiteViewsInScene();
        }

        Debug.Log($"[CLEAR ALL] New account created. Energy={ResourceBank.Get(ResourceType.Energy)}/{Data.encounterMax}");
    }

    public static void OnResume()
    {
        PruneExpiredCaptureBands(true);
        PruneExpiredLures(true);
        PruneExpiredLuckBoosts(true);

        JobManager.I?.ProcessOfflineAllSites();
        HealthRegenSystem.I?.TryApplyOfflineRegen();
    }

    // ─────────────────────────────────────────────
    // Defaults / Normalization
    // ─────────────────────────────────────────────

    static PlayerManager NewFreshPlayer()
    {
        var p = new PlayerManager
        {
            encounterMax = 50,
            encounterCost = 5,
            lastEncounterResetYMD = TodayYMD(),
            lastSavedUnix = NowUnix(),

            // JSON-only energy regen timing (required by EncounterManager.Energy bank-only)
            energyLastUnix = NowUnix(),
            energyRemainderSecs = 0f,

            // Win streak
            winStreak = 0,

            // Collections (ensure non-null)
            ownedIds = new HashSet<string>(),
            ownedIdsList = new List<string>(),

            seenTypes = new HashSet<MonsterType>(),
            seenTypesList = new List<MonsterType>(),

            unlockedJobSites = new HashSet<JobType>(),
            unlockedJobSitesList = new List<JobType>(),

            // ✅ pack discovery
            discoveredMonsterIds = new HashSet<string>(),
            discoveredMonsterIdsList = new List<string>(),

            team = new List<OwnedMonsterData>(),
            owned = new List<OwnedMonsterData>(),

            activeFlyers = new List<FlyerBiasData>(),
            activeCaptureBands = new List<CaptureBandData>(),
            activeLuckBoosts = new List<LuckBoostData>(),

            jobStorageUpgrades = new List<JobStorageUpgrade>(),
            jobAssignments = new List<JobAssignment>(),
            jobProgress = new List<JobProgress>(),

            fieldOps = new FieldOpsStats(),

            settings = new SettingsState()
        };

        // Auto-generate a handler name for a totally fresh save
        p.playerName = GeneratePlayerName();

        // IMPORTANT: Resource counts are managed by ResourceBank.EnsureSize().
        // Here we can leave resourceCounts null; EnsureDefaults/ResourceBank will size it.

        return p;
    }

    static void EnsureDefaults()
    {
        if (Data == null)
        {
            Data = NewFreshPlayer();
            return;
        }

        // Base collections
        Data.owned ??= new List<OwnedMonsterData>();
        Data.team ??= new List<OwnedMonsterData>();
        Data.activeFlyers ??= new List<FlyerBiasData>();
        Data.activeCaptureBands ??= new List<CaptureBandData>();
        Data.activeLuckBoosts ??= new List<LuckBoostData>();
        Data.jobAssignments ??= new List<JobAssignment>();
        Data.jobProgress ??= new List<JobProgress>();
        Data.jobStorageUpgrades ??= new List<JobStorageUpgrade>();
        Data.unlockedPacks ??= new List<string>();

        if (Data.fieldOps == null)
            Data.fieldOps = new FieldOpsStats();

        // Identity
        if (string.IsNullOrEmpty(Data.playerId)) Data.playerId = Guid.NewGuid().ToString("N");
        if (string.IsNullOrEmpty(Data.playerName)) Data.playerName = GeneratePlayerName();

        // Encounter config
        if (Data.encounterMax <= 0) Data.encounterMax = 50;
        if (Data.encounterCost <= 0) Data.encounterCost = 5;
        if (Data.lastEncounterResetYMD == 0) Data.lastEncounterResetYMD = TodayYMD();

        // JSON-only energy regen timing
        if (Data.energyLastUnix <= 0) Data.energyLastUnix = NowUnix();
        if (Data.energyRemainderSecs < 0f) Data.energyRemainderSecs = 0f;

        // Win streak defaults / clamp
        if (Data.winStreak < 0) Data.winStreak = 0;

        // Resource counts: size using the same rule as ResourceBank.EnsureSize()
        EnsureResourceCountsSized();

        // Mirrors for hashsets/lists
        Data.ownedIds ??= new HashSet<string>();
        Data.ownedIdsList ??= new List<string>();
        Data.seenTypes ??= new HashSet<MonsterType>();
        Data.seenTypesList ??= new List<MonsterType>();
        Data.unlockedJobSites ??= new HashSet<JobType>();
        Data.unlockedJobSitesList ??= new List<JobType>();

        // ✅ discovery mirrors
        Data.discoveredMonsterIds ??= new HashSet<string>();
        Data.discoveredMonsterIdsList ??= new List<string>();

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

        // ✅ rebuild discoveredMonsterIds from JSON list
        Data.discoveredMonsterIds.Clear();
        for (int i = 0; i < Data.discoveredMonsterIdsList.Count; i++)
        {
            var id = Data.discoveredMonsterIdsList[i];
            if (!string.IsNullOrEmpty(id))
                Data.discoveredMonsterIds.Add(id);
        }

        // Settings
        Data.settings ??= new SettingsState();

        // Normalize team & owned and ensure cross-consistency
        NormalizeOwnedEntries(Data.owned);
        NormalizeOwnedEntries(Data.team);

        // Ensure any team member exists in owned list AND that the team slot references the owned instance
        for (int i = 0; i < Data.team.Count; i++)
        {
            var t = Data.team[i];
            if (t == null || string.IsNullOrEmpty(t.monsterId))
                continue;

            OwnedMonsterData canonical = null;

            if (!string.IsNullOrEmpty(t.ownedUID))
                canonical = Data.owned.Find(o => o != null && o.ownedUID == t.ownedUID);

            if (canonical == null)
                canonical = Data.owned.Find(o => o != null && o.monsterId == t.monsterId);

            if (canonical == null)
            {
                canonical = new OwnedMonsterData
                {
                    monsterId = t.monsterId,
                    level = Mathf.Max(1, t.level),
                    currentHP = t.currentHP <= -1 ? t.currentHP : -1,
                    currentXP = Mathf.Max(0, t.currentXP),
                    ownedUID = string.IsNullOrEmpty(t.ownedUID) ? Guid.NewGuid().ToString("N") : t.ownedUID
                };
                Data.owned.Add(canonical);
            }

            if (string.IsNullOrEmpty(canonical.ownedUID))
            {
                canonical.ownedUID = string.IsNullOrEmpty(t.ownedUID)
                    ? Guid.NewGuid().ToString("N")
                    : t.ownedUID;
            }

            Data.team[i] = canonical;

            if (!string.IsNullOrEmpty(canonical.monsterId))
                Data.ownedIds.Add(canonical.monsterId);
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

    static void EnsureResourceCountsSized()
    {
        if (Data == null) return;

        Data.resourceCounts ??= new List<int>();

        int need = 0;
        foreach (ResourceType t in Enum.GetValues(typeof(ResourceType)))
            need = Mathf.Max(need, (int)t + 1);

        while (Data.resourceCounts.Count < need)
            Data.resourceCounts.Add(0);
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

            if (om.level <= 0) om.level = 1;
            if (om.currentXP < 0) om.currentXP = 0;

            if (om.currentHP < -1) om.currentHP = -1;

            if (string.IsNullOrEmpty(om.ownedUID))
                om.ownedUID = Guid.NewGuid().ToString("N");

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

    // ─────────────────────────────────────────────
    // Discovery API
    // ─────────────────────────────────────────────

    public static bool IsDiscovered(string monsterId)
    {
        if (Data == null || string.IsNullOrEmpty(monsterId)) return false;
        Data.discoveredMonsterIds ??= new HashSet<string>();
        return Data.discoveredMonsterIds.Contains(monsterId);
    }

    public static bool Discover(string monsterId, bool save = true)
    {
        if (Data == null || string.IsNullOrEmpty(monsterId)) return false;

        Data.discoveredMonsterIds ??= new HashSet<string>();
        bool added = Data.discoveredMonsterIds.Add(monsterId);
        if (!added) return false;

        Data.discoveredMonsterIdsList ??= new List<string>();
        if (!Data.discoveredMonsterIdsList.Contains(monsterId))
            Data.discoveredMonsterIdsList.Add(monsterId);

        if (save) Save();
        return true;
    }

    // ─────────────────────────────────────────────
    // Buff/Lure Expiration
    // ─────────────────────────────────────────────

    static bool PruneExpiredLures(bool saveIfChanged)
    {
        if (Data?.activeFlyers == null || Data.activeFlyers.Count == 0) return false;

        long now = NowUnix();
        int before = Data.activeFlyers.Count;
        Data.activeFlyers.RemoveAll(l =>
        {
            long exp = (l != null) ? l.expireUnix : 0L;
            return exp > 0 && exp <= now;
        });
        bool changed = Data.activeFlyers.Count != before;

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

    // ─────────────────────────────────────────────
    // Utilities
    // ─────────────────────────────────────────────

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

        var ownedMonster = Data.owned.Find(o => o != null && o.monsterId == monsterId);
        if (ownedMonster == null)
        {
            ownedMonster = new OwnedMonsterData
            {
                monsterId = monsterId,
                level = Mathf.Max(1, level),
                currentHP = -1,
                currentXP = 0,
                ownedUID = Guid.NewGuid().ToString("N")
            };
            Data.owned.Add(ownedMonster);
        }
        else
        {
            if (ownedMonster.level <= 0) ownedMonster.level = Mathf.Max(1, level);
            if (ownedMonster.currentHP == 0) ownedMonster.currentHP = -1;
            if (string.IsNullOrEmpty(ownedMonster.ownedUID))
                ownedMonster.ownedUID = Guid.NewGuid().ToString("N");
        }

        bool onTeam = Data.team.Exists(t => t != null && t.ownedUID == ownedMonster.ownedUID);
        if (!onTeam)
        {
            Data.team.Add(ownedMonster);
            if (string.IsNullOrEmpty(Data.trainingMonsterId))
                Data.trainingMonsterId = monsterId;
        }

        Data.ownedIds ??= new HashSet<string>();
        Data.ownedIds.Add(monsterId);

        // ✅ Use centralized discovery helper
        Discover(monsterId, save: false);

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

    // ─────────────────────────────────────────────
    // Job runtime sidecar I/O (slot fatigue + cooldown)
    // ─────────────────────────────────────────────

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

    public static bool HasSeenStory()
    {
        return Data != null && Data.hasSeenStory;
    }

    public static void MarkStorySeen()
    {
        if (Data == null) return;
        Data.hasSeenStory = true;
        Save();
    }
}
