using System;
using System.Collections.Generic;
using UnityEngine;

public enum CheatEffectKind
{
    None = 0,
    AddResource,
    SetResource,
    AddPackShards,
    AddPackVouchers,
    RefillEnergy,
    UnlockAllJobSites,
    AdvanceTimeHours,
    ResetCooldowns,
    ClearAllJobFatigue,
    DiscoverAllMonsters,
    StartRiftWithMonsterId,
    ForcePremiumNextCapture,
    ReviveTeam,
    HealTeamFull,
    UnlockAllPacks,
    Add500ToAllResources,
    Add5000ToAllResources,
    ToggleDiagnosticsPanel,
    MaxPromotionRank,
    RankUpBy1,
    RankUpBy5,

    // Arena debug
    ArenaForceUnlock,
    ArenaGrantTickets,
    ArenaInstantTournament,
    ArenaFullReset,
}

[Serializable]
public class CheatDefinition
{
    [Tooltip("Text you type into the cheat panel (case-insensitive).")]
    public string code;

    [Tooltip("Short description so you remember what this does.")]
    [TextArea]
    public string description;

    [Tooltip("What the cheat actually does.")]
    public CheatEffectKind effect = CheatEffectKind.None;

    [Header("AddResource settings (used if effect = AddResource / SetResource)")]
    [Tooltip("Resource to add or set.")]
    public ResourceType resourceType = ResourceType.Credits;

    [Tooltip("Amount to add (AddResource) or set to (SetResource).")]
    public int amount = 0;

    [Header("AdvanceTimeHours settings (used if effect = AdvanceTimeHours)")]
    [Tooltip("How many hours to simulate for offline systems.")]
    [Min(1)] public int hours = 1;

    [Header("StartRiftWithMonsterId settings")]
    [Tooltip("Monster ID to start an rift with (e.g., M-001).")]
    public string monsterId;

    [Tooltip("If true, spend energy as if the player tapped Rift.")]
    public bool spendEnergy = false;
}

public class CheatCodeManager : MonoBehaviour
{
    public static CheatCodeManager I { get; private set; }

    [Header("Cheat Definitions")]
    [Tooltip("Configure your secret codes and their effects here.")]
    [SerializeField] private List<CheatDefinition> cheats = new();

    [Header("Security")]
    [SerializeField] private int maxInvalidAttempts = 3;
    [SerializeField] private int lockHours = 24;

    [Tooltip("If false, cheats are only usable in UNITY_EDITOR or DEVELOPMENT_BUILD.")]
    [SerializeField] private bool allowCheatsInReleaseBuilds = false;

    const long SECONDS_PER_HOUR = 3600;
    const long SECONDS_PER_DAY = 86400;

    void Awake()
    {
        if (I != null && I != this)
        {
            Destroy(gameObject);
            return;
        }
        I = this;

#if !UNITY_EDITOR && !DEVELOPMENT_BUILD
        allowCheatsInReleaseBuilds = false;
#endif

        // Normalize codes at startup (trim + upper)
        for (int i = 0; i < cheats.Count; i++)
        {
            var cd = cheats[i];
            if (cd == null) continue;
            if (string.IsNullOrWhiteSpace(cd.code)) continue;
            cd.code = NormalizeCode(cd.code);
        }
    }

    string NormalizeCode(string raw)
    {
        return string.IsNullOrWhiteSpace(raw)
            ? string.Empty
            : raw.Trim().ToUpperInvariant();
    }

    bool CheatsAllowed(out string blockedMessage)
    {
        blockedMessage = string.Empty;

        // NOTE:
        // We intentionally use runtime checks (Application.isEditor / Debug.isDebugBuild)
        // instead of #if UNITY_EDITOR / #if DEVELOPMENT_BUILD so the serialized security
        // fields are referenced in all builds and don't produce CS0414 warnings.

        if (Application.isEditor)
            return true;

        // Debug.isDebugBuild is true for Development Builds (and editor playmode),
        // but we already handled editor above. In dev builds, always allow cheats.
        if (Debug.isDebugBuild)
            return true;

        // Release build logic
        if (!allowCheatsInReleaseBuilds)
        {
            blockedMessage = "Cheats are disabled in this build.";
            return false;
        }

        return true;
    }

    // ─────────────────────────────────────────────────────────────
    // Lock / Attempts (persistent)
    // ─────────────────────────────────────────────────────────────

