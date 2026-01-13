// Assets/Scripts/Jobs/JobUnlockBridge.cs
using UnityEngine;

public static class JobUnlockBridge
{
    /// <summary>
    /// Unlock a job site in SaveManager data (authoritative list + cache set),
    /// and optionally sync the corresponding FeatureId as unlocked (for Upgrade UI state).
    /// </summary>
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

        // Cache set
        if (d.unlockedJobSites.Add(job))
            changed = true;

        // Authoritative list
        if (!d.unlockedJobSitesList.Contains(job))
        {
            d.unlockedJobSitesList.Add(job);
            changed = true;
        }

        // Sync the feature flag so upgrades reflect the unlock
        if (syncFeatureUnlock && FeatureUnlockManager.I != null)
        {
            if (FeatureIdJobs.TryGetJobFeature(job, out var feat) && feat != FeatureId.None)
            {
                // This is a free sync; no cost is charged here.
                FeatureUnlockManager.I.Unlock(feat);
            }
        }

        if (changed)
        {
            SaveManager.Save();

            // If JobManager exists, refresh views + events.
            if (JobManager.I != null)
            {
                JobManager.I.RefreshAllJobSiteViewsInScene();
            }

            GameEvents.OnJobsChanged?.Invoke();
        }

        return changed;
    }

    /// <summary>
    /// Returns true if the job is unlocked via either:
    /// - SaveManager job unlock list/set
    /// - OR the job FeatureId is unlocked (upgrade purchase)
    /// </summary>
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
}
