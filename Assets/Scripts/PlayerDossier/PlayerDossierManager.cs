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
    public string rankName;
    public string operationId;

    [Header("Promotions")]
    public int promotionRank;
    public int promotionXP;
    public int promotionXpIntoRank;
    public int promotionXpToNext;
    [Range(0f, 1f)] public float promotionProgress01;

    [Header("Overview Stats")]
    public int totalOwnedBitlings;
    public int discoveredSpecies;
    public float averageLevel;
    public int premiumOwned;

    [Header("Care Score")]
    [Range(0f, 100f)] public float careScorePercent;
    public string careScoreNote;

    [Header("Care Score Breakdown")]
    [Range(0f, 100f)] public float careDevelopmentPercent;
    [Range(0f, 100f)] public float careBalancePercent;
    [Range(0f, 100f)] public float careRecoveryPercent;
    [Range(0f, 100f)] public float careAssignmentPercent;

    // ─────────────────────────────────────────────────────────────
    // PAGE 2 – JOB NETWORK
    // ─────────────────────────────────────────────────────────────
    [Header("Job Network")]
    public JobSiteRowSnapshot[] jobSites;

    // ─────────────────────────────────────────────────────────────
    // PAGE 3 – FIELD OPERATIONS
    // ─────────────────────────────────────────────────────────────
    [Header("Field Operations")]
    public int riftsInitiated;
    public int captureSuccessRate; // 0–100
    public int riftStabilizations;
    public int rareBitlingsFound;
    public int premiumDiscoveries;
    public int longestCaptureStreak;
    public string[] fieldOpsHighlights;

    // ─────────────────────────────────────────────────────────────
    // PAGE 4 – RESOURCES
    // ─────────────────────────────────────────────────────────────
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
    public int premiumOrbCount;
    public int blessingScaleCount;
    public int restChargeCount;
    public int growthCoreCount;
    public int packVoucherCount;

    public int bullTokenCount;
    public int bearTokenCount;
    public int arenaTicketCount;
    public bool bullTokensUnlocked;
    public bool bearTokensUnlocked;
    public bool arenaTicketsUnlocked;

    public int conversionEfficiencyPercent;

    // ─────────────────────────────────────────────────────────────
    // PAGE 5 – BRN RÉSUMÉ
    // ─────────────────────────────────────────────────────────────
    [Header("Page 5 – BRN Résumé")]
    public string[] resumeLines;
    public string brnResumeNote;

    // ─────────────────────────────────────────────────────────────
    // PAGE 6 – ACHIEVEMENTS
    // ─────────────────────────────────────────────────────────────
    [Header("Page 6 - Achievements")]
    public int achievementsUnlocked;
    public int achievementsTotal;
    public AchievementRowSnapshot[] achievements;

    // ─────────────────────────────────────────────────────────────
    // PAGE 7 – RANKS (scroll list)
    // (UI builds rows from PromotionTableSO; snapshot keeps the current totals.)
    // ─────────────────────────────────────────────────────────────
}

[Serializable]
public class AchievementRowSnapshot
{
    public string id;
    public Sprite icon;
    public string name;
    public string description;
    public bool unlocked;
    public int value;
    public int goal;
    public bool isNew;
}

[Serializable]
public class JobSiteRowSnapshot
{
    public JobType job;
    public string displayName;
    public bool unlocked;

    public int hoursSupervised;    // derived from Job XP
    public int materialsProcessed; // approx lifetime output
    public int outputPerHour;      // current rate/hr (approx)

    public int assignedWorkers;

    public string topPerformerName; // e.g. "FLAREBYTE"
    public int topPerformerLevel;   // e.g. 14
}

/// <summary>
/// Pure data layer for the dossier. Builds and caches snapshot data from SaveManager.
/// No UI logic here.
/// </summary>
public class PlayerDossierManager : MonoBehaviour
{
    public static PlayerDossierManager I { get; private set; }

