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
    StartEncounterWithMonsterId,
    ForceShinyNextCapture,
    ReviveTeam,
    HealTeamFull,
    UnlockAllPacks,
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

    [Header("StartEncounterWithMonsterId settings")]
    [Tooltip("Monster ID to start an encounter with (e.g., M-001).")]
    public string monsterId;

    [Tooltip("If true, spend energy as if the player tapped Encounter.")]
    public bool spendEnergy = false;
}

public class CheatCodeManager : MonoBehaviour
{
    public static CheatCodeManager I { get; private set; }

    [Header("Cheat Definitions")]
    [Tooltip("Configure your secret codes and their effects here.")]
    [SerializeField] private List<CheatDefinition> cheats = new();

    void Awake()
    {
        if (I != null && I != this)
        {
            Destroy(gameObject);
            return;
        }
        I = this;

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

    /// <summary>
    /// Tries to apply a cheat. Returns true on success, plus a user-facing message.
    /// </summary>
    public bool TryApplyCheat(string rawCode, out string message)
    {
        message = string.Empty;

        string normalized = NormalizeCode(rawCode);
        if (string.IsNullOrEmpty(normalized))
        {
            message = "Enter a code first.";
            return false;
        }

        var cd = cheats.Find(c => c != null && c.code == normalized);
        if (cd == null)
        {
            message = "Invalid code.";
            return false;
        }

        bool ok = ExecuteCheat(cd, out message);
        if (!ok && string.IsNullOrEmpty(message))
            message = "Cheat failed.";

        return ok;
    }

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

            case CheatEffectKind.StartEncounterWithMonsterId:
                return ExecuteStartEncounterWithMonsterId(cd.monsterId, cd.spendEnergy, out message);

            case CheatEffectKind.ForceShinyNextCapture:
                return ExecuteForceShinyNextCapture(out message);

            case CheatEffectKind.ReviveTeam:
                return ExecuteReviveTeam(out message);

            case CheatEffectKind.HealTeamFull:
                return ExecuteHealTeamFull(out message);

            case CheatEffectKind.UnlockAllPacks:
                return ExecuteUnlockAllPacks(out message);


            default:
                message = "Cheat not configured.";
                return false;
        }
    }

    // ─────────────────────────────────────────────────────────────
    // Add Resource
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

        // Prefer ResourceManager so listeners (UI, popups, etc.) stay consistent.
        if (ResourceManager.I != null) ResourceManager.I.Add(cd.resourceType, cd.amount);
        else ResourceBank.Add(cd.resourceType, cd.amount);

        message = $"Gave {cd.amount} {cd.resourceType}.";
        return true;
    }

    // ─────────────────────────────────────────────────────────────
    // Set Resource
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

        // Pack currency is shards-only in this project. It's stored as ResourceType.PackVoucher.
        if (ResourceManager.I != null) ResourceManager.I.Add(ResourceType.PackVoucher, amount);
        else ResourceBank.Add(ResourceType.PackVoucher, amount);

        message = $"Gave {amount} Pack {label}.";
        return true;
    }

    // ─────────────────────────────────────────────────────────────
    // Refill Energy
    // ─────────────────────────────────────────────────────────────
    bool ExecuteRefillEnergy(out string message)
    {
        message = string.Empty;

        if (SaveManager.Data == null)
        {
            message = "Save data not loaded.";
            return false;
        }

        if (EncounterManager.I == null)
        {
            message = "EncounterManager missing.";
            return false;
        }

        ResourceBank.EnsureSize();
        int current = ResourceBank.Get(ResourceType.Energy);
        int max = SaveManager.Data.encounterMax;

        int missing = max - current;
        if (missing <= 0)
        {
            message = "Energy already full.";
            return false;
        }

        EncounterManager.I.AddEnergy(missing, allowOvercap: false);
        message = $"Energy refilled to {max}.";
        return true;
    }

    // ─────────────────────────────────────────────────────────────
    // Unlock All Job Sites
    // ─────────────────────────────────────────────────────────────
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

        SaveManager.Data.unlockedJobSites ??= new HashSet<JobType>();

        int before = SaveManager.Data.unlockedJobSites.Count;

        // Use JobManager's configured sites so we only unlock valid ones.
        var sites = JobManager.I.GetSitesArray();
        if (sites != null)
        {
            for (int i = 0; i < sites.Length; i++)
            {
                var so = sites[i];
                if (so == null) continue;
                if (so.jobType == JobType.None) continue;

                SaveManager.Data.unlockedJobSites.Add(so.jobType);
            }
        }

        int after = SaveManager.Data.unlockedJobSites.Count;
        int added = after - before;

        // Persist + refresh views + notify listeners
        SaveManager.Save();
        JobManager.I.RefreshAllJobSiteViewsInScene();
        GameEvents.OnJobsChanged?.Invoke();

        message = added > 0
            ? $"Unlocked {added} job site(s)."
            : "All job sites already unlocked.";

        return true;
    }

    // ─────────────────────────────────────────────────────────────
    // Advance Time (hours)
    // ─────────────────────────────────────────────────────────────
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

        // Shift the save timestamps backward so existing offline systems see elapsed time.
        SaveManager.Data.lastSavedUnix = Math.Max(0, SaveManager.Data.lastSavedUnix - delta);
        SaveManager.Data.energyLastUnix = Math.Max(0, SaveManager.Data.energyLastUnix - delta);

        // Also shift job runtime savedAtUnix (informational, but keeps it coherent).
        var rt = SaveManager.LoadJobRuntime();
        if (rt != null)
        {
            rt.savedAtUnix = Math.Max(0, rt.savedAtUnix - delta);
            SaveManager.SaveJobRuntime(rt);
        }

        // Run offline systems.
        if (JobManager.I != null) JobManager.I.ProcessOfflineAllSites();
        HealthRegenSystem.I?.TryApplyOfflineRegen();
        if (EncounterManager.I != null) EncounterManager.I.Cheat_ApplyOfflineEnergyRegen();

        SaveManager.Save();

        message = $"Advanced time by {hours} hour(s).";
        return true;
    }

    // ─────────────────────────────────────────────────────────────
    // Reset Cooldowns (Jobs)
    // ─────────────────────────────────────────────────────────────
    bool ExecuteResetCooldowns(out string message)
    {
        message = string.Empty;

        // Prefer in-memory reset if JobManager exists (ensures internal cooldown map clears).
        if (JobManager.I != null)
        {
            int cleared = JobManager.I.Cheat_ResetCooldowns();
            message = $"Reset {cleared} cooldown(s).";
            return true;
        }

        // Fallback: clear runtime sidecar only.
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

    // ─────────────────────────────────────────────────────────────
    // Clear all job fatigue
    // ─────────────────────────────────────────────────────────────
    bool ExecuteClearAllJobFatigue(out string message)
    {
        message = string.Empty;

        if (JobManager.I != null)
        {
            int cleared = JobManager.I.Cheat_ClearAllFatigue();
            message = $"Cleared fatigue on {cleared} slot(s).";
            return true;
        }

        // Fallback: clear runtime sidecar only.
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

    // ─────────────────────────────────────────────────────────────
    // Discover all monsters
    // ─────────────────────────────────────────────────────────────
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

    // ─────────────────────────────────────────────────────────────
    // Start encounter with monster ID
    // ─────────────────────────────────────────────────────────────
    bool ExecuteStartEncounterWithMonsterId(string monsterId, bool spendEnergy, out string message)
    {
        message = string.Empty;

        if (EncounterManager.I == null)
        {
            message = "EncounterManager missing.";
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

        bool ok = EncounterManager.I.RequestForcedEncounter(monsterId, spendEnergy: spendEnergy, out string reason);
        if (!ok)
        {
            message = string.IsNullOrEmpty(reason) ? "Could not start encounter." : reason;
            return false;
        }

        message = $"Forced encounter: {def.displayName} ({def.id}).";
        return true;
    }

    bool ExecuteForceShinyNextCapture(out string message)
    {
        message = string.Empty;

        if (SaveManager.Data == null)
        {
            message = "Save data not loaded.";
            return false;
        }

        SaveManager.Data.forceShinyCapturesRemaining = Mathf.Max(1, SaveManager.Data.forceShinyCapturesRemaining + 1);
        SaveManager.Save();

        message = "Next capture will be SHINY.";
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

            // Only revive those that are KO'd
            if (t.currentHP > 0) continue;

            var def = lib.GetById(t.monsterId);
            if (def == null) continue;

            int maxHP = HealingService.CalcMaxHP(def, t.level);

            // Revive to 1 HP (classic revive behavior)
            t.currentHP = 1;
            t.lastHPUnix = now;

            data.team[i] = t;
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

            t.currentHP = maxHP;
            t.lastHPUnix = now;

            data.team[i] = t;
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

}
