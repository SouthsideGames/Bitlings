using UnityEngine;

public static class JobUnlockBridge
{
    private const float DefaultUnlockStarterHours = 0.10f;
    private const int DefaultUnlockStarterMinAmount = 1;

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
            TryGrantUnlockResources(job);

            SaveManager.Save();

            if (JobManager.I != null)
                JobManager.I.RefreshAllJobSiteViewsInScene();

            GameEvents.OnJobsChanged?.Invoke();
        }

        return changed;
    }

    private static void TryGrantUnlockResources(JobType job)
    {
        if (job == JobType.None)
            return;

        if (ResourceManager.I == null)
            return;

        if (!TryResolveUnlockGrant(job, out var resource, out var amount))
            return;

        if (resource == ResourceType.None || amount <= 0)
            return;

        ResourceManager.I.Add(resource, amount);
    }

    private static bool TryResolveUnlockGrant(JobType job, out ResourceType resource, out int amount)
    {
        resource = JobOutput.Output(job);
        float basePerHour = 0f;

        var jm = JobManager.I;
        if (jm != null)
        {
            var sites = jm.GetSitesArray();
            if (sites != null)
            {
                for (int i = 0; i < sites.Length; i++)
                {
                    var so = sites[i];
                    if (!so || so.jobType != job)
                        continue;

                    if (so.produces != ResourceType.None)
                        resource = so.produces;

                    basePerHour = Mathf.Max(0f, so.baseRatePerHour);
                    break;
                }
            }
        }

        if (resource == ResourceType.None)
        {
            amount = 0;
            return false;
        }

        float starterHours = DefaultUnlockStarterHours;
        int minAmount = DefaultUnlockStarterMinAmount;

        if (GameBalance.TryGet(out var balance) && balance != null)
        {
            starterHours = Mathf.Max(0f, balance.jobUnlockStarterHours);
            minAmount = Mathf.Max(1, balance.jobUnlockStarterMinAmount);
        }

        amount = Mathf.Max(minAmount, Mathf.CeilToInt(basePerHour * starterHours));
        return true;
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