    public bool IsLocked(out long remainingSeconds)
    {
        remainingSeconds = 0;

        if (SaveManager.Data == null) return false;

        long now = SaveManager.NowUnix();
        long until = SaveManager.Data.cheatLockedUntilUnix;

        if (until <= 0) return false;

        if (now >= until)
        {
            // Expired -> clear state
            SaveManager.Data.cheatLockedUntilUnix = 0;
            SaveManager.Data.cheatInvalidAttempts = 0;
            SaveManager.Save();
            return false;
        }

        remainingSeconds = Math.Max(0, until - now);
        return true;
    }

    public string GetLockedMessage()
    {
        if (!IsLocked(out long remain))
            return string.Empty;

        // Themed lock message requested by you
        return "REISSUING NEW SECURITY BADGES...\n" +
               $"Process will be completed in: {FormatRemaining(remain)}";
    }

    string FormatRemaining(long seconds)
    {
        if (seconds < 0) seconds = 0;
        var ts = TimeSpan.FromSeconds(seconds);

        // Always show HH:MM:SS, even if > 24h (shouldn't be, but safe)
        long totalHours = (long)ts.TotalHours;
        return $"{totalHours:00}:{ts.Minutes:00}:{ts.Seconds:00}";
    }

    void RegisterInvalidAttempt(out string message, out bool triggeredLock)
    {
        triggeredLock = false;
        message = "INVALID PASSCODE.";

        if (SaveManager.Data == null)
        {
            message = "Save data not loaded.";
            return;
        }

        // If already locked, just report lock message
        if (IsLocked(out _))
        {
            message = GetLockedMessage();
            return;
        }

        SaveManager.Data.cheatInvalidAttempts = Mathf.Max(0, SaveManager.Data.cheatInvalidAttempts);
        SaveManager.Data.cheatInvalidAttempts++;

        int a = SaveManager.Data.cheatInvalidAttempts;

        if (a <= 1)
        {
            message = "INVALID PASSCODE.\nCheck authorization with Management.";
        }
        else if (a == 2)
        {
            message = "ACCESS DENIED.\nSecurity team has been notified.";
        }
        else
        {
            // 3rd+ -> lock for 24 hours
            long now = SaveManager.NowUnix();
            long lockSeconds = (lockHours <= 0 ? SECONDS_PER_DAY : lockHours * SECONDS_PER_HOUR);
            SaveManager.Data.cheatLockedUntilUnix = now + lockSeconds;
            SaveManager.Data.cheatInvalidAttempts = maxInvalidAttempts; // clamp to max
            triggeredLock = true;

            message = "SYSTEM LOCKDOWN INITIATED.\nShutting down access.";
        }

        SaveManager.Save();
    }

    void ResetInvalidAttemptsIfAny()
    {
        if (SaveManager.Data == null) return;

        if (SaveManager.Data.cheatInvalidAttempts != 0)
        {
            SaveManager.Data.cheatInvalidAttempts = 0;
            SaveManager.Save();
        }
    }

    // ─────────────────────────────────────────────────────────────
    // Apply Cheat
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Tries to apply a cheat. Returns true on success, plus a user-facing message.
    /// On third invalid attempt, this will lock cheats for 24 hours (or lockHours).
    /// </summary>
    public bool TryApplyCheat(string rawCode, out string message)
    {
        message = string.Empty;

        // If locked, do not allow any attempts
        if (IsLocked(out _))
        {
            message = GetLockedMessage();
            return false;
        }

        string normalized = NormalizeCode(rawCode);
        if (string.IsNullOrEmpty(normalized))
        {
            message = "Enter a code first.";
            return false;
        }

        // Build gating: keep cheats from accidentally shipping live.
        if (!CheatsAllowed(out message))
            return false;

        var cd = cheats.Find(c => c != null && c.code == normalized);
        if (cd == null)
        {
            RegisterInvalidAttempt(out message, out _);
            return false;
        }

        bool ok = ExecuteCheat(cd, out message);

        if (ok)
        {
            // Successful use resets invalid attempt streak
            ResetInvalidAttemptsIfAny();
            return true;
        }

        if (string.IsNullOrEmpty(message))
            message = "Cheat failed.";

        // Note: A *valid code* that fails does NOT count as an invalid passcode.
        return false;
    }

    // ─────────────────────────────────────────────────────────────
    // Execution
    // ─────────────────────────────────────────────────────────────

