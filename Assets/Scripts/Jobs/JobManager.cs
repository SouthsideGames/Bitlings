using UnityEngine;
using UnityEngine.Serialization;
using System;
using System.Linq;
using System.Collections.Generic;

[Serializable]
public class JobProgress
{
    public JobType job;
    public int level;
    public int currentXP;
    public int maxXPForLevel;
}

[Serializable]
public class WorkerRef
{
    // NOTE:
    // - ownedUID is the stable identity for an owned instance (survives evolution).
    // - monsterId is the current species id (can change on evolution).
    public string monsterId;
    public string ownedUID;

    public MonsterDataSO def;
}

[Serializable]
public class BlessingBuff
{
    public JobType job;
    public int flatBonus;
    public long untilUnix;
}

/// <summary>Runtime state for a single job site.</summary>
[Serializable]
public class JobSiteState
{
    public JobSiteSO config;
    public List<WorkerRef> workers = new List<WorkerRef>();

    // Per-slot fatigue and cooldown (0..1 fatigue, unix seconds for cooldown)
    public float[] slotFatigue01 = new float[3];
    public long[] slotCooldownUntilUnix = new long[3];

    // Legacy (kept to avoid null refs in any old UI; not used for production calc)
    [Range(0f, 1f)] public float fatigue01;

    public bool allowClinicRelief = true;

    // Production bookkeeping
    public float storedAmount;
    public float cachedRatePerHour;

    [Range(1, 3)] public int level = 1;
    public int currentXP = 0;
    public int maxXPForLevel = 20;
}

public sealed class JobManager : MonoBehaviour
{
    public static JobManager I;

    // ---------------------------- Config / Inspector ----------------------------
    [Header("Config")]
    [SerializeField] private List<JobSiteSO> jobSites = new();

    [Header("Runtime")]
    [SerializeField] private float tickSeconds = 1f;

    [Header("Unlocks")]
    [SerializeField] private bool lockSitesUntilEligible = true;

    [Header("Fatigue Tunables (slot rest decay)")]
    [SerializeField] private float siteRestDecayPerHour = 0.05f;   // decay when slot empty

    [Header("Clinic Relief (optional; reduces slot fatigue)")]
    [SerializeField] private float reliefPerCharge = 0.01f;
    [SerializeField] private float maxReliefPerHourPerSite = 0.05f;

    [Header("Offline Simulation")]
    [SerializeField] private bool simulateOfflineOnLoad = true;
    [SerializeField, Min(0f)] private float offlineSimMultiplier = 1f;
    [SerializeField, Min(60f)] private float offlineChunkSeconds = 1200f;

    [Header("Shiny Team Bonus (pre-fatigue)")]
    [SerializeField] private float shiny1Bonus = 0.03f;
    [SerializeField] private float shiny2Bonus = 0.07f;
    [SerializeField] private float shiny3Bonus = 0.12f;

    [Header("Starter Fallback Unlocks")]
    [SerializeField] private bool enableStarterDefaultSitesFallback = true;

    [Tooltip("Unlocked if the starter type maps to zero sites (edge case).")]
    [SerializeField]
    private List<JobType> starterDefaultSites = new List<JobType>
    {
        JobType.Gym,
        JobType.Quarry,
        JobType.PowerPlant
    };

#if UNITY_EDITOR
    [Header("Debug (Editor Only)")]
    [SerializeField] private bool logProductionBreakdown = false;
#endif

    // ---------------------------- State ----------------------------
    public readonly List<JobSiteState> States = new();

    // Per-monster cooldown (key = ownedUID when possible; else monsterId). Persisted via SaveManager sidecar.
    private readonly Dictionary<string, long> _cooldownUntil = new();

    private List<BlessingBuff> _blessingBuffs = new List<BlessingBuff>();
    private readonly Dictionary<string, MonsterDataSO> _idToDef = new();
    private readonly Dictionary<string, long> _assignedUnix = new();
    private Dictionary<JobType, float> _auraByJob = new Dictionary<JobType, float>(16);

    private float _accum;

    // Settings mirrors (live-loaded from SettingsManager JSON)
    private bool autoBenchEnabled = true;
    private float autoBenchHPThreshold01 = 0.20f;
    private bool autoBenchAutoFill = true;
    private bool autoReliefEnabled = true;

    // ---------------------------- Unity lifecycle ----------------------------
    private void Awake()
    {
        if (I != null && I != this) { Destroy(gameObject); return; }
        I = this;

        PullSettings();
        if (SettingsManager.I) SettingsManager.I.OnSettingsChanged += PullSettings;

        if (lockSitesUntilEligible && !SaveManager.IsHardResetting)
            RecalculateUnlocksFromSeenTypes();


        BuildDefIndex();
        InitStates();
        LoadProgressFromSave();
        LoadAssignmentsFromSave();

        // Load slot fatigue + cooldowns from sidecar file
        LoadRuntimeFromSave();

        // IMPORTANT: After load, re-resolve all worker defs from their keys so:
        // - ownedUID workers show the CURRENT species after evolution
        // - stale / invalid keys are removed
        SanitizeAndRefreshWorkersFromSaveKeys(saveIfChanged: true);

        // Ensure unlocks reflect both capture history and purchased features.
        if (lockSitesUntilEligible) RecalculateUnlocksFromSeenTypes();

        if (simulateOfflineOnLoad) ResolveOfflineIfAny();

        RefreshAllJobSiteViewsInScene();
    }

    private void OnEnable()
    {
        GameEvents.StarterChosen += OnStarterChosen;
        GameEvents.MonsterCaptured += OnMonsterCaptured;
        GameEvents.JobGlobalModsChanged += OnJobModsChanged;

        // When evolution occurs, ownedUID stays stable but monsterId changes.
        // We refresh worker refs so job UI and production uses the new def.
        GameEvents.MonsterEvolved += HandleMonsterEvolved;

        // Also refresh if save reload/hard wipe occurs in-session (safe no-op if you don’t fire this event).
        GameEvents.OnSaveReloaded += HandleSaveReloaded;
    }

    private void OnDisable()
    {
        GameEvents.StarterChosen -= OnStarterChosen;
        GameEvents.MonsterCaptured -= OnMonsterCaptured;
        GameEvents.JobGlobalModsChanged -= OnJobModsChanged;
        GameEvents.MonsterEvolved -= HandleMonsterEvolved;
        GameEvents.OnSaveReloaded -= HandleSaveReloaded;

        if (SettingsManager.I) SettingsManager.I.OnSettingsChanged -= PullSettings;

        // Persist runtime on destroy to be safe
        SaveRuntimeToSave();
    }

    private void Update()
    {
        if (SaveManager.IsHardWiping) return;
        _accum += Time.unscaledDeltaTime;
        if (_accum >= tickSeconds)
        {
            Produce(_accum);
            _accum = 0f;
        }
    }

    // ---------------------------- Public: Purchasable unlock bridge ----------------------------

    /// <summary>
    /// Public API for upgrades / admin usage: force-unlock a job site.
    /// </summary>
    public void ForceUnlock(JobType job)
    {
        JobUnlockBridge.UnlockJob(job, syncFeatureUnlock: true);
    }

    // ---------------------------- Global change hooks ----------------------------
    private void OnJobModsChanged()
    {
        RefreshAllJobSiteViewsInScene();
        GameEvents.OnJobsChanged?.Invoke();
    }

