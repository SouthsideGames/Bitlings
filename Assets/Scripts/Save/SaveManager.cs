using UnityEngine;
using System;
using System.IO;
using System.Text;
using System.Collections.Generic;

#region Job runtime sidecar (slot fatigue + cooldown)

[Serializable]
public class JobRuntimeSite
{
    public JobType job;
    public float[] slotFatigue01;
    public long[] slotCooldownUntilUnix;

    // Per-site auto-collect toggle. Only triggers collection when storage is full.
    public bool autoCollectEnabled;
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

// ─────────────────────────────────────────────
// World Events save (rotation + cooldowns)
// ─────────────────────────────────────────────

[Serializable]
public class WorldEventRollCooldown
{
    public string id;
    public long lastRolledUnix;
}

[Serializable]
public class WorldEventSaveData
{
    // Legacy rotation fields (kept for backward compatibility)
    public string rotationActiveEventId;
    public long rotationUntilUnix;
    public long nextRotationRollUnix;

    // Weekly system
    public string weeklyActiveEventId;
    public long weeklyWeekStartUnix;

    // First week after unlocking World Events should force a Flavor roll.
    public bool firstUnlockFlavorConsumed;

    public List<WorldEventRollCooldown> cooldowns = new();
}

#endregion

public static class SaveManager
{
    public static PlayerManager Data;

    // ─────────────────────────────────────────────
    // Combined Save Root (Option B)
    // - PlayerManager (main state)
    // - Tutorial flags
    // - Job runtime sidecar
    // - Titles (previously TitleSaveStore)
    //
    // NOTE: We keep existing SaveManager APIs (Data, tutorial helpers, job runtime helpers)
    // so the rest of the project does not need refactors.
    // ─────────────────────────────────────────────

    [Serializable]
    private sealed class PlayerSaveRoot
    {
        public int version = 1;
        public PlayerManager player;
        public List<string> tutorialCompleted = new List<string>();
        public JobRuntimeSave jobRuntime;
        public TitleSaveData titles;
        public WorldEventSaveData worldEvents;
    }

    private static bool _loaded;
    private static bool _isSaving;

    // Cached sidecar blobs now stored inside PlayerSave.json
    private static JobRuntimeSave _jobRuntimeCache;
    private static TitleSaveData _titlesCache;
    private static WorldEventSaveData _worldEventsCache;

    // ─────────────────────────────────────────────
    // Hard reset guard (prevents sidecar/runtime re-saves during scene reload)
    // ─────────────────────────────────────────────
    public static bool IsHardResetting { get; private set; }

    // Alias for older/newer scripts that expect a different naming.
    // SettingsManager / others may reference IsHardWiping.
    public static bool IsHardWiping => IsHardResetting;

    public static void BeginHardReset() => IsHardResetting = true;
    public static void EndHardReset() => IsHardResetting = false;

    // ─────────────────────────────────────────────
    // Paths
    // ─────────────────────────────────────────────

    // NEW (authoritative)
    public static string SavePath => Path.Combine(Application.persistentDataPath, "PlayerSave.json");
    public static string BackupPath => Path.Combine(Application.persistentDataPath, "PlayerSave.bak");

    // Legacy (migration only)
    private static string LegacySavePath => Path.Combine(Application.persistentDataPath, "idle_mon_save.json");
    private static string LegacyBackupPath => Path.Combine(Application.persistentDataPath, "idle_mon_save.bak");
    private static string LegacyTutorialFlagsPath => Path.Combine(Application.persistentDataPath, "tutorial_flags.json");
    private static string LegacyJobRuntimePath => Path.Combine(Application.persistentDataPath, "idle_job_runtime.json");

    // ─────────────────────────────────────────────
    // Auto-generated handler names
    // ─────────────────────────────────────────────

    private static readonly string[] namePrefixes = new[]
    {
        "Handler", "Operator", "Agent", "Keeper", "Caretaker",
        "Riftwatcher", "Observer", "Archivist", "Custodian",
        "Tech", "Warden", "Cipher", "Bitmaster"
    };

    private static readonly string[] nameStems = new[]
    {
        "Flux", "Byte", "Voxel", "Data", "Prism", "Spark", "Root", "Fracture",
        "Shard", "Node", "Pulse", "Shift", "Core", "Patch", "Signal",
        "Ripple", "Trace", "Flow"
    };

    private static string GeneratePlayerName()
    {
        if (UnityEngine.Random.value <= 0.01f)
        {
            int rareNum = UnityEngine.Random.Range(1, 99);
            return $"Prime Overseer-Ω{rareNum:00}";
        }

        string prefix = namePrefixes[UnityEngine.Random.Range(0, namePrefixes.Length)];
        string stem = nameStems[UnityEngine.Random.Range(0, nameStems.Length)];
        string hex = UnityEngine.Random.Range(0, 4095).ToString("X3");
        return $"{prefix} {stem}-{hex}";
    }

    // ─────────────────────────────────────────────
    // Lifecycle
    // ─────────────────────────────────────────────

    public static void LoadOrCreate()
    {
        if (_loaded) return;
        _loaded = true;

        SaveFiles.EnsureFolder(SavePath);

        // 1) Load combined save first.
        PlayerSaveRoot root = null;
        if (!TryLoad(SavePath, out root))
        {
            // 2) Fallback to combined backup.
            if (!TryLoad(BackupPath, out root))
            {
                // 3) Migrate from legacy multi-file layout.
                root = MigrateFromLegacyOrCreateFresh();
            }
        }

        Data = root?.player ?? NewFreshPlayer();

        // Hydrate caches from root.
        LoadTutorialFromRoot(root);
        _jobRuntimeCache = root?.jobRuntime;
        _titlesCache = root?.titles;
        _worldEventsCache = root?.worldEvents;

        NormalizeAfterLoad();

        // Apply energy offline catch-up even if EncounterManager is not active yet (menu-first boot).
        // Safe to call multiple times; it advances Data.energyLastUnix.
        EnergyRegenSystem.TryApplyOfflineRegen();

        if (!File.Exists(SavePath) && !IsHardWiping)
            Save();

        EndHardReset();
    }