    bool ExecuteCheat(CheatDefinition cd, out string message)
    {
        message = string.Empty;

        switch (cd.effect)
        {
            case CheatEffectKind.AddResource:
                return ExecuteAddResource(cd, out message);

            case CheatEffectKind.SetResource:
                return ExecuteSetResource(cd, out message);

            case CheatEffectKind.AddPackShards:
                return ExecuteAddPackCurrency("Shards", cd.amount, out message);

            case CheatEffectKind.AddPackVouchers:
                return ExecuteAddPackCurrency("Vouchers", cd.amount, out message);

            case CheatEffectKind.RefillEnergy:
                return ExecuteRefillEnergy(out message);

            case CheatEffectKind.UnlockAllJobSites:
                return ExecuteUnlockAllJobSites(out message);

            case CheatEffectKind.AdvanceTimeHours:
                return ExecuteAdvanceTimeHours(cd.hours, out message);

            case CheatEffectKind.ResetCooldowns:
                return ExecuteResetCooldowns(out message);

            case CheatEffectKind.ClearAllJobFatigue:
                return ExecuteClearAllJobFatigue(out message);

            case CheatEffectKind.DiscoverAllMonsters:
                return ExecuteDiscoverAllMonsters(out message);

            case CheatEffectKind.StartRiftWithMonsterId:
                return ExecuteStartRiftWithMonsterId(cd.monsterId, cd.spendEnergy, out message);

            case CheatEffectKind.ForcePremiumNextCapture:
                return ExecuteForcePremiumNextCapture(out message);

            case CheatEffectKind.ReviveTeam:
                return ExecuteReviveTeam(out message);

            case CheatEffectKind.HealTeamFull:
                return ExecuteHealTeamFull(out message);

            case CheatEffectKind.UnlockAllPacks:
                return ExecuteUnlockAllPacks(out message);

            case CheatEffectKind.Add500ToAllResources:
                return ExecuteAdd500ToAllResources(out message);

            case CheatEffectKind.Add5000ToAllResources:
                return ExecuteAdd5000ToAllResources(out message);

            case CheatEffectKind.ToggleDiagnosticsPanel:
                return ExecuteToggleDiagnosticsPanel(out message);

            case CheatEffectKind.MaxPromotionRank:
                return ExecuteMaxPromotionRank(out message);

            case CheatEffectKind.RankUpBy1:
                return ExecuteRankUp(1, out message);

            case CheatEffectKind.RankUpBy5:
                return ExecuteRankUp(5, out message);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            case CheatEffectKind.ArenaForceUnlock:
                ArenaDebugHelper.ForceUnlockArena();
                message = "Arena force-unlocked.";
                return true;

            case CheatEffectKind.ArenaGrantTickets:
                ArenaDebugHelper.GrantTickets(cd.amount > 0 ? cd.amount : 3);
                message = $"Granted {(cd.amount > 0 ? cd.amount : 3)} arena ticket(s).";
                return true;

            case CheatEffectKind.ArenaInstantTournament:
                var record = ArenaDebugHelper.CreateFakeTournament();
                if (record != null) ArenaDebugHelper.InstantlyCompleteTournament(record);
                message = record != null ? "Instant tournament complete." : "Failed to create tournament.";
                return record != null;

            case CheatEffectKind.ArenaFullReset:
                ArenaDebugHelper.FullArenaReset();
                message = "Arena fully reset.";
                return true;
#endif

            default:
                message = "Cheat not configured.";
                return false;
        }
    }

    // ─────────────────────────────────────────────────────────────
    // Booster helper (per-type capped)
    // ─────────────────────────────────────────────────────────────
    static bool IsBooster(ResourceType t)
    {
        return t == ResourceType.PPEPermit
            || t == ResourceType.TrainingVoucher
            || t == ResourceType.WellnessVoucher
            || t == ResourceType.EfficiencyVoucher;
    }

    // ─────────────────────────────────────────────────────────────
    // Add Resource (UPDATED for per-type cap)
    // ─────────────────────────────────────────────────────────────
    bool ExecuteAddResource(CheatDefinition cd, out string message)
    {
        message = string.Empty;

        if (cd.amount <= 0)
        {
            message = "Cheat amount must be > 0.";
            return false;
        }

        if (SaveManager.Data == null)
        {
            message = "Save data not loaded.";
            return false;
        }

        int amt = cd.amount;

        if (IsBooster(cd.resourceType))
        {
            int room = ResourceBank.GetBoosterRoom(cd.resourceType);
            if (room <= 0)
            {
                message = $"{cd.resourceType} already at cap ({ResourceBank.BoosterCapPerType}).";
                return false;
            }
            amt = Mathf.Min(amt, room);
        }

        if (ResourceManager.I != null) ResourceManager.I.Add(cd.resourceType, amt);
        else ResourceBank.Add(cd.resourceType, amt);

        if (IsBooster(cd.resourceType) && amt < cd.amount)
            message = $"Gave {amt}/{cd.amount} {cd.resourceType} (capped at {ResourceBank.BoosterCapPerType}).";
        else
            message = $"Gave {amt} {cd.resourceType}.";

        return true;
    }

