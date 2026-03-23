// Assets/Scripts/UI/UpgradesPanelUI.cs
using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;

public class UpgradesPanelUI : MonoBehaviour
{
    public enum UpgradeSection
    {
        IdleBattle,
        AutoGrowth,
        DailySeeds,
        Directory,
        Jobs,
        Exchange
    }

    [Header("List")]
    [SerializeField] private Transform listRoot;
    [SerializeField] private GameObject rowPrefab;
    [SerializeField] private ScrollContentAutoSizer scrollContentAutoSizer;

    [Header("Catalog (Master)")]
    [SerializeField] private List<UpgradeCatalogEntry> catalog = new();

    [Header("Tab Buttons")]
    [SerializeField] private Button idleBattleButton;
    [SerializeField] private Button autoGrowthButton;
    [SerializeField] private Button dailySeedsButton;
    [SerializeField] private Button directoryButton;
    [SerializeField] private Button jobsButton;
    [SerializeField] private Button exchangeButton;

    [Header("Section Overrides")]
    [Tooltip("If provided, these entries will be used for that section instead of filtering from the Master Catalog.")]
    [SerializeField] private SectionOverride idleBattleOverride;
    [SerializeField] private SectionOverride autoGrowthOverride;
    [SerializeField] private SectionOverride dailySeedsOverride;
    [SerializeField] private SectionOverride directoryOverride;
    [SerializeField] private SectionOverride jobsOverride;
    [SerializeField] private SectionOverride exchangeOverride;

    [System.Serializable]
    public class SectionOverride
    {
        [Tooltip("Optional: Use a different row prefab for this section. If null, UpgradesPanelUI.rowPrefab is used.")]
        public GameObject overrideRowPrefab;

        [Tooltip("Optional: If non-empty, these entries are used directly for this section.")]
        public List<UpgradeCatalogEntry> entries = new();
    }

    private readonly List<UpgradeRowUI> _rows = new();
    private UpgradeSection _currentSection = UpgradeSection.IdleBattle;

    private bool _hooksAdded;
    private bool _isShuttingDown;
    private Coroutine _refreshCo;

    void OnEnable()
    {
        _isShuttingDown = false;
        AddButtonHooks();
        ResolveAutoSizer();

        ShowSection(_currentSection);
    }

    void OnDisable()
    {
        _isShuttingDown = true;
        RemoveButtonHooks();
        _rows.Clear();

        if (_refreshCo != null)
        {
            StopCoroutine(_refreshCo);
            _refreshCo = null;
        }
    }

    void OnDestroy()
    {
        _isShuttingDown = true;
        RemoveButtonHooks();
    }

    // ─────────────────────────────────────────────────────────────
    // Button Wiring (SAFE)
    // ─────────────────────────────────────────────────────────────

    private void AddButtonHooks()
    {
        if (_hooksAdded) return;

        if (idleBattleButton) idleBattleButton.onClick.AddListener(ShowIdleBattle);
        if (autoGrowthButton) autoGrowthButton.onClick.AddListener(ShowAutoGrowth);
        if (dailySeedsButton) dailySeedsButton.onClick.AddListener(ShowDailySeeds);
        if (directoryButton) directoryButton.onClick.AddListener(ShowDirectory);
        if (jobsButton) jobsButton.onClick.AddListener(ShowJobs);
        if (exchangeButton) exchangeButton.onClick.AddListener(ShowExchange);

        _hooksAdded = true;
    }

    private void RemoveButtonHooks()
    {
        if (!_hooksAdded) return;

        if (idleBattleButton) idleBattleButton.onClick.RemoveListener(ShowIdleBattle);
        if (autoGrowthButton) autoGrowthButton.onClick.RemoveListener(ShowAutoGrowth);
        if (dailySeedsButton) dailySeedsButton.onClick.RemoveListener(ShowDailySeeds);
        if (directoryButton) directoryButton.onClick.RemoveListener(ShowDirectory);
        if (jobsButton) jobsButton.onClick.RemoveListener(ShowJobs);
        if (exchangeButton) exchangeButton.onClick.RemoveListener(ShowExchange);

        _hooksAdded = false;
    }

    // Public methods for Unity OnClick hookups
    public void ShowIdleBattle() => ShowSection(UpgradeSection.IdleBattle);
    public void ShowAutoGrowth() => ShowSection(UpgradeSection.AutoGrowth);
    public void ShowDailySeeds() => ShowSection(UpgradeSection.DailySeeds);
    public void ShowDirectory() => ShowSection(UpgradeSection.Directory);
    public void ShowJobs() => ShowSection(UpgradeSection.Jobs);
    public void ShowExchange() => ShowSection(UpgradeSection.Exchange);

    // ─────────────────────────────────────────────────────────────
    // Section Switching
    // ─────────────────────────────────────────────────────────────

    private void ShowSection(UpgradeSection section)
    {
        if (_isShuttingDown) return;
        if (!this || !isActiveAndEnabled) return;

        _currentSection = section;

        var (entries, prefab) = GetSectionData(section);
        BuildRows(entries, prefab);
        RequestAutoSizerRefresh();

    }

    private void ResolveAutoSizer()
    {
        if (scrollContentAutoSizer != null)
            return;

        if (listRoot != null)
        {
            scrollContentAutoSizer = listRoot.GetComponentInParent<ScrollContentAutoSizer>();
            if (scrollContentAutoSizer != null)
                return;
        }

        scrollContentAutoSizer = GetComponentInParent<ScrollContentAutoSizer>();
    }