    private void HandleMonsterEvolved(string newDefId)
    {
        // Keep it cheap: only re-resolve defs from keys (ownedUID -> current monsterId -> def).
        // Also cleans invalid keys.
        SanitizeAndRefreshWorkersFromSaveKeys(saveIfChanged: true);
        RefreshAllJobSiteViewsInScene();
        GameEvents.OnJobsChanged?.Invoke();
    }

    private void HandleSaveReloaded()
    {
        // If SaveManager.HardWipeAll(reloadFresh:true) happens, JobManager should reload cleanly.
        PullSettings();
        BuildDefIndex();

        LoadProgressFromSave();
        LoadAssignmentsFromSave();
        LoadRuntimeFromSave();

        SanitizeAndRefreshWorkersFromSaveKeys(saveIfChanged: false);
        if (lockSitesUntilEligible) RecalculateUnlocksFromSeenTypes();

        RefreshAllJobSiteViewsInScene();
        GameEvents.OnJobsChanged?.Invoke();
    }

    // ---------------------------- Initialization ----------------------------
    private void BuildDefIndex()
    {
        _idToDef.Clear();
        var lib = MonsterLibraryLocator.Lib;
        if (lib?.monsters == null) return;

        foreach (var def in lib.monsters)
        {
            if (!def || string.IsNullOrEmpty(def.id)) continue;
            if (!_idToDef.ContainsKey(def.id)) _idToDef.Add(def.id, def);
        }
    }

    private void InitStates()
    {
        States.Clear();

        foreach (var so in jobSites)
        {
            if (!so) continue;

            var st = new JobSiteState { config = so, storedAmount = 0f };

            int cap = Mathf.Clamp(so.maxWorkers, 1, 3);
            for (int i = 0; i < cap; i++) st.workers.Add(null);

            st.slotFatigue01 = new float[cap];
            st.slotCooldownUntilUnix = new long[cap];

            st.level = Mathf.Max(1, st.level);
            st.maxXPForLevel = JobLeveling.MaxXpForLevel(so.jobType, st.level);
            st.currentXP = Mathf.Clamp(st.currentXP, 0, st.maxXPForLevel);

            States.Add(st);
        }
    }

    private void EnsureWorkerListSize(JobSiteState s, int size)
    {
        while (s.workers.Count < size) s.workers.Add(null);
        while (s.workers.Count > size) s.workers.RemoveAt(s.workers.Count - 1);

        if (s.slotFatigue01 == null || s.slotFatigue01.Length != size)
        {
            var nf = new float[size];
            if (s.slotFatigue01 != null) Array.Copy(s.slotFatigue01, nf, Mathf.Min(nf.Length, s.slotFatigue01.Length));
            s.slotFatigue01 = nf;
        }
        if (s.slotCooldownUntilUnix == null || s.slotCooldownUntilUnix.Length != size)
        {
            var nc = new long[size];
            if (s.slotCooldownUntilUnix != null) Array.Copy(s.slotCooldownUntilUnix, nc, Mathf.Min(nc.Length, s.slotCooldownUntilUnix.Length));
            s.slotCooldownUntilUnix = nc;
        }
    }

    // ---------------------------- Tick / Produce ----------------------------
    private void Produce(float dtSeconds)
    {
        float dtHours = dtSeconds / 3600f;

        try { _auraByJob = TitlesAdapter.BuildJobAuras(SaveManager.Data?.team) ?? new Dictionary<JobType, float>(); }
        catch { _auraByJob = new Dictionary<JobType, float>(); }

        if (autoBenchEnabled) AutoBenchSweep(autoBenchHPThreshold01);

        for (int si = 0; si < States.Count; si++)
        {
            var s = States[si];
            if (s?.config == null) continue;

            // Opportunistic refresh: if a worker ref is keyed by ownedUID but def is stale/null, fix it.
            // This helps if any edge case evolved without event propagation.
            RefreshSiteWorkerDefsIfNeeded(s);

            float grossRateHr = ComputeRatePerHour(s);

            ApplyPerSlotFatigue(dtHours, s);

            float avgFatigue = AverageWorkingSlotFatigue(s);
            float finalRateHr = grossRateHr * (1f - Mathf.Clamp01(avgFatigue));
            s.cachedRatePerHour = finalRateHr;

            s.storedAmount = Mathf.Min(GetEffectiveStorageCap(s.config), s.storedAmount + finalRateHr * dtHours);
        }

        if (autoReliefEnabled) ApplyClinicRelief(dtHours);

        SaveRuntimeToSave();
    }

    private static float AverageWorkingSlotFatigue(JobSiteState s)
    {
        if (s == null || s.workers == null || s.slotFatigue01 == null) return 0f;
        float sum = 0f; int count = 0;
        int cap = Mathf.Min(s.workers.Count, s.slotFatigue01.Length);
        for (int i = 0; i < cap; i++)
        {
            var w = s.workers[i];
            if (w?.def == null) continue;
            sum += Mathf.Clamp01(s.slotFatigue01[i]);
            count++;
        }
        return count > 0 ? sum / count : 0f;
    }

    private void ApplyPerSlotFatigue(float dtHours, JobSiteState s)
    {
        int cap = Mathf.Clamp(s.config.maxWorkers, 1, 3);
        EnsureWorkerListSize(s, cap);

        for (int i = 0; i < cap; i++)
        {
            var w = s.workers[i];

            if (w?.def == null)
            {
                if (s.slotFatigue01[i] > 0f)
                    s.slotFatigue01[i] = Mathf.Max(0f, s.slotFatigue01[i] - siteRestDecayPerHour * dtHours);
                continue;
            }

            float perHour = Mathf.Max(0f, w.def.fatigueRatePerHour);

            try
            {
                string key = GetWorkerKey(w);
                int lvl = GetOwnedLevelOr1(key, w.def);
                float mul = Mathf.Max(0f, TitlesAdapter.GetJobFatigueMult(key, w.def, lvl, s.config.jobType));
                perHour *= mul;
            }
            catch { }

            s.slotFatigue01[i] = Mathf.Min(1f, s.slotFatigue01[i] + perHour * dtHours);

            if (s.slotFatigue01[i] >= 1f - 0.0001f)
            {
                string key = GetWorkerKey(w);
                float hrsCD = Mathf.Max(0f, w.def.fatigueCooldownHours);

                long until = SaveManager.NowUnix() + Mathf.RoundToInt(hrsCD * 3600f);
                s.slotCooldownUntilUnix[i] = until;

                if (!string.IsNullOrEmpty(key)) _cooldownUntil[key] = until;

                s.workers[i] = null;
                RemoveAssignedUnix(key);

                Collect(s.config.jobType);

                s.slotFatigue01[i] = 1f;

                SaveAssignmentsToSave();
                GameEvents.OnJobsChanged?.Invoke();
            }
        }
    }