    // ─────────────────────────────────────────────────────────────
    // Set Resource (unchanged; ResourceBank.Set enforces booster cap)
    // ─────────────────────────────────────────────────────────────
    bool ExecuteSetResource(CheatDefinition cd, out string message)
    {
        message = string.Empty;

        if (cd.amount < 0)
        {
            message = "Value must be >= 0.";
            return false;
        }

        if (SaveManager.Data == null)
        {
            message = "Save data not loaded.";
            return false;
        }

        if (ResourceManager.I != null) ResourceManager.I.Set(cd.resourceType, cd.amount);
        else ResourceBank.Set(cd.resourceType, cd.amount);

        GameEvents.OnResourcesChanged?.Invoke();
        message = $"Set {cd.resourceType} to {cd.amount}.";
        return true;
    }

    // ─────────────────────────────────────────────────────────────
    // Pack currency (UPDATED: boosters cap individually)
    // ─────────────────────────────────────────────────────────────
    bool ExecuteAddPackCurrency(string label, int amount, out string message)
    {
        message = string.Empty;

        if (amount <= 0)
        {
            message = "Cheat amount must be > 0.";
            return false;
        }

        if (SaveManager.Data == null)
        {
            message = "Save data not loaded.";
            return false;
        }

        // PackVoucher itself is not capped here.
        if (ResourceManager.I != null) ResourceManager.I.Add(ResourceType.PackVoucher, amount);
        else ResourceBank.Add(ResourceType.PackVoucher, amount);

        // Also grant the three voucher boosters (each caps at BoosterCapPerType).
        if (ResourceManager.I != null)
        {
            ResourceManager.I.Add(ResourceType.TrainingVoucher, amount);
            ResourceManager.I.Add(ResourceType.WellnessVoucher, amount);
            ResourceManager.I.Add(ResourceType.EfficiencyVoucher, amount);
        }
        else
        {
            ResourceBank.Add(ResourceType.TrainingVoucher, amount);
            ResourceBank.Add(ResourceType.WellnessVoucher, amount);
            ResourceBank.Add(ResourceType.EfficiencyVoucher, amount);
        }

        message = $"Gave {amount} Pack {label}.";
        return true;
    }

    // ─────────────────────────────────────────────────────────────
    // Add 500 to all resources (UPDATED: boosters cap individually)
    // ─────────────────────────────────────────────────────────────
    bool ExecuteAdd500ToAllResources(out string message)
    {
        message = string.Empty;

        if (SaveManager.Data == null)
        {
            message = "Save data not loaded.";
            return false;
        }

        const int AMOUNT = 500;
        int touched = 0;

        ResourceBank.BeginBatch();
        try
        {
            foreach (ResourceType t in Enum.GetValues(typeof(ResourceType)))
            {
                if (t == ResourceType.None) continue;

                // Use ResourceBank.Set directly to bypass WorldEvent gain multipliers
                // that can zero-out the delta for certain resource types (e.g. Coffee).
                int cur = ResourceBank.Get(t);
                ResourceBank.Set(t, cur + AMOUNT);

                GameEvents.ResourceAdded?.Invoke(t, AMOUNT);
                touched++;
            }
        }
        finally
        {
            ResourceBank.EndBatch();
        }

        GameEvents.OnResourcesChanged?.Invoke();

        message = $"Applied cheat: +{AMOUNT} to each resource (boosters capped at {ResourceBank.BoosterCapPerType}). Updated {touched} entries.";
        return true;
    }

    bool ExecuteAdd5000ToAllResources(out string message)
    {
        message = string.Empty;

        if (SaveManager.Data == null)
        {
            message = "Save data not loaded.";
            return false;
        }

        const int AMOUNT = 5000;
        int touched = 0;

        ResourceBank.BeginBatch();
        try
        {
            foreach (ResourceType t in Enum.GetValues(typeof(ResourceType)))
            {
                if (t == ResourceType.None) continue;

                // Use ResourceBank.Set directly to bypass WorldEvent gain multipliers
                // that can zero-out the delta for certain resource types (e.g. Coffee).
                int cur = ResourceBank.Get(t);
                ResourceBank.Set(t, cur + AMOUNT);

                GameEvents.ResourceAdded?.Invoke(t, AMOUNT);
                touched++;
            }
        }
        finally
        {
            ResourceBank.EndBatch();
        }

        GameEvents.OnResourcesChanged?.Invoke();

        message = $"Applied cheat: +{AMOUNT} to each resource (boosters capped at {ResourceBank.BoosterCapPerType}). Updated {touched} entries.";
        return true;
    }