    [Header("Behavior")]
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
            RefreshSnapshot();
    }

    /// <summary>
    /// Public read-only access to the most recent snapshot.
    /// </summary>
    public PlayerDossierSnapshot CurrentSnapshot
    {
        get
        {
            if (_cachedSnapshot == null)
                RefreshSnapshot();

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
            SaveManager.LoadOrCreate();

        _cachedSnapshot = BuildSnapshotFromSave();
    }

    private PlayerDossierSnapshot BuildSnapshotFromSave()
    {
        var snapshot = new PlayerDossierSnapshot();

        var data = SaveManager.Data;
        if (data == null)
        {
            snapshot.rankName = "Rank: Trainee";
            snapshot.operationId = "Operation ID: BRN-0000-XXXX";

            snapshot.totalOwnedBitlings = 0;
            snapshot.discoveredSpecies = 0;
            snapshot.averageLevel = 0f;
            snapshot.premiumOwned = 0;

            snapshot.careScorePercent = 0f;
            snapshot.careScoreNote = "BRN notes: No data available.";

            snapshot.careDevelopmentPercent = 0f;
            snapshot.careBalancePercent = 0f;
            snapshot.careRecoveryPercent = 0f;
            snapshot.careAssignmentPercent = 0f;

            snapshot.jobSites = Array.Empty<JobSiteRowSnapshot>();

            snapshot.riftsInitiated = 0;
            snapshot.captureSuccessRate = 0;
            snapshot.riftStabilizations = 0;
            snapshot.rareBitlingsFound = 0;
            snapshot.premiumDiscoveries = 0;
            snapshot.longestCaptureStreak = 0;
            snapshot.fieldOpsHighlights = Array.Empty<string>();

            snapshot.achievementsUnlocked = 0;
            snapshot.achievementsTotal = 0;
            snapshot.achievements = Array.Empty<AchievementRowSnapshot>();

            return snapshot;
        }

        data.EnsureTransientSets();

        // Page 1 shows the title/name of the player's current Promotion Rank (e.g., "Intern").
        snapshot.rankName = $"Rank: {DeriveRankName(data)}";
        snapshot.operationId = $"Operation ID: {FormatOperationId(data.playerId)}";

        // Promotions
        snapshot.promotionRank = Mathf.Max(1, data.promotionRank);
        snapshot.promotionXP = Mathf.Max(0, data.promotionXP);

        int floor = (PromotionManager.I != null)
            ? PromotionManager.I.GetTotalXpToReach(snapshot.promotionRank)
            : GetTotalXpToReach_Fallback(snapshot.promotionRank);

        int xpInto = Mathf.Max(0, snapshot.promotionXP - Mathf.Max(0, floor));
        int xpToNext = (PromotionManager.I != null)
            ? PromotionManager.I.GetXpToNext(snapshot.promotionRank, snapshot.promotionXP)
            : GetXpToNext_Fallback(snapshot.promotionRank, snapshot.promotionXP);

        snapshot.promotionXpIntoRank = xpInto;
        snapshot.promotionXpToNext = xpToNext;
        snapshot.promotionProgress01 = (xpToNext <= 0) ? 1f : Mathf.Clamp01((float)xpInto / (xpInto + xpToNext));

        // Owned monsters
        int totalOwned = 0;
        int levelSum = 0;
        int premiumCount = 0;

        if (data.owned != null)
        {
            for (int i = 0; i < data.owned.Count; i++)
            {
                var owned = data.owned[i];
                if (owned == null) continue;

                totalOwned++;
                levelSum += Mathf.Max(1, owned.level);

                if (owned.isPremium)
                    premiumCount++;
            }
        }

        snapshot.totalOwnedBitlings = totalOwned;
        snapshot.premiumOwned = premiumCount;
        snapshot.averageLevel = totalOwned > 0 ? (float)levelSum / totalOwned : 0f;

        // Discovered species
        int discovered = 0;
        if (data.ownedIds != null && data.ownedIds.Count > 0)
            discovered = data.ownedIds.Count;
        else if (data.seenTypes != null && data.seenTypes.Count > 0)
            discovered = data.seenTypes.Count;

        snapshot.discoveredSpecies = discovered;

        // Page 2 – Jobs (also supports care "Assignment" score)
        BuildJobStats(data, snapshot);

        // Page 3 – Field Ops
        BuildFieldOps(data, snapshot);

        // Page 4 – Resources (also supports care "Recovery" score)
        BuildResourceSummary(data, snapshot);

        // Care score + breakdown (computed AFTER jobs/resources are available)
        ComputeCareScore(data, snapshot);

        // Page 5 – Resume
        BuildResumePage(data, snapshot);

        // Page 6 – Achievements
        BuildAchievementsPage(data, snapshot);

        return snapshot;
    }

    // ─────────────────────────────────────────────────────────────
    // Care Score
    // ─────────────────────────────────────────────────────────────

    private void ComputeCareScore(PlayerManager data, PlayerDossierSnapshot snap)
    {
        if (data == null || snap == null)
            return;

        if (snap.totalOwnedBitlings <= 0)
        {
            snap.careDevelopmentPercent = 0f;
            snap.careBalancePercent = 0f;
            snap.careRecoveryPercent = 0f;
            snap.careAssignmentPercent = 0f;

            snap.careScorePercent = 0f;
            snap.careScoreNote = "BRN notes: No data available.";
            return;
        }

        // DEVELOPMENT (0–100): average level normalized to 30
        float levelFactor = Mathf.Clamp01(snap.averageLevel / 30f);
        float development = Mathf.Lerp(30f, 100f, levelFactor);

        // BALANCE (0–100): % of roster within 50% of avg level
        int balancedCount = 0;
        float threshold = snap.averageLevel * 0.5f;

        if (data.owned != null)
        {
            for (int i = 0; i < data.owned.Count; i++)
            {
                var om = data.owned[i];
                if (om == null) continue;
                if (Mathf.Max(1, om.level) >= threshold) balancedCount++;
            }
        }

        float balanceRatio = balancedCount / Mathf.Max(1f, snap.totalOwnedBitlings);
        float balance = Mathf.Lerp(25f, 100f, Mathf.Clamp01(balanceRatio));

        // RECOVERY (0–100): based on available rest + medkits (simple, non-punitive proxy)
        // Uses values already populated in BuildResourceSummary.
        float restNorm = Mathf.Clamp01(snap.restChargeCount / 10f);
        float medNorm = Mathf.Clamp01(snap.medkitCount / 10f);
        float recovery = Mathf.Lerp(20f, 100f, (restNorm * 0.6f) + (medNorm * 0.4f));

        // ASSIGNMENT (0–100): utilization = assigned workers / total owned
        int assignedWorkers = 0;
        if (data.jobAssignments != null)
        {
            for (int i = 0; i < data.jobAssignments.Count; i++)
            {
                var a = data.jobAssignments[i];
                if (a?.workerIds == null) continue;
                assignedWorkers += a.workerIds.Count;
            }
        }

        float util = Mathf.Clamp01(assignedWorkers / Mathf.Max(1f, snap.totalOwnedBitlings));
        float assignment = Mathf.Lerp(20f, 100f, util);

        snap.careDevelopmentPercent = Mathf.Clamp(development, 0f, 100f);
        snap.careBalancePercent = Mathf.Clamp(balance, 0f, 100f);
        snap.careRecoveryPercent = Mathf.Clamp(recovery, 0f, 100f);
        snap.careAssignmentPercent = Mathf.Clamp(assignment, 0f, 100f);

        // Final headline care score (weighted average)
        float combined =
            (snap.careDevelopmentPercent * 0.40f) +
            (snap.careBalancePercent * 0.20f) +
            (snap.careRecoveryPercent * 0.20f) +
            (snap.careAssignmentPercent * 0.20f);

        snap.careScorePercent = Mathf.Clamp(combined, 0f, 100f);
        snap.careScoreNote = "BRN notes: Bitling care is within stable parameters.";
    }

    // ─────────────────────────────────────────────────────────────
    // Page 2 – Job stats
    // ─────────────────────────────────────────────────────────────

    private void BuildJobStats(PlayerManager data, PlayerDossierSnapshot snapshot)
    {
        var jobs = (JobType[])Enum.GetValues(typeof(JobType));
        var rows = new List<JobSiteRowSnapshot>();

        data.jobAssignments ??= new List<JobAssignment>();
        data.jobProgress ??= new List<JobProgress>();

        foreach (var job in jobs)
        {
            if (job == JobType.None)
                continue;

            var row = new JobSiteRowSnapshot
            {
                job = job,
                displayName = JobStrings.SiteName(job)
            };

            row.unlocked = JobUnlockBridge.IsJobUnlocked(job);

            if (!row.unlocked)
                continue;

            row.assignedWorkers = CountWorkersAssigned(data, job);

            var prog = FindJobProgress(data, job);
            row.hoursSupervised = EstimateHoursFromProgress(job, prog);

            float rateHr = GetCurrentRatePerHour(job, prog?.level ?? 1);
            row.outputPerHour = Mathf.Max(0, Mathf.RoundToInt(rateHr));

            int mats = Mathf.RoundToInt(row.hoursSupervised * rateHr);
            row.materialsProcessed = Mathf.Max(0, mats);

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
                row.topPerformerName = string.Empty;
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

        for (int lvl = 1; lvl < prog.level; lvl++)
            totalXp += JobLeveling.MaxXpForLevel(job, lvl);

        totalXp += Mathf.Max(0, prog.currentXP);

        float hours = totalXp / JOB_XP_PER_HOUR;
        return Mathf.Max(0, Mathf.FloorToInt(hours));
    }

    private float GetCurrentRatePerHour(JobType job, int siteLevel)
    {
        var jm = JobManager.I;
        if (jm != null)
        {
            for (int i = 0; i < jm.States.Count; i++)
            {
                var s = jm.States[i];
                if (s?.config == null) continue;
                if (s.config.jobType != job) continue;

                return Mathf.Max(0f, s.cachedRatePerHour);
            }
        }

        JobSiteSO site = FindJobSiteConfig(job);
        if (site == null) return 0f;

        float baseRate = site.baseRatePerHour;
        int lvl = Mathf.Clamp(siteLevel, 1, JobLeveling.MaxLevel);

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
    // Page 3 – Field Ops
    // ─────────────────────────────────────────────────────────────

    private void BuildFieldOps(PlayerManager data, PlayerDossierSnapshot snapshot)
    {
        var f = data.fieldOps ?? new FieldOpsStats();

        snapshot.riftsInitiated = Mathf.Max(0, f.riftsInitiated);
        snapshot.riftStabilizations = Mathf.Max(0, f.riftStabilizations);
        snapshot.rareBitlingsFound = Mathf.Max(0, f.rareBitlingsFound);
        snapshot.premiumDiscoveries = Mathf.Max(0, f.premiumDiscoveries);
        snapshot.longestCaptureStreak = Mathf.Max(0, f.longestCaptureStreak);

        int attempts = Mathf.Max(0, f.captureAttempts);
        int successes = Mathf.Max(0, f.capturesSuccessful);

        int ratePct = 0;
        if (attempts > 0)
        {
            float ratio = successes / (float)attempts;
            ratePct = Mathf.Clamp(Mathf.RoundToInt(ratio * 100f), 0, 100);
        }

        snapshot.captureSuccessRate = ratePct;

        snapshot.fieldOpsHighlights = (f.recentHighlights != null && f.recentHighlights.Count > 0)
            ? f.recentHighlights.ToArray()
            : Array.Empty<string>();
    }

    // ─────────────────────────────────────────────────────────────
    // Page 4 – Resources
    // ─────────────────────────────────────────────────────────────

    private void BuildResourceSummary(PlayerManager data, PlayerDossierSnapshot s)
    {
        var bank = ResourceManager.I;
        if (bank == null)
        {
            #if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.LogWarning("ResourceManager not found for dossier Page 4.");
            #endif
            return;
        }

        s.creditCount = GetLifetimeCollected(data, ResourceType.Credits, bank.Get(ResourceType.Credits));
        s.energyCount = GetLifetimeCollected(data, ResourceType.Energy, bank.Get(ResourceType.Energy));
        s.medkitCount = GetLifetimeCollected(data, ResourceType.Medkit, bank.Get(ResourceType.Medkit));
        s.materialCount = GetLifetimeCollected(data, ResourceType.Material, bank.Get(ResourceType.Material));
        s.typeResBoosterCount = GetLifetimeCollected(data, ResourceType.PPEPermit, bank.Get(ResourceType.PPEPermit));
        s.lureCount = GetLifetimeCollected(data, ResourceType.Flyer, bank.Get(ResourceType.Flyer));
        s.captureBandCount = GetLifetimeCollected(data, ResourceType.WorkOrder, bank.Get(ResourceType.WorkOrder));
        s.luckCount = GetLifetimeCollected(data, ResourceType.Favor, bank.Get(ResourceType.Favor));
        s.atkBoosterCount = GetLifetimeCollected(data, ResourceType.TrainingVoucher, bank.Get(ResourceType.TrainingVoucher));
        s.hpBoosterCount = GetLifetimeCollected(data, ResourceType.WellnessVoucher, bank.Get(ResourceType.WellnessVoucher));
        s.speedBoosterCount = GetLifetimeCollected(data, ResourceType.EfficiencyVoucher, bank.Get(ResourceType.EfficiencyVoucher));
        s.premiumOrbCount = GetLifetimeCollected(data, ResourceType.PremiumOrb, bank.Get(ResourceType.PremiumOrb));
        s.blessingScaleCount = GetLifetimeCollected(data, ResourceType.BlessingScale, bank.Get(ResourceType.BlessingScale));
        s.restChargeCount = GetLifetimeCollected(data, ResourceType.Coffee, bank.Get(ResourceType.Coffee));
        s.growthCoreCount = GetLifetimeCollected(data, ResourceType.GrowthCore, bank.Get(ResourceType.GrowthCore));
        s.packVoucherCount = GetLifetimeCollected(data, ResourceType.PackVoucher, bank.Get(ResourceType.PackVoucher));

        bool tokenFeature = FeatureUnlockManager.I != null && FeatureUnlockManager.I.IsUnlocked(FeatureId.Exchange_BearBullTokens);
        s.bullTokensUnlocked = tokenFeature;
        s.bearTokensUnlocked = tokenFeature;
        s.bullTokenCount = tokenFeature ? GetLifetimeCollected(data, ResourceType.BullToken, bank.Get(ResourceType.BullToken)) : 0;
        s.bearTokenCount = tokenFeature ? GetLifetimeCollected(data, ResourceType.BearToken, bank.Get(ResourceType.BearToken)) : 0;

        s.arenaTicketsUnlocked = ArenaSaveHelper.IsArenaUnlocked();
        s.arenaTicketCount = s.arenaTicketsUnlocked ? ArenaSaveHelper.GetArenaTicketCount() : 0;

        s.conversionEfficiencyPercent = ComputeHandlerEfficiency(data, s);
    }

    private int GetLifetimeCollected(PlayerManager data, ResourceType type, int fallbackCurrent)
    {
        if (data == null || data.lifetimeResourceCollected == null)
            return Mathf.Max(0, fallbackCurrent);

        int idx = (int)type;
        if (idx < 0 || idx >= data.lifetimeResourceCollected.Count)
            return Mathf.Max(0, fallbackCurrent);

        int lifetime = Mathf.Max(0, data.lifetimeResourceCollected[idx]);
        return Mathf.Max(lifetime, Mathf.Max(0, fallbackCurrent));
    }

    private int ComputeHandlerEfficiency(PlayerManager data, PlayerDossierSnapshot snap)
    {
        if (data == null) return 0;

        float jobLevelScore = 0f;
        if (data.jobProgress != null && data.jobProgress.Count > 0)
        {
            int levelSum = 0;
            int count = 0;

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
                jobLevelScore = Mathf.Clamp01(avgLevel / JobLeveling.MaxLevel);
            }
        }

        float careScoreNorm = Mathf.Clamp01(snap.careScorePercent / 100f);

        var f = data.fieldOps ?? new FieldOpsStats();

        float successNorm = 0f;
        if (f.captureAttempts > 0)
            successNorm = Mathf.Clamp01(f.capturesSuccessful / Mathf.Max(1f, f.captureAttempts));

        float streakNorm = Mathf.Clamp01(f.longestCaptureStreak / 20f);
        float rareNorm = Mathf.Clamp01(f.rareBitlingsFound / 30f);

        float captureScore = (successNorm * 0.5f) + (streakNorm * 0.3f) + (rareNorm * 0.2f);

        int progTotal =
            snap.growthCoreCount +
            snap.blessingScaleCount +
            snap.packVoucherCount +
            snap.premiumOrbCount +
            snap.atkBoosterCount +
            snap.hpBoosterCount +
            snap.speedBoosterCount +
            snap.captureBandCount +
            snap.lureCount +
            snap.luckCount;

        float progNorm = Mathf.Clamp01(progTotal / 100f);
        float creditNorm = Mathf.Clamp01(snap.creditCount / 50000f);

        float resourceUsageScore = (progNorm * 0.6f) + (creditNorm * 0.4f);

        float efficiency =
            (jobLevelScore * 0.35f) +
            (careScoreNorm * 0.25f) +
            (captureScore * 0.25f) +
            (resourceUsageScore * 0.15f);

        return Mathf.RoundToInt(Mathf.Clamp01(efficiency) * 100f);
    }

    // ─────────────────────────────────────────────────────────────
    // Page 5 – Resume
    // ─────────────────────────────────────────────────────────────

    private void BuildResumePage(PlayerManager data, PlayerDossierSnapshot snap)
    {
        var lines = new List<string>();

        var firstOwned = GetFirstOwnedBitling(data);
        if (firstOwned != null)
        {
            string name = GetMonsterDisplayName(firstOwned.monsterId);
            if (!string.IsNullOrEmpty(name))
                lines.Add($"First Bitling acquired: {name}.");
        }

        var highest = GetHighestLevelBitling(data);
        if (highest != null)
        {
            string name = GetMonsterDisplayName(highest.monsterId);
            if (!string.IsNullOrEmpty(name))
                lines.Add($"{name} reached level {Mathf.Max(1, highest.level)}, becoming a core team member.");
        }

        var premium = GetNotablePremium(data);
        if (premium != null)
        {
            string name = GetMonsterDisplayName(premium.monsterId);
            if (!string.IsNullOrEmpty(name))
                lines.Add($"Premium Bitling detected in field operations: {name}.");
        }

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
                lines.Add($"Most productive job site: {bestSite.displayName} ({bestSite.materialsProcessed:N0} units processed to date).");
            }
        }

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

        if (snap.discoveredSpecies > 0)
            lines.Add($"Discovered {snap.discoveredSpecies} Bitling species so far.");

        const int MAX_LINES = 7;
        if (lines.Count > MAX_LINES)
            lines.RemoveRange(MAX_LINES, lines.Count - MAX_LINES);

        snap.resumeLines = lines.ToArray();

        int eff = Mathf.Clamp(snap.conversionEfficiencyPercent, 0, 100);
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

    private OwnedMonsterData GetNotablePremium(PlayerManager data)
    {
        if (data?.owned == null || data.owned.Count == 0)
            return null;

        OwnedMonsterData best = null;
        for (int i = 0; i < data.owned.Count; i++)
        {
            var om = data.owned[i];
            if (om == null || !om.isPremium || string.IsNullOrEmpty(om.monsterId)) continue;

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

    // ─────────────────────────────────────────────────────────────
    // Page 6 – Achievements
    // ─────────────────────────────────────────────────────────────

    private void BuildAchievementsPage(PlayerManager data, PlayerDossierSnapshot snap)
    {
        var am = AchievementManager.I;
        if (am == null)
        {
            snap.achievementsUnlocked = 0;
            snap.achievementsTotal = 0;
            snap.achievements = Array.Empty<AchievementRowSnapshot>();
            return;
        }

        var entries = am.GetAllEntries();
        if (entries == null || entries.Count == 0)
        {
            snap.achievementsUnlocked = 0;
            snap.achievementsTotal = 0;
            snap.achievements = Array.Empty<AchievementRowSnapshot>();
            return;
        }

        int total = 0;
        int unlockedCount = 0;

        var rows = new List<AchievementRowSnapshot>(entries.Count);

        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            if (e == null || string.IsNullOrEmpty(e.id)) continue;

            total++;

            var prog = am.GetProgress(e.id);
            bool unlocked = prog != null && prog.unlocked;

            if (unlocked) unlockedCount++;

            int goal = Mathf.Max(1, e.goal);
            int value = prog != null ? Mathf.Clamp(prog.value, 0, goal) : 0;

            bool showSecret = e.secretUntilUnlocked && !unlocked;

            rows.Add(new AchievementRowSnapshot
            {
                id = e.id,
                icon = e.icon,
                name = showSecret ? "???" : e.displayName,
                description = showSecret ? "Unlock this achievement to reveal details." : e.description,
                unlocked = unlocked,
                value = value,
                goal = goal,
                isNew = unlocked && prog != null && !prog.seen
            });
        }

        rows.Sort((a, b) =>
        {
            int unlockedCompare = b.unlocked.CompareTo(a.unlocked);
            if (unlockedCompare != 0) return unlockedCompare;

            if (!a.unlocked && !b.unlocked)
            {
                float pa = a.goal > 0 ? (a.value / (float)a.goal) : 0f;
                float pb = b.goal > 0 ? (b.value / (float)b.goal) : 0f;

                int progCompare = pb.CompareTo(pa);
                if (progCompare != 0) return progCompare;
            }

            return string.CompareOrdinal(a.id, b.id);
        });

        snap.achievementsTotal = total;
        snap.achievementsUnlocked = unlockedCount;
        snap.achievements = rows.ToArray();
    }

    // ─────────────────────────────────────────────────────────────
    // Rank derivation
    // - Deterministic + safe for older saves.
    // - Uses available progression signals; falls back gracefully.
    // ─────────────────────────────────────────────────────────────
    private static string DeriveRankName(PlayerManager data)
    {
        // Phase 5: Rank name is driven by Promotion Rank (1–20).
        if (data == null) return "Intern";

        int rank = 1;
        try { rank = Mathf.Max(1, data.promotionRank); } catch { rank = 1; }

        if (PromotionManager.I != null)
            return PromotionManager.I.GetRankDisplayName(rank);

        // Safe fallback if PromotionManager is not present in the scene.
        switch (rank)
        {
            case 1: return "Intern";
            case 2: return "Clerk";
            case 3: return "Technician";
            case 4: return "Coordinator";
            case 5: return "Supervisor";
            case 6: return "Auditor";
            case 7: return "Recruiter";
            case 8: return "Compliance Officer";
            case 9: return "Operations Lead";
            case 10: return "Manager";
            case 11: return "Project Lead";
            case 12: return "Department Head";
            case 13: return "Program Lead";
            case 14: return "Regional Manager";
            case 15: return "Director";
            case 16: return "Executive Manager";
            case 17: return "Division Head";
            case 18: return "Senior Director";
            case 19: return "Executive Director";
            case 20: return "Commissioner";
            default: return $"Rank {rank}";
        }
    }

    // ─────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────

    // Fallback promotion curve (must match PromotionManager's fallback so UI is consistent
    // even if PromotionManager is not present in a scene).
    private int GetTotalXpToReach_Fallback(int rank)
    {
        rank = Mathf.Max(1, rank);
        if (rank == 1) return 0;

        int total = 0;
        for (int r = 2; r <= rank; r++)
        {
            // Rank 2 requires 50 XP, then +20 per subsequent rank step.
            int reqForThisStep = 50 + 20 * (r - 2);
            total += Mathf.Max(1, reqForThisStep);
        }
        return total;
    }

    private int GetXpToNext_Fallback(int currentRank, int totalXp)
    {
        currentRank = Mathf.Max(1, currentRank);
        totalXp = Mathf.Max(0, totalXp);

        const int maxRank = 20;
        if (currentRank >= maxRank) return 0;

        int curFloor = GetTotalXpToReach_Fallback(currentRank);
        int nextReq = GetTotalXpToReach_Fallback(currentRank + 1);

        int xpInto = Mathf.Max(0, totalXp - curFloor);
        int xpNeededThisRank = Mathf.Max(1, nextReq - curFloor);
        return Mathf.Max(0, xpNeededThisRank - xpInto);
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