    private float ComputeRatePerHour(JobSiteState s)
    {
        if (!HasAnyWorker(s.workers))
            return 0f;

        float sum = 0f;

        for (int i = 0; i < s.workers.Count; i++)
        {
            var w = s.workers[i];
            if (w?.def == null) continue;

            float mult = w.def.jobSkill
                * JobBalance.RarityMult(w.def.rarity)
                * JobBalance.EvolutionMult(w.def.evolutionStage)
                * JobBalance.AffinityMult(s.config.jobType, w.def.type);

            string key = GetWorkerKey(w);
            int lvl = GetOwnedLevelOr1(key, w.def);

            mult *= GetPerResourceWorkerMul(key, s.config, here: true);

            try { mult *= Mathf.Max(0f, TitlesAdapter.GetJobRateMult(key, s.config.jobType)); } catch { }

            sum += mult;
        }

        float normalized = 1f + (sum / 3f);
        float perHour = s.config.baseRatePerHour * normalized;

        perHour *= BossDebuffSystem.GetMultiplier(s.config.jobType, SaveManager.NowUnix());

        float auraPct = 0f;
        if (_auraByJob != null) _auraByJob.TryGetValue(s.config.jobType, out auraPct);
        if (auraPct > 0f) perHour *= (1f + auraPct);

        float shinyAura = ShinySystems.SiteShinyAuraMult(s.workers);
        int shinyCount = CountShinies(s.workers);
        float shinySet = 1f + (shinyCount >= 3 ? shiny3Bonus : (shinyCount == 2 ? shiny2Bonus : (shinyCount == 1 ? shiny1Bonus : 0f)));

        return perHour * shinyAura * shinySet;
    }

    // ---------------------------- Assignment API ----------------------------
    public bool TryAssignWorkerAt(JobType job, int slotIndex, MonsterDataSO monster, string ownedId = null)
    {
        var s = FindState(job);
        if (s == null || monster == null) return false;

        if (!IsTypeEligibleFor(job, monster.type))
            return false;

        // ownedId is expected to be ownedUID when available. If not provided, falls back to monster.id (species).
        string key = ownedId ?? monster.id;
        if (IsOnCooldown(key))
            return false;

        int cap = Mathf.Clamp(s.config.maxWorkers, 1, 3);
        if (slotIndex < 0 || slotIndex >= cap) return false;

        EnsureWorkerListSize(s, cap);

        // Populate both ownedUID and monsterId for robustness:
        // - If key is an ownedUID, store it in ownedUID and also store current monster.id as monsterId.
        // - If key is species id, store it in monsterId only.
        var wr = BuildWorkerRefFromKey(key, fallbackDef: monster);
        s.workers[slotIndex] = wr;

        TouchAssignedUnix(GetWorkerKey(wr));
        SaveAssignmentsToSave();
        SaveRuntimeToSave();
        GameEvents.OnJobsChanged?.Invoke();
        return true;
    }

    public bool TryAssignWorker(JobType job, MonsterDataSO monster, string ownedId = null)
    {
        var s = FindState(job);
        if (s == null || monster == null) return false;

        if (!IsTypeEligibleFor(job, monster.type))
            return false;

        string key = ownedId ?? monster.id;
        if (IsOnCooldown(key))
            return false;

        int cap = Mathf.Clamp(s.config.maxWorkers, 1, 3);
        EnsureWorkerListSize(s, cap);

        int empty = s.workers.FindIndex(w => w == null);
        if (empty == -1) return false;

        var wr = BuildWorkerRefFromKey(key, fallbackDef: monster);
        s.workers[empty] = wr;

        TouchAssignedUnix(GetWorkerKey(wr));
        SaveAssignmentsToSave();
        SaveRuntimeToSave();
        GameEvents.OnJobsChanged?.Invoke();
        return true;
    }

    public bool RemoveWorker(JobType job, string ownedIdOrDefId)
    {
        var s = FindState(job);
        if (s == null) return false;

        for (int i = 0; i < s.workers.Count; i++)
        {
            var w = s.workers[i];
            if (!IsWorkerMatchKey(w, ownedIdOrDefId)) continue;

            string key = GetWorkerKey(w);
            s.workers[i] = null;
            RemoveAssignedUnix(key);

            SaveAssignmentsToSave();
            SaveRuntimeToSave();
            GameEvents.OnJobsChanged?.Invoke();
            return true;
        }
        return false;
    }

    public void RemoveFromAnyJob(string ownedIdOrDefId)
    {
        foreach (var s in States)
        {
            for (int i = 0; i < s.workers.Count; i++)
            {
                var w = s.workers[i];
                if (!IsWorkerMatchKey(w, ownedIdOrDefId)) continue;

                string key = GetWorkerKey(w);
                s.workers[i] = null;
                RemoveAssignedUnix(key);
            }
        }

        SaveAssignmentsToSave();
        SaveRuntimeToSave();
        GameEvents.OnJobsChanged?.Invoke();
    }

    public int Collect(JobType job)
    {
        var s = FindState(job);
        if (s == null) return 0;

        int whole = Mathf.FloorToInt(s.storedAmount);
        if (whole <= 0) return 0;

        s.storedAmount -= whole;

        var res = JobOutput.Output(job);
        switch (res)
        {
            case ResourceType.Credits:
                ResourceManager.I.Add(ResourceType.Credits, whole);
                break;

            case ResourceType.Energy:
                if (EncounterManager.I) EncounterManager.I.AddEnergy(whole);
                else ResourceBank.Add(ResourceType.Energy, whole);
                break;

            case ResourceType.GrowthCore:
                ResourceManager.I.Add(ResourceType.GrowthCore, whole);
                break;

            default:
                ResourceBank.Add(res, whole);
                break;
        }

        SaveAssignmentsToSave();
        SaveRuntimeToSave();
        GameEvents.OnJobsChanged?.Invoke();
        return whole;
    }

    public int GetWorkerCount(JobType job)
    {
        var s = FindState(job);
        if (s?.workers == null) return 0;

        int count = 0;
        for (int i = 0; i < s.workers.Count; i++)
        {
            var w = s.workers[i];
            if (w != null && (!string.IsNullOrEmpty(GetWorkerKey(w)) || w.def != null)) count++;
        }
        return count;
    }

    public int GymWorkerCount => GetWorkerCount(JobType.Gym);

    // ---------------------------- Save / Load (assignments/progress) ----------------------------
    public void SaveAssignmentsToSave()
    {
        if (SaveManager.Data == null) return;

        foreach (var s in States) EnsureWorkerListSize(s, Mathf.Clamp(s.config.maxWorkers, 1, 3));

        SaveManager.Data.jobAssignments.Clear();

        foreach (var s in States)
        {
            var ja = new JobAssignment { job = s.config.jobType, workerIds = new List<string>() };
            foreach (var w in s.workers) ja.workerIds.Add(GetWorkerKey(w) ?? "");
            SaveManager.Data.jobAssignments.Add(ja);
        }

        // IMPORTANT: during hard reset/reload cycles, SaveManager may be mid-flight.
        if (!SaveManager.IsHardResetting)
            SaveManager.Save();
    }

    public void LoadAssignmentsFromSave()
    {
        if (SaveManager.Data?.jobAssignments == null) return;

        // Reset all sites
        foreach (var s in States)
        {
            s.workers.Clear();
            int cap = Mathf.Clamp(s.config.maxWorkers, 1, 3);
            for (int i = 0; i < cap; i++) s.workers.Add(null);
            EnsureWorkerListSize(s, cap);
        }

        _assignedUnix.Clear();

        foreach (var ja in SaveManager.Data.jobAssignments)
        {
            var s = FindState(ja.job);
            if (s == null) continue;

            int cap = Mathf.Clamp(s.config.maxWorkers, 1, 3);
            EnsureWorkerListSize(s, cap);

            for (int i = 0; i < Mathf.Min(cap, ja.workerIds.Count); i++)
            {
                var key = ja.workerIds[i];
                if (string.IsNullOrEmpty(key)) { s.workers[i] = null; continue; }

                // key can be ownedUID (preferred) or species monsterId (legacy).
                var wr = BuildWorkerRefFromKey(key, fallbackDef: null);
                if (wr == null || wr.def == null)
                {
                    s.workers[i] = null;
                    continue;
                }

                s.workers[i] = wr;
                TouchAssignedUnix(GetWorkerKey(wr));
            }
        }

        GameEvents.OnJobsChanged?.Invoke();
    }