    bool ExecuteMaxPromotionRank(out string message)
    {
        message = string.Empty;

        var data = SaveManager.Data;
        if (data == null)
        {
            message = "Save data not loaded.";
            return false;
        }

        // Prevent meta progression edits during an active Iron run; the guard will flag these events.
        if (IronCareerRuntime.IsActive)
        {
            message = "Promotion rank cheats are disabled during Iron Career runs.";
            return false;
        }

        int maxRank = GetPromotionMaxRankForCheat();
        int oldRank = Mathf.Max(1, data.promotionRank);
        int targetRank = Mathf.Max(1, maxRank);

        int targetXp = GetPromotionTotalXpToReachForCheat(targetRank);
        if (targetXp < 0) targetXp = data.promotionXP;

        data.promotionRank = targetRank;
        data.promotionXP = Mathf.Max(targetXp, data.promotionXP);

        SaveManager.Save();

        // Grant any rank-up rewards that were skipped by setting rank directly.
        if (oldRank < targetRank && PromotionManager.I != null)
            PromotionManager.I.GrantRankRewards(oldRank + 1, targetRank);

        ComputePromotionProgressForCheat(out int xpThisRank, out int xpToNext);

        GameEvents.PromotionProgressChanged?.Invoke(data.promotionRank, data.promotionXP, xpThisRank, xpToNext);
        if (oldRank != data.promotionRank)
            GameEvents.PromotionRankChanged?.Invoke(oldRank, data.promotionRank);

        message = $"Promotion rank set to {data.promotionRank} (max). Synergy, difficulty, and Iron Career unlocked.";
        return true;
    }

    int GetPromotionMaxRankForCheat()
    {
        if (PromotionManager.I != null)
            return PromotionManager.I.GetMaxRank();
        return 25;
    }

    int GetPromotionTotalXpToReachForCheat(int rank)
    {
        if (PromotionManager.I != null)
            return PromotionManager.I.GetTotalXpToReach(rank);
        return GetPromotionTotalXpFallback(rank);
    }

    int GetPromotionTotalXpFallback(int rank)
    {
        if (rank <= 1) return 0;

        int total = 0;
        for (int r = 2; r <= rank; r++)
        {
            int reqForThisStep = 50 + 20 * (r - 2);
            total += Mathf.Max(1, reqForThisStep);
        }
        return total;
    }

    void ComputePromotionProgressForCheat(out int xpThisRank, out int xpToNext)
    {
        xpThisRank = 0;
        xpToNext = 0;

        var data = SaveManager.Data;
        if (data == null) return;

        if (PromotionManager.I != null)
        {
            xpThisRank = PromotionManager.I.GetXpIntoCurrentRank(data.promotionRank, data.promotionXP);
            xpToNext = PromotionManager.I.GetXpToNext(data.promotionRank, data.promotionXP);
            return;
        }

        int curFloor = GetPromotionTotalXpFallback(data.promotionRank);
        int nextReq = GetPromotionTotalXpFallback(data.promotionRank + 1);
        int maxRank = GetPromotionMaxRankForCheat();

        xpThisRank = Mathf.Max(0, data.promotionXP - curFloor);
        xpToNext = data.promotionRank >= maxRank ? 0 : Mathf.Max(0, nextReq - data.promotionXP);
    }

    // ─────────────────────────────────────────────────────────────
    // Everything below here is your original code (unchanged)
    // ─────────────────────────────────────────────────────────────

