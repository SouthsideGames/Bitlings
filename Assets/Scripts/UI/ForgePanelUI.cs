using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

public class ForgePanelUI : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private RectTransform listRoot;
    [SerializeField] private GameObject jobItemPrefab;
    [SerializeField] private TextMeshProUGUI totalMaterialsText;
    [SerializeField] private Button confirmBtn;
    [SerializeField] private Button cancelBtn;

    [Header("Visual")]
    [SerializeField] private Color totalNormal = Color.white;
    [SerializeField] private Color totalWarn = new Color(1f, 0.3f, 0.3f);

    [Header("Behavior")]
    [SerializeField] private bool openOnStart = false;

    private JobManager _jobs;
    private readonly List<JobMaterialItemUI> _items = new();
    private readonly Dictionary<JobType, int> _pendingByJob = new();

    private int _baseMaterials;
    private int _tempMaterials;

    void Awake()
    {
        _jobs ??= FindFirstObjectByType<JobManager>();

        if (confirmBtn) { confirmBtn.onClick.RemoveAllListeners(); confirmBtn.onClick.AddListener(Confirm); }
        if (cancelBtn)  { cancelBtn.onClick.RemoveAllListeners();  cancelBtn.onClick.AddListener(Cancel); }
    }

    void Start()
    {
        if (openOnStart) Open();
    }

    void OnEnable()
    {
        Build();
        RefreshTotals();
    }

    void OnDisable() => Clear();

    // ---------- Public API ----------
    public void Open()
    {
        gameObject.SetActive(true);
        Build();
        RefreshTotals();
    }

    public void Close()
    {
        Clear();
        gameObject.SetActive(false);
    }

    // ---------- Internal ----------
    private void Clear()
    {
        if (listRoot)
        {
            for (int i = listRoot.childCount - 1; i >= 0; i--)
                Destroy(listRoot.GetChild(i).gameObject);
        }
        _items.Clear();
        _pendingByJob.Clear();
    }

    private void Build()
    {
        Clear();
        if (_jobs == null || listRoot == null || jobItemPrefab == null) return;

        _baseMaterials = ResourceBank.Get(ResourceType.Materials);
        _tempMaterials = _baseMaterials;

        var states = _jobs.States;
        int spawned = 0;

        foreach (var s in states)
        {
            if (s == null || s.config == null) continue;
            if (!_jobs.IsSiteUnlocked(s.config.jobType)) continue;

            _pendingByJob[s.config.jobType] = 0;

            var go = Instantiate(jobItemPrefab, listRoot);
            if (!go.TryGetComponent<JobMaterialItemUI>(out var ui))
            {
                Destroy(go);
                continue;
            }

            _items.Add(ui);
            spawned++;

            var icon = s.config.icon;
            var jobName = string.IsNullOrEmpty(s.config.displayName)
                ? s.config.jobType.ToString()
                : s.config.displayName;

            if (s.maxXPForLevel <= 0)
                s.maxXPForLevel = JobLeveling.MaxXpForLevel(s.config.jobType, s.level);

            ui.Setup(
                iconSprite: icon,
                jobDisplayName: jobName,
                level: s.level,
                maxLevel: JobLeveling.MaxLevel,
                currentXP: s.currentXP,
                maxXPForLevel: s.maxXPForLevel,
                canSpendOneMaterial: () => _tempMaterials > 0,
                onDeltaChanged: (delta) =>
                {
                    _pendingByJob[s.config.jobType] += delta;
                    _tempMaterials -= delta;
                },
                requestRefresh: RefreshTotals
            );
        }

        if (spawned == 0)
        {
            var go = new GameObject("EmptyState", typeof(RectTransform));
            go.transform.SetParent(listRoot, false);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = "No job sites unlocked yet.";
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.fontSize = 28;
            tmp.color = new Color(1f, 1f, 1f, 0.85f);
        }
    }

    private void RefreshTotals()
    {
        if (totalMaterialsText)
        {
            totalMaterialsText.text = $"Materials: {_tempMaterials}";
            totalMaterialsText.color = (_tempMaterials < 0) ? totalWarn : totalNormal;
        }

        if (confirmBtn) confirmBtn.interactable = _tempMaterials >= 0;

        for (int i = 0; i < _items.Count; i++)
            _items[i].RefreshVisuals();
    }

    private void Confirm()
    {
        if (_jobs == null) return;

        ResourceBank.BeginBatch();

        int totalSpend = 0;
        int totalRefund = 0;

        foreach (var s in _jobs.States)
        {
            if (s == null || s.config == null) continue;
            if (!_jobs.IsSiteUnlocked(s.config.jobType)) continue;

            int delta = _pendingByJob.TryGetValue(s.config.jobType, out var d) ? d : 0;
            if (delta == 0) continue;

            if (delta > 0) totalSpend += delta;
            else           totalRefund += -delta;

            s.currentXP = Mathf.Clamp(s.currentXP + delta, 0, s.maxXPForLevel);

            if (s.currentXP >= s.maxXPForLevel && s.level < JobLeveling.MaxLevel)
            {
                s.level++;
                s.currentXP = 0;
                s.maxXPForLevel = JobLeveling.MaxXpForLevel(s.config.jobType, s.level);
            }
        }

        if (totalSpend > 0)  ResourceBank.TrySpend(ResourceType.Materials, totalSpend);
        if (totalRefund > 0) ResourceBank.Add(ResourceType.Materials, totalRefund);

        ResourceBank.EndBatch();

        JobManager.I?.SaveProgressToSave();

        GameEvents.OnResourcesChanged?.Invoke();
        GameEvents.OnJobsChanged?.Invoke();

        Build();
        RefreshTotals();
    }

    private void Cancel()
    {
        Build();
        RefreshTotals();
    }
}