    public void SaveProgressToSave()
    {
        if (SaveManager.Data == null) return;

        SaveManager.Data.jobProgress ??= new List<JobProgress>();
        SaveManager.Data.jobProgress.Clear();

        foreach (var s in States)
        {
            if (s?.config == null) continue;
            SaveManager.Data.jobProgress.Add(new JobProgress
            {
                job = s.config.jobType,
                level = s.level,
                currentXP = s.currentXP,
                maxXPForLevel = s.maxXPForLevel
            });
        }

        if (!SaveManager.IsHardResetting)
            SaveManager.Save();
    }

    public void LoadProgressFromSave()
    {
        if (SaveManager.Data?.jobProgress == null) return;

        foreach (var jp in SaveManager.Data.jobProgress)
        {
            var st = FindState(jp.job);
            if (st == null) continue;

            st.level = Mathf.Clamp(jp.level, 1, JobLeveling.MaxLevel);
            st.maxXPForLevel = (jp.maxXPForLevel > 0) ? jp.maxXPForLevel : JobLeveling.MaxXpForLevel(jp.job, st.level);
            st.currentXP = Mathf.Clamp(jp.currentXP, 0, st.maxXPForLevel);
        }
    }

    // ---------------------------- Runtime sidecar (slot fatigue + cooldown) ----------------------------
    private void SaveRuntimeToSave()
    {
        try
        {
            // Avoid writing during hard wipe cycles; SaveManager will rewrite baseline.
            if (SaveManager.IsHardResetting) return;

            var blob = new JobRuntimeSave { savedAtUnix = SaveManager.NowUnix() };

            foreach (var s in States)
            {
                if (s?.config == null) continue;
                blob.sites.Add(new JobRuntimeSite
                {
                    job = s.config.jobType,
                    slotFatigue01 = (float[])(s.slotFatigue01?.Clone() ?? Array.Empty<float>()),
                    slotCooldownUntilUnix = (long[])(s.slotCooldownUntilUnix?.Clone() ?? Array.Empty<long>())
                });
            }

            foreach (var kv in _cooldownUntil)
                if (!string.IsNullOrEmpty(kv.Key))
                    blob.cooldowns.Add(new MonsterCooldownKV { id = kv.Key, until = kv.Value });

            SaveManager.SaveJobRuntime(blob);
        }
        catch { }
    }

    private void LoadRuntimeFromSave()
    {
        try
        {
            var blob = SaveManager.LoadJobRuntime(); // returns SaveManager.JobRuntimeSave
            if (blob == null) return;

            _cooldownUntil.Clear();
            if (blob.cooldowns != null)
            {
                for (int i = 0; i < blob.cooldowns.Count; i++)
                {
                    var kv = blob.cooldowns[i];
                    if (kv != null && !string.IsNullOrEmpty(kv.id))
                        _cooldownUntil[kv.id] = kv.until;
                }
            }

            if (blob.sites != null)
            {
                for (int i = 0; i < blob.sites.Count; i++)
                {
                    var rs = blob.sites[i];
                    var st = FindState(rs.job);
                    if (st == null) continue;

                    int cap = Mathf.Clamp(st.config.maxWorkers, 1, 3);
                    EnsureWorkerListSize(st, cap);

                    if (rs.slotFatigue01 != null && rs.slotFatigue01.Length == cap)
                        Array.Copy(rs.slotFatigue01, st.slotFatigue01, cap);

                    if (rs.slotCooldownUntilUnix != null && rs.slotCooldownUntilUnix.Length == cap)
                        Array.Copy(rs.slotCooldownUntilUnix, st.slotCooldownUntilUnix, cap);
                }
            }
        }
        catch { }
    }

    private bool IsOnCooldown(string key)
    {
        if (string.IsNullOrEmpty(key)) return false;

        // If key is a species id but we also track ownedUID cooldowns, check both where possible.
        if (_cooldownUntil.TryGetValue(key, out long until))
            return until > SaveManager.NowUnix();

        // If key looks like a species id, attempt to see if there is an owned entry with that id currently on cooldown.
        // (This preserves legacy behavior where callers pass monsterId.)
        var data = SaveManager.Data;
        var owned = data?.owned;
        if (owned != null)
        {
            for (int i = 0; i < owned.Count; i++)
            {
                var om = owned[i];
                if (om == null) continue;
                if (om.monsterId != key) continue;
                if (!string.IsNullOrEmpty(om.ownedUID) && _cooldownUntil.TryGetValue(om.ownedUID, out long until2))
                    return until2 > SaveManager.NowUnix();
            }
        }

        return false;
    }

    private void OnStarterChosen(MonsterType type)
    {
        bool unlockedAny = TryUnlockSitesForType_ReturnsChanged(type);

        if (enableStarterDefaultSitesFallback && !unlockedAny)
        {
            bool fallbackChanged = EnsureStarterDefaultSitesUnlocked();
            if (fallbackChanged)
            {
                RefreshAllJobSiteViewsInScene();
                GameEvents.OnJobsChanged?.Invoke();
            }
        }

        RecalculateUnlocksFromSeenTypes();
    }

    private void OnMonsterCaptured(string monsterId, MonsterType type)
    {
        RegisterSeenType(type);
        TryUnlockSitesForType(type);
    }

    private void RegisterSeenType(MonsterType type)
    {
        if (SaveManager.Data == null) return;

        SaveManager.Data.seenTypes ??= new HashSet<MonsterType>();
        SaveManager.Data.seenTypesList ??= new List<MonsterType>();

        bool added = SaveManager.Data.seenTypes.Add(type);
        bool addedList = false;

        if (!SaveManager.Data.seenTypesList.Contains(type))
        {
            SaveManager.Data.seenTypesList.Add(type);
            addedList = true;
        }

        if ((added || addedList) && !SaveManager.IsHardResetting)
            SaveManager.Save();
    }

    public void RecalculateUnlocksFromSeenTypes()
    {
        if (SaveManager.Data == null) return;

        SaveManager.Data.unlockedJobSites ??= new HashSet<JobType>();
        SaveManager.Data.unlockedJobSitesList ??= new List<JobType>();

        if (SaveManager.Data.seenTypes != null)
        {
            foreach (var t in SaveManager.Data.seenTypes)
                TryUnlockSitesForType(t);
        }

        if (!SaveManager.IsHardResetting)
            SaveManager.Save();
    }

    private void TryUnlockSitesForType(MonsterType type)
    {
        if (!lockSitesUntilEligible || SaveManager.Data == null)
            return;

        bool changed = false;

        foreach (var job in JobBalance.JobsUnlockedByType(type))
        {
            if (JobUnlockBridge.UnlockJob(job, syncFeatureUnlock: true))
                changed = true;
        }

        if (changed)
        {
            RefreshAllJobSiteViewsInScene();
            GameEvents.OnJobsChanged?.Invoke();
        }
    }