    bool ExecuteRankUp(int ranks, out string message)
    {
        message = string.Empty;

        var data = SaveManager.Data;
        if (data == null)
        {
            message = "Save data not loaded.";
            return false;
        }

        if (IronCareerRuntime.IsActive)
        {
            message = "Promotion rank cheats are disabled during Iron Career runs.";
            return false;
        }

        int maxRank = GetPromotionMaxRankForCheat();
        int oldRank = Mathf.Max(1, data.promotionRank);
        int targetRank = Mathf.Min(oldRank + ranks, maxRank);

        if (oldRank >= maxRank)
        {
            message = $"Already at max rank ({maxRank}).";
            return false;
        }

        int targetXp = GetPromotionTotalXpToReachForCheat(targetRank);
        if (targetXp < 0) targetXp = data.promotionXP;

        data.promotionRank = targetRank;
        data.promotionXP = Mathf.Max(targetXp, data.promotionXP);

        SaveManager.Save();

        // Grant any rank-up rewards that were skipped by setting rank directly.
        if (oldRank < targetRank && PromotionManager.I != null)
            PromotionManager.I.GrantRankRewards(oldRank + 1, targetRank);

        ComputePromotionProgressForCheat(out int xpThisRank, out int xpToNext);

        GameEvents.PromotionProgressChanged?.Invoke(data.promotionRank, data.promotionXP, xpThisRank, xpToNext);
        GameEvents.PromotionRankChanged?.Invoke(oldRank, data.promotionRank);

        message = $"Promotion rank increased from {oldRank} to {data.promotionRank}.";
        return true;
    }

    bool ExecuteRefillEnergy(out string message)
    {
        message = string.Empty;

        if (SaveManager.Data == null)
        {
            message = "Save data not loaded.";
            return false;
        }

        if (RiftManager.I == null)
        {
            message = "RiftManager missing.";
            return false;
        }

        ResourceBank.EnsureSize();
        int current = ResourceBank.Get(ResourceType.Energy);
        int max = SaveManager.Data.riftMax;

        int missing = max - current;
        if (missing <= 0)
        {
            message = "Energy already full.";
            return false;
        }

        RiftManager.I.AddEnergy(missing, allowOvercap: false);
        message = $"Energy refilled to {max}.";
        return true;
    }

    bool ExecuteUnlockAllJobSites(out string message)
    {
        message = string.Empty;

        if (SaveManager.Data == null)
        {
            message = "Save data not loaded.";
            return false;
        }

        if (JobManager.I == null)
        {
            message = "JobManager missing.";
            return false;
        }

        // IMPORTANT:
        // Jobs can be unlocked via the upgrades system (FeatureUnlockManager) OR directly via save flags.
        // This cheat must keep BOTH systems in sync, otherwise the Upgrades UI will still show jobs as purchasable.

        SaveManager.Data.unlockedJobSitesList ??= new List<JobType>();
        SaveManager.Data.unlockedJobSites ??= new HashSet<JobType>();

        int before = SaveManager.Data.unlockedJobSites.Count;

        var sites = JobManager.I.GetSitesArray();
        if (sites != null)
        {
            for (int i = 0; i < sites.Length; i++)
            {
                var so = sites[i];
                if (so == null) continue;
                if (so.jobType == JobType.None) continue;

                // Use the bridge so FeatureUnlockManager stays consistent.
                JobUnlockBridge.UnlockJob(so.jobType, syncFeatureUnlock: true);
            }
        }

        SaveManager.Data.EnsureTransientSets();

        int after = SaveManager.Data.unlockedJobSites.Count;
        int added = after - before;

        // Bridge already saves/refreshes per-job, but we keep this final save to be safe.
        SaveManager.Save();

        if (JobManager.I != null)
            JobManager.I.RefreshAllJobSiteViewsInScene();

        GameEvents.OnJobsChanged?.Invoke();

        foreach (var v in FindObjectsByType<JobSiteView>(FindObjectsSortMode.None))
            v.Refresh();

        message = added > 0
            ? $"Unlocked {added} job site(s)."
            : "All job sites already unlocked.";

        return true;
    }

    bool ExecuteAdvanceTimeHours(int hours, out string message)
    {
        message = string.Empty;

        if (hours <= 0)
        {
            message = "Hours must be > 0.";
            return false;
        }

        if (SaveManager.Data == null)
        {
            message = "Save data not loaded.";
            return false;
        }

        long delta = (long)hours * 3600L;

        SaveManager.Data.lastSavedUnix = Math.Max(0, SaveManager.Data.lastSavedUnix - delta);
        SaveManager.Data.energyLastUnix = Math.Max(0, SaveManager.Data.energyLastUnix - delta);

        var rt = SaveManager.LoadJobRuntime();
        if (rt != null)
        {
            rt.savedAtUnix = Math.Max(0, rt.savedAtUnix - delta);
            SaveManager.SaveJobRuntime(rt);
        }

        if (JobManager.I != null) JobManager.I.ProcessOfflineAllSites();
        HealthRegenSystem.I?.TryApplyOfflineRegen();
        if (RiftManager.I != null) RiftManager.I.Cheat_ApplyOfflineEnergyRegen();

        SaveManager.Save();

        message = $"Advanced time by {hours} hour(s).";
        return true;
    }

