using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Plain data for the Player Dossier. No UI logic here.
/// </summary>
[Serializable]
public class PlayerDossierSnapshot
{
    [Header("Identity")]
    public string handlerName;
    public string rankName;
    public string operationId;

    [Header("Overview Stats")]
    public int totalOwnedBitlings;
    public int discoveredSpecies;
    public float averageLevel;
    public int shinyOwned;

    [Header("Care Score")]
    [Range(0f, 100f)]
    public float careScorePercent;
    public string careScoreNote;

    // ─────────────────────────────────────────────────────────────
    // PAGE 2 – JOB NETWORK
    // ─────────────────────────────────────────────────────────────
    [Header("Job Network")]
    public JobSiteRowSnapshot[] jobSites;
}

[Serializable]
public class JobSiteRowSnapshot
{
    public JobType job;
    public string displayName;
    public bool unlocked;

    public int hoursSupervised;      // derived from Job XP
    public int materialsProcessed;   // approx lifetime output
    public int outputPerHour;        // current rate/hr (approx)

    public int assignedWorkers;

    public string topPerformerName;  // e.g. "FLAREBYTE"
    public int topPerformerLevel;    // e.g. 14
}

/// <summary>
/// Pure data layer for the dossier. Builds and caches snapshot data from SaveManager.
/// No UI logic here.
/// </summary>
public class PlayerDossierManager : MonoBehaviour
{
    public static PlayerDossierManager I { get; private set; }

    [Header("Debug")]
    [SerializeField] private bool autoRefreshOnEnable = true;

    private PlayerDossierSnapshot _cachedSnapshot;

    private const float JOB_XP_PER_HOUR = 100f; // matches JobXpTracker.xpPerHour design

    private void Awake()
    {
        if (I != null && I != this)
        {
            Destroy(gameObject);
            return;
        }

        I = this;
        DontDestroyOnLoad(gameObject);
    }

    private void OnEnable()
    {
        if (autoRefreshOnEnable)
        {
            RefreshSnapshot();
        }
    }

    /// <summary>
    /// Public read-only access to the most recent snapshot.
    /// </summary>
    public PlayerDossierSnapshot CurrentSnapshot
    {
        get
        {
            if (_cachedSnapshot == null)
            {
                RefreshSnapshot();
            }

            return _cachedSnapshot;
        }
    }

    /// <summary>
    /// Rebuilds the snapshot from the current save data.
    /// Call this whenever you know the player data has changed significantly.
    /// </summary>
    public void RefreshSnapshot()
    {
        if (SaveManager.Data == null)
        {
            SaveManager.LoadOrCreate();
        }

        _cachedSnapshot = BuildSnapshotFromSave();
    }

    // ─────────────────────────────────────────────────────────────
    // Internal: Build snapshot from SaveManager.Data
    // ─────────────────────────────────────────────────────────────