    public bool IsSiteUnlocked(JobType job)
    {
        if (!lockSitesUntilEligible)
            return true;

        return JobUnlockBridge.IsJobUnlocked(job);
    }

    // ---------------------------- Offline sim & clinic relief ----------------------------
    public void ProcessOfflineAllSites()
    {
        ResolveOfflineIfAny();
        RefreshAllJobSiteViewsInScene();
        GameEvents.OnJobsChanged?.Invoke();
    }

    private void ResolveOfflineIfAny()
    {
        if (SaveManager.Data == null) return;

        long last = SaveManager.Data.lastSavedUnix;
        long now = SaveManager.NowUnix();
        float elapsed = Mathf.Max(0f, now - last);
        if (elapsed < 1f) return;

        float totalSim = elapsed * Mathf.Max(0f, offlineSimMultiplier);
        float remaining = totalSim;

        float step = Mathf.Max(60f, offlineChunkSeconds);
        while (remaining > 0f)
        {
            float dt = Mathf.Min(remaining, step);
            Produce(dt);
            remaining -= dt;
        }
    }

    private void ApplyClinicRelief(float dtHours)
    {
        if (dtHours <= 0f) return;

        // Optional: only spend Coffee if user enabled it in settings AND a site permits relief.
        int available = ResourceBank.Get(ResourceType.Coffee);
        if (available <= 0) return;

        var targets = new List<JobSiteState>();
        foreach (var s in States)
        {
            if (s?.config == null) continue;
            if (s.config.jobType == JobType.Clinic) continue;
            if (!s.allowClinicRelief) continue;

            bool hasFatigue = s.slotFatigue01 != null && s.slotFatigue01.Any(v => v > 0f);
            if (!hasFatigue) continue;

            targets.Add(s);
        }
        if (targets.Count == 0) return;

        float perSiteMax = Mathf.Max(0f, maxReliefPerHourPerSite * dtHours);

        int idx = 0;
        int guard = 0;

        while (available > 0 && guard++ < 100000)
        {
            var s = targets[idx];

            float remainingForSite = perSiteMax;
            for (int si = 0; si < s.slotFatigue01.Length && remainingForSite > 0f && available > 0; si++)
            {
                float slotFat = s.slotFatigue01[si];
                if (slotFat <= 0f) continue;

                float reliefStep = Mathf.Min(remainingForSite, reliefPerCharge, slotFat);
                s.slotFatigue01[si] = Mathf.Max(0f, slotFat - reliefStep);
                remainingForSite -= reliefStep;
                available--;
                if (available <= 0) break;
            }

            idx = (idx + 1) % targets.Count;

            bool allZero = true;
            for (int i = 0; i < targets.Count; i++)
            {
                if (targets[i].slotFatigue01 != null && targets[i].slotFatigue01.Any(v => v > 0f))
                {
                    allZero = false;
                    break;
                }
            }
            if (allZero) break;
        }

        int spent = ResourceBank.Get(ResourceType.Coffee) - available;
        if (spent > 0) ResourceBank.Add(ResourceType.Coffee, -spent);
    }

    // ---------------------------- UI / Views ----------------------------
    public void RefreshAllJobSiteViewsInScene()
    {
        var views = GameObject.FindObjectsByType<JobSiteView>(FindObjectsSortMode.None);
        for (int i = 0; i < views.Length; i++) views[i].Refresh();
    }

    // ---------------------------- Queries / helpers ----------------------------
    public JobSiteSO[] GetSitesArray() => jobSites.ToArray();

    private JobSiteState FindState(JobType job)
    {
        for (int i = 0; i < States.Count; i++)
            if (States[i].config != null && States[i].config.jobType == job) return States[i];
        return null;
    }

    private MonsterDataSO ResolveMonsterDef(string idOrOwnedId)
    {
        if (string.IsNullOrEmpty(idOrOwnedId)) return null;

        // 1) Direct species ID lookup (M-###)
        if (_idToDef.TryGetValue(idOrOwnedId, out var def) && def) return def;

        var direct = MonsterLibraryLocator.GetById(idOrOwnedId);
        if (direct) return direct;

        // 2) ownedUID -> current species id
        var owned = SaveManager.Data?.owned;
        if (owned != null)
        {
            for (int i = 0; i < owned.Count; i++)
            {
                var om = owned[i];
                if (om == null) continue;

                if (!string.IsNullOrEmpty(om.ownedUID) && om.ownedUID == idOrOwnedId)
                {
                    if (!string.IsNullOrEmpty(om.monsterId))
                    {
                        if (_idToDef.TryGetValue(om.monsterId, out var def2) && def2) return def2;
                        return MonsterLibraryLocator.GetById(om.monsterId);
                    }
                    return null;
                }
            }
        }

        // team as fallback (some projects only store team entries)
        var team = SaveManager.Data?.team;
        if (team != null)
        {
            for (int i = 0; i < team.Count; i++)
            {
                var tm = team[i];
                if (tm == null) continue;
                if (!string.IsNullOrEmpty(tm.ownedUID) && tm.ownedUID == idOrOwnedId)
                {
                    if (!string.IsNullOrEmpty(tm.monsterId))
                    {
                        if (_idToDef.TryGetValue(tm.monsterId, out var def2) && def2) return def2;
                        return MonsterLibraryLocator.GetById(tm.monsterId);
                    }
                    return null;
                }
            }
        }

        return null;
    }

    public int GetEffectiveStorageCap(JobSiteSO site)
    {
        if (site == null) return 0;

        int baseCap = site.storageCap;

        int extraFromSave = 0;
        if (SaveManager.Data != null)
        {
            try { extraFromSave = SaveManager.Data.GetJobStorageExtra(site.jobType); }
            catch { extraFromSave = 0; }
        }

        int flatFromTitles = 0;
        try { flatFromTitles = Mathf.Max(0, TitlesAdapter.GetJobCapacityBonus(site.jobType)); }
        catch { flatFromTitles = 0; }

        int tempFromBlessings = 0;
        try { tempFromBlessings = Mathf.Max(0, GetActiveBlessingBonus(site.jobType)); }
        catch { tempFromBlessings = 0; }

        var st = FindState(site.jobType);
        float levelMul = (st != null) ? JobLeveling.StorageMultForLevel(st.level) : 1f;

        int preMultFlat = Mathf.Max(0, baseCap + extraFromSave + flatFromTitles + tempFromBlessings);
        return Mathf.Max(0, Mathf.RoundToInt(preMultFlat * levelMul));
    }

    public (JobType job, float hours) GetCurrentJobAndHours(string key)
    {
        if (string.IsNullOrEmpty(key)) return (JobType.None, 0f);

        for (int si = 0; si < States.Count; si++)
        {
            var st = States[si];
            if (st?.workers == null) continue;

            for (int wi = 0; wi < st.workers.Count; wi++)
            {
                var w = st.workers[wi];
                if (!IsWorkerMatchKey(w, key)) continue;

                var k = GetWorkerKey(w);
                if (!_assignedUnix.TryGetValue(k, out long start)) start = SaveManager.NowUnix();
                float hrs = Mathf.Max(0f, (SaveManager.NowUnix() - start) / 3600f);

                return (st.config ? st.config.jobType : JobType.None, hrs);
            }
        }
        return (JobType.None, 0f);
    }