    public static void Save()
    {
        if (IsHardWiping) return;
        if (_isSaving) return;

        _isSaving = true;

        try
        {
            if (Data == null)
                Data = NewFreshPlayer();

            NormalizeBeforeSave();

            long now = NowUnix();
            if (Data.lastSavedUnix > 0 && now + 300 < Data.lastSavedUnix) now = Data.lastSavedUnix;
            Data.lastSavedUnix = Math.Max(Data.lastSavedUnix, now);

            // Build single-file root.
            var root = BuildRootForSave();
            string json = JsonUtility.ToJson(root, prettyPrint: true);
            SaveFiles.AtomicWriteUtf8(SavePath, json);
            SaveFiles.TryCopy(SavePath, BackupPath);
        }
        catch (Exception e)
        {
            Debug.LogError($"SaveManager.Save failed: {e}");
        }
        finally
        {
            _isSaving = false;
        }
    }

    public static void OnResume()
    {
        if (IsHardWiping) return;

        PruneExpiredCaptureBands(saveIfChanged: true);
        PruneExpiredLures(saveIfChanged: true);
        PruneExpiredLuckBoosts(saveIfChanged: true);

        JobManager.I?.ProcessOfflineAllSites();
        HealthRegenSystem.I?.TryApplyOfflineRegen();
        EnergyRegenSystem.TryApplyOfflineRegen();
    }

    // ─────────────────────────────────────────────
    // Hard reset (new account)
    // NOTE: ClearAll is not the "button reset" path anymore; HardWipeAll is.
    // Kept for dev/testing, but guarded to avoid firing events mid-reset.
    // ─────────────────────────────────────────────

    public static void ClearAll()
    {
        BeginHardReset();

        try
        {
            SaveFiles.TryDelete(SavePath);
            SaveFiles.TryDelete(BackupPath);
            // Legacy files (kept for safety during transition)
            SaveFiles.TryDelete(LegacySavePath);
            SaveFiles.TryDelete(LegacyBackupPath);
            SaveFiles.TryDelete(LegacyJobRuntimePath);
            SaveFiles.TryDelete(LegacyTutorialFlagsPath);
            SaveFiles.TryDelete(TitleSaveStore.SavePath);

            ClearTutorialFlags();

            _jobRuntimeCache = null;
            _titlesCache = null;

            Data = NewFreshPlayer();
            EnsureDefaults();

            ResourceBank.EnsureSize();
            foreach (ResourceType t in Enum.GetValues(typeof(ResourceType)))
                ResourceBank.Set(t, 0);

            ResourceBank.Set(ResourceType.Energy, 50);
            ResourceBank.Set(ResourceType.Credits, 0);
            ResourceBank.Set(ResourceType.Medkit, 0);
            ResourceBank.Set(ResourceType.PackVoucher, 0);

            Data.encounterMax = 50;
            Data.encounterCost = 1;
            Data.lastEncounterResetYMD = 0;
            Data.energyLastUnix = NowUnix();
            Data.energyRemainderSecs = 0f;

            NormalizeBeforeSave();
        }
        finally
        {
            EndHardReset();
        }

        if (!IsHardWiping)
        {
            GameEvents.OnJobsChanged?.Invoke();
            GameEvents.OnResourcesChanged?.Invoke();
            GameEvents.EnergyChanged?.Invoke();
        }

        if (JobManager.I)
        {
            JobManager.I.LoadAssignmentsFromSave();
            JobManager.I.ProcessOfflineAllSites();
            JobManager.I.RefreshAllJobSiteViewsInScene();
        }

        Debug.Log($"[CLEAR ALL] New account created. Energy={ResourceBank.Get(ResourceType.Energy)}/{Data.encounterMax}");
    }

    // ─────────────────────────────────────────────
    // HARD WIPE (true cold reset)
    // - Deletes JSON files AND clears in-memory caches/guards
    // - Designed to prevent “old session” objects from re-saving sidecar/runtime during reload
    // ─────────────────────────────────────────────
    public static void HardWipeAll(bool reloadFresh = true)
    {
        BeginHardReset();
        GameEvents.HardResetting?.Invoke(true);

        try
        {
            // 1) Stop the "already loaded" guard so LoadOrCreate can run again this session.
            _loaded = false;

            // 2) Wipe tutorial cache fully (not just the file).
            _tutorialLoaded = false;
            _tutorialData = null;
            _tutorialSet = null;

            // 3) Delete all known save files (and any temp leftovers).
            SaveFiles.TryDelete(SavePath);
            SaveFiles.TryDelete(BackupPath);
            // Legacy files (multi-json layout)
            SaveFiles.TryDelete(LegacySavePath);
            SaveFiles.TryDelete(LegacyBackupPath);
            SaveFiles.TryDelete(LegacyJobRuntimePath);
            SaveFiles.TryDelete(LegacyTutorialFlagsPath);
            SaveFiles.TryDelete(TitleSaveStore.SavePath);

            SaveFiles.TryDelete(SavePath + ".tmp");
            SaveFiles.TryDelete(BackupPath + ".tmp");
            SaveFiles.TryDelete(LegacySavePath + ".tmp");
            SaveFiles.TryDelete(LegacyBackupPath + ".tmp");
            SaveFiles.TryDelete(LegacyJobRuntimePath + ".tmp");
            SaveFiles.TryDelete(LegacyTutorialFlagsPath + ".tmp");
            SaveFiles.TryDelete(TitleSaveStore.SavePath + ".tmp");

            _jobRuntimeCache = null;
            _titlesCache = null;

            // 4) Rebuild a truly fresh PlayerManager in memory.
            Data = NewFreshPlayer();
            JobUnlockBridge.ResetAllJobUnlocks(alsoResetPurchasedFlags: true);

            // IMPORTANT ORDER:
            // Ensure defaults/resources exist BEFORE ResourceBank touches anything.
            EnsureDefaults();
            EnsureResourceCountsSized();

            NormalizeBeforeSave();

            // 5) Reset ResourceBank mirror to zeros so nothing "sticks" in memory.
            ResourceBank.EnsureSize();
            foreach (ResourceType t in Enum.GetValues(typeof(ResourceType)))
                ResourceBank.Set(t, 0);

            ResourceBank.Set(ResourceType.Energy, 50);
            ResourceBank.Set(ResourceType.Credits, 0);
            ResourceBank.Set(ResourceType.Medkit, 0);
            ResourceBank.Set(ResourceType.PackVoucher, 0);

            Data.encounterMax = 50;
            Data.encounterCost = 1;
            Data.lastEncounterResetYMD = 0;
            Data.energyLastUnix = NowUnix();
            Data.energyRemainderSecs = 0f;

            // 6) Persist baseline directly (bypass Save() because Save() is guarded during wipe).
            // This ensures disk truth is correct before the scene reload.
            ForceWriteBaselineNow();

            // 7) If reloadFresh was requested, DO NOT set Data = null.
            // Leaving Data non-null prevents EncounterManager/UI from null-refing mid-frame.
            if (reloadFresh)
            {
                // Optional: keep IO minimal and rely on scene reload.
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"SaveManager.HardWipeAll failed: {e}");
        }
        finally
        {
            EndHardReset();
            GameEvents.HardResetting?.Invoke(false);
        }
    }

