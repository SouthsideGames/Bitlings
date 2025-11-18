using System;
using System.Collections.Generic;
using UnityEngine;

public enum CheatEffectKind
{
    None = 0,

    // Simple, safe debug helpers
    AddResource,        // Add a specific ResourceType by amount
    RefillEnergy,       // Top off encounter energy
    UnlockAllJobSites,  // Mark every job site as unlocked
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

    [Header("AddResource settings (used if effect = AddResource)")]
    [Tooltip("Resource to add when using AddResource.")]
    public ResourceType resourceType = ResourceType.Coins;

    [Tooltip("Amount to add when using AddResource.")]
    public int amount = 0;
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
        DontDestroyOnLoad(gameObject);

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

            case CheatEffectKind.RefillEnergy:
                return ExecuteRefillEnergy(out message);

            case CheatEffectKind.UnlockAllJobSites:
                return ExecuteUnlockAllJobSites(out message);

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

        // Use your existing ResourceBank so all events fire correctly.
        ResourceBank.Add(cd.resourceType, cd.amount);

        message = $"Gave {cd.amount} {cd.resourceType}.";
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

        int current = SaveManager.Data.encounterPoints;
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

        if (added > 0)
        {
            message = $"Unlocked {added} job site(s).";
        }
        else
        {
            message = "All job sites already unlocked.";
        }

        return true;
    }
}