    private static float GetPerResourceWorkerMul(string workerKey, JobSiteSO site, bool here)
    {
        return 1f;
    }

    private static bool HasAnyWorker(List<WorkerRef> workers)
    {
        if (workers == null) return false;
        for (int i = 0; i < workers.Count; i++)
        {
            var w = workers[i];
            if (w == null) continue;
            if (w.def != null) return true;
            if (!string.IsNullOrEmpty(GetWorkerKey(w))) return true;
        }
        return false;
    }

    // ---------------------------- Settings hookup ----------------------------
    private void PullSettings()
    {
        var s = SettingsManager.I?.S;
        if (s == null) return;

        autoBenchEnabled = s.autoBenchEnabled;
        autoBenchHPThreshold01 = Mathf.Clamp01(s.autoBenchThreshold01);
        autoBenchAutoFill = s.autoBenchAutoFill;
        autoReliefEnabled = s.autoClinicReliefEnabled;

#if UNITY_EDITOR
        logProductionBreakdown = s.logProductionBreakdown;
#endif
    }

    // ---------------------------- Auto-bench (injury) ----------------------------
    private void AutoBenchSweep(float threshold01)
    {
        var team = SaveManager.Data?.team;
        if (team == null || team.Count == 0) return;

        threshold01 = Mathf.Clamp01(threshold01);

        for (int si = 0; si < States.Count; si++)
        {
            var s = States[si];
            if (s?.config == null || s.workers == null) continue;

            for (int wi = 0; wi < s.workers.Count; wi++)
            {
                var w = s.workers[wi];
                if (w == null) continue;

                string key = GetWorkerKey(w);
                if (string.IsNullOrEmpty(key)) continue;

                if (!TryGetTeamEntryByKey(key, out int teamIndex)) continue;

                var entry = team[teamIndex];
                if (entry == null) continue;

                int level = Mathf.Max(1, entry.level);
                float maxHP = Mathf.Max(1f, BattleCalc.CalcHP(w.def, level));

                float curHP = entry.currentHP;
                if (curHP < 0) curHP = maxHP;

                float hp01 = Mathf.Clamp01(curHP / maxHP);

                if (hp01 < threshold01)
                {
                    RemoveWorker(s.config.jobType, key);

                    if (autoBenchAutoFill)
                        TryFillSlotFromTeam(s, wi, threshold01);
                }
            }
        }
    }

    private bool TryFillSlotFromTeam(JobSiteState site, int slotIndex, float threshold01)
    {
        var team = SaveManager.Data?.team;
        if (team == null || team.Count == 0) return false;

        var used = new HashSet<string>();
        foreach (var st in States)
        {
            if (st?.workers == null) continue;
            for (int wi = 0; wi < st.workers.Count; wi++)
            {
                var w = st.workers[wi];
                var k = GetWorkerKey(w);
                if (!string.IsNullOrEmpty(k)) used.Add(k);
            }
        }

        for (int i = 0; i < team.Count; i++)
        {
            var entry = team[i];
            if (entry == null) continue;

            string candKey = !string.IsNullOrEmpty(entry.ownedUID) ? entry.ownedUID : entry.monsterId;
            if (string.IsNullOrEmpty(candKey) || used.Contains(candKey)) continue;

            if (IsOnCooldown(candKey)) continue;

            var def = MonsterLibraryLocator.GetById(entry.monsterId);
            if (!def) continue;

            if (!IsTypeEligibleFor(site.config.jobType, def.type)) continue;

            int level = Mathf.Max(1, entry.level);
            int curHP = Mathf.Max(0, entry.currentHP);
            float maxHP = Mathf.Max(1f, BattleCalc.CalcHP(def, level));
            float hp01 = curHP / maxHP;

            if (hp01 < threshold01) continue;

            return TryAssignWorkerAt(site.config.jobType, slotIndex, def, candKey);
        }
        return false;
    }

    private bool TryGetTeamEntryByKey(string key, out int index)
    {
        index = -1;
        var team = SaveManager.Data?.team;
        if (team == null) return false;

        for (int i = 0; i < team.Count; i++)
        {
            var e = team[i];
            if (e == null) continue;

            if (!string.IsNullOrEmpty(e.ownedUID) && e.ownedUID == key)
            {
                index = i;
                return true;
            }

            if (!string.IsNullOrEmpty(e.monsterId) && e.monsterId == key)
            {
                index = i;
                return true;
            }
        }

        return false;
    }

    // ---------------------------- Local helpers ----------------------------
    private static int GetOwnedLevelOr1(string key, MonsterDataSO fallbackDef)
    {
        if (string.IsNullOrEmpty(key)) return 1;

        var data = SaveManager.Data;

        var owned = data?.owned;
        if (owned != null)
        {
            for (int i = 0; i < owned.Count; i++)
            {
                var om = owned[i];
                if (om == null) continue;

                if (!string.IsNullOrEmpty(om.ownedUID) && om.ownedUID == key)
                    return Mathf.Max(1, om.level);

                if (!string.IsNullOrEmpty(om.monsterId) && om.monsterId == key)
                    return Mathf.Max(1, om.level);
            }
        }

        var team = data?.team;
        if (team != null)
        {
            for (int i = 0; i < team.Count; i++)
            {
                var e = team[i];
                if (e == null) continue;

                if (!string.IsNullOrEmpty(e.ownedUID) && e.ownedUID == key)
                    return Mathf.Max(1, e.level);

                if (!string.IsNullOrEmpty(e.monsterId) && e.monsterId == key)
                    return Mathf.Max(1, e.level);
            }
        }

        return 1;
    }

    private static int CountShinies(List<WorkerRef> workers)
    {
        if (workers == null || workers.Count == 0) return 0;
        int c = 0;
        for (int i = 0; i < workers.Count; i++) if (IsWorkerShiny(workers[i])) c++;
        return c;
    }

    private static bool IsWorkerShiny(WorkerRef w)
    {
        if (w == null) return false;

        var data = SaveManager.Data;

        string uid = w.ownedUID;
        string mid = w.monsterId;

        var ownedList = data?.owned;
        if (ownedList != null)
        {
            for (int i = 0; i < ownedList.Count; i++)
            {
                var om = ownedList[i];
                if (om == null) continue;

                if (!string.IsNullOrEmpty(uid) && om.ownedUID == uid) return om.isShiny;
                if (!string.IsNullOrEmpty(mid) && om.monsterId == mid) return om.isShiny;
            }
        }

        var team = data?.team;
        if (team != null)
        {
            for (int i = 0; i < team.Count; i++)
            {
                var tm = team[i];
                if (tm == null) continue;

                if (!string.IsNullOrEmpty(uid) && tm.ownedUID == uid) return tm.isShiny;
                if (!string.IsNullOrEmpty(mid) && tm.monsterId == mid) return tm.isShiny;
            }
        }

        // fallback to def reflection if your def supports it
        var def = w.def;
        if (!def) return false;
        try
        {
            var f = def.GetType().GetField("isShiny");
            if (f != null && f.FieldType == typeof(bool)) return (bool)f.GetValue(def);

            var p = def.GetType().GetProperty("IsShiny");
            if (p != null && p.PropertyType == typeof(bool)) return (bool)p.GetValue(def, null);
        }
        catch { }

        return false;
    }

