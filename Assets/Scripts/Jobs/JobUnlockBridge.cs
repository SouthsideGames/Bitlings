using UnityEngine;

public static class JobUnlockBridge
{
    public static bool UnlockJob(JobType job, bool syncFeatureUnlock = true)
    {
        if (job == JobType.None)
            return false;

        var d = SaveManager.Data;
        if (d == null)
            return false;

        d.unlockedJobSites ??= new System.Collections.Generic.HashSet<JobType>();
        d.unlockedJobSitesList ??= new System.Collections.Generic.List<JobType>();

        bool changed = false;

        if (d.unlockedJobSites.Add(job))
            changed = true;

        if (!d.unlockedJobSitesList.Contains(job))
        {
            d.unlockedJobSitesList.Add(job);
            changed = true;
        }

        if (syncFeatureUnlock && FeatureUnlockManager.I != null)
        {
            if (FeatureIdJobs.TryGetJobFeature(job, out var feat) && feat != FeatureId.None)
            {
                FeatureUnlockManager.I.Unlock(feat);
            }
        }

        if (changed)
        {
            SaveManager.Save();

            if (JobManager.I != null)
                JobManager.I.RefreshAllJobSiteViewsInScene();

            GameEvents.OnJobsChanged?.Invoke();
        }

        return changed;
    }

    public static bool IsJobUnlocked(JobType job)
    {
        if (job == JobType.None)
            return false;

        // 1) Feature-based unlock (upgrade purchase)
        if (FeatureUnlockManager.I != null && FeatureIdJobs.TryGetJobFeature(job, out var f) && f != FeatureId.None)
        {
            if (FeatureUnlockManager.I.IsUnlocked(f))
                return true;
        }

        // 2) Save-based unlock (capture / legacy)
        var d = SaveManager.Data;
        if (d == null)
            return false;

        if (d.unlockedJobSitesList != null && d.unlockedJobSitesList.Contains(job))
            return true;

        if (d.unlockedJobSites != null && d.unlockedJobSites.Contains(job))
            return true;

        return false;
    }

    public static void ResetAllJobUnlocks(bool alsoResetPurchasedFlags)
    {
        // 1) Clear SaveManager job unlock sets/lists
        if (SaveManager.Data != null)
        {
            SaveManager.Data.unlockedJobSites?.Clear();
            SaveManager.Data.unlockedJobSitesList?.Clear();
        }

        // 2) Clear purchased/feature side if requested (OPTION A)
        if (alsoResetPurchasedFlags && FeatureUnlockManager.I != null)
        {
            // If this reset is meant to wipe the account, prefer the full reset:
            FeatureUnlockManager.I.HardResetAllUnlocksToDefaults(fireEvents: false);

            // If later you decide you ONLY want job purchases wiped, swap to:
            // FeatureUnlockManager.I.ClearJobUnlockFeaturesToDefaults(fireEvents: false);
        }
    }
}