    private static void ForceWriteBaselineNow()
    {
        try
        {
            if (Data == null)
                Data = NewFreshPlayer();

            NormalizeBeforeSave();

            long now = NowUnix();
            if (Data.lastSavedUnix > 0 && now + 300 < Data.lastSavedUnix) now = Data.lastSavedUnix;
            Data.lastSavedUnix = Math.Max(Data.lastSavedUnix, now);

            var root = BuildRootForSave();
            string json = JsonUtility.ToJson(root, prettyPrint: true);
            SaveFiles.AtomicWriteUtf8(SavePath, json);
            SaveFiles.TryCopy(SavePath, BackupPath);
        }
        catch (Exception e)
        {
            Debug.LogError($"SaveManager.ForceWriteBaselineNow failed: {e}");
        }
    }

    // ─────────────────────────────────────────────
    // Normalization (single source of truth)
    // ─────────────────────────────────────────────

    private static void NormalizeAfterLoad()
    {
        EnsureDefaults();
        Data?.EnsureTransientSets();

        EnsureTrainingDefaults();

        PruneExpiredCaptureBands(saveIfChanged: true);
        PruneExpiredLures(saveIfChanged: true);
        PruneExpiredLuckBoosts(saveIfChanged: true);

        RebuildTransientSetsFromLists();

        EnsureTutorialFlagsLoaded();
    }

    private static void NormalizeBeforeSave()
    {
        EnsureDefaults();
        SyncListsFromSets();
    }

    // ─────────────────────────────────────────────
    // Combined save helpers
    // ─────────────────────────────────────────────

    private static PlayerSaveRoot BuildRootForSave()
    {
        var root = new PlayerSaveRoot();
        root.player = Data;

        // Tutorial flags
        EnsureTutorialFlagsLoaded();
        if (_tutorialSet != null)
            root.tutorialCompleted = new List<string>(_tutorialSet);

        // Job runtime + titles blobs
        root.jobRuntime = _jobRuntimeCache;
        root.titles = _titlesCache;
        root.worldEvents = _worldEventsCache;
        return root;
    }

    private static void LoadTutorialFromRoot(PlayerSaveRoot root)
    {
        _tutorialLoaded = true;
        _tutorialData = new TutorialFlagsData();
        _tutorialSet = new HashSet<string>(StringComparer.Ordinal);

        if (root?.tutorialCompleted == null) return;
        for (int i = 0; i < root.tutorialCompleted.Count; i++)
        {
            var k = root.tutorialCompleted[i];
            if (!string.IsNullOrWhiteSpace(k)) _tutorialSet.Add(k);
        }
    }

    private static PlayerSaveRoot MigrateFromLegacyOrCreateFresh()
    {
        var root = new PlayerSaveRoot();

        // 1) PlayerManager
        PlayerManager legacyPlayer = null;
        if (!TryLoad(LegacySavePath, out legacyPlayer))
            TryLoad(LegacyBackupPath, out legacyPlayer);
        root.player = legacyPlayer ?? NewFreshPlayer();

        // 2) Tutorial flags
        root.tutorialCompleted = ReadLegacyTutorialFlags();

        // 3) Job runtime
        root.jobRuntime = ReadLegacyJobRuntime();

        // 4) Titles
        root.titles = TitleSaveStore.TryLoadLegacyDirect();

        return root;
    }

    private static List<string> ReadLegacyTutorialFlags()
    {
        try
        {
            if (!SaveFiles.TryReadAllTextUtf8(LegacyTutorialFlagsPath, out var json) || string.IsNullOrWhiteSpace(json))
                return new List<string>();

            var tmp = JsonUtility.FromJson<LegacyTutorialFlagsData>(json);
            return tmp?.completed ?? new List<string>();
        }
        catch
        {
            return new List<string>();
        }
    }

    [Serializable]
    private sealed class LegacyTutorialFlagsData
    {
        public List<string> completed = new List<string>();
    }

    private static JobRuntimeSave ReadLegacyJobRuntime()
    {
        try
        {
            if (!SaveFiles.TryReadAllTextUtf8(LegacyJobRuntimePath, out var json) || string.IsNullOrWhiteSpace(json))
                return null;
            return JsonUtility.FromJson<JobRuntimeSave>(json);
        }
        catch
        {
            return null;
        }
    }