    bool ExecuteResetCooldowns(out string message)
    {
        message = string.Empty;

        if (JobManager.I != null)
        {
            int cleared = JobManager.I.Cheat_ResetCooldowns();
            message = $"Reset {cleared} cooldown(s).";
            return true;
        }

        var blob = SaveManager.LoadJobRuntime();
        if (blob == null)
        {
            message = "No job runtime data found.";
            return false;
        }

        int count = 0;

        if (blob.sites != null)
        {
            foreach (var s in blob.sites)
            {
                if (s?.slotCooldownUntilUnix == null) continue;
                for (int i = 0; i < s.slotCooldownUntilUnix.Length; i++)
                {
                    if (s.slotCooldownUntilUnix[i] != 0) count++;
                    s.slotCooldownUntilUnix[i] = 0;
                }
            }
        }

        if (blob.cooldowns != null)
        {
            count += blob.cooldowns.Count;
            blob.cooldowns.Clear();
        }

        SaveManager.SaveJobRuntime(blob);
        message = $"Reset {count} cooldown(s).";
        return true;
    }

    bool ExecuteClearAllJobFatigue(out string message)
    {
        message = string.Empty;

        if (JobManager.I != null)
        {
            int cleared = JobManager.I.Cheat_ClearAllFatigue();
            message = $"Cleared fatigue on {cleared} slot(s).";
            return true;
        }

        var blob = SaveManager.LoadJobRuntime();
        if (blob == null)
        {
            message = "No job runtime data found.";
            return false;
        }

        int count = 0;
        if (blob.sites != null)
        {
            foreach (var s in blob.sites)
            {
                if (s?.slotFatigue01 == null) continue;
                for (int i = 0; i < s.slotFatigue01.Length; i++)
                {
                    if (s.slotFatigue01[i] > 0f) count++;
                    s.slotFatigue01[i] = 0f;
                }
            }
        }

        SaveManager.SaveJobRuntime(blob);
        message = $"Cleared fatigue on {count} slot(s).";
        return true;
    }

    bool ExecuteDiscoverAllMonsters(out string message)
    {
        message = string.Empty;

        if (SaveManager.Data == null)
        {
            message = "Save data not loaded.";
            return false;
        }

        var lib = MonsterLibraryLocator.Lib;
        if (lib == null)
        {
            message = "Monster library missing.";
            return false;
        }

        int added = 0;
        foreach (var def in lib.All)
        {
            if (def == null || string.IsNullOrEmpty(def.id)) continue;
            if (SaveManager.Discover(def.id, save: false)) added++;
        }

        SaveManager.Save();
        MonsterCatalog.Invalidate();

        message = added > 0 ? $"Discovered {added} monster(s)." : "All monsters already discovered.";
        return true;
    }

