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

/// <summary>Reference to a worker assigned to a site.</summary>
[Serializable]
public class WorkerRef
{
    public string monsterId;     // Prefer owned-instance ID when available
    public MonsterDataSO def;    // Fallback to base definition
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
    public long[]  slotCooldownUntilUnix = new long[3];

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

#if UNITY_EDITOR
    [Header("Debug (Editor Only)")]
    [SerializeField] private bool logProductionBreakdown = false;
#endif

    // ---------------------------- State ----------------------------
    public readonly List<JobSiteState> States = new();

    // Per-monster cooldown (key = ownedId or def.id). Persisted via SaveManager sidecar.
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

        BuildDefIndex();
        InitStates();
        LoadProgressFromSave();
        LoadAssignmentsFromSave();

        // Load slot fatigue + cooldowns from sidecar file
        LoadRuntimeFromSave();

        SubscribeUnlockEvents();
        if (lockSitesUntilEligible) RecalculateUnlocksFromSeenTypes();
        if (simulateOfflineOnLoad) ResolveOfflineIfAny();

        RefreshAllJobSiteViewsInScene();
    }

    private void OnEnable()
    {
        GameEvents.StarterChosen += OnStarterChosen;
        GameEvents.MonsterCaptured += OnMonsterCaptured;
        GameEvents.JobGlobalModsChanged += OnJobModsChanged;
    }

    private void OnDisable()
    {
        GameEvents.StarterChosen -= OnStarterChosen;
        GameEvents.MonsterCaptured -= OnMonsterCaptured;
        GameEvents.JobGlobalModsChanged -= OnJobModsChanged;
    }

    private void OnDestroy()
    {
        UnsubscribeUnlockEvents();
        if (SettingsManager.I) SettingsManager.I.OnSettingsChanged -= PullSettings;

        // Persist runtime on destroy to be safe
        SaveRuntimeToSave();
    }

    private void Update()
    {
        _accum += Time.unscaledDeltaTime;
        if (_accum >= tickSeconds)
        {
            Produce(_accum);
            _accum = 0f;
        }
    }

    // ---------------------------- Global change hooks ----------------------------
    private void OnJobModsChanged()
    {
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
            // workers
            for (int i = 0; i < cap; i++) st.workers.Add(null);
            // ensure arrays
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

            float grossRateHr = ComputeRatePerHour(s);

            // Per-slot fatigue & auto-remove logic
            ApplyPerSlotFatigue(dtHours, s);

            // If you also want fatigue to reduce production while working, use avg of active slots
            float avgFatigue = AverageWorkingSlotFatigue(s);
            float finalRateHr = grossRateHr * (1f - Mathf.Clamp01(avgFatigue));
            s.cachedRatePerHour = finalRateHr;

            // Produce & store
            s.storedAmount = Mathf.Min(GetEffectiveStorageCap(s.config), s.storedAmount + finalRateHr * dtHours);

#if UNITY_EDITOR
            if (logProductionBreakdown)
            {
                float shinyAura = ShinySystems.SiteShinyAuraMult(s.workers);
                int shinyCount = CountShinies(s.workers);
                float shinySetMult = 1f + (shinyCount >= 3 ? shiny3Bonus : (shinyCount == 2 ? shiny2Bonus : (shinyCount == 1 ? shiny1Bonus : 0f)));
                float baseAfterSpecies = (grossRateHr == 0f) ? 0f : (grossRateHr / Mathf.Max(1e-4f, shinyAura * shinySetMult));
                DebugLogSiteBreakdown(s, baseAfterSpecies, shinyAura, shinySetMult, avgFatigue, finalRateHr);
            }
#endif
        }

        if (autoReliefEnabled) ApplyClinicRelief(dtHours);

        // Persist runtime periodically (lightweight)
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

            // Empty slot: decay visible fatigue (rest look-n-feel)
            if (w?.def == null)
            {
                if (s.slotFatigue01[i] > 0f)
                {
                    s.slotFatigue01[i] = Mathf.Max(0f, s.slotFatigue01[i] - siteRestDecayPerHour * dtHours);
                }
                continue;
            }

            // Increase fatigue for THIS worker
            float perHour = Mathf.Max(0f, w.def.fatigueRatePerHour);

            // Titles fatigue multiplier (safe-guarded)
            try
            {
                string speciesId = w.def ? w.def.id : null;
                int lvl = GetOwnedLevelOr1(speciesId, w.def);
                float mul = Mathf.Max(0f, TitlesAdapter.GetJobFatigueMult(speciesId, w.def, lvl, s.config.jobType));
                perHour *= mul;
            }
            catch { }

            s.slotFatigue01[i] = Mathf.Min(1f, s.slotFatigue01[i] + perHour * dtHours);

            if (s.slotFatigue01[i] >= 1f - 0.0001f)
            {
                string key = GetBestId(w);
                float hrsCD = Mathf.Max(0f, w.def.fatigueCooldownHours);

                long until = SaveManager.NowUnix() + Mathf.RoundToInt(hrsCD * 3600f);
                s.slotCooldownUntilUnix[i] = until;

                if (!string.IsNullOrEmpty(key)) _cooldownUntil[key] = until;

                // Remove worker from the slot
                s.workers[i] = null;
                RemoveAssignedUnix(key);

                // NEW: auto-collect this site's stored resources
                Collect(s.config.jobType);

                s.slotFatigue01[i] = 1f;

                SaveAssignmentsToSave();
                GameEvents.OnJobsChanged?.Invoke();
            }
        }
    }

    private float ComputeRatePerHour(JobSiteState s)
    {
        // Require at least one worker
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

            string wid = GetBestId(w);
            int lvl = GetOwnedLevelOr1(wid, w.def);

            // Neutral resource hook (tags removed)
            mult *= GetPerResourceWorkerMul(wid, s.config, here: true);

            // Titles: per-worker production multiplier
            try { mult *= Mathf.Max(0f, TitlesAdapter.GetJobRateMult(wid, s.config.jobType)); } catch { }

            sum += mult;
        }

        // “1 + sum/3” scaling for staffed sites
        float normalized = 1f + (sum / 3f);
        float perHour = s.config.baseRatePerHour * normalized;

        // Boss/global
        perHour *= BossDebuffSystem.GetMultiplier(s.config.jobType, SaveManager.NowUnix());

        // Titles site-wide aura: multiply by (1 + auraPct)
        float auraPct = 0f;
        if (_auraByJob != null) _auraByJob.TryGetValue(s.config.jobType, out auraPct);
        if (auraPct > 0f) perHour *= (1f + auraPct);

        // Shiny stacking
        float shinyAura = ShinySystems.SiteShinyAuraMult(s.workers);
        int shinyCount  = CountShinies(s.workers);
        float shinySet  = 1f + (shinyCount >= 3 ? shiny3Bonus : (shinyCount == 2 ? shiny2Bonus : (shinyCount == 1 ? shiny1Bonus : 0f)));

        return perHour * shinyAura * shinySet;
    }

    // ---------------------------- Assignment API ----------------------------
    public bool TryAssignWorkerAt(JobType job, int slotIndex, MonsterDataSO monster, string ownedId = null)
    {
        var s = FindState(job);
        if (s == null || monster == null) return false;

        // ELIGIBILITY GATE (strict)
        if (!IsTypeEligibleFor(job, monster.type))
        {
            return false;
        }

        // cooldown gate
        string key = ownedId ?? monster.id;
        if (IsOnCooldown(key))
        {
            return false;
        }

        int cap = Mathf.Clamp(s.config.maxWorkers, 1, 3);
        if (slotIndex < 0 || slotIndex >= cap) return false;

        EnsureWorkerListSize(s, cap);
        s.workers[slotIndex] = new WorkerRef { def = monster, monsterId = key };

        TouchAssignedUnix(key);
        SaveAssignmentsToSave();
        SaveRuntimeToSave();
        GameEvents.OnJobsChanged?.Invoke();
        return true;
    }

    public bool TryAssignWorker(JobType job, MonsterDataSO monster, string ownedId = null)
    {
        var s = FindState(job);
        if (s == null || monster == null) return false;

        // ELIGIBILITY GATE (strict)
        if (!IsTypeEligibleFor(job, monster.type))
        {
            return false;
        }

        // cooldown gate
        string key = ownedId ?? monster.id;
        if (IsOnCooldown(key))
        {
            return false;
        }

        int cap = Mathf.Clamp(s.config.maxWorkers, 1, 3);
        EnsureWorkerListSize(s, cap);

        int empty = s.workers.FindIndex(w => w == null);
        if (empty == -1) return false;

        s.workers[empty] = new WorkerRef { def = monster, monsterId = key };

        TouchAssignedUnix(key);
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
            if (!IsWorkerMatch(w, ownedIdOrDefId)) continue;

            s.workers[i] = null;
            RemoveAssignedUnix(GetBestId(w));
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
                if (!IsWorkerMatch(w, ownedIdOrDefId)) continue;

                s.workers[i] = null;
                RemoveAssignedUnix(GetBestId(w));
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
            if (w != null && (!string.IsNullOrEmpty(w.monsterId) || w.def != null)) count++;
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
            foreach (var w in s.workers) ja.workerIds.Add(GetBestId(w) ?? "");
            SaveManager.Data.jobAssignments.Add(ja);
        }

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

        foreach (var ja in SaveManager.Data.jobAssignments)
        {
            var s = FindState(ja.job);
            if (s == null) continue;

            int cap = Mathf.Clamp(s.config.maxWorkers, 1, 3);
            EnsureWorkerListSize(s, cap);

            for (int i = 0; i < Mathf.Min(cap, ja.workerIds.Count); i++)
            {
                var wid = ja.workerIds[i];
                if (string.IsNullOrEmpty(wid)) { s.workers[i] = null; continue; }

                var def = ResolveMonsterDef(wid);
                s.workers[i] = def != null ? new WorkerRef { monsterId = wid, def = def } : null;

                if (def != null) TouchAssignedUnix(wid);
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

            // cooldown map
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

            // per-site restore
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

    private bool IsOnCooldown(string ownedOrDefId)
    {
        if (string.IsNullOrEmpty(ownedOrDefId)) return false;
        if (_cooldownUntil.TryGetValue(ownedOrDefId, out long until))
            return until > SaveManager.NowUnix();
        return false;
    }

    // ---------------------------- Unlocks ----------------------------
    private void SubscribeUnlockEvents()
    {
        GameEvents.MonsterCaptured += OnMonsterCaptured;
        GameEvents.StarterChosen += OnStarterChosen;
    }

    private void UnsubscribeUnlockEvents()
    {
        GameEvents.MonsterCaptured -= OnMonsterCaptured;
        GameEvents.StarterChosen -= OnStarterChosen;
    }

    private void OnStarterChosen(MonsterType type)
    {
        TryUnlockSitesForType(type);
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
        if (SaveManager.Data.seenTypes.Add(type)) SaveManager.Save();
    }

    public void RecalculateUnlocksFromSeenTypes()
    {
        if (SaveManager.Data == null) return;

        SaveManager.Data.unlockedJobSites ??= new HashSet<JobType>();
        int before = SaveManager.Data.unlockedJobSites.Count;

        if (SaveManager.Data.seenTypes != null)
        {
            foreach (var t in SaveManager.Data.seenTypes) TryUnlockSitesForType(t);
        }

        if (SaveManager.Data.unlockedJobSites.Count != before) SaveManager.Save();
    }

    private void TryUnlockSitesForType(MonsterType type)
    {
        if (!lockSitesUntilEligible || SaveManager.Data == null)
        {
            return;
        }

        SaveManager.Data.unlockedJobSites ??= new HashSet<JobType>();

        bool changed = false;

        foreach (var job in JobBalance.JobsUnlockedByType(type))
        {
            if (SaveManager.Data.unlockedJobSites.Add(job))
            {
                changed = true;
            }
        }

        if (changed)
        {
            SaveManager.Save();
            RefreshAllJobSiteViewsInScene();
            GameEvents.OnJobsChanged?.Invoke();
        }
    }

    public bool IsSiteUnlocked(JobType job)
    {
        if (!lockSitesUntilEligible) return true;
        return SaveManager.Data?.unlockedJobSites != null && SaveManager.Data.unlockedJobSites.Contains(job);
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

        int available = ResourceBank.Get(ResourceType.Coffee);
        if (available <= 0) return;

        // targets are non-clinic sites that allow relief and have any slot fatigue > 0
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

        // Round-robin across sites and slots
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

            bool allCapped = true;
            for (int i = 0; i < targets.Count; i++)
            {
                float rem = Mathf.Max(0f, targets[i].slotFatigue01.Sum());
                if (rem > 0f) { allCapped = false; break; }
            }
            if (allCapped) break;
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
        if (_idToDef.TryGetValue(idOrOwnedId, out var def) && def) return def;
        return MonsterLibraryLocator.GetById(idOrOwnedId);
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

        // NEW: temporary Sanctum blessing (runtime-only)
        int tempFromBlessings = 0;
        try { tempFromBlessings = Mathf.Max(0, GetActiveBlessingBonus(site.jobType)); }
        catch { tempFromBlessings = 0; }

        var st = FindState(site.jobType);
        float levelMul = (st != null) ? JobLeveling.StorageMultForLevel(st.level) : 1f;

        int preMultFlat = Mathf.Max(0, baseCap + extraFromSave + flatFromTitles + tempFromBlessings);
        return Mathf.Max(0, Mathf.RoundToInt(preMultFlat * levelMul));
    }



    public (JobType job, float hours) GetCurrentJobAndHours(string monsterId)
    {
        if (string.IsNullOrEmpty(monsterId)) return (JobType.None, 0f);

        for (int si = 0; si < States.Count; si++)
        {
            var st = States[si];
            if (st?.workers == null) continue;

            for (int wi = 0; wi < st.workers.Count; wi++)
            {
                var w = st.workers[wi];
                if (!IsWorkerMatch(w, monsterId)) continue;

                var key = GetBestId(w);
                if (!_assignedUnix.TryGetValue(key, out long start)) start = SaveManager.NowUnix();
                float hrs = Mathf.Max(0f, (SaveManager.NowUnix() - start) / 3600f);

                return (st.config ? st.config.jobType : JobType.None, hrs);
            }
        }
        return (JobType.None, 0f);
    }

    private static float GetPerResourceWorkerMul(string workerId, JobSiteSO site, bool here)
    {
        // No tag multipliers; neutral hook.
        return 1f;
    }

    private static bool HasAnyWorker(List<WorkerRef> workers)
    {
        if (workers == null) return false;
        for (int i = 0; i < workers.Count; i++) if (workers[i]?.def != null || !string.IsNullOrEmpty(workers[i]?.monsterId)) return true;
        return false;
    }

    private static List<string> CollectWorkerIds(List<WorkerRef> workers)
    {
        var ids = new List<string>();
        if (workers == null) return ids;

        for (int i = 0; i < workers.Count; i++)
        {
            var w = workers[i];
            var id = GetBestId(w);
            if (!string.IsNullOrEmpty(id)) ids.Add(id);
        }
        return ids;
    }

    private static void ForEachWorkerId(List<WorkerRef> workers, Action<string> action)
    {
        if (workers == null || action == null) return;
        for (int i = 0; i < workers.Count; i++)
        {
            var id = GetBestId(workers[i]);
            if (!string.IsNullOrEmpty(id)) action(id);
        }
    }

    private static string GetBestId(WorkerRef w)
    {
        if (w == null) return null;
        if (!string.IsNullOrEmpty(w.monsterId)) return w.monsterId;
        return w.def ? w.def.id : null;
    }

    private static bool IsWorkerMatch(WorkerRef w, string ownedIdOrDefId)
    {
        if (w == null || string.IsNullOrEmpty(ownedIdOrDefId)) return false;
        if (!string.IsNullOrEmpty(w.monsterId) && w.monsterId == ownedIdOrDefId) return true;
        return w.def != null && w.def.id == ownedIdOrDefId;
    }

    private void TouchAssignedUnix(string key)
    {
        if (!string.IsNullOrEmpty(key)) _assignedUnix[key] = SaveManager.NowUnix();
    }

    private void RemoveAssignedUnix(string key)
    {
        if (!string.IsNullOrEmpty(key)) _assignedUnix.Remove(key);
    }

    private bool TryGetTeamEntry(string ownedId, out int index)
    {
        index = -1;
        var team = SaveManager.Data?.team;
        if (team == null) return false;

        for (int i = 0; i < team.Count; i++)
        {
            var e = team[i];
            if (!string.IsNullOrEmpty(e?.monsterId) && e.monsterId == ownedId)
            {
                index = i;
                return true;
            }
        }
        return false;
    }

    // ---------------------------- Auto-bench (injury) ----------------------------
    private void AutoBenchSweep(float threshold01)
    {
        var team = SaveManager.Data?.team;
        if (team == null || team.Count == 0) return;

        for (int si = 0; si < States.Count; si++)
        {
            var s = States[si];
            if (s?.config == null || s.workers == null) continue;

            for (int wi = 0; wi < s.workers.Count; wi++)
            {
                var w = s.workers[wi];
                if (w == null) continue;

                string ownedId = w.monsterId;
                if (string.IsNullOrEmpty(ownedId)) continue;
                if (!TryGetTeamEntry(ownedId, out int teamIndex)) continue;

                var entry = team[teamIndex];
                int level = Mathf.Max(1, entry.level);
                int curHP = Mathf.Max(0, entry.currentHP);
                float maxHP = Mathf.Max(1f, BattleCalc.CalcHP(w.def, level));
                float hp01 = curHP / maxHP;

                if (hp01 < threshold01)
                {
                    RemoveWorker(s.config.jobType, ownedId);
                    if (autoBenchAutoFill) TryFillSlotFromTeam(s, wi, threshold01);
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
                if (!string.IsNullOrEmpty(w?.monsterId)) used.Add(w.monsterId);
            }
        }

        for (int i = 0; i < team.Count; i++)
        {
            var entry = team[i];
            string candId = entry.monsterId;
            if (string.IsNullOrEmpty(candId) || used.Contains(candId)) continue;

            if (IsOnCooldown(candId)) continue; // don't auto-fill with resting monster

            var def = MonsterLibraryLocator.GetById(entry.monsterId);
            if (!def) continue;

            // NEW: ensure auto-fill respects eligibility
            if (!IsTypeEligibleFor(site.config.jobType, def.type)) continue;

            int level = Mathf.Max(1, entry.level);
            int curHP = Mathf.Max(0, entry.currentHP);
            float maxHP = Mathf.Max(1f, BattleCalc.CalcHP(def, level));
            float hp01 = curHP / maxHP;

            if (hp01 < threshold01) continue;

            return TryAssignWorkerAt(site.config.jobType, slotIndex, def, candId);
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

    // ---------------------------- Editor-only logging ----------------------------
#if UNITY_EDITOR
    private void DebugLogSiteBreakdown(JobSiteState s, float basePerHour, float shinyAura, float shinySetMult, float avgFatigue01, float finalRateHr)
    {
        if (!logProductionBreakdown) return;
        Debug.Log(
            $"[Job Debug] Site={s.config.jobType} | " +
            $"Base={basePerHour:F1}/hr | Aura×{shinyAura:F2} | Set×{shinySetMult:F2} | " +
            $"AvgFatigue={avgFatigue01:P0} | Final={finalRateHr:F1}/hr");
    }
#endif

    // ---------------------------- Local helpers (missing in your file) ----------------------------
    private static int GetOwnedLevelOr1(string ownedOrDefId, MonsterDataSO fallbackDef)
    {
        if (string.IsNullOrEmpty(ownedOrDefId)) return 1;

        var owned = SaveManager.Data?.owned;
        if (owned != null)
        {
            for (int i = 0; i < owned.Count; i++)
            {
                var om = owned[i];
                if (om != null && om.monsterId == ownedOrDefId)
                    return Mathf.Max(1, om.level);
            }
        }

        var team = SaveManager.Data?.team;
        if (team != null)
        {
            for (int i = 0; i < team.Count; i++)
            {
                var e = team[i];
                if (e != null && (e.monsterId == ownedOrDefId))
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

        // Prefer owned-instance record
        var ownedId = w.monsterId;
        if (!string.IsNullOrEmpty(ownedId))
        {
            var ownedList = SaveManager.Data?.owned;
            if (ownedList != null)
            {
                for (int i = 0; i < ownedList.Count; i++)
                {
                    var om = ownedList[i];
                    if (om != null && om.monsterId == ownedId) return om.isShiny;
                }
            }
        }

        // Fallback to def via reflection if present
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

    private JobSiteSO GetSiteConfig(JobType job)
    {
        for (int i = 0; i < jobSites.Count; i++)
        {
            var c = jobSites[i];
            if (!c) continue;
            if (c.jobType == job) return c;
        }
        return null;
    }

    public void ApplyTemporaryStorageBlessing(JobType job, int flatBonus, float durationSeconds)
    {
        if (flatBonus == 0 || durationSeconds <= 0f) return;

        if (_blessingBuffs == null) _blessingBuffs = new List<BlessingBuff>();

        long now   = SaveManager.NowUnix();
        long until = now + Mathf.RoundToInt(durationSeconds);

        // If there's already an active blessing for this site, stack and extend it
        BlessingBuff existing = null;
        for (int i = 0; i < _blessingBuffs.Count; i++)
        {
            var b = _blessingBuffs[i];
            if (b != null && b.job == job && b.untilUnix > now)
            {
                existing = b;
                break;
            }
        }

        if (existing != null)
        {
            existing.flatBonus += flatBonus;
            if (until > existing.untilUnix) existing.untilUnix = until;
        }
        else
        {
            _blessingBuffs.Add(new BlessingBuff
            {
                job = job,
                flatBonus = flatBonus,
                untilUnix = until
            });
        }
    }

    private int GetActiveBlessingBonus(JobType job)
    {
        if (_blessingBuffs == null || _blessingBuffs.Count == 0) return 0;

        long now = SaveManager.NowUnix();
        int total = 0;

        // Clean up expired buffs as we go
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


    public int GetTemporaryStorageBonus(JobType job)
    {
        return GetActiveBlessingBonus(job);
    }

    public float GetBlessingSecondsRemaining(JobType job)
    {
        if (_blessingBuffs == null || _blessingBuffs.Count == 0) return 0f;

        long now = SaveManager.NowUnix();
        long latestUntil = 0;

        for (int i = _blessingBuffs.Count - 1; i >= 0; i--)
        {
            var b = _blessingBuffs[i];
            if (b == null || b.untilUnix <= now)
            {
                _blessingBuffs.RemoveAt(i);
                continue;
            }

            if (b.job == job && b.untilUnix > latestUntil)
                latestUntil = b.untilUnix;
        }

        if (latestUntil <= now) return 0f;
        return Mathf.Max(0f, latestUntil - now);
    }

}
