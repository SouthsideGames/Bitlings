using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class JobPanelUI : MonoBehaviour
{
    [System.Serializable]
    public class JobTile
    {
        public JobType job;
        public TextMeshProUGUI title;
        public TextMeshProUGUI stored;
        public TextMeshProUGUI rate;
        public Button collectBtn;

        [Header("Full State (optional)")]
        public GameObject fullBadge; // set in prefab if you want a "FULL" indicator

        [Header("Slots (size = maxWorkers)")]
        public JobSlotUI[] slots;
    }

    [SerializeField] private JobTile[] tiles;

    [Header("Visuals")]
    [SerializeField] private Sprite emptySlotSprite;
    [SerializeField] private Color emptySlotColor = new Color(1f, 1f, 1f, 0.35f);
    [SerializeField] private Color filledSlotColor = Color.white;

    void OnEnable()
    {
        // Pull in any offline accrual before first draw
        JobManager.I?.ProcessOfflineAllSites();

        Refresh();
        GameEvents.OnJobsChanged += Refresh;
        GameEvents.OnResourcesChanged += Refresh; // when currency/resources update after collect
    }

    void OnDisable()
    {
        GameEvents.OnJobsChanged -= Refresh;
        GameEvents.OnResourcesChanged -= Refresh;
    }

    public void Refresh()
    {
        if (JobManager.I == null || tiles == null) return;

        foreach (var t in tiles)
        {
            var s = JobManager.I.States.Find(x => x.config.jobType == t.job);
            if (s == null) continue;

            if (t.title)  t.title.text  = $"{t.job} ({CountWorkers(s)}/{s.config.maxWorkers})";

            int cap = JobManager.I.GetEffectiveStorageCap(s.config);
            int storedWhole = Mathf.FloorToInt(s.storedAmount);
            if (t.stored)
                t.stored.text = $"Stored: {storedWhole}/{cap}";

            if (t.rate)
                t.rate.text = $"{Mathf.FloorToInt(s.cachedRatePerHour)}/hr";

            // Collect button state
            if (t.collectBtn)
            {
                t.collectBtn.onClick.RemoveAllListeners();
                t.collectBtn.interactable = storedWhole > 0;
                t.collectBtn.onClick.AddListener(() =>
                {
                    int got = JobManager.I.Collect(t.job);
                    Refresh(); // re-pull stored/rate/counts after collecting
                });
            }

            // Full badge state (optional)
            if (t.fullBadge)
                t.fullBadge.SetActive(cap > 0 && storedWhole >= cap);

            // Slots
            int max = t.slots != null ? t.slots.Length : 0;
            for (int i = 0; i < max; i++)
            {
                var ui = t.slots[i];
                if (!ui) continue;

                ui.job = t.job;
                ui.slotIndex = i;

                WorkerRef worker = (i < s.workers.Count) ? s.workers[i] : null;
                if (worker != null && worker.def != null)
                {
                    var spr = worker.def.icon ? worker.def.icon : emptySlotSprite;
                    ui.SetWorker(spr, filledSlotColor);
                }
                else
                {
                    ui.SetEmpty(emptySlotSprite, emptySlotColor);
                }

                ui.WireToPicker();
            }
        }
    }

    private int CountWorkers(JobSiteState s)
    {
        int count = 0;
        for (int i = 0; i < s.workers.Count; i++)
            if (s.workers[i] != null && s.workers[i].def != null) count++;
        return count;
    }
}