    bool ExecuteStartRiftWithMonsterId(string monsterId, bool spendEnergy, out string message)
    {
        message = string.Empty;

        if (RiftManager.I == null)
        {
            message = "RiftManager missing.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(monsterId))
        {
            message = "Monster ID param is empty.";
            return false;
        }

        monsterId = monsterId.Trim();
        if (!MonsterLibraryLocator.TryGet(monsterId, out var def) || def == null)
        {
            message = $"Monster '{monsterId}' not found in library.";
            return false;
        }

        bool ok = RiftManager.I.RequestForcedRift(monsterId, spendEnergy: spendEnergy, out string reason);
        if (!ok)
        {
            message = string.IsNullOrEmpty(reason) ? "Could not start rift." : reason;
            return false;
        }

        message = $"Forced rift: {def.displayName} ({def.id}).";
        return true;
    }

    bool ExecuteForcePremiumNextCapture(out string message)
    {
        message = string.Empty;

        if (SaveManager.Data == null)
        {
            message = "Save data not loaded.";
            return false;
        }

        SaveManager.Data.forcePremiumCapturesRemaining = Mathf.Max(1, SaveManager.Data.forcePremiumCapturesRemaining + 1);
        SaveManager.Save();

        message = "Next capture will be PREMIUM.";
        return true;
    }

    bool ExecuteReviveTeam(out string message)
    {
        message = string.Empty;

        var data = SaveManager.Data;
        if (data == null || data.team == null || data.team.Count == 0)
        {
            message = "No team found.";
            return false;
        }

        var lib = MonsterLibraryLocator.Lib;
        if (lib == null)
        {
            message = "Monster library missing.";
            return false;
        }

        long now = SaveManager.NowUnix();
        int revived = 0;

        for (int i = 0; i < data.team.Count; i++)
        {
            var t = data.team[i];
            if (t == null || string.IsNullOrEmpty(t.monsterId)) continue;
            if (t.currentHP > 0) continue;

            var def = lib.GetById(t.monsterId);
            if (def == null) continue;

            int maxHP = HealingService.CalcMaxHP(def, t.level);

            // Centralized HP write (also syncs owned via ownedUID when present)
            SaveManager.SetTeamSlotHP(i, 1, stampLastHpUnix: true, nowUnix: now, save: false, fireEvents: false);
            revived++;
        }

        if (revived <= 0)
        {
            message = "No KO'd team members to revive.";
            return false;
        }

        SaveManager.Save();
        GameEvents.OnTeamChanged?.Invoke();

        message = $"Revived {revived} team member(s).";
        return true;
    }

    bool ExecuteHealTeamFull(out string message)
    {
        message = string.Empty;

        var data = SaveManager.Data;
        if (data == null || data.team == null || data.team.Count == 0)
        {
            message = "No team found.";
            return false;
        }

        var lib = MonsterLibraryLocator.Lib;
        if (lib == null)
        {
            message = "Monster library missing.";
            return false;
        }

        long now = SaveManager.NowUnix();
        int healed = 0;

        for (int i = 0; i < data.team.Count; i++)
        {
            var t = data.team[i];
            if (t == null || string.IsNullOrEmpty(t.monsterId)) continue;

            var def = lib.GetById(t.monsterId);
            if (def == null) continue;

            int maxHP = HealingService.CalcMaxHP(def, t.level);

            int curHP = (t.currentHP >= 0) ? Mathf.Clamp(t.currentHP, 0, maxHP) : maxHP;
            if (curHP >= maxHP) continue;

            // Centralized HP write (also syncs owned via ownedUID when present)
            SaveManager.SetTeamSlotHP(i, maxHP, stampLastHpUnix: true, nowUnix: now, save: false, fireEvents: false);
            healed++;
        }

        if (healed <= 0)
        {
            message = "Team already at full HP.";
            return false;
        }

        SaveManager.Save();
        GameEvents.OnTeamChanged?.Invoke();

        message = $"Healed {healed} team member(s) to full.";
        return true;
    }

    bool ExecuteUnlockAllPacks(out string message)
    {
        message = string.Empty;

        if (SaveManager.Data == null)
        {
            message = "Save data not loaded.";
            return false;
        }

        if (MonsterPackManager.I == null)
        {
            message = "MonsterPackManager missing in scene.";
            return false;
        }

        int added = MonsterPackManager.I.Cheat_UnlockAllPacks();
        message = added > 0 ? $"Unlocked {added} pack(s)." : "All packs already unlocked.";
        return true;
    }

    public void Cheat_AddPackVouchers(int amount)
    {
        amount = Mathf.Max(0, amount);

        ResourceBank.EnsureSize(); // important if enum changed recently
        ResourceBank.Add(ResourceType.PackVoucher, amount);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        DevLog.Log($"[CHEAT] PackVoucher now = {ResourceBank.Get(ResourceType.PackVoucher)} (+{amount})");
#endif

        GameEvents.OnResourcesChanged?.Invoke();
    }

    bool ExecuteToggleDiagnosticsPanel(out string message)
    {
        message = string.Empty;

        if (SaveManager.Data == null)
        {
            message = "Save data missing.";
            return false;
        }

        if (SaveManager.Data.diagnosticsUnlocked)
        {
            message = "Diagnostics already unlocked.";
            return true;
        }

        // One-way unlock (safe for release builds).
        SaveManager.Data.diagnosticsUnlocked = true;
        SaveManager.Save();

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        DevLog.Log("[DIAG] diagnosticsUnlocked set TRUE and saved.");
#endif

        var btnUI = DiagnosticsButtonUI.I != null
            ? DiagnosticsButtonUI.I
            : FindFirstObjectByType<DiagnosticsButtonUI>(FindObjectsInactive.Include);

        if (btnUI != null)
        {
            btnUI.ApplyFromSave("CheatUnlock");
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            DevLog.Log("[DIAG] Forced DiagnosticsButtonUI.ApplyFromSave()");
#endif
        }
        else
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            DevLog.Log("[DIAG] DiagnosticsButtonUI not found in scene.");
#endif
        }

        message = "Diagnostics unlocked.";
        return true;
    }
}
