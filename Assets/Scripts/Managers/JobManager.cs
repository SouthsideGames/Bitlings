using UnityEngine;
using UnityEngine.Serialization;
using System;
using System.Linq;
using System.Collections.Generic;

/// <summary>Reference to a worker assigned to a site.</summary>
[Serializable]
public class WorkerRef
{
    public string monsterId;     // Prefer owned-instance ID when available
    public MonsterDataSO def;    // Fallback to base definition
}

/// <summary>Save blob for job-site progression.</summary>
[Serializable]
public class JobProgressSave
{
    public JobType job;
    public int level = 1;
    public int maxXPForLevel = 100;
    public int currentXP = 0;
}

/// <summary>Per-site runtime state.</summary>
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
    public float lastTickDelta;
    public float lastRatePerHour;

    // Progression
    public int level = 1;
    public int maxXPForLevel = 100;
    public int currentXP = 0;
}

class JobManager : MonoBehaviour
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

    private readonly Dictionary<string, MonsterDataSO> _idToDef = new();
    private readonly Dictionary<string, long> _assignedUnix = new();
    private Dictionary<JobType, float> _auraByJob = new Dictionary<JobType, float>(16);

    private float _accum;

    // Settings mirrors (live-loaded from SettingsManager JSON)
    private bool autoBenchEnabled = true;
    private float autoBenchHPThreshold01 = 0.20f;
    private bool autoBenchAutoFill = true;
    private bool autoReliefEnabled = true;

    // ---------------------------- Unity ----------------------------
    void Awake()
    {
        if (I != null && I != this) { Destroy(gameObject); return; }
        I = this;
        DontDestroyOnLoad(gameObject);
        Bootstrap();
    }

    void OnEnable()
    {
        LoadAssignmentsFromSave();
        LoadRuntimeFromSave();
        RebuildStates();

        if (simulateOfflineOnLoad)
            SimulateOffline();

        _accum = 0f;
    }

    void Update()
    {
        _accum += Time.unscaledDeltaTime;
        if (_accum >= tickSeconds)
        {
            Tick(_accum);
            _accum = 0f;
        }
    }

    // ---------------------------- Bootstrapping ----------------------------
    private void Bootstrap()
    {
        _idToDef.Clear();

        // Pre-index MonsterLibrary if needed
        var lib = MonsterLibraryLocator.Library;
        if (lib != null && lib.all != null)
        {
            foreach (var d in lib.all)
                if (d) _idToDef[d.id] = d;
        }
    }

    private void RebuildStates()
    {
        States.Clear();
        foreach (var site in jobSites)
        {
            if (!site) continue;
            var s = new JobSiteState { config = site };
            EnsureWorkerListSize(s, Mathf.Clamp(site.maxWorkers, 1, 3));
            RestoreAssignmentsInto(s);
            RestoreProgressionInto(s);
            States.Add(s);
        }
    }

    private void EnsureWorkerListSize(JobSiteState s, int size)
    {
        while (s.workers.Count < size) s.workers.Add(null);
        while (s.workers.Count > size) s.workers.RemoveAt(s.workers.Count - 1);
        if (s.slotFatigue01 == null || s.slotFatigue01.Length != 3) s.slotFatigue01 = new float[3];
        if (s.slotCooldownUntilUnix == null || s.slotCooldownUntilUnix.Length != 3) s.slotCooldownUntilUnix = new long[3];
    }

    private void RestoreAssignmentsInto(JobSiteState s)
    {
        // Pull from SaveManager.Data.jobAssignments
        var save = SaveManager.Data;
        if (save == null || save.jobAssignments == null) return;

        foreach (var ja in save.jobAssignments)
        {
            if (ja == null || ja.job != s.config.jobType) continue;
            EnsureWorkerListSize(s, Mathf.Clamp(s.config.maxWorkers, 1, 3));

            for (int i = 0; i < s.workers.Count && i < ja.ids.Count; i++)
            {
                string id = ja.ids[i];
                if (string.IsNullOrEmpty(id))
                {
                    s.workers[i] = null;
                    continue;
                }

                var def = ResolveDef(id);
                s.workers[i] = new WorkerRef { monsterId = id, def = def };
                TouchAssignedUnix(id);
            }
        }
    }

    private void RestoreProgressionInto(JobSiteState st)
    {
        var data = SaveManager.Data;
        if (data == null || data.jobProgress == null) return;
        foreach (var jp in SaveManager.Data.jobProgress)
        {
            if (jp.job != st.config.jobType) continue;
            st.level = Mathf.Clamp(jp.level, 1, JobLeveling.MaxLevel);
            st.maxXPForLevel = (jp.maxXPForLevel > 0) ? jp.maxXPForLevel : JobLeveling.MaxXpForLevel(jp.job, st.level);
            st.currentXP = Mathf.Clamp(jp.currentXP, 0, st.maxXPForLevel);
            break;
        }
    }

    private MonsterDataSO ResolveDef(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        if (_idToDef.TryGetValue(id, out var cached)) return cached;
        var def = MonsterLibraryLocator.GetById(id);
        if (def) _idToDef[id] = def;
        return def;
    }

    // ---------------------------- Persistence plumbing (assignments / runtime / xp) ----------------------------
    private void LoadAssignmentsFromSave()
    {
        // No-op here; RestoreAssignmentsInto reads from SaveManager.Data.jobAssignments on RebuildStates
    }

    private void SaveAssignmentsToSave()
    {
        var data = SaveManager.Data;
        if (data == null) return;

        if (data.jobAssignments == null) data.jobAssignments = new List<JobAssignmentSave>();

        data.jobAssignments.Clear();
        foreach (var s in States)
        {
            var a = new JobAssignmentSave();
            a.job = s.config.jobType;
            a.ids = new List<string>(s.workers.Count);
            for (int i = 0; i < s.workers.Count; i++)
            {
                var w = s.workers[i];
                a.ids.Add(GetBestId(w));
            }
            data.jobAssignments.Add(a);
        }

        SaveManager.Save();
    }

    private void LoadRuntimeFromSave()
    {
        // Cooldown sidecar
        _cooldownUntil.Clear();
        var data = SaveManager.Data;
        if (data != null && data.jobCooldowns != null)
        {
            foreach (var kv in data.jobCooldowns)
                _cooldownUntil[kv.key] = kv.untilUnix;
        }

        // Assigned-at timestamps
        _assignedUnix.Clear();
        if (data != null && data.jobAssignedAt != null)
        {
            foreach (var kv in data.jobAssignedAt)
                _assignedUnix[kv.key] = kv.unix;
        }

        // Progression mirrors are loaded in RestoreProgressionInto
    }

    private void SaveRuntimeToSave()
    {
        try
        {
            var data = SaveManager.Data;
            if (data == null) return;

            // Cooldowns
            data.jobCooldowns = new List<JobCooldownSave>();
            foreach (var kv in _cooldownUntil)
                data.jobCooldowns.Add(new JobCooldownSave { key = kv.Key, untilUnix = kv.Value });

            // Assigned-at timestamps
            data.jobAssignedAt = new List<JobAssignedAtSave>();
            foreach (var kv in _assignedUnix)
                data.jobAssignedAt.Add(new JobAssignedAtSave { key = kv.Key, unix = kv.Value });

            // Progression mirrors
            if (data.jobProgress == null) data.jobProgress = new List<JobProgressSave>();
            data.jobProgress.Clear();
            foreach (var st in States)
            {
                data.jobProgress.Add(new JobProgressSave
                {
                    job = st.config.jobType,
                    level = st.level,
                    maxXPForLevel = st.maxXPForLevel,
                    currentXP = st.currentXP
                });
            }

            SaveManager.Save();
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }
    }

    // ---------------------------- Core loop ----------------------------
    private void Tick(float dt)
    {
        // Production + relief + fatigue decay, etc. (unchanged from your current logic)
        for (int sIndex = 0; sIndex < States.Count; sIndex++)
        {
            var s = States[sIndex];
            if (s == null || s.config == null) continue;

            float hourly = s.config.baseRatePerHour;
            float shinyBonus = ComputeShinyTeamBonus(s);
            hourly *= (1f + shinyBonus);

            int cap = Mathf.Clamp(s.config.maxWorkers, 1, 3);
            EnsureWorkerListSize(s, cap);

            float activeSlots = 0f;

            for (int i = 0; i < cap; i++)
            {
                var w = s.workers[i];
                if (w == null || w.def == null) continue;

                // cooldown check per-slot worker
                if (IsOnCooldown(GetBestId(w))) continue;

                // apply fatigue (simple sample; keep yours if different)
                float fatigue01 = Mathf.Clamp01(s.slotFatigue01[i]);
                float slotRate = hourly * (1f - 0.5f * fatigue01);
                s.storedAmount += slotRate * (dt / 3600f);
                activeSlots += 1f;

                // build fatigue up a little over time
                s.slotFatigue01[i] = Mathf.Clamp01(s.slotFatigue01[i] + dt / 3600f * 0.0125f);
            }

            // empty-slot decay (rest)
            if (activeSlots < cap)
            {
                float restPerHour = siteRestDecayPerHour;
                for (int i = 0; i < cap; i++)
                {
                    if (s.workers[i] != null && s.workers[i].def != null) continue;
                    s.slotFatigue01[i] = Mathf.Clamp01(s.slotFatigue01[i] - restPerHour * (dt / 3600f));
                }
            }

            s.lastRatePerHour = hourly;
            s.lastTickDelta = dt;
            if (s.storedAmount > s.config.storageCap)
                s.storedAmount = s.config.storageCap;
        }
    }

    private float ComputeShinyTeamBonus(JobSiteState s)
    {
        int shinyCount = 0;
        foreach (var w in s.workers)
        {
            if (w == null || w.def == null) continue;
            if (IsShiny(w.def)) shinyCount++;
        }

        return shinyCount switch
        {
            0 => 0f,
            1 => shiny1Bonus,
            2 => shiny2Bonus,
            _ => shiny3Bonus
        };
    }

    // ---------------------------- Public API ----------------------------
    public JobSiteState FindState(JobType job)
    {
        for (int i = 0; i < States.Count; i++)
        {
            var s = States[i];
            if (s != null && s.config != null && s.config.jobType == job) return s;
        }
        return null;
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

        var res = JobOutput.OutputFor(s.config.produces);
        ResourceBank.I?.Add(res, whole);
        GameEvents.OnJobsChanged?.Invoke();
        return whole;
    }

    // --- NEW: Eligibility check ---
    /// <summary>Check if a monster type is eligible to work at a given job site.</summary>
    public bool IsTypeEligibleFor(JobType job, MonsterType type)
    {
        // Find site config
        JobSiteSO cfg = null;
        for (int i = 0; i < jobSites.Count; i++)
        {
            var c = jobSites[i];
            if (!c) continue;
            if (c.jobType == job) { cfg = c; break; }
        }
        if (cfg == null) return true; // permissive if no config found

        var list = cfg.eligibleTypes;
        if (list == null || list.Length == 0) return true; // allow all if not configured

        for (int i = 0; i < list.Length; i++)
            if (list[i] == type) return true;

        return false;
    }

    public bool TryAssignWorkerAt(JobType job, int slotIndex, MonsterDataSO monster, string ownedId = null)
    {
        var s = FindState(job);
        if (s == null || monster == null) return false;

        // eligibility gate
        if (!IsTypeEligibleFor(job, monster.type))
        {
            Debug.LogWarning($"[JobManager] {monster.displayName} ({monster.type}) is not eligible for {job}.");
            return false;
        }

        // cooldown gate
        string key = ownedId ?? monster.id;
        if (IsOnCooldown(key))
        {
            Debug.LogWarning($"[JobManager] {key} is resting; cannot assign yet.");
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

    // ---------------------------- Helpers ----------------------------
    private static string GetBestId(WorkerRef w)
    {
        if (w == null) return null;
        if (!string.IsNullOrEmpty(w.monsterId)) return w.monsterId;
        return w.def ? w.def.id : null;
    }

    private static bool IsWorkerMatch(WorkerRef w, string ownedOrDefId)
    {
        if (w == null || string.IsNullOrEmpty(ownedOrDefId)) return false;
        if (!string.IsNullOrEmpty(w.monsterId) && w.monsterId == ownedOrDefId) return true;
        if (w.def && w.def.id == ownedOrDefId) return true;
        return false;
    }

    private bool IsOnCooldown(string key)
    {
        if (string.IsNullOrEmpty(key)) return false;
        if (_cooldownUntil.TryGetValue(key, out var until))
        {
            long now = TimeUtils.UnixNow();
            return now < until;
        }
        return false;
    }

    private void TouchAssignedUnix(string key)
    {
        if (string.IsNullOrEmpty(key)) return;
        _assignedUnix[key] = TimeUtils.UnixNow();
    }

    private void RemoveAssignedUnix(string key)
    {
        if (string.IsNullOrEmpty(key)) return;
        _assignedUnix.Remove(key);
    }

    private MonsterDataSO ResolveOwnedOrBase(string idOrBase)
    {
        if (string.IsNullOrEmpty(idOrBase)) return null;
        return ResolveDef(idOrBase);
    }

    private static bool IsShiny(MonsterDataSO def)
    {
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
}