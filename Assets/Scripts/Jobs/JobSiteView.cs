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
    [SerializeField] private bool showMultiplierDebug = false;  // shows “×0.50” next to rate if true

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
        SetSlotVisible(slot1Group, cap >= 1);
        SetSlotVisible(slot2Group, cap >= 2);
        SetSlotVisible(slot3Group, cap >= 3);
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

    private void RenderSlot(JobSiteState st, int slotIndex, TextMeshProUGUI cdText, TextMeshProUGUI rateText, TextMeshProUGUI dbgText)
    {
        int cap = Mathf.Clamp(st.config.maxWorkers, 1, 3);
        if (slotIndex < 0 || slotIndex >= cap)
        {
            if (cdText) cdText.text = "";
            if (rateText) rateText.text = "";
            if (dbgText) dbgText.text = "";
            return;
        }

        // Current fatigue (0..1)
        float f = (st.slotFatigue01 != null && slotIndex < st.slotFatigue01.Length) ? Mathf.Clamp01(st.slotFatigue01[slotIndex]) : 0f;

        // Monster assigned?
        WorkerRef w = (st.workers != null && slotIndex < st.workers.Count) ? st.workers[slotIndex] : null;
        bool hasWorker = (w != null && w.def != null);

        // Cooldown remaining (if any)
        long now = SaveManager.NowUnix();
        long untilUnix = (st.slotCooldownUntilUnix != null && slotIndex < st.slotCooldownUntilUnix.Length) ? st.slotCooldownUntilUnix[slotIndex] : 0L;
        bool resting = (!hasWorker && untilUnix > now);
        long restRemain = resting ? (untilUnix - now) : 0L;

        // Compute effective fatigue rate per hour (base × multiplier)
        float ratePerHour = 0f;
        float usedMul = 1f;

        if (hasWorker)
        {
            ratePerHour = Mathf.Max(0f, w.def.fatigueRatePerHour);

            string wid = GetBestId(w);
            int lvl = GetOwnedLevelOr1(wid, w.def);

            // Reflection path (adapter)
            float mulA = 1f;
            try { mulA = Mathf.Max(0f, TitlesAdapter.GetJobFatigueMult(wid, w.def, lvl, st.config.jobType)); } catch { mulA = 1f; }

            // Direct path (manager) — safety net if reflection fails/binds wrong overload
            float mulB = 1f;
            try
            {
                if (TitleManager.I != null)
                    mulB = Mathf.Max(0f, TitleManager.I.GetJobFatigueMultiplier(wid, w.def, lvl, st.config.jobType));
            }
            catch { mulB = 1f; }

            // Prefer the *smaller* (more reduction) but never below 0
            usedMul = Mathf.Max(0f, Mathf.Min(mulA, mulB));

            ratePerHour *= usedMul;
        }

        // Text: fatigue rate per hour as a percentage (show one decimal to catch 1.5% cases)
        if (rateText)
        {
            if (hasWorker && ratePerHour > 0f) rateText.text = $"{ratePerHour * 100f:0.#}% / hr";
            else rateText.text = "";
        }

        // Optional debug: show the multiplier (helps verify titles are applied)
        if (dbgText)
        {
            dbgText.gameObject.SetActive(showMultiplierDebug && hasWorker);
            if (showMultiplierDebug && hasWorker) dbgText.text = $"×{usedMul:0.##}";
            else if (!showMultiplierDebug) dbgText.text = "";
        }

        // Text: "Resting in ..." (while working) OR "Resting ..." (cooldown)
        if (cdText)
        {
            if (hasWorker && ratePerHour > 0f)
            {
                float remain01 = Mathf.Clamp01(1f - f);
                float hrs = (ratePerHour > 0f) ? (remain01 / ratePerHour) : 0f;
                long secs = Math.Max(0L, (long)Math.Round(hrs * 3600f));
                cdText.text = $"Resting in {FormatHm(secs)}";
            }
            else if (resting)
            {
                cdText.text = $"Resting {FormatHm(restRemain)}";
            }
            else
            {
                cdText.text = "";
            }
        }
    }

    private static void SetSlotVisible(CanvasGroup cg, bool vis)
    {
        if (!cg) return;
        cg.alpha = vis ? 1f : 0f;
        cg.interactable = vis;
        cg.blocksRaycasts = vis;
        if (cg.gameObject.activeSelf != vis) cg.gameObject.SetActive(vis);
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