    public bool IsTypeEligibleFor(JobType job, MonsterType type)
    {
        if (JobBalance.IsTypeAllowedStrict(job, type))
            return true;

        var cfg = FindState(job)?.config;
        if (cfg != null && cfg.eligibleTypes != null && cfg.eligibleTypes.Length > 0)
            return Array.Exists(cfg.eligibleTypes, t => t == type);

        return false;
    }

    private int GetActiveBlessingBonus(JobType job)
    {
        if (_blessingBuffs == null || _blessingBuffs.Count == 0) return 0;

        long now = SaveManager.NowUnix();
        int total = 0;

        for (int i = _blessingBuffs.Count - 1; i >= 0; i--)
        {
            var b = _blessingBuffs[i];
            if (b == null || b.untilUnix <= now)
            {
                _blessingBuffs.RemoveAt(i);
                continue;
            }

            if (b.job == job)
                total += Mathf.Max(0, b.flatBonus);
        }

        return total;
    }

    private bool TryUnlockSitesForType_ReturnsChanged(MonsterType type)
    {
        if (!lockSitesUntilEligible || SaveManager.Data == null)
            return false;

        bool changed = false;
        bool foundAnyMapping = false;

        foreach (var job in JobBalance.JobsUnlockedByType(type))
        {
            foundAnyMapping = true;

            if (JobUnlockBridge.UnlockJob(job, syncFeatureUnlock: true))
                changed = true;
        }

        if (!foundAnyMapping)
            return false;

        if (changed)
        {
            RefreshAllJobSiteViewsInScene();
            GameEvents.OnJobsChanged?.Invoke();
        }

        return true;
    }

    private bool EnsureStarterDefaultSitesUnlocked()
    {
        if (SaveManager.Data == null) return false;
        if (starterDefaultSites == null || starterDefaultSites.Count == 0) return false;

        bool changed = false;

        for (int i = 0; i < starterDefaultSites.Count; i++)
        {
            var job = starterDefaultSites[i];
            if (job == JobType.None) continue;

            if (JobUnlockBridge.UnlockJob(job, syncFeatureUnlock: true))
                changed = true;
        }

        return changed;
    }