    private PlayerDossierSnapshot BuildSnapshotFromSave()
    {
        var snapshot = new PlayerDossierSnapshot();

        var data = SaveManager.Data;
        if (data == null)
        {
            snapshot.handlerName        = "Handler: BRN Operator";
            snapshot.rankName           = "Rank: Trainee";
            snapshot.operationId        = "Operation ID: BRN-0000-XXXX";

            snapshot.totalOwnedBitlings = 0;
            snapshot.discoveredSpecies  = 0;
            snapshot.averageLevel       = 0f;
            snapshot.shinyOwned         = 0;

            snapshot.careScorePercent   = 0f;
            snapshot.careScoreNote      = "BRN notes: No data available.";

            snapshot.jobSites           = Array.Empty<JobSiteRowSnapshot>();
            return snapshot;
        }

        data.EnsureTransientSets();

        // Identity
        string displayName = string.IsNullOrEmpty(data.playerName) ? "BRN Operator" : data.playerName;
        snapshot.handlerName = $"Handler: {displayName}";
        snapshot.rankName    = "Rank: Trainee"; // TODO: derive from progression later
        snapshot.operationId = $"Operation ID: {FormatOperationId(data.playerId)}";

        // Owned monsters
        int totalOwned = 0;
        int levelSum   = 0;
        int shinyCount = 0;

        if (data.owned != null)
        {
            for (int i = 0; i < data.owned.Count; i++)
            {
                var owned = data.owned[i];
                if (owned == null) continue;

                totalOwned++;
                levelSum += Mathf.Max(1, owned.level);

                if (owned.isShiny)
                    shinyCount++;
            }
        }

        snapshot.totalOwnedBitlings = totalOwned;
        snapshot.shinyOwned         = shinyCount;
        snapshot.averageLevel       = totalOwned > 0 ? (float)levelSum / totalOwned : 0f;

        // Discovered species (prefer ownedIds mirror; fallback to seenTypes)
        int discovered = 0;
        if (data.ownedIds != null && data.ownedIds.Count > 0)
        {
            discovered = data.ownedIds.Count;
        }
        else if (data.seenTypes != null && data.seenTypes.Count > 0)
        {
            discovered = data.seenTypes.Count;
        }
        snapshot.discoveredSpecies = discovered;

        // Care score (simple composite of shinies + avg level)
        float careScore = 0f;
        if (totalOwned > 0)
        {
            float shinyFactor = Mathf.Clamp01(shinyCount / Mathf.Max(1f, totalOwned));
            float levelFactor = Mathf.Clamp01(snapshot.averageLevel / 30f); // assume ~30 is “strong”

            careScore = Mathf.Lerp(40f, 95f, (shinyFactor + levelFactor) * 0.5f);
        }

        snapshot.careScorePercent = careScore;
        snapshot.careScoreNote    = "BRN notes: Bitling care is within stable parameters.";

        // Page 2 – Job stats
        BuildJobStats(data, snapshot);

        return snapshot;
    }

    // ─────────────────────────────────────────────────────────────
    // Job stats (Page 2)
    // ─────────────────────────────────────────────────────────────

    private void BuildJobStats(PlayerManager data, PlayerDossierSnapshot snapshot)
    {
        var jobs = (JobType[])Enum.GetValues(typeof(JobType));
        var rows = new List<JobSiteRowSnapshot>();

        data.jobAssignments ??= new List<JobAssignment>();
        data.jobProgress    ??= new List<JobProgress>();

        foreach (var job in jobs)
        {
            if (job == JobType.None)
                continue; // sentinel

            var row = new JobSiteRowSnapshot
            {
                job         = job,
                displayName = JobStrings.SiteName(job)
            };

            // Unlocked?
            row.unlocked = data.unlockedJobSites != null && data.unlockedJobSites.Contains(job);

            // Assigned workers
            row.assignedWorkers = CountWorkersAssigned(data, job);

            // Derived “hours supervised” from Job XP
            var prog = FindJobProgress(data, job);
            row.hoursSupervised = EstimateHoursFromProgress(job, prog);

            // Production rate (per hour)
            float rateHr = GetCurrentRatePerHour(job, prog?.level ?? 1);
            row.outputPerHour = Mathf.Max(0, Mathf.RoundToInt(rateHr));

            // Approx lifetime materials/output processed
            int mats = Mathf.RoundToInt(row.hoursSupervised * rateHr);
            row.materialsProcessed = Mathf.Max(0, mats);

            // Top performer (highest-level worker assigned)
            var top = FindTopPerformerForJob(data, job);
            if (top != null)
            {
                row.topPerformerLevel = Mathf.Max(1, top.level);

                try
                {
                    var def = MonsterLibraryLocator.GetById(top.monsterId);
                    row.topPerformerName = def != null ? def.displayName : top.monsterId;
                }
                catch
                {
                    row.topPerformerName = top.monsterId;
                }
            }
            else
            {
                row.topPerformerName  = string.Empty;
                row.topPerformerLevel = 0;
            }

            rows.Add(row);
        }

        snapshot.jobSites = rows.ToArray();
    }

