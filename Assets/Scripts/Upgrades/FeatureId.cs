// Assets/Scripts/Static/FeatureId.cs
public enum FeatureId
{
    None = 0,

    // ───── Idle Battle System ─────
    IdleBattle_Basic = 1,          // Unlocks idle battles UI / access
    IdleBattle_RewardBoost = 2,    // Improves idle rewards
    IdleBattle_OfflineCapture = 3, // Unlock offline monster captures
    IdleBattle_LogArchive = 14,        // Save auto-battle logs for later review
    IdleBattle_SpeedControl = 15,  

    // ───── World Events ─────
    WorldEvents_Basic = 16,        // Enables weekly world events + ticker

    // ───── Auto-Growth System ─────
    AutoGrowth_Basic = 4,          // Auto-spend Growth Cores over threshold
    AutoGrowth_UsePresets = 5,     // Auto-apply bucket presets

    // ───── Title Fusion System ─────
    Recycle_Basic = 6,             // Combine 2 titles into 1 hybrid

    // ───── Daily Seeds / Custom Seeds ─────
    Seeds_DailyBasic = 8,          // Daily seed runs
    Seeds_CustomInput = 9,         // Custom seed input
    Seeds_RerollDailyOnce = 10,    // Reroll daily seed once per day

    // ───── Codex Upgrades ─────
    Codex_Favorites = 12,          // Favorite / pin sorting
    Codex_CaptureOnlyFilter = 13,  // "Capture only" filter


    // ─────────────────────────────────────────────────────────────
    // Jobs (Purchasable Unlocks)
    // Keep these in a reserved range for clarity.
    // ─────────────────────────────────────────────────────────────
    Job_Gym = 100,
    Job_Quarry = 101,
    Job_Mine = 102,
    Job_PowerPlant = 103,
    Job_Grove = 104,
    Job_Forge = 105,
    Job_Workshop = 106,
    Job_Harbor = 107,
    Job_CryoLab = 108,
    Job_Observatory = 109,
    Job_Containment = 110,
    Job_WyrmDen = 111,
    Job_ShadowMarket = 112,
    Job_Sanctum = 113,
    Job_Clinic = 114,
    Job_Expedition = 115
}

public static class FeatureIdJobs
{
    public static bool TryGetJobFeature(JobType job, out FeatureId feature)
    {
        feature = FeatureId.None;

        switch (job)
        {
            case JobType.Gym:          feature = FeatureId.Job_Gym; break;
            case JobType.Quarry:       feature = FeatureId.Job_Quarry; break;
            case JobType.Mine:         feature = FeatureId.Job_Mine; break;
            case JobType.Power_Plant:   feature = FeatureId.Job_PowerPlant; break;
            case JobType.Grove:        feature = FeatureId.Job_Grove; break;
            case JobType.Forge:        feature = FeatureId.Job_Forge; break;
            case JobType.Workshop:     feature = FeatureId.Job_Workshop; break;
            case JobType.Harbor:       feature = FeatureId.Job_Harbor; break;
            case JobType.Cryo_Lab:      feature = FeatureId.Job_CryoLab; break;
            case JobType.Observatory:  feature = FeatureId.Job_Observatory; break;
            case JobType.Containment:  feature = FeatureId.Job_Containment; break;
            case JobType.Wyrm_Den:      feature = FeatureId.Job_WyrmDen; break;
            case JobType.Shadow_Market: feature = FeatureId.Job_ShadowMarket; break;
            case JobType.Sanctum:      feature = FeatureId.Job_Sanctum; break;
            case JobType.Clinic:       feature = FeatureId.Job_Clinic; break;
            case JobType.Expedition:   feature = FeatureId.Job_Expedition; break;
            default:
                feature = FeatureId.None;
                return false;
        }

        return feature != FeatureId.None;
    }

    public static bool TryGetJobFromFeature(FeatureId feature, out JobType job)
    {
        job = JobType.None;

        switch (feature)
        {
            case FeatureId.Job_Gym:          job = JobType.Gym; break;
            case FeatureId.Job_Quarry:       job = JobType.Quarry; break;
            case FeatureId.Job_Mine:         job = JobType.Mine; break;
            case FeatureId.Job_PowerPlant:   job = JobType.Power_Plant; break;
            case FeatureId.Job_Grove:        job = JobType.Grove; break;
            case FeatureId.Job_Forge:        job = JobType.Forge; break;
            case FeatureId.Job_Workshop:     job = JobType.Workshop; break;
            case FeatureId.Job_Harbor:       job = JobType.Harbor; break;
            case FeatureId.Job_CryoLab:      job = JobType.Cryo_Lab; break;
            case FeatureId.Job_Observatory:  job = JobType.Observatory; break;
            case FeatureId.Job_Containment:  job = JobType.Containment; break;
            case FeatureId.Job_WyrmDen:      job = JobType.Wyrm_Den; break;
            case FeatureId.Job_ShadowMarket: job = JobType.Shadow_Market; break;
            case FeatureId.Job_Sanctum:      job = JobType.Sanctum; break;
            case FeatureId.Job_Clinic:       job = JobType.Clinic; break;
            case FeatureId.Job_Expedition:   job = JobType.Expedition; break;
            default:
                job = JobType.None;
                return false;
        }

        return job != JobType.None;
    }
}