    public void ApplyStarterUnlocksNow(MonsterType type)
    {
        var d = SaveManager.Data;
        if (d == null) return;

        d.seenTypes ??= new HashSet<MonsterType>();
        d.seenTypesList ??= new List<MonsterType>();

        if (d.seenTypes.Add(type) && !d.seenTypesList.Contains(type))
            d.seenTypesList.Add(type);

        bool mappedAny = false;

        foreach (var job in JobBalance.JobsUnlockedByType(type))
        {
            mappedAny = true;
            JobUnlockBridge.UnlockJob(job, syncFeatureUnlock: true);
        }

        if (enableStarterDefaultSitesFallback && !mappedAny)
        {
            for (int i = 0; i < starterDefaultSites.Count; i++)
                JobUnlockBridge.UnlockJob(starterDefaultSites[i], syncFeatureUnlock: true);
        }

        if (!SaveManager.IsHardResetting)
            SaveManager.Save();

        RefreshAllJobSiteViewsInScene();
        GameEvents.OnJobsChanged?.Invoke();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Cheats / Debug Helpers (kept intact)
    // ─────────────────────────────────────────────────────────────────────────
    public int Cheat_ClearAllFatigue()
    {
        int cleared = 0;

        for (int si = 0; si < States.Count; si++)
        {
            var s = States[si];
            if (s == null || s.slotFatigue01 == null) continue;
            for (int i = 0; i < s.slotFatigue01.Length; i++)
            {
                if (s.slotFatigue01[i] > 0f) cleared++;
                s.slotFatigue01[i] = 0f;
            }
        }

        SaveRuntimeToSave();
        RefreshAllJobSiteViewsInScene();
        GameEvents.OnJobsChanged?.Invoke();
        return cleared;
    }

    public int Cheat_ResetCooldowns()
    {
        int cleared = 0;

        for (int si = 0; si < States.Count; si++)
        {
            var s = States[si];
            if (s == null || s.slotCooldownUntilUnix == null) continue;
            for (int i = 0; i < s.slotCooldownUntilUnix.Length; i++)
            {
                if (s.slotCooldownUntilUnix[i] > 0) cleared++;
                s.slotCooldownUntilUnix[i] = 0;
            }
        }

        if (_cooldownUntil != null)
        {
            cleared += _cooldownUntil.Count;
            _cooldownUntil.Clear();
        }

        SaveRuntimeToSave();
        RefreshAllJobSiteViewsInScene();
        GameEvents.OnJobsChanged?.Invoke();
        return cleared;
    }

    // ─────────────────────────────────────────────────────────────
    // Sanctum: Temporary Storage Blessings (RESTORED API)
    // These methods are referenced by SanctumUI.cs
    // ─────────────────────────────────────────────────────────────
    public void ApplyTemporaryStorageBlessing(JobType targetJob, int flatBonus, int durationSeconds)
    {
        ApplyTemporaryStorageBlessing(targetJob, flatBonus, (long)durationSeconds);
    }

    public void ApplyTemporaryStorageBlessing(JobType targetJob, int flatBonus, float durationSeconds)
    {
        ApplyTemporaryStorageBlessing(targetJob, flatBonus, (long)Mathf.RoundToInt(durationSeconds));
    }

    public void ApplyTemporaryStorageBlessing(JobType targetJob, int flatBonus, long durationSeconds)
    {
        if (targetJob == JobType.None) return;
        if (flatBonus <= 0) return;

        long now = SaveManager.NowUnix();
        long until = now + Mathf.Max(1, (int)durationSeconds);

        _blessingBuffs ??= new List<BlessingBuff>();
        PruneExpiredBlessings(now);

        _blessingBuffs.Add(new BlessingBuff
        {
            job = targetJob,
            flatBonus = flatBonus,
            untilUnix = until
        });

        RefreshAllJobSiteViewsInScene();
        GameEvents.OnJobsChanged?.Invoke();
    }

    public int GetTemporaryStorageBonus(JobType job)
    {
        if (job == JobType.None) return 0;

        _blessingBuffs ??= new List<BlessingBuff>();
        long now = SaveManager.NowUnix();
        PruneExpiredBlessings(now);

        int total = 0;
        for (int i = 0; i < _blessingBuffs.Count; i++)
        {
            var b = _blessingBuffs[i];
            if (b == null) continue;
            if (b.job != job) continue;
            if (b.untilUnix <= now) continue;

            total += Mathf.Max(0, b.flatBonus);
        }
        return total;
    }

    public int GetBlessingSecondsRemaining(JobType job)
    {
        if (job == JobType.None) return 0;

        _blessingBuffs ??= new List<BlessingBuff>();
        long now = SaveManager.NowUnix();
        PruneExpiredBlessings(now);

        long bestUntil = 0;
        for (int i = 0; i < _blessingBuffs.Count; i++)
        {
            var b = _blessingBuffs[i];
            if (b == null) continue;
            if (b.job != job) continue;
            if (b.untilUnix <= now) continue;

            if (b.untilUnix > bestUntil)
                bestUntil = b.untilUnix;
        }

        if (bestUntil <= now) return 0;
        long remaining = bestUntil - now;

        if (remaining > int.MaxValue) return int.MaxValue;
        return (int)remaining;
    }

    private void PruneExpiredBlessings(long nowUnix)
    {
        if (_blessingBuffs == null || _blessingBuffs.Count == 0) return;

        for (int i = _blessingBuffs.Count - 1; i >= 0; i--)
        {
            var b = _blessingBuffs[i];
            if (b == null || b.untilUnix <= nowUnix)
                _blessingBuffs.RemoveAt(i);
        }
    }

    // ---------------------------- Worker Keying (ownedUID-first) ----------------------------
    private static string GetWorkerKey(WorkerRef w)
    {
        if (w == null) return null;

        if (!string.IsNullOrEmpty(w.ownedUID))
            return w.ownedUID;

        if (!string.IsNullOrEmpty(w.monsterId))
            return w.monsterId;

        return w.def ? w.def.id : null;
    }

    private static bool IsWorkerMatchKey(WorkerRef w, string key)
    {
        if (w == null || string.IsNullOrEmpty(key)) return false;
        if (!string.IsNullOrEmpty(w.ownedUID) && w.ownedUID == key) return true;
        if (!string.IsNullOrEmpty(w.monsterId) && w.monsterId == key) return true;
        return w.def != null && w.def.id == key;
    }

    private void TouchAssignedUnix(string key)
    {
        if (!string.IsNullOrEmpty(key)) _assignedUnix[key] = SaveManager.NowUnix();
    }

    private void RemoveAssignedUnix(string key)
    {
        if (!string.IsNullOrEmpty(key)) _assignedUnix.Remove(key);
    }

    private OwnedMonsterData FindOwnedByUID(string ownedUID)
    {
        if (string.IsNullOrEmpty(ownedUID)) return null;

        var data = SaveManager.Data;
        if (data == null) return null;

        if (data.owned != null)
            for (int i = 0; i < data.owned.Count; i++)
                if (data.owned[i] != null && data.owned[i].ownedUID == ownedUID)
                    return data.owned[i];

        if (data.team != null)
            for (int i = 0; i < data.team.Count; i++)
                if (data.team[i] != null && data.team[i].ownedUID == ownedUID)
                    return data.team[i];

        return null;
    }

    private WorkerRef BuildWorkerRefFromKey(string key, MonsterDataSO fallbackDef)
    {
        if (string.IsNullOrEmpty(key)) return null;

        // If key maps to an owned instance, prefer ownedUID binding (stable across evolution)
        var owned = FindOwnedByUID(key);
        if (owned != null)
        {
            var def = !string.IsNullOrEmpty(owned.monsterId) ? MonsterLibraryLocator.GetById(owned.monsterId) : null;
            if (!def) def = fallbackDef;

            if (!def) return null;

            return new WorkerRef
            {
                ownedUID = owned.ownedUID,
                monsterId = owned.monsterId, // current species (may have changed due to evolution)
                def = def
            };
        }

        // Else treat key as species monsterId (legacy)
        var def2 = ResolveMonsterDef(key);
        if (!def2) def2 = fallbackDef;
        if (!def2) return null;

        return new WorkerRef
        {
            ownedUID = null,
            monsterId = key,
            def = def2
        };
    }

    private void RefreshSiteWorkerDefsIfNeeded(JobSiteState s)
    {
        if (s == null || s.workers == null) return;

        for (int i = 0; i < s.workers.Count; i++)
        {
            var w = s.workers[i];
            if (w == null) continue;

            var key = GetWorkerKey(w);
            if (string.IsNullOrEmpty(key))
            {
                s.workers[i] = null;
                continue;
            }

            // If this is ownedUID-bound, ensure monsterId and def reflect current owned state
            if (!string.IsNullOrEmpty(w.ownedUID))
            {
                var owned = FindOwnedByUID(w.ownedUID);
                if (owned == null || string.IsNullOrEmpty(owned.monsterId))
                {
                    s.workers[i] = null;
                    continue;
                }

                w.monsterId = owned.monsterId;

                var newDef = MonsterLibraryLocator.GetById(owned.monsterId);
                if (newDef) w.def = newDef;
                else { s.workers[i] = null; }
            }
            else
            {
                // species-bound: ensure def is resolved
                if (w.def == null)
                {
                    var newDef = ResolveMonsterDef(w.monsterId);
                    if (newDef != null) w.def = newDef;
                    else s.workers[i] = null;
                }
            }
        }
    }

    /// <summary>
    /// Ensures:
    /// - Worker keys are valid (ownedUID exists or species exists)
    /// - ownedUID workers use CURRENT evolved species/def
    /// - stale / invalid saved keys are removed
    /// </summary>
    private void SanitizeAndRefreshWorkersFromSaveKeys(bool saveIfChanged)
    {
        bool changed = false;

        // Build valid ownedUID set (owned + team)
        var validUIDs = new HashSet<string>();
        var data = SaveManager.Data;
        if (data != null)
        {
            if (data.owned != null)
                for (int i = 0; i < data.owned.Count; i++)
                    if (data.owned[i] != null && !string.IsNullOrEmpty(data.owned[i].ownedUID))
                        validUIDs.Add(data.owned[i].ownedUID);

            if (data.team != null)
                for (int i = 0; i < data.team.Count; i++)
                    if (data.team[i] != null && !string.IsNullOrEmpty(data.team[i].ownedUID))
                        validUIDs.Add(data.team[i].ownedUID);
        }

        foreach (var s in States)
        {
            if (s?.config == null) continue;

            int cap = Mathf.Clamp(s.config.maxWorkers, 1, 3);
            EnsureWorkerListSize(s, cap);

            for (int i = 0; i < s.workers.Count; i++)
            {
                var w = s.workers[i];
                if (w == null) continue;

                // If ownedUID was stored, validate it exists
                if (!string.IsNullOrEmpty(w.ownedUID))
                {
                    if (!validUIDs.Contains(w.ownedUID))
                    {
                        s.workers[i] = null;
                        changed = true;
                        continue;
                    }

                    // Refresh to current evolved def/monsterId
                    var owned = FindOwnedByUID(w.ownedUID);
                    if (owned == null || string.IsNullOrEmpty(owned.monsterId))
                    {
                        s.workers[i] = null;
                        changed = true;
                        continue;
                    }

                    var def = MonsterLibraryLocator.GetById(owned.monsterId);
                    if (!def)
                    {
                        s.workers[i] = null;
                        changed = true;
                        continue;
                    }

                    if (w.monsterId != owned.monsterId) { w.monsterId = owned.monsterId; changed = true; }
                    if (w.def != def) { w.def = def; changed = true; }

                    continue;
                }

                // Legacy: if monsterId is actually an ownedUID, upgrade it
                if (!string.IsNullOrEmpty(w.monsterId) && validUIDs.Contains(w.monsterId))
                {
                    var upgraded = BuildWorkerRefFromKey(w.monsterId, fallbackDef: w.def);
                    s.workers[i] = upgraded;
                    changed = true;
                    continue;
                }

                // Species-bound: validate def exists
                if (string.IsNullOrEmpty(w.monsterId))
                {
                    s.workers[i] = null;
                    changed = true;
                    continue;
                }

                var def2 = ResolveMonsterDef(w.monsterId);
                if (!def2)
                {
                    s.workers[i] = null;
                    changed = true;
                    continue;
                }

                if (w.def != def2)
                {
                    w.def = def2;
                    changed = true;
                }
            }
        }

        if (changed && saveIfChanged)
            SaveAssignmentsToSave();
    }
}