    private int CountWorkersAssigned(PlayerManager data, JobType job)
    {
        int workers = 0;
        if (data.jobAssignments == null) return 0;

        for (int i = 0; i < data.jobAssignments.Count; i++)
        {
            var assign = data.jobAssignments[i];
            if (assign == null || assign.job != job || assign.workerIds == null)
                continue;

            workers += assign.workerIds.Count;
        }

        return workers;
    }

    private JobProgress FindJobProgress(PlayerManager data, JobType job)
    {
        if (data.jobProgress == null) return null;

        for (int i = 0; i < data.jobProgress.Count; i++)
        {
            var jp = data.jobProgress[i];
            if (jp != null && jp.job == job)
                return jp;
        }

        return null;
    }

    private int EstimateHoursFromProgress(JobType job, JobProgress prog)
    {
        if (prog == null) return 0;

        int totalXp = 0;

        // Add full XP of completed levels
        for (int lvl = 1; lvl < prog.level; lvl++)
            totalXp += JobLeveling.MaxXpForLevel(job, lvl);

        // Add current level progress
        int clampedXP = Mathf.Max(0, prog.currentXP);
        totalXp += clampedXP;

        float hours = totalXp / JOB_XP_PER_HOUR;
        return Mathf.Max(0, Mathf.FloorToInt(hours));
    }

    private float GetCurrentRatePerHour(JobType job, int siteLevel)
    {
        // Prefer live JobManager data if available
        var jm = JobManager.I;
        if (jm != null)
        {
            for (int i = 0; i < jm.States.Count; i++)
            {
                var s = jm.States[i];
                if (s?.config == null) continue;
                if (s.config.jobType != job) continue;

                // cachedRatePerHour is already post-modifiers & fatigue
                return Mathf.Max(0f, s.cachedRatePerHour);
            }
        }

        // Fallback: approximate from JobSiteSO baseRate + level
        JobSiteSO site = FindJobSiteConfig(job);
        if (site == null) return 0f;

        float baseRate = site.baseRatePerHour;
        int lvl = Mathf.Clamp(siteLevel, 1, JobLeveling.MaxLevel);

        // Simple level bonus: 1, 1.25, 1.5 for levels 1–3
        float levelMult = lvl switch
        {
            1 => 1.0f,
            2 => 1.25f,
            _ => 1.5f
        };

        return baseRate * levelMult;
    }

    private JobSiteSO FindJobSiteConfig(JobType job)
    {
        // Use JobManager's config list if available
        var jm = JobManager.I;
        if (jm == null) return null;

        var sites = jm.GetSitesArray();
        for (int i = 0; i < sites.Length; i++)
        {
            var s = sites[i];
            if (s != null && s.jobType == job)
                return s;
        }

        return null;
    }

    private OwnedMonsterData FindTopPerformerForJob(PlayerManager data, JobType job)
    {
        if (data.jobAssignments == null || data.owned == null)
            return null;

        JobAssignment assignment = null;
        for (int i = 0; i < data.jobAssignments.Count; i++)
        {
            var a = data.jobAssignments[i];
            if (a != null && a.job == job)
            {
                assignment = a;
                break;
            }
        }

        if (assignment == null || assignment.workerIds == null || assignment.workerIds.Count == 0)
            return null;

        OwnedMonsterData best = null;

        for (int i = 0; i < assignment.workerIds.Count; i++)
        {
            string id = assignment.workerIds[i];
            if (string.IsNullOrEmpty(id)) continue;

            // Try to resolve by ownedUID first, then by monsterId as a fallback
            OwnedMonsterData om = data.owned.Find(o =>
                o != null &&
                (o.ownedUID == id || o.monsterId == id));

            if (om == null) continue;

            if (best == null || om.level > best.level)
                best = om;
        }

        return best;
    }

    private string FormatOperationId(string playerId)
    {
        if (string.IsNullOrEmpty(playerId))
            return "BRN-0000-XXXX";

        string trimmed = playerId.Replace("-", string.Empty).ToUpperInvariant();
        if (trimmed.Length <= 8)
            return $"BRN-{trimmed}";

        string head = trimmed.Substring(0, 4);
        string tail = trimmed.Substring(trimmed.Length - 4, 4);
        return $"{head}-{tail}";
    }
}