    private static PlayerManager NewFreshPlayer()
    {
        return new PlayerManager
        {
            playerId = Guid.NewGuid().ToString("N"),
            playerName = GeneratePlayerName(),

            encounterMax = 50,
            encounterCost = 1,
            lastEncounterResetYMD = TodayYMD(),
            lastSavedUnix = NowUnix(),

            energyLastUnix = NowUnix(),
            energyRemainderSecs = 0f,

            winStreak = 0,

            ownedIds = new HashSet<string>(),
            ownedIdsList = new List<string>(),

            seenTypes = new HashSet<MonsterType>(),
            seenTypesList = new List<MonsterType>(),

            unlockedJobSites = new HashSet<JobType>(),
            unlockedJobSitesList = new List<JobType>(),

            discoveredMonsterIds = new HashSet<string>(),
            discoveredMonsterIdsList = new List<string>(),

            team = new List<OwnedMonsterData>(),
            owned = new List<OwnedMonsterData>(),

            activeFlyers = new List<FlyerBiasData>(),
            activeWorkOrders = new List<WorkOrderData>(),
            activeFavorBoosts = new List<LuckBoostData>(),

            jobStorageUpgrades = new List<JobStorageUpgrade>(),
            jobAssignments = new List<JobAssignment>(),
            jobProgress = new List<JobProgress>(),

            fieldOps = new FieldOpsStats(),
            settings = new SettingsState()
        };
    }

    private static void EnsureDefaults()
    {
        if (Data == null)
        {
            Data = NewFreshPlayer();
            return;
        }

        // Lists
        Data.owned ??= new List<OwnedMonsterData>();
        Data.team ??= new List<OwnedMonsterData>();
        Data.activeFlyers ??= new List<FlyerBiasData>();
        Data.activeWorkOrders ??= new List<WorkOrderData>();
        Data.activeFavorBoosts ??= new List<LuckBoostData>();
        Data.jobAssignments ??= new List<JobAssignment>();
        Data.jobProgress ??= new List<JobProgress>();
        Data.jobStorageUpgrades ??= new List<JobStorageUpgrade>();
        Data.unlockedPacks ??= new List<string>();
        Data.resourceCounts ??= new List<int>();
        Data.preferredVariants ??= new List<PreferredVariantKV>();
        Data.activeJobMods ??= new List<JobGlobalMod>();
        Data.activeShinyBoosts ??= new List<ShinyBoostData>();
        Data.favoriteMonsterIdsList ??= new List<string>();
        Data.discoveredMonsterIdsList ??= new List<string>();

        Data.fieldOps ??= new FieldOpsStats();
        Data.settings ??= new SettingsState();

        // Identity
        if (string.IsNullOrEmpty(Data.playerId)) Data.playerId = Guid.NewGuid().ToString("N");
        if (string.IsNullOrEmpty(Data.playerName)) Data.playerName = GeneratePlayerName();

        // Encounter config
        if (Data.encounterMax <= 0) Data.encounterMax = 50;
        if (Data.encounterCost <= 0) Data.encounterCost = 1;

        // Balance migration: Encounter cost is a tuning value (not player preference).
        // If older saves have the previous default (5), migrate them to the new default (1).
        if (Data.encounterCost == 5) Data.encounterCost = 1;
        if (Data.lastEncounterResetYMD == 0) Data.lastEncounterResetYMD = TodayYMD();

        // Energy timing
        if (Data.energyLastUnix <= 0) Data.energyLastUnix = NowUnix();
        if (Data.energyRemainderSecs < 0f) Data.energyRemainderSecs = 0f;

        if (Data.winStreak < 0) Data.winStreak = 0;

        EnsureResourceCountsSized();

        // Sets
        Data.ownedIds ??= new HashSet<string>();
        Data.favoriteMonsterIds ??= new HashSet<string>();
        Data.discoveredMonsterIds ??= new HashSet<string>();
        Data.seenTypes ??= new HashSet<MonsterType>();
        Data.unlockedJobSites ??= new HashSet<JobType>();

        Data.ownedIdsList ??= new List<string>();
        Data.favoriteMonsterIdsList ??= new List<string>();
        Data.discoveredMonsterIdsList ??= new List<string>();
        Data.seenTypesList ??= new List<MonsterType>();
        Data.unlockedJobSitesList ??= new List<JobType>();
        Data.achievements ??= new List<AchievementProgressData>();
        Data.achievementMap ??= new Dictionary<string, AchievementProgressData>(StringComparer.Ordinal);
        Data.achievementMap.Clear();

        // Authoritative for persistence: LISTS.
        RebuildTransientSetsFromLists();

        // Normalize owned/team entries (uids, clamps, shiny normalization)
        NormalizeOwnedEntries(Data.owned);
        NormalizeOwnedEntries(Data.team);

        // Defensive: ensure ownedUIDs are unique in the OWNED list.
        // If duplicates exist (e.g., older bug or manual save edits), multiple
        // team slots can accidentally bind to the same underlying object.
        EnsureUniqueOwnedUIDs(Data.owned, null);

        // Defensive: ensure TEAM entries don't contain ownedUID collisions that
        // point at the wrong monster. If a team entry's ownedUID collides with
        // a different monsterId, clear it so canonicalization falls back to the
        // safer monsterId/unique matching below.
        EnsureTeamOwnedUidMatchesMonsterId(Data.team, Data.owned);

        for (int i = 0; i < Data.achievements.Count; i++)
        {
            var a = Data.achievements[i];
            if (a == null || string.IsNullOrEmpty(a.id)) continue;
            if (!Data.achievementMap.ContainsKey(a.id))
                Data.achievementMap.Add(a.id, a);
        }

        // Ensure team entries reference the owned instance (canonicalize)
        // IMPORTANT: Team slots can contain duplicate monsterIds. We must map each slot to a UNIQUE owned instance.
        // Otherwise multiple slots can end up sharing the same reference, causing HP/heal to affect multiple cards.
        var ownedUidMap = new Dictionary<string, OwnedMonsterData>(StringComparer.Ordinal);
        var ownedByMonsterId = new Dictionary<string, List<OwnedMonsterData>>(StringComparer.Ordinal);
        var usedOwnedUids = new HashSet<string>(StringComparer.Ordinal);

        if (Data.owned != null)
        {
            for (int oi = 0; oi < Data.owned.Count; oi++)
            {
                var o = Data.owned[oi];
                if (o == null || string.IsNullOrEmpty(o.monsterId)) continue;

                if (!string.IsNullOrEmpty(o.ownedUID) && !ownedUidMap.ContainsKey(o.ownedUID))
                    ownedUidMap.Add(o.ownedUID, o);

                if (!ownedByMonsterId.TryGetValue(o.monsterId, out var list))
                {
                    list = new List<OwnedMonsterData>();
                    ownedByMonsterId.Add(o.monsterId, list);
                }
                list.Add(o);
            }
        }

        for (int i = 0; i < Data.team.Count; i++)
        {
            var t = Data.team[i];
            if (t == null || string.IsNullOrEmpty(t.monsterId)) continue;

            OwnedMonsterData canonical = null;

            // 1) Strong match: ownedUID (preferred)
            if (!string.IsNullOrEmpty(t.ownedUID) && ownedUidMap.TryGetValue(t.ownedUID, out var byUid))
            {
                canonical = byUid;
            }
            else
            {
                // 2) Weak match: monsterId BUT pick an UNUSED owned instance for this slot.
                if (ownedByMonsterId.TryGetValue(t.monsterId, out var candidates))
                {
                    for (int ci = 0; ci < candidates.Count; ci++)
                    {
                        var cand = candidates[ci];
                        if (cand == null) continue;
                        if (string.IsNullOrEmpty(cand.ownedUID)) continue;
                        if (usedOwnedUids.Contains(cand.ownedUID)) continue;

                        canonical = cand;
                        break;
                    }
                }
            }

            // 3) If we still didn't find a unique owned instance, clone the TEAM entry into OWNED.
            if (canonical == null)
            {
                try
                {
                    canonical = JsonUtility.FromJson<OwnedMonsterData>(JsonUtility.ToJson(t));
                }
                catch
                {
                    canonical = t;
                }

                if (canonical == null) canonical = t;

                if (string.IsNullOrEmpty(canonical.ownedUID))
                    canonical.ownedUID = Guid.NewGuid().ToString("N");

                Data.owned ??= new List<OwnedMonsterData>();
                Data.owned.Add(canonical);

                // update maps so subsequent slots can find it
                if (!ownedUidMap.ContainsKey(canonical.ownedUID))
                    ownedUidMap.Add(canonical.ownedUID, canonical);

                if (!ownedByMonsterId.TryGetValue(canonical.monsterId, out var list))
                {
                    list = new List<OwnedMonsterData>();
                    ownedByMonsterId.Add(canonical.monsterId, list);
                }
                list.Add(canonical);
            }

            // Prefer TEAM HP snapshot if present (prevents losing battle damage on load)
            if (t.currentHP >= 0)
                canonical.currentHP = t.currentHP;

            // Ensure canonical ownedUID exists
            if (string.IsNullOrEmpty(canonical.ownedUID))
                canonical.ownedUID = Guid.NewGuid().ToString("N");

            usedOwnedUids.Add(canonical.ownedUID);

            // Replace team entry with canonical owned instance (unique per slot)
            Data.team[i] = canonical;

            if (!string.IsNullOrEmpty(canonical.monsterId))
                Data.ownedIds.Add(canonical.monsterId);
        }

        // Training pointer default
        if (string.IsNullOrEmpty(Data.trainingMonsterId) && Data.team.Count > 0)
        {
            Data.trainingMonsterId = Data.team[0]?.monsterId;
            var om = Data.owned.Find(o => o != null && o.monsterId == Data.trainingMonsterId);
            if (om != null) om.isTraining = true;
        }

        if (Data.lastSavedUnix <= 0) Data.lastSavedUnix = NowUnix();
    }

