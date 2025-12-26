using UnityEngine;
using System.Collections;

public class JobSiteListReorderer : MonoBehaviour
{
    [SerializeField] private Transform jobSiteContainer; // the parent that holds all JobSiteView controllers

    void OnEnable()
    {
        GameEvents.OnJobsChanged += ReorderNow;
        StartCoroutine(ReorderNextFrame());
    }

    void OnDisable()
    {
        GameEvents.OnJobsChanged -= ReorderNow;
    }

    IEnumerator ReorderNextFrame()
    {
        yield return null;
        ReorderNow();
    }

    void ReorderNow()
    {
        if (jobSiteContainer == null || SaveManager.Data == null) return;

        // Move all unlocked to top, keep locked below.
        // Within each group, preserve current order.
        int insertAt = 0;

        // First pass: unlocked to top
        for (int i = 0; i < jobSiteContainer.childCount; i++)
        {
            var child = jobSiteContainer.GetChild(i);
            var view = child.GetComponent<JobSiteView>();
            if (view == null) continue;

            if (IsUnlocked(view))
            {
                child.SetSiblingIndex(insertAt);
                insertAt++;
            }
        }

        // Optional: force a Layout rebuild if you’re using VerticalLayoutGroup/ContentSizeFitter
        UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(jobSiteContainer as RectTransform);
    }

    bool IsUnlocked(JobSiteView view)
    {
        // You need a way to read the JobType from the view.
        // Best: add a tiny getter: public JobType Site => site;
        // If you do not want to change JobSiteView, see Option B below.
        return SaveManager.Data.unlockedJobSites.Contains(view.Site);
    }
}
