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

    // ─────────────────────────────────────────────────────────────
    // PAGE 3 – FIELD OPERATIONS
    // ─────────────────────────────────────────────────────────────
    [Header("Field Operations")]
    public int encountersInitiated;
    public int captureSuccessRate;      // 0–100
    public int riftStabilizations;
    public int rareBitlingsFound;
    public int shinyDiscoveries;
    public int longestCaptureStreak;
    public string[] fieldOpsHighlights;

    [Header("Page 4 – Resources")]
    public int creditCount;
    public int energyCount;
    public int medkitCount;
    public int materialCount;
    public int typeResBoosterCount;
    public int lureCount;
    public int captureBandCount;
    public int luckCount;
    public int atkBoosterCount;
    public int hpBoosterCount;
    public int speedBoosterCount;
    public int shinyOrbCount;
    public int blessingScaleCount;
    public int restChargeCount;
    public int growthCoreCount;
    public int packVoucherCount;

    public int conversionEfficiencyPercent;

    [Header("Page 5 – BRN Résumé")]
    public string[] resumeLines;      // bullet lines for the page
    public string brnResumeNote;      // short supervisor-style note


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

            snapshot.encountersInitiated   = 0;
            snapshot.captureSuccessRate    = 0;
            snapshot.riftStabilizations    = 0;
            snapshot.rareBitlingsFound     = 0;
            snapshot.shinyDiscoveries      = 0;
            snapshot.longestCaptureStreak  = 0;
            snapshot.fieldOpsHighlights    = Array.Empty<string>();

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

        // Page 3 – Field Ops stats
        BuildFieldOps(data, snapshot);

        // Page 4 – Resources
        BuildResourceSummary(data, snapshot);

        // Page 5 – BRN Résumé
        BuildResumePage(data, snapshot);

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

    // ─────────────────────────────────────────────────────────────
    // Field Ops (Page 3)
    // ─────────────────────────────────────────────────────────────

    private void BuildFieldOps(PlayerManager data, PlayerDossierSnapshot snapshot)
    {
        var f = data.fieldOps ?? new FieldOpsStats();

        snapshot.encountersInitiated  = Mathf.Max(0, f.encountersInitiated);
        snapshot.riftStabilizations   = Mathf.Max(0, f.riftStabilizations);
        snapshot.rareBitlingsFound    = Mathf.Max(0, f.rareBitlingsFound);
        snapshot.shinyDiscoveries     = Mathf.Max(0, f.shinyDiscoveries);
        snapshot.longestCaptureStreak = Mathf.Max(0, f.longestCaptureStreak);

        int attempts  = Mathf.Max(0, f.captureAttempts);
        int successes = Mathf.Max(0, f.capturesSuccessful);
        int ratePct   = 0;
        if (attempts > 0)
        {
            float ratio = successes / (float)attempts;
            ratePct = Mathf.Clamp(Mathf.RoundToInt(ratio * 100f), 0, 100);
        }
        snapshot.captureSuccessRate = ratePct;

        if (f.recentHighlights != null && f.recentHighlights.Count > 0)
            snapshot.fieldOpsHighlights = f.recentHighlights.ToArray();
        else
            snapshot.fieldOpsHighlights = Array.Empty<string>();
    }

    // ─────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────

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

    private void BuildResourceSummary(PlayerManager data, PlayerDossierSnapshot s)
    {
        var bank = ResourceManager.I;
        if (bank == null)
        {
            Debug.LogWarning("ResourceManager not found for dossier Page 4.");
            return;
        }

        // ─────────────────────────────────────────────────────────────
        // Raw resource counts (live from ResourceManager)
        // ─────────────────────────────────────────────────────────────
        s.creditCount           = bank.Get(ResourceType.Credits);
        s.energyCount         = bank.Get(ResourceType.Energy);
        s.medkitCount         = bank.Get(ResourceType.Medkit);
        s.materialCount       = bank.Get(ResourceType.Material);
        s.typeResBoosterCount = bank.Get(ResourceType.PPEPermit);
        s.lureCount           = bank.Get(ResourceType.Flyer);
        s.captureBandCount    = bank.Get(ResourceType.WorkOrder);
        s.luckCount           = bank.Get(ResourceType.Favor);
        s.atkBoosterCount     = bank.Get(ResourceType.TrainingVoucher_ATK);
        s.hpBoosterCount      = bank.Get(ResourceType.WellnessVoucher);
        s.speedBoosterCount   = bank.Get(ResourceType.EfficiencyVoucher);
        s.shinyOrbCount       = bank.Get(ResourceType.ShinyOrb);
        s.blessingScaleCount  = bank.Get(ResourceType.BlessingScale);
        s.restChargeCount     = bank.Get(ResourceType.Coffee);
        s.growthCoreCount     = bank.Get(ResourceType.GrowthCore);
        s.packVoucherCount      = bank.Get(ResourceType.PackVoucher);

        // ─────────────────────────────────────────────────────────────
        // BRN Composite Handler Efficiency Score (0–100)
        // ─────────────────────────────────────────────────────────────
        s.conversionEfficiencyPercent = ComputeHandlerEfficiency(data, s);
    }

    /// <summary>
    /// Computes the BRN "Handler Efficiency" score as a composite of:
    /// - Job stability (avg job level)
    /// - Bitling care (careScorePercent)
    /// - Field operations (captures, streaks, rares)
    /// - Resource stewardship (progression items & credits)
    /// Returns 0–100.
    /// </summary>
    private int ComputeHandlerEfficiency(PlayerManager data, PlayerDossierSnapshot snap)
    {
        if (data == null) return 0;

        // ─────────────────────────────────────────────────────────────
        // 1) Job stability / management (0–1)
        // ─────────────────────────────────────────────────────────────
        float jobLevelScore = 0f;
        if (data.jobProgress != null && data.jobProgress.Count > 0)
        {
            int levelSum = 0;
            int count    = 0;

            for (int i = 0; i < data.jobProgress.Count; i++)
            {
                var jp = data.jobProgress[i];
                if (jp == null) continue;

                levelSum += Mathf.Max(1, jp.level);
                count++;
            }

            if (count > 0)
            {
                float avgLevel = levelSum / (float)count;
                jobLevelScore  = Mathf.Clamp01(avgLevel / JobLeveling.MaxLevel);
            }
        }

        // ─────────────────────────────────────────────────────────────
        // 2) Bitling care (0–1) – from Page 1 careScorePercent
        // ─────────────────────────────────────────────────────────────
        float careScoreNorm = Mathf.Clamp01(snap.careScorePercent / 100f);

        // ─────────────────────────────────────────────────────────────
        // 3) Field operations skill (0–1)
        //    Uses: success rate, streak, rare finds
        // ─────────────────────────────────────────────────────────────
        var f = data.fieldOps ?? new FieldOpsStats();

        float successNorm = 0f;
        if (f.captureAttempts > 0)
            successNorm = Mathf.Clamp01(f.capturesSuccessful / Mathf.Max(1f, f.captureAttempts));

        float streakNorm = Mathf.Clamp01(f.longestCaptureStreak / 20f);   // 20+ streak = capped
        float rareNorm   = Mathf.Clamp01(f.rareBitlingsFound / 30f);      // 30+ rares = capped

        float captureScore =
            (successNorm * 0.5f) +
            (streakNorm  * 0.3f) +
            (rareNorm    * 0.2f);

        // ─────────────────────────────────────────────────────────────
        // 4) Resource stewardship (0–1)
        //    Progression items + credits
        // ─────────────────────────────────────────────────────────────
        int progTotal =
            snap.growthCoreCount      +
            snap.blessingScaleCount   +
            snap.packVoucherCount       +
            snap.shinyOrbCount        +
            snap.atkBoosterCount      +
            snap.hpBoosterCount       +
            snap.speedBoosterCount    +
            snap.captureBandCount     +
            snap.lureCount            +
            snap.luckCount;

        // 100+ total progression items is treated as "max utilization"
        float progNorm = Mathf.Clamp01(progTotal / 100f);

        // 50,000+ credits is treated as "max economic readiness"
        float creditNorm = Mathf.Clamp01(snap.creditCount / 50000f);

        float resourceUsageScore =
            (progNorm * 0.6f) +
            (creditNorm * 0.4f);

        // ─────────────────────────────────────────────────────────────
        // 5) Combine into final BRN rating (0–1)
        // ─────────────────────────────────────────────────────────────
        float efficiency =
            (jobLevelScore       * 0.35f) +   // job management
            (careScoreNorm       * 0.25f) +   // bitling care
            (captureScore        * 0.25f) +   // field ops
            (resourceUsageScore  * 0.15f);    // resources

        efficiency = Mathf.Clamp01(efficiency);

        return Mathf.RoundToInt(efficiency * 100f);
    }

    private void BuildResumePage(PlayerManager data, PlayerDossierSnapshot snap)
    {
        var lines = new List<string>();

        // ─────────────────────────────────────────────────────────────
        // 1) First Bitling acquired
        // ─────────────────────────────────────────────────────────────
        var firstOwned = GetFirstOwnedBitling(data);
        if (firstOwned != null)
        {
            string name = GetMonsterDisplayName(firstOwned.monsterId);
            if (!string.IsNullOrEmpty(name))
                lines.Add($"First Bitling acquired: {name}.");
        }

        // ─────────────────────────────────────────────────────────────
        // 2) Highest-level Bitling
        // ─────────────────────────────────────────────────────────────
        var highest = GetHighestLevelBitling(data);
        if (highest != null)
        {
            string name = GetMonsterDisplayName(highest.monsterId);
            if (!string.IsNullOrEmpty(name))
                lines.Add($"{name} reached level {Mathf.Max(1, highest.level)}, becoming a core team member.");
        }

        // ─────────────────────────────────────────────────────────────
        // 3) Shiny highlight
        // ─────────────────────────────────────────────────────────────
        var shiny = GetNotableShiny(data);
        if (shiny != null)
        {
            string name = GetMonsterDisplayName(shiny.monsterId);
            if (!string.IsNullOrEmpty(name))
                lines.Add($"Shiny Bitling detected in field operations: {name}.");
        }

        // ─────────────────────────────────────────────────────────────
        // 4) Job network highlight (most productive site)
        // ─────────────────────────────────────────────────────────────
        if (snap.jobSites != null && snap.jobSites.Length > 0)
        {
            JobSiteRowSnapshot bestSite = null;
            for (int i = 0; i < snap.jobSites.Length; i++)
            {
                var js = snap.jobSites[i];
                if (js == null) continue;
                if (bestSite == null || js.materialsProcessed > bestSite.materialsProcessed)
                    bestSite = js;
            }

            if (bestSite != null && bestSite.materialsProcessed > 0)
            {
                string siteName = bestSite.displayName;
                string units    = bestSite.materialsProcessed.ToString("N0");
                lines.Add($"Most productive job site: {siteName} ({units} units processed to date).");
            }
        }

        // ─────────────────────────────────────────────────────────────
        // 5) Field operations highlights (streaks, rares, rifts)
        // ─────────────────────────────────────────────────────────────
        var f = data.fieldOps ?? new FieldOpsStats();

        if (f.longestCaptureStreak > 0)
            lines.Add($"Capture streak record: {f.longestCaptureStreak} successful captures in a row.");

        if (f.rareBitlingsFound > 0)
            lines.Add($"Rare Bitlings handled in the field: {f.rareBitlingsFound}.");

        if (f.riftStabilizations > 0)
        {
            string rs = f.riftStabilizations == 1 ? "Rift stabilization" : "Rift stabilizations";
            lines.Add($"{rs} completed: {f.riftStabilizations}.");
        }

        // ─────────────────────────────────────────────────────────────
        // 6) Species & care overview
        // ─────────────────────────────────────────────────────────────
        if (snap.discoveredSpecies > 0)
        {
            lines.Add($"Discovered {snap.discoveredSpecies} Bitling species so far.");
        }

        // Keep list from getting too long
        const int MAX_LINES = 7;
        if (lines.Count > MAX_LINES)
            lines.RemoveRange(MAX_LINES, lines.Count - MAX_LINES);

        snap.resumeLines = lines.ToArray();

        // ─────────────────────────────────────────────────────────────
        // BRN résumé note – based on efficiency & care
        // ─────────────────────────────────────────────────────────────
        int eff  = Mathf.Clamp(snap.conversionEfficiencyPercent, 0, 100);
        float careNorm = Mathf.Clamp01(snap.careScorePercent / 100f);

        if (eff >= 75 && careNorm >= 0.7f)
        {
            snap.brnResumeNote =
                "Handler performance is strong across care, job management, and field operations. Recommended for rank review at the next BRN audit.";
        }
        else if (eff >= 40)
        {
            snap.brnResumeNote =
                "Handler performance remains stable during the Rift crisis. Continued monitoring and gradual increase in responsibilities is advised.";
        }
        else
        {
            snap.brnResumeNote =
                "Handler record indicates developing competencies. Additional supervision, training, and controlled field exposure are recommended.";
        }
    }

    private OwnedMonsterData GetFirstOwnedBitling(PlayerManager data)
    {
        if (data?.owned == null || data.owned.Count == 0)
            return null;

        // Best-effort: first non-null entry
        for (int i = 0; i < data.owned.Count; i++)
        {
            var om = data.owned[i];
            if (om != null && !string.IsNullOrEmpty(om.monsterId))
                return om;
        }

        return null;
    }

    private OwnedMonsterData GetHighestLevelBitling(PlayerManager data)
    {
        if (data?.owned == null || data.owned.Count == 0)
            return null;

        OwnedMonsterData best = null;
        for (int i = 0; i < data.owned.Count; i++)
        {
            var om = data.owned[i];
            if (om == null || string.IsNullOrEmpty(om.monsterId)) continue;

            if (best == null || om.level > best.level)
                best = om;
        }
        return best;
    }

    private OwnedMonsterData GetNotableShiny(PlayerManager data)
    {
        if (data?.owned == null || data.owned.Count == 0)
            return null;

        // Prefer highest-level shiny
        OwnedMonsterData best = null;
        for (int i = 0; i < data.owned.Count; i++)
        {
            var om = data.owned[i];
            if (om == null || !om.isShiny || string.IsNullOrEmpty(om.monsterId)) continue;

            if (best == null || om.level > best.level)
                best = om;
        }
        return best;
    }

    private string GetMonsterDisplayName(string monsterId)
    {
        if (string.IsNullOrEmpty(monsterId))
            return null;

        try
        {
            var def = MonsterLibraryLocator.GetById(monsterId);
            if (def != null && !string.IsNullOrEmpty(def.displayName))
                return def.displayName;
        }
        catch { }

        return monsterId;
    }



}
