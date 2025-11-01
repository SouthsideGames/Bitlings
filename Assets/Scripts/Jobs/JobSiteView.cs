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
    [SerializeField] private TextMeshProUGUI slot1CDText;      // "Resting in 1h 20m" OR "Resting 45m"
    [SerializeField] private TextMeshProUGUI slot1RateText;    // "1.5% / hr"

    [Header("Slot 2")]
    [SerializeField] private CanvasGroup slot2Group;
    [SerializeField] private TextMeshProUGUI slot2CDText;
    [SerializeField] private TextMeshProUGUI slot2RateText;

    [Header("Slot 3")]
    [SerializeField] private CanvasGroup slot3Group;
    [SerializeField] private TextMeshProUGUI slot3CDText;
    [SerializeField] private TextMeshProUGUI slot3RateText;

    [Header("Debug")]
    [SerializeField] private bool showMultiplierDebug = false;  // kept for future use

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
        bool unlocked = SaveManager.Data != null
                     && SaveManager.Data.unlockedJobSites != null
                     && SaveManager.Data.unlockedJobSites.Contains(site);
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

        // Show/hide by site cap
        SetSlotVisible(slot1Group, cap >= 1);
        SetSlotVisible(slot2Group, cap >= 2);
        SetSlotVisible(slot3Group, cap >= 3);

        // Render each slot and set alpha to 0 if no worker assigned
        RenderAndAlpha(st, 0, slot1Group, slot1CDText, slot1RateText);
        RenderAndAlpha(st, 1, slot2Group, slot2CDText, slot2RateText);
        RenderAndAlpha(st, 2, slot3Group, slot3CDText, slot3RateText);
    }

    private JobSiteState GetRuntimeState(JobType job)
    {
        if (JobManager.I == null) return null;
        for (int i = 0; i < JobManager.I.States.Count; i++)
        {
            var st = JobManager.I.States[i];
            if (st != null && st.config != null && st.config.jobType == job) return st;
        }
        return null;
    }

    private void RenderAndAlpha(JobSiteState st, int slotIndex, CanvasGroup group, TextMeshProUGUI cdText, TextMeshProUGUI rateText)
    {
        int cap = Mathf.Clamp(st.config.maxWorkers, 1, 3);
        if (slotIndex < 0 || slotIndex >= cap)
        {
            // beyond cap: hide slot entirely
            SetSlotVisible(group, false);
            if (cdText) cdText.text = "";
            if (rateText) rateText.text = "";
            return;
        }

        // Worker present?
        WorkerRef w = (st.workers != null && slotIndex < st.workers.Count) ? st.workers[slotIndex] : null;
        bool hasWorker = (w != null && w.def != null);

        if (!hasWorker)
        {
            // Empty slot: alpha 0 and show "-" for texts
            SetGroupAlpha(group, 0f, interactable: false, blocksRaycasts: false);
            if (cdText) cdText.text = "-";
            if (rateText) rateText.text = "-";
            return;
        }

        // Has worker: alpha 1 and render normal labels
        SetGroupAlpha(group, 1f, interactable: true, blocksRaycasts: true);
        RenderSlot(st, slotIndex, cdText, rateText);
    }

    private void RenderSlot(JobSiteState st, int slotIndex, TextMeshProUGUI cdText, TextMeshProUGUI rateText)
    {
        // Guard
        int cap = Mathf.Clamp(st.config.maxWorkers, 1, 3);
        if (slotIndex < 0 || slotIndex >= cap)
        {
            if (cdText) cdText.text = "";
            if (rateText) rateText.text = "";
            return;
        }

        // Current fatigue (0..1)
        float f = (st.slotFatigue01 != null && slotIndex < st.slotFatigue01.Length) ? Mathf.Clamp01(st.slotFatigue01[slotIndex]) : 0f;

        // Worker (we already checked presence in caller)
        WorkerRef w = (st.workers != null && slotIndex < st.workers.Count) ? st.workers[slotIndex] : null;

        // Cooldown remaining (if any) — only shown while working
        long now = SaveManager.NowUnix();
        long untilUnix = (st.slotCooldownUntilUnix != null && slotIndex < st.slotCooldownUntilUnix.Length) ? st.slotCooldownUntilUnix[slotIndex] : 0L;

        // Effective fatigue rate per hour (base × multiplier)
        float ratePerHour = 0f;
        if (w != null && w.def != null)
        {
            ratePerHour = Mathf.Max(0f, w.def.fatigueRatePerHour);

            string wid = GetBestId(w);
            int lvl = GetOwnedLevelOr1(wid, w.def);

            float mul = 1f;
            try { mul = Mathf.Max(0f, TitlesAdapter.GetJobFatigueMult(wid, w.def, lvl, st.config.jobType)); }
            catch { mul = 1f; }

            // Safety-net through manager (if desired)
            try
            {
                if (TitleManager.I != null)
                    mul = Mathf.Min(mul, Mathf.Max(0f, TitleManager.I.GetJobFatigueMultiplier(wid, w.def, lvl, st.config.jobType)));
            }
            catch { }

            ratePerHour *= mul;
        }

        // Rate text (one decimal place to display 1.5% cases)
        if (rateText) rateText.text = (ratePerHour > 0f) ? $"{ratePerHour * 100f:0.#}% / hr" : "-";

        // "Resting in ..." while working (we only show this for assigned workers)
        if (cdText)
        {
            if (ratePerHour > 0f)
            {
                float remain01 = Mathf.Clamp01(1f - f);
                float hrs = (ratePerHour > 0f) ? (remain01 / ratePerHour) : 0f;
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
        // When visible by cap, default alpha 1 (specific slot alpha handled elsewhere)
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
        if (string.IsNullOrEmpty(ownedOrDefId)) return 1;

        var owned = SaveManager.Data?.owned;
        if (owned != null)
        {
            for (int i = 0; i < owned.Count; i++)
            {
                var om = owned[i];
                if (om != null && om.monsterId == ownedOrDefId)
                    return Mathf.Max(1, om.level);
            }
        }

        var team = SaveManager.Data?.team;
        if (team != null)
        {
            for (int i = 0; i < team.Count; i++)
            {
                var e = team[i];
                if (e != null && (e.monsterId == ownedOrDefId))
                    return Mathf.Max(1, e.level);
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
