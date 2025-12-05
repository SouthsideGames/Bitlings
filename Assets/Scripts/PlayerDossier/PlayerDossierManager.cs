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
    public int totalJobSites;
    public int unlockedJobSites;
    public int totalWorkersAssigned;

    public JobSiteRowSnapshot[] jobSites; // one per JobType (except None)
}

[Serializable]
public class JobSiteRowSnapshot
{
    public JobType job;
    public string displayName;
    public bool unlocked;
    public int assignedWorkers;
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
            snapshot.handlerName = "Handler: BRN Operator";
            snapshot.rankName = "Rank: Trainee";
            snapshot.operationId = "Operation ID: N/A";

            snapshot.totalOwnedBitlings = 0;
            snapshot.discoveredSpecies = 0;
            snapshot.averageLevel = 0;
            snapshot.shinyOwned = 0;

            snapshot.careScorePercent = 0;
            snapshot.careScoreNote = "BRN notes: No data available.";

            // Page 2 defaults
            snapshot.totalJobSites = 0;
            snapshot.unlockedJobSites = 0;
            snapshot.totalWorkersAssigned = 0;
            snapshot.jobSites = Array.Empty<JobSiteRowSnapshot>();

            return snapshot;
        }

        // Identity
        string displayName = string.IsNullOrEmpty(data.playerName) ? "BRN Operator" : data.playerName;
        snapshot.handlerName = $"Handler: {displayName}";
        snapshot.rankName = "Rank: Trainee"; // TODO: derive from progression later
        snapshot.operationId = string.IsNullOrEmpty(data.playerId)
            ? "Operation ID: BRN-0000-XXXX"
            : $"Operation ID: {data.playerId}";

        // Owned monsters
        int totalOwned = 0;
        int levelSum = 0;
        int shinyCount = 0;

        if (data.owned != null)
        {
            for (int i = 0; i < data.owned.Count; i++)
            {
                var owned = data.owned[i];
                if (owned == null) continue;

                totalOwned++;
                levelSum += owned.level;

                if (owned.isShiny)
                    shinyCount++;
            }
        }

        snapshot.totalOwnedBitlings = totalOwned;
        snapshot.shinyOwned = shinyCount;
        snapshot.averageLevel = totalOwned > 0 ? (float)levelSum / totalOwned : 0f;

        // Discovered species
        int discovered = 0;
        if (data.seenTypes != null && data.seenTypes.Count > 0)
        {
            discovered = data.seenTypes.Count;
        }
        else if (data.ownedIds != null && data.ownedIds.Count > 0)
        {
            discovered = data.ownedIds.Count;
        }
        snapshot.discoveredSpecies = discovered;

        // Care score (placeholder formula)
        float careScore = 0f;
        if (totalOwned > 0)
        {
            float shinyFactor = Mathf.Clamp01(shinyCount / Mathf.Max(1f, totalOwned));
            float levelFactor = Mathf.Clamp01(snapshot.averageLevel / 30f); // assume ~30 as "good" level

            careScore = Mathf.Lerp(40f, 95f, (shinyFactor + levelFactor) * 0.5f);
        }

        snapshot.careScorePercent = careScore;
        snapshot.careScoreNote = "BRN notes: Bitling care is within stable parameters.";

        // ─────────────────────────────────────────────────────────
        // PAGE 2 – JOB NETWORK STATS
        // ─────────────────────────────────────────────────────────
        BuildJobStats(data, snapshot);

        return snapshot;
    }

    private void BuildJobStats(PlayerManager data, PlayerDossierSnapshot snapshot)
    {
        // Safety
        data.unlockedJobSites ??= new HashSet<JobType>();
        data.jobAssignments ??= new System.Collections.Generic.List<JobAssignment>();

        var jobTypes = (JobType[])Enum.GetValues(typeof(JobType));
        var rows = new List<JobSiteRowSnapshot>();

        int totalJobs = 0;
        int unlockedJobs = 0;
        int totalWorkers = 0;

        foreach (var job in jobTypes)
        {
            if (job == JobType.None)
                continue; // Skip sentinel

            totalJobs++;

            var row = new JobSiteRowSnapshot
            {
                job = job,
                displayName = GetJobDisplayName(job)
            };

            // Unlocked?
            bool isUnlocked = data.unlockedJobSites.Contains(job);
            row.unlocked = isUnlocked;
            if (isUnlocked) unlockedJobs++;

            // Workers assigned
            int workerCount = 0;
            for (int i = 0; i < data.jobAssignments.Count; i++)
            {
                var assign = data.jobAssignments[i];
                if (assign == null || assign.job != job || assign.workerIds == null)
                    continue;

                workerCount += assign.workerIds.Count;
            }

            row.assignedWorkers = workerCount;
            totalWorkers += workerCount;

            rows.Add(row);
        }

        snapshot.totalJobSites = totalJobs;
        snapshot.unlockedJobSites = unlockedJobs;
        snapshot.totalWorkersAssigned = totalWorkers;
        snapshot.jobSites = rows.ToArray();
    }

    private string GetJobDisplayName(JobType job)
    {
        switch (job)
        {
            case JobType.Gym:          return "Gym";
            case JobType.Quarry:       return "Quarry";
            case JobType.Mine:         return "Mine";
            case JobType.PowerPlant:   return "Power Plant";
            case JobType.Grove:        return "Grove";
            case JobType.Forge:        return "Forge";
            case JobType.Workshop:     return "Workshop";
            case JobType.Harbor:       return "Harbor";
            case JobType.CryoLab:      return "CryoLab";
            case JobType.Observatory:  return "Observatory";
            case JobType.Containment:  return "Containment";
            case JobType.WyrmDen:      return "Wyrm Den";
            case JobType.ShadowMarket: return "Shadow Market";
            case JobType.Sanctum:      return "Sanctum";
            case JobType.Clinic:       return "Clinic";
            default:                   return job.ToString();
        }
    }
}