    // ─────────────────────────────────────────────
    // Defensive UID de-dupe helpers
    // ─────────────────────────────────────────────

    /// <summary>
    /// Ensures ownedUIDs are unique within the provided list.
    /// If global is provided, uniqueness is enforced against that set too.
    /// </summary>
    private static void EnsureUniqueOwnedUIDs(List<OwnedMonsterData> list, HashSet<string> global)
    {
        if (list == null) return;

        global ??= new HashSet<string>(StringComparer.Ordinal);

        for (int i = 0; i < list.Count; i++)
        {
            var m = list[i];
            if (m == null) continue;

            if (string.IsNullOrEmpty(m.ownedUID))
                m.ownedUID = Guid.NewGuid().ToString("N");

            // If this UID is already used, regenerate.
            if (global.Contains(m.ownedUID))
                m.ownedUID = Guid.NewGuid().ToString("N");

            global.Add(m.ownedUID);
        }
    }

    /// <summary>
    /// If a TEAM entry's ownedUID points at an OWNED entry with a different monsterId,
    /// clear the team's ownedUID to prevent cross-binding.
    /// </summary>
    private static void EnsureTeamOwnedUidMatchesMonsterId(List<OwnedMonsterData> team, List<OwnedMonsterData> owned)
    {
        if (team == null || owned == null) return;

        // Build UID → monsterId map from owned list.
        var uidToId = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 0; i < owned.Count; i++)
        {
            var o = owned[i];
            if (o == null) continue;
            if (string.IsNullOrEmpty(o.ownedUID) || string.IsNullOrEmpty(o.monsterId)) continue;
            if (!uidToId.ContainsKey(o.ownedUID))
                uidToId.Add(o.ownedUID, o.monsterId);
        }

        for (int i = 0; i < team.Count; i++)
        {
            var t = team[i];
            if (t == null) continue;
            if (string.IsNullOrEmpty(t.ownedUID) || string.IsNullOrEmpty(t.monsterId)) continue;

            if (uidToId.TryGetValue(t.ownedUID, out var id) && !string.Equals(id, t.monsterId, StringComparison.Ordinal))
            {
                // Clear to force safer matching below (by UID if corrected later, else by unique monsterId).
                t.ownedUID = null;
            }
        }
    }

    private static void EnsureResourceCountsSized()
    {
        if (Data == null) return;
        Data.resourceCounts ??= new List<int>();

        int need = 0;
        foreach (ResourceType t in Enum.GetValues(typeof(ResourceType)))
            need = Mathf.Max(need, (int)t + 1);

        while (Data.resourceCounts.Count < need)
            Data.resourceCounts.Add(0);
    }

