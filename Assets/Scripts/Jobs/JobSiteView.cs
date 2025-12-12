using TMPro;
using UnityEngine;
using UnityEngine.UI;
using System;

public class JobSiteView : MonoBehaviour
{
    [Header("Identity")]
    [SerializeField] private JobType site;

    [Header("Visibility")]
    [SerializeField] private GameObject rootToToggle;

    [Header("Controls")]
    [SerializeField] private Toggle allowReliefToggle;

    [Header("Slot 1")]
    [SerializeField] private CanvasGroup slot1Group;
    [SerializeField] private TextMeshProUGUI slot1CDText;

    [Header("Slot 2")]
    [SerializeField] private CanvasGroup slot2Group;
    [SerializeField] private TextMeshProUGUI slot2CDText;

    [Header("Slot 3")]
    [SerializeField] private CanvasGroup slot3Group;
    [SerializeField] private TextMeshProUGUI slot3CDText;

    void Awake()
    {
        if (!rootToToggle) rootToToggle = gameObject;
    }

    void Start()
    {
        if (allowReliefToggle)
        {
            allowReliefToggle.onValueChanged.AddListener(v =>
            {
                var st = GetRuntimeState(site);
                if (st != null) st.allowClinicRelief = v;
            });
        }
        Refresh();
    }

    void OnEnable()
    {
        GameEvents.OnJobsChanged += Refresh;
        Refresh();
    }

    void OnDisable()
    {
        GameEvents.OnJobsChanged -= Refresh;
    }

    public void Refresh()
    {
        bool unlocked = SaveManager.Data != null &&
                        SaveManager.Data.unlockedJobSites != null &&
                        SaveManager.Data.unlockedJobSites.Contains(site);

        if (rootToToggle) rootToToggle.SetActive(unlocked);
        if (!unlocked) return;

        var st = GetRuntimeState(site);
        if (st == null || st.config == null)
        {
            SetSlotVisible(slot1Group, false);
            SetSlotVisible(slot2Group, false);
            SetSlotVisible(slot3Group, false);
            return;
        }

        if (allowReliefToggle) allowReliefToggle.SetIsOnWithoutNotify(st.allowClinicRelief);

        int cap = Mathf.Clamp(st.config.maxWorkers, 1, 3);
        SetSlotVisible(slot1Group, cap >= 1);
        SetSlotVisible(slot2Group, cap >= 2);
        SetSlotVisible(slot3Group, cap >= 3);

        RenderAndAlpha(st, 0, slot1Group, slot1CDText);
        RenderAndAlpha(st, 1, slot2Group, slot2CDText);
        RenderAndAlpha(st, 2, slot3Group, slot3CDText);
    }

    private JobSiteState GetRuntimeState(JobType job)
    {
        if (JobManager.I == null) return null;
        foreach (var st in JobManager.I.States)
        {
            if (st != null && st.config != null && st.config.jobType == job)
                return st;
        }
        return null;
    }

    private void RenderAndAlpha(JobSiteState st, int slotIndex, CanvasGroup group, TextMeshProUGUI cdText)
    {
        int cap = Mathf.Clamp(st.config.maxWorkers, 1, 3);
        if (slotIndex < 0 || slotIndex >= cap)
        {
            SetSlotVisible(group, false);
            if (cdText) cdText.text = "";
            return;
        }

        WorkerRef w = (st.workers != null && slotIndex < st.workers.Count)
            ? st.workers[slotIndex]
            : null;

        bool hasWorker = (w != null && w.def != null);

        if (!hasWorker)
        {
            SetGroupAlpha(group, 0f, false, false);
            if (cdText) cdText.text = "-";
            return;
        }

        SetGroupAlpha(group, 1f, true, true);
        RenderSlot(st, slotIndex, cdText);
    }

    private void RenderSlot(JobSiteState st, int slotIndex, TextMeshProUGUI cdText)
    {
        int cap = Mathf.Clamp(st.config.maxWorkers, 1, 3);
        if (slotIndex < 0 || slotIndex >= cap)
        {
            if (cdText) cdText.text = "";
            return;
        }

        float f = (st.slotFatigue01 != null && slotIndex < st.slotFatigue01.Length)
            ? Mathf.Clamp01(st.slotFatigue01[slotIndex])
            : 0f;

        WorkerRef w = (st.workers != null && slotIndex < st.workers.Count)
            ? st.workers[slotIndex]
            : null;

        // No rate text used anymore
        float ratePerHour = 0f;
        if (w != null && w.def != null)
        {
            ratePerHour = Mathf.Max(0f, w.def.fatigueRatePerHour);
            string wid = GetBestId(w);
            int lvl = GetOwnedLevelOr1(wid, w.def);
            float mul = 1f;

            try { mul = Mathf.Max(0f, TitlesAdapter.GetJobFatigueMult(wid, w.def, lvl, st.config.jobType)); }
            catch { }

            try
            {
                if (TitleManager.I != null)
                    mul = Mathf.Min(mul, Mathf.Max(0f, TitleManager.I.GetJobFatigueMultiplier(wid, w.def, lvl, st.config.jobType)));
            }
            catch { }

            ratePerHour *= mul;
        }

        if (cdText)
        {
            if (ratePerHour > 0f)
            {
                float remain01 = Mathf.Clamp01(1f - f);
                float hrs = remain01 / ratePerHour;
                long secs = Math.Max(0L, (long)Math.Round(hrs * 3600f));
                cdText.text = $"Resting in {FormatHm(secs)}";
            }
            else
            {
                cdText.text = "-";
            }
        }
    }

    private static void SetSlotVisible(CanvasGroup cg, bool vis)
    {
        if (!cg) return;
        cg.gameObject.SetActive(vis);
        if (!vis) return;

        cg.alpha = 1f;
        cg.interactable = true;
        cg.blocksRaycasts = true;
    }

    private static void SetGroupAlpha(CanvasGroup cg, float a, bool interactable, bool blocksRaycasts)
    {
        if (!cg) return;
        cg.alpha = Mathf.Clamp01(a);
        cg.interactable = interactable;
        cg.blocksRaycasts = blocksRaycasts;
    }

    private static string GetBestId(WorkerRef w)
    {
        if (w == null) return null;
        if (!string.IsNullOrEmpty(w.monsterId)) return w.monsterId;
        return w.def ? w.def.id : null;
    }

    private static int GetOwnedLevelOr1(string ownedOrDefId, MonsterDataSO fallbackDef)
    {
        var owned = SaveManager.Data?.owned;
        if (owned != null)
        {
            foreach (var om in owned)
            {
                if (om != null && om.monsterId == ownedOrDefId)
                    return Mathf.Max(1, om.level);
            }
        }

        var team = SaveManager.Data?.team;
        if (team != null)
        {
            foreach (var tm in team)
            {
                if (tm != null && tm.monsterId == ownedOrDefId)
                    return Mathf.Max(1, tm.level);
            }
        }

        return 1;
    }

    private static string FormatHm(long seconds)
    {
        if (seconds <= 0) return "0m";
        long h = seconds / 3600;
        long m = (seconds % 3600) / 60;

        if (h > 0) return $"{h}h {m}m";
        return $"{Mathf.Max(1, (int)m)}m";
    }
}