    private void RequestAutoSizerRefresh()
    {
        if (_isShuttingDown)
            return;

        ResolveAutoSizer();
        if (scrollContentAutoSizer == null)
            return;

        scrollContentAutoSizer.Refresh(force: true);

        if (_refreshCo != null)
            StopCoroutine(_refreshCo);

        _refreshCo = StartCoroutine(RefreshAutoSizerNextFrame());
    }

    private IEnumerator RefreshAutoSizerNextFrame()
    {
        yield return null;

        if (!_isShuttingDown && this && isActiveAndEnabled && scrollContentAutoSizer != null)
            scrollContentAutoSizer.Refresh(force: true);

        _refreshCo = null;
    }

    private (List<UpgradeCatalogEntry> entries, GameObject prefab) GetSectionData(UpgradeSection section)
    {
        SectionOverride ov = section switch
        {
            UpgradeSection.IdleBattle => idleBattleOverride,
            UpgradeSection.AutoGrowth => autoGrowthOverride,
            UpgradeSection.DailySeeds => dailySeedsOverride,
            UpgradeSection.Directory => directoryOverride,
            UpgradeSection.Jobs => jobsOverride,
            UpgradeSection.Exchange => exchangeOverride,
            _ => null
        };

        GameObject prefab = (ov != null && ov.overrideRowPrefab != null) ? ov.overrideRowPrefab : rowPrefab;

        if (ov != null && ov.entries != null && ov.entries.Count > 0)
            return (ov.entries, prefab);

        var filtered = FilterCatalogForSection(section, catalog);
        return (filtered, prefab);
    }

    private List<UpgradeCatalogEntry> FilterCatalogForSection(UpgradeSection section, List<UpgradeCatalogEntry> source)
    {
        var result = new List<UpgradeCatalogEntry>();
        if (source == null) return result;

        foreach (var entry in source)
        {
            if (entry == null || entry.featureId == FeatureId.None)
                continue;

            if (GetSectionForEntry(entry) == section)
                result.Add(entry);
        }

        return result;
    }

    // ─────────────────────────────────────────────────────────────
    // Section Classification (NO catalog edits required)
    // Primary: infoId prefix: upg.idle / upg.growth / upg.jobs / upg.seeds / upg.Directory
    // Fallback: FeatureId mapping
    // ─────────────────────────────────────────────────────────────

    private UpgradeSection GetSectionForEntry(UpgradeCatalogEntry entry)
    {
        if (entry == null) return UpgradeSection.IdleBattle;

        var fromInfo = GetSectionFromInfoId(entry.infoId);
        if (fromInfo.HasValue) return fromInfo.Value;

        return GetSectionFromFeatureId(entry.featureId);
    }

    private UpgradeSection? GetSectionFromInfoId(string infoId)
    {
        if (string.IsNullOrEmpty(infoId))
            return null;

        if (infoId.StartsWith("upg.idle"))   return UpgradeSection.IdleBattle;
        if (infoId.StartsWith("upg.growth")) return UpgradeSection.AutoGrowth;
        if (infoId.StartsWith("upg.jobs"))   return UpgradeSection.Jobs;
        if (infoId.StartsWith("upg.seeds"))  return UpgradeSection.DailySeeds;
        if (infoId.StartsWith("upg.directory"))  return UpgradeSection.Directory;
        if (infoId.StartsWith("upg.exchange")) return UpgradeSection.Exchange;

        return null;
    }

    private UpgradeSection GetSectionFromFeatureId(FeatureId featureId)
    {
        int v = (int)featureId;
        if (v >= 100) return UpgradeSection.Jobs;

        switch (featureId)
        {
            case FeatureId.Exchange_SurgeAlert:
            case FeatureId.Exchange_BearBullTokens:
            case FeatureId.Exchange_MonopolyBonus:
            case FeatureId.Exchange_DividendYield:
            case FeatureId.Exchange_MarketForecast:
            case FeatureId.Exchange_LicensedBroker_T1:
            case FeatureId.Exchange_LicensedBroker_T2:
            case FeatureId.Exchange_ShinyAppraiser:
                return UpgradeSection.Exchange;

            case FeatureId.IdleBattle_Basic:
            case FeatureId.IdleBattle_RewardBoost:
            case FeatureId.IdleBattle_OfflineCapture:
                return UpgradeSection.IdleBattle;

            case FeatureId.AutoGrowth_Basic:
            case FeatureId.AutoGrowth_UsePresets:
                return UpgradeSection.AutoGrowth;

            case FeatureId.Seeds_DailyBasic:
            case FeatureId.Seeds_CustomInput:
            case FeatureId.Seeds_RerollDailyOnce:
                return UpgradeSection.DailySeeds;

            case FeatureId.Directory_Favorites:
            case FeatureId.Directory_CaptureOnlyFilter:
                return UpgradeSection.Directory;

            case FeatureId.Recycle_Basic:
            default:
                return UpgradeSection.AutoGrowth;
        }
    }

    // ─────────────────────────────────────────────────────────────
    // Row Building
    // ─────────────────────────────────────────────────────────────

    void BuildRows(List<UpgradeCatalogEntry> entries, GameObject prefabToUse)
    {
        if (_isShuttingDown) return;
        if (listRoot == null || prefabToUse == null)
            return;

        // Clear old
        for (int i = listRoot.childCount - 1; i >= 0; i--)
            Destroy(listRoot.GetChild(i).gameObject);

        _rows.Clear();

        if (entries == null)
            return;

        // Build new
        foreach (var entry in entries)
        {
            if (entry == null || entry.featureId == FeatureId.None)
                continue;

            var go = Instantiate(prefabToUse, listRoot);
            var row = go.GetComponent<UpgradeRowUI>();
            if (row != null)
            {
                row.Init(entry);
                _rows.Add(row);
            }
        }
    }



}