    private static void NormalizeOwnedEntries(List<OwnedMonsterData> list)
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

	            // HP invariant:
	            // - currentHP should never be negative in persisted data.
	            // - legacy saves used -1 to mean "uninitialized" (treat as full HP).
	            // Normalize any negative HP to a sensible full-health value.
	            if (om.currentHP < 0)
	                om.currentHP = ResolveFullHPFor(om);

	            // Absolute floor: never allow negative.
	            if (om.currentHP < 0) om.currentHP = 0;

            if (string.IsNullOrEmpty(om.ownedUID))
                om.ownedUID = Guid.NewGuid().ToString("N");

            // ✅ Shiny normalization (supports legacy / partial saves)
            // - shinyTier > 0 implies isShiny
            // - isShiny implies at least shinyTier 1
            if (om.shinyTier > 0 && !om.isShiny) om.isShiny = true;
            if (om.isShiny && om.shinyTier <= 0) om.shinyTier = 1;

            if (ReferenceEquals(list, Data.owned) && !string.IsNullOrEmpty(om.monsterId))
                Data.ownedIds.Add(om.monsterId);
        }
    }

	    private static int ResolveFullHPFor(OwnedMonsterData om)
	    {
	        if (om == null) return 1;
	        if (string.IsNullOrEmpty(om.monsterId)) return 1;

	        var def = MonsterLibraryLocator.GetById(om.monsterId);
	        if (def == null) return 1;

	        // Menu-safe HP (includes training, excludes battle-only title conditionals).
	        return HealingService.CalcMaxHP(def, Mathf.Max(1, om.level), includeTraining: true, includeTitles: false);
	    }

    // ─────────────────────────────────────────────
    // ownedUID collision guards
    // ─────────────────────────────────────────────

    /// <summary>
    /// Ensures every entry in the list has a unique ownedUID.
    /// If duplicates are found, the later duplicates are assigned a new GUID.
    /// This prevents UI binding and team canonicalization from accidentally
    /// pointing multiple monsters at the same underlying instance.
    /// </summary>
    private static void EnsureUniqueOwnedUIDs_Legacy(List<OwnedMonsterData> list, HashSet<string> seen)
    {
        if (list == null) return;
        seen ??= new HashSet<string>(StringComparer.Ordinal);

        for (int i = 0; i < list.Count; i++)
        {
            var om = list[i];
            if (om == null) continue;

            if (string.IsNullOrEmpty(om.ownedUID))
                om.ownedUID = Guid.NewGuid().ToString("N");

            // If ownedUID collides, re-key this entry.
            if (seen.Contains(om.ownedUID))
                om.ownedUID = Guid.NewGuid().ToString("N");

            seen.Add(om.ownedUID);
        }
    }

    /// <summary>
    /// If a team entry's ownedUID points to an owned entry with a different monsterId,
    /// clear the team ownedUID so later canonicalization prefers safer matching.
    /// </summary>
    private static void EnsureTeamOwnedUidMatchesMonsterId_Legacy(List<OwnedMonsterData> team, List<OwnedMonsterData> owned)
    {
        if (team == null || owned == null) return;

        for (int i = 0; i < team.Count; i++)
        {
            var t = team[i];
            if (t == null) continue;
            if (string.IsNullOrEmpty(t.ownedUID) || string.IsNullOrEmpty(t.monsterId)) continue;

            var match = owned.Find(o => o != null && o.ownedUID == t.ownedUID);
            if (match == null) continue;

            if (!string.Equals(match.monsterId, t.monsterId, StringComparison.Ordinal))
            {
                // Break the incorrect binding.
                t.ownedUID = null;
            }
        }
    }

    private static void EnsureTrainingDefaults()
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

            // ✅ Shiny normalization here too (defensive)
            if (om.shinyTier > 0 && !om.isShiny) om.isShiny = true;
            if (om.isShiny && om.shinyTier <= 0) om.shinyTier = 1;
        }
    }

    // ─────────────────────────────────────────────
    // Tutorial flags (SaveManager-owned persistence)
    // ─────────────────────────────────────────────

    [Serializable]
    private sealed class TutorialFlagsData
    {
        public List<string> completed = new List<string>();
    }

    private static TutorialFlagsData _tutorialData;
    private static HashSet<string> _tutorialSet;
    private static bool _tutorialLoaded;

    private static void EnsureTutorialFlagsLoaded()
    {
        if (_tutorialLoaded) return;
        _tutorialLoaded = true;

        _tutorialData = new TutorialFlagsData();
        _tutorialSet = new HashSet<string>(StringComparer.Ordinal);

        // Combined-save model: tutorial flags are hydrated from PlayerSaveRoot during LoadOrCreate().
        // If EnsureTutorialFlagsLoaded is called before LoadOrCreate(), we simply start empty.
    }

    private static void SaveTutorialFlagsFile()
    {
        if (IsHardWiping) return;
        if (_tutorialSet == null) return;

        // Combined-save model: tutorial flags are persisted as part of PlayerSave.json.
        Save();
    }

    public static bool IsTutorialComplete(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return false;
        EnsureTutorialFlagsLoaded();
        return _tutorialSet != null && _tutorialSet.Contains(key);
    }

    public static void SetTutorialComplete(string key, bool done)
    {
        if (IsHardWiping) return;
        if (string.IsNullOrWhiteSpace(key)) return;

        EnsureTutorialFlagsLoaded();
        _tutorialSet ??= new HashSet<string>(StringComparer.Ordinal);

        bool changed = done ? _tutorialSet.Add(key) : _tutorialSet.Remove(key);
        if (!changed) return;

        SaveTutorialFlagsFile();
    }

    public static void ClearTutorialFlags()
    {
        _tutorialLoaded = true;
        _tutorialData = new TutorialFlagsData();
        _tutorialSet = new HashSet<string>(StringComparer.Ordinal);

        // Also remove legacy file if present.
        SaveFiles.TryDelete(LegacyTutorialFlagsPath);
        SaveFiles.TryDelete(LegacyTutorialFlagsPath + ".tmp");
    }

    // ─────────────────────────────────────────────
    // CRITICAL: mirror sync helpers
    // ─────────────────────────────────────────────

    private static void RebuildTransientSetsFromLists()
    {
        if (Data == null) return;

        Data.ownedIds ??= new HashSet<string>();
        Data.favoriteMonsterIds ??= new HashSet<string>();
        Data.discoveredMonsterIds ??= new HashSet<string>();
        Data.seenTypes ??= new HashSet<MonsterType>();
        Data.unlockedJobSites ??= new HashSet<JobType>();

        Data.ownedIds.Clear();
        if (Data.ownedIdsList != null)
        {
            for (int i = 0; i < Data.ownedIdsList.Count; i++)
            {
                var id = Data.ownedIdsList[i];
                if (!string.IsNullOrEmpty(id)) Data.ownedIds.Add(id);
            }
        }

        Data.favoriteMonsterIds.Clear();
        if (Data.favoriteMonsterIdsList != null)
        {
            for (int i = 0; i < Data.favoriteMonsterIdsList.Count; i++)
            {
                var id = Data.favoriteMonsterIdsList[i];
                if (!string.IsNullOrEmpty(id)) Data.favoriteMonsterIds.Add(id);
            }
        }

        Data.discoveredMonsterIds.Clear();
        if (Data.discoveredMonsterIdsList != null)
        {
            for (int i = 0; i < Data.discoveredMonsterIdsList.Count; i++)
            {
                var id = Data.discoveredMonsterIdsList[i];
                if (!string.IsNullOrEmpty(id)) Data.discoveredMonsterIds.Add(id);
            }
        }

        Data.seenTypes.Clear();
        if (Data.seenTypesList != null)
        {
            for (int i = 0; i < Data.seenTypesList.Count; i++)
                Data.seenTypes.Add(Data.seenTypesList[i]);
        }

        Data.unlockedJobSites.Clear();
        if (Data.unlockedJobSitesList != null)
        {
            for (int i = 0; i < Data.unlockedJobSitesList.Count; i++)
                Data.unlockedJobSites.Add(Data.unlockedJobSitesList[i]);
        }
    }

    private static void SyncListsFromSets()
    {
        if (Data == null) return;

        // owned ids
        Data.ownedIdsList ??= new List<string>();
        Data.ownedIdsList.Clear();
        if (Data.ownedIds != null)
        {
            foreach (var id in Data.ownedIds)
                if (!string.IsNullOrEmpty(id)) Data.ownedIdsList.Add(id);
            Data.ownedIdsList.Sort(StringComparer.Ordinal);
        }

        // favorites
        Data.favoriteMonsterIdsList ??= new List<string>();
        Data.favoriteMonsterIdsList.Clear();
        if (Data.favoriteMonsterIds != null)
        {
            foreach (var id in Data.favoriteMonsterIds)
                if (!string.IsNullOrEmpty(id)) Data.favoriteMonsterIdsList.Add(id);
            Data.favoriteMonsterIdsList.Sort(StringComparer.Ordinal);
        }

        // discovered
        Data.discoveredMonsterIdsList ??= new List<string>();
        Data.discoveredMonsterIdsList.Clear();
        if (Data.discoveredMonsterIds != null)
        {
            foreach (var id in Data.discoveredMonsterIds)
                if (!string.IsNullOrEmpty(id)) Data.discoveredMonsterIdsList.Add(id);
            Data.discoveredMonsterIdsList.Sort(StringComparer.Ordinal);
        }

        // seen types
        Data.seenTypesList ??= new List<MonsterType>();
        Data.seenTypesList.Clear();
        if (Data.seenTypes != null)
            foreach (var t in Data.seenTypes) Data.seenTypesList.Add(t);

        // unlocked sites
        Data.unlockedJobSitesList ??= new List<JobType>();
        Data.unlockedJobSitesList.Clear();
        if (Data.unlockedJobSites != null)
            foreach (var j in Data.unlockedJobSites) Data.unlockedJobSitesList.Add(j);
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

    private static bool PruneExpiredLures(bool saveIfChanged)
    {
        if (IsHardWiping) return false;
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

    private static bool PruneExpiredCaptureBands(bool saveIfChanged)
    {
        if (IsHardWiping) return false;
        if (Data?.activeWorkOrders == null || Data.activeWorkOrders.Count == 0) return false;

        long now = NowUnix();
        int before = Data.activeWorkOrders.Count;

        Data.activeWorkOrders.RemoveAll(b => b != null && b.expireUnix <= now);

        bool changed = Data.activeWorkOrders.Count != before;
        if (saveIfChanged && changed) Save();
        return changed;
    }

    private static bool PruneExpiredLuckBoosts(bool saveIfChanged)
    {
        if (IsHardWiping) return false;
        if (Data?.activeFavorBoosts == null || Data.activeFavorBoosts.Count == 0) return false;

        long now = NowUnix();
        int before = Data.activeFavorBoosts.Count;

        Data.activeFavorBoosts.RemoveAll(b => b != null && b.expireUnix <= now);

        bool changed = Data.activeFavorBoosts.Count != before;
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

    // ─────────────────────────────────────────────
    // Owned monster helpers
    // ─────────────────────────────────────────────

    /// <summary>
    /// Returns the first owned monster entry matching the provided ownedUID.
    /// This is a convenience helper used by systems that need stable identity
    /// across Owned vs Team lists.
    /// </summary>
    public static OwnedMonsterData GetOwnedByUid(string ownedUid)
    {
        if (string.IsNullOrEmpty(ownedUid)) return null;
        if (Data == null || Data.owned == null) return null;

        for (int i = 0; i < Data.owned.Count; i++)
        {
            var e = Data.owned[i];
            if (e == null) continue;
            if (!string.IsNullOrEmpty(e.ownedUID) && e.ownedUID == ownedUid)
                return e;
        }

        return null;
    }

    private static bool TryLoad(string path, out PlayerManager data)
    {
        data = null;

        if (!SaveFiles.TryReadAllTextUtf8(path, out var json)) return false;
        if (string.IsNullOrWhiteSpace(json)) return false;

        try
        {
            data = JsonUtility.FromJson<PlayerManager>(json);
            return data != null;
        }
        catch
        {
            data = null;
            return false;
        }
    }

    private static bool TryLoad(string path, out PlayerSaveRoot root)
    {
        root = null;
        if (!SaveFiles.TryReadAllTextUtf8(path, out var json)) return false;
        if (string.IsNullOrWhiteSpace(json)) return false;

        try
        {
            root = JsonUtility.FromJson<PlayerSaveRoot>(json);
            return root != null;
        }
        catch
        {
            root = null;
            return false;
        }
    }

    // ─────────────────────────────────────────────
    // Starter granting
    // ─────────────────────────────────────────────

    public static bool HasStarter() => Data != null && Data.hasChosenStarter;

    public static void GrantStarter(string monsterId, int level = 1)
    {
        if (IsHardWiping) return;
        if (string.IsNullOrEmpty(monsterId)) return;

        if (!_loaded) LoadOrCreate();
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
                ownedUID = Guid.NewGuid().ToString("N"),

                // ensure non-shiny starter by default
                isShiny = false,
                shinyTier = 0
            };
            Data.owned.Add(ownedMonster);
        }
        else
        {
            if (ownedMonster.level <= 0) ownedMonster.level = Mathf.Max(1, level);
            if (ownedMonster.currentHP == 0) ownedMonster.currentHP = -1;
            if (string.IsNullOrEmpty(ownedMonster.ownedUID))
                ownedMonster.ownedUID = Guid.NewGuid().ToString("N");

            // normalize shiny fields in case older data is partially filled
            if (ownedMonster.shinyTier > 0 && !ownedMonster.isShiny) ownedMonster.isShiny = true;
            if (ownedMonster.isShiny && ownedMonster.shinyTier <= 0) ownedMonster.shinyTier = 1;
        }

        bool onTeam = Data.team.Exists(t => t != null && t.ownedUID == ownedMonster.ownedUID);
        if (!onTeam)
        {
            Data.team.Add(ownedMonster);
            if (string.IsNullOrEmpty(Data.trainingMonsterId))
                Data.trainingMonsterId = monsterId;
        }

        Data.ownedIds.Add(monsterId);
        Discover(monsterId, save: false);

        Data.hasChosenStarter = true;

        var def = MonsterLibraryLocator.GetById(monsterId);
        if (def != null)
        {
            Data.seenTypes.Add(def.type);
            GameEvents.StarterChosen?.Invoke(def.type);
        }

        Save();
    }

    // ─────────────────────────────────────────────
    // Job runtime sidecar I/O
    // ─────────────────────────────────────────────

    public static void SaveJobRuntime(JobRuntimeSave blob)
    {
        if (IsHardWiping) return;
        _jobRuntimeCache = blob;
        Save();
    }

    public static JobRuntimeSave LoadJobRuntime()
    {
        return _jobRuntimeCache;
    }

    // ─────────────────────────────────────────────
    // Titles blob (used by TitleSaveStore facade)
    // ─────────────────────────────────────────────

    public static TitleSaveData GetTitlesBlob() => _titlesCache;

    public static void SetTitlesBlob(TitleSaveData data)
    {
        if (IsHardWiping) return;
        _titlesCache = data;
        Save();
    }

    // ─────────────────────────────────────────────
    // World Events blob
    // ─────────────────────────────────────────────

    public static WorldEventSaveData GetWorldEventsBlob() => _worldEventsCache;

    public static void SetWorldEventsBlob(WorldEventSaveData data)
    {
        if (IsHardWiping) return;
        _worldEventsCache = data;
        Save();
    }

    // ─────────────────────────────────────────────
    // Story gate
    // ─────────────────────────────────────────────

    public static bool HasSeenStory() => Data != null && Data.hasSeenStory;

    public static void MarkStorySeen()
    {
        if (IsHardWiping) return;
        if (!_loaded) LoadOrCreate();
        if (Data == null) return;
        Data.hasSeenStory = true;
        Save();
    }

    public static bool UnlockJobSite(JobType site, bool save = true, bool fireEvent = true)
    {
        if (IsHardWiping) return false;
        if (!_loaded) LoadOrCreate();
        if (Data == null) return false;

        Data.unlockedJobSites ??= new HashSet<JobType>();
        Data.unlockedJobSitesList ??= new List<JobType>();

        bool addedSet = Data.unlockedJobSites.Add(site);
        bool addedList = false;

        if (!Data.unlockedJobSitesList.Contains(site))
        {
            Data.unlockedJobSitesList.Add(site);
            addedList = true;
        }

        if (save && (addedSet || addedList))
            Save();

        if (fireEvent && (addedSet || addedList))
            GameEvents.OnJobsChanged?.Invoke();

        return addedSet || addedList;
    }

    // ─────────────────────────────────────────────
    // File helper (isolates I/O + error handling)
    // ─────────────────────────────────────────────

    private static class SaveFiles
    {
        public static void EnsureFolder(string anyFilePath)
        {
            try
            {
                var dir = Path.GetDirectoryName(anyFilePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
            }
            catch { }
        }

        public static bool TryReadAllTextUtf8(string path, out string text)
        {
            text = null;
            try
            {
                if (!File.Exists(path)) return false;
                text = File.ReadAllText(path, Encoding.UTF8);
                return true;
            }
            catch
            {
                text = null;
                return false;
            }
        }

        public static void AtomicWriteUtf8(string path, string contents)
        {
            EnsureFolder(path);

            string tmp = path + ".tmp";
            File.WriteAllText(tmp, contents ?? string.Empty, Encoding.UTF8);

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

        public static void TryCopy(string src, string dst)
        {
            try
            {
                if (File.Exists(src))
                    File.Copy(src, dst, overwrite: true);
            }
            catch { }
        }

        public static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch { }
        }
    }
}