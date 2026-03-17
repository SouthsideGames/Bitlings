using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// ─────────────────────────────────────────────────────────────
// ExchangePanelUI — main Exchange menu with four tabs:
// Market, Portfolio, Requests, Trends
// ─────────────────────────────────────────────────────────────

public class ExchangePanelUI : MonoBehaviour
{
    public static ExchangePanelUI I;

    public enum ExchangeSection { Market, Portfolio, Requests, Trends }

    [Header("Tab Buttons")]
    [SerializeField] private Button marketTabButton;
    [SerializeField] private Button portfolioTabButton;
    [SerializeField] private Button requestsTabButton;
    [SerializeField] private Button trendsTabButton;

    [Header("Tab Content Roots")]
    [SerializeField] private GameObject marketContent;
    [SerializeField] private GameObject portfolioContent;
    [SerializeField] private GameObject requestsContent;
    [SerializeField] private GameObject trendsContent;

    [Header("Market Tab")]
    [SerializeField] private Transform marketListParent;
    [SerializeField] private GameObject marketRowPrefab;
    [SerializeField] private Button sortByValueButton;
    [SerializeField] private Button sortByTrendButton;
    [SerializeField] private Button sortByRarityButton;

    [Header("Portfolio Tab")]
    [SerializeField] private Transform portfolioListParent;
    [SerializeField] private GameObject portfolioRowPrefab;
    [SerializeField] private TextMeshProUGUI totalRosterValueLabel;
    [SerializeField] private TextMeshProUGUI lifetimeBrokeredLabel;

    [Header("Requests Tab")]
    [SerializeField] private Transform requestsListParent;
    [SerializeField] private GameObject requestRowPrefab;
    [SerializeField] private GameObject noRequestsLabel;

    [Header("Trends Tab")]
    [SerializeField] private Transform trendsListParent;
    [SerializeField] private GameObject trendRowPrefab;
    [SerializeField] private TextMeshProUGUI worldEventTickerLabel;

    [Header("Close")]
    [SerializeField] private Button closeButton;

    private ExchangeSection _currentSection = ExchangeSection.Market;

    private enum SortMode { Value, Trend, Rarity }
    private SortMode _sortMode = SortMode.Value;

    void Awake()
    {
        I = this;
    }

    void OnEnable()
    {
        // Wire tab buttons
        if (marketTabButton)    marketTabButton.onClick.AddListener(ShowMarket);
        if (portfolioTabButton) portfolioTabButton.onClick.AddListener(ShowPortfolio);
        if (requestsTabButton)  requestsTabButton.onClick.AddListener(ShowRequests);
        if (trendsTabButton)    trendsTabButton.onClick.AddListener(ShowTrends);

        if (sortByValueButton)  sortByValueButton.onClick.AddListener(() => { _sortMode = SortMode.Value;  RefreshMarket(); });
        if (sortByTrendButton)  sortByTrendButton.onClick.AddListener(() => { _sortMode = SortMode.Trend;  RefreshMarket(); });
        if (sortByRarityButton) sortByRarityButton.onClick.AddListener(() => { _sortMode = SortMode.Rarity; RefreshMarket(); });

        if (closeButton) closeButton.onClick.AddListener(Close);

        GameEvents.ExchangeValuesChanged += OnValuesChanged;

        ShowSection(_currentSection);
    }

    void OnDisable()
    {
        if (marketTabButton)    marketTabButton.onClick.RemoveAllListeners();
        if (portfolioTabButton) portfolioTabButton.onClick.RemoveAllListeners();
        if (requestsTabButton)  requestsTabButton.onClick.RemoveAllListeners();
        if (trendsTabButton)    trendsTabButton.onClick.RemoveAllListeners();
        if (sortByValueButton)  sortByValueButton.onClick.RemoveAllListeners();
        if (sortByTrendButton)  sortByTrendButton.onClick.RemoveAllListeners();
        if (sortByRarityButton) sortByRarityButton.onClick.RemoveAllListeners();
        if (closeButton) closeButton.onClick.RemoveAllListeners();

        GameEvents.ExchangeValuesChanged -= OnValuesChanged;
    }

    // ─────────── Public Section Switching ───────────

    public void ShowMarket()    => ShowSection(ExchangeSection.Market);
    public void ShowPortfolio() => ShowSection(ExchangeSection.Portfolio);
    public void ShowRequests()  => ShowSection(ExchangeSection.Requests);
    public void ShowTrends()    => ShowSection(ExchangeSection.Trends);

    public void ShowSection(ExchangeSection section)
    {
        _currentSection = section;

        // Always force-hide all sections first, then enable only the selected one.
        // This prevents stale content from staying visible if switching quickly or if
        // a tab has previously been activated.
        if (marketContent) marketContent.SetActive(false);
        if (portfolioContent) portfolioContent.SetActive(false);
        if (requestsContent) requestsContent.SetActive(false);
        if (trendsContent) trendsContent.SetActive(false);

        switch (section)
        {
            case ExchangeSection.Market:
                if (marketContent) marketContent.SetActive(true);
                break;
            case ExchangeSection.Portfolio:
                if (portfolioContent) portfolioContent.SetActive(true);
                break;
            case ExchangeSection.Requests:
                if (requestsContent) requestsContent.SetActive(true);
                break;
            case ExchangeSection.Trends:
                if (trendsContent) trendsContent.SetActive(true);
                break;
        }

        switch (section)
        {
            case ExchangeSection.Market:    RefreshMarket();    break;
            case ExchangeSection.Portfolio: RefreshPortfolio(); break;
            case ExchangeSection.Requests:  RefreshRequests();  break;
            case ExchangeSection.Trends:    RefreshTrends();    break;
        }
    }

    // ─────────── Market Tab ───────────

    private void RefreshMarket()
    {
        if (marketListParent == null || marketRowPrefab == null) return;
        ClearChildren(marketListParent);

        var data = SaveManager.Data;
        if (data == null) return;

        // Build list of discovered species with market data
        var allMonsters = MonsterCatalog.All;
        if (allMonsters == null) return;

        var entries = new List<(MonsterDataSO def, MarketSpeciesState state)>();
        for (int i = 0; i < allMonsters.Count; i++)
        {
            var def = allMonsters[i];
            if (def == null || def.rarity == Rarity.Boss || def.baseMarketValue <= 0) continue;

            // Only show discovered species
            if (data.discoveredMonsterIds == null || !data.discoveredMonsterIds.Contains(def.id)) continue;

            var state = ExchangeManager.I?.GetState(def.id);
            entries.Add((def, state));
        }

        // Sort
        switch (_sortMode)
        {
            case SortMode.Value:
                entries.Sort((a, b) =>
                {
                    int va = a.state?.currentValue ?? a.def.baseMarketValue;
                    int vb = b.state?.currentValue ?? b.def.baseMarketValue;
                    return vb.CompareTo(va);
                });
                break;
            case SortMode.Trend:
                entries.Sort((a, b) =>
                {
                    int ta = a.state != null ? (int)a.state.trend : 1;
                    int tb = b.state != null ? (int)b.state.trend : 1;
                    return tb.CompareTo(ta);
                });
                break;
            case SortMode.Rarity:
                entries.Sort((a, b) => ((int)b.def.rarity).CompareTo((int)a.def.rarity));
                break;
        }

        // Build rows
        int ownedCount;
        for (int i = 0; i < entries.Count; i++)
        {
            var (def, state) = entries[i];
            var go = Instantiate(marketRowPrefab, marketListParent);
            go.SetActive(true);

            var row = go.GetComponent<ExchangeMarketRowUI>();
            if (row != null)
            {
                ownedCount = CountOwned(def.id);
                row.Populate(def, state, ownedCount);
            }
        }
    }

    // ─────────── Portfolio Tab ───────────

    private void RefreshPortfolio()
    {
        if (portfolioListParent == null) return;
        ClearChildren(portfolioListParent);

        var data = SaveManager.Data;
        if (data?.owned == null) return;

        // Build unique species list from owned
        var seen = new HashSet<string>(StringComparer.Ordinal);
        int totalValue = 0;

        for (int i = 0; i < data.owned.Count; i++)
        {
            var o = data.owned[i];
            if (o == null || string.IsNullOrEmpty(o.monsterId)) continue;
            if (!seen.Add(o.monsterId)) continue;

            var def = MonsterCatalog.GetById(o.monsterId);
            if (def == null || def.baseMarketValue <= 0) continue;

            int value = ExchangeManager.I != null ? ExchangeManager.I.GetCurrentValue(def.id) : def.baseMarketValue;
            int payout = ExchangeManager.I != null ? ExchangeManager.I.GetBrokerPayout(def.id) : Mathf.RoundToInt(value * 0.85f);
            totalValue += value;

            if (portfolioRowPrefab != null)
            {
                var go = Instantiate(portfolioRowPrefab, portfolioListParent);
                go.SetActive(true);

                var row = go.GetComponent<ExchangeMarketRowUI>();
                if (row != null)
                {
                    var state = ExchangeManager.I?.GetState(def.id);
                    row.Populate(def, state, CountOwned(def.id), o.level, payout);
                }
            }
        }

        if (totalRosterValueLabel != null) totalRosterValueLabel.text = $"Total Roster Value: {totalValue} Credits";

        int lifetimeBrokered = ExchangeManager.I?.SaveData?.totalCreditsBrokered ?? 0;
        if (lifetimeBrokeredLabel != null) lifetimeBrokeredLabel.text = $"Lifetime Brokered: {lifetimeBrokered} Credits";
    }

    // ─────────── Requests Tab ───────────

    private void RefreshRequests()
    {
        if (requestsListParent == null) return;
        ClearChildren(requestsListParent);

        if (ExchangeRequestManager.I == null)
        {
            if (noRequestsLabel != null) noRequestsLabel.gameObject.SetActive(true);
            return;
        }

        var active = ExchangeRequestManager.I.ActiveRequests;
        if (active == null || active.Count == 0)
        {
            if (noRequestsLabel != null)
            {
                noRequestsLabel.SetActive(true);
                var noRequestsText = noRequestsLabel.GetComponent<TextMeshProUGUI>()
                    ?? noRequestsLabel.GetComponentInChildren<TextMeshProUGUI>(true);
                if (noRequestsText != null)
                    noRequestsText.text = "No active requests. Check back tomorrow!";
            }
            return;
        }

        int shownRequests = 0;

        for (int i = 0; i < active.Count; i++)
        {
            var req = active[i];
            if (req == null) continue;
            if (req.fulfilled) continue;

            if (requestRowPrefab != null)
            {
                var go = Instantiate(requestRowPrefab, requestsListParent);
                go.SetActive(true);

                var row = go.GetComponent<ExchangeRequestRowUI>();
                if (row != null) row.Populate(req);
                shownRequests++;
            }
        }

        if (noRequestsLabel != null)
        {
            bool hasVisibleRequests = shownRequests > 0;
            noRequestsLabel.SetActive(!hasVisibleRequests);

            if (!hasVisibleRequests)
            {
                var noRequestsText = noRequestsLabel.GetComponent<TextMeshProUGUI>()
                    ?? noRequestsLabel.GetComponentInChildren<TextMeshProUGUI>(true);
                if (noRequestsText != null)
                    noRequestsText.text = "No active requests. Check back tomorrow!";
            }
        }
    }

    // ─────────── Trends Tab ───────────

    private void RefreshTrends()
    {
        if (trendsListParent == null) return;
        ClearChildren(trendsListParent);

        if (ExchangeManager.I == null) return;

        var allStates = ExchangeManager.I.AllStates;
        if (allStates == null) return;

        // Build sorted lists: top risers and fallers
        var risers = new List<MarketSpeciesState>();
        var fallers = new List<MarketSpeciesState>();

        foreach (var kv in allStates)
        {
            var s = kv.Value;
            int delta = s.currentValue - s.previousValue;
            if (delta > 0) risers.Add(s);
            else if (delta < 0) fallers.Add(s);
        }

        risers.Sort((a, b) => (b.currentValue - b.previousValue).CompareTo(a.currentValue - a.previousValue));
        fallers.Sort((a, b) => (a.currentValue - a.previousValue).CompareTo(b.currentValue - b.previousValue));

        int showCount = 5;

        // Risers
        for (int i = 0; i < Mathf.Min(showCount, risers.Count); i++)
            SpawnTrendRow(risers[i], true);

        // Fallers
        for (int i = 0; i < Mathf.Min(showCount, fallers.Count); i++)
            SpawnTrendRow(fallers[i], false);

        // World event ticker
        if (worldEventTickerLabel != null)
        {
            if (WorldEventSystem.I != null && WorldEventSystem.I.ActiveEvents.Count > 0)
            {
                var sb = new System.Text.StringBuilder();
                for (int i = 0; i < WorldEventSystem.I.ActiveEvents.Count; i++)
                {
                    var evt = WorldEventSystem.I.ActiveEvents[i];
                    if (evt == null) continue;
                    if (sb.Length > 0) sb.Append(" | ");
                    sb.Append(evt.displayName);
                    if (!string.IsNullOrEmpty(evt.tickerMessage))
                    {
                        sb.Append(": ");
                        sb.Append(evt.tickerMessage);
                    }
                }
                worldEventTickerLabel.text = sb.ToString();
                worldEventTickerLabel.gameObject.SetActive(true);
            }
            else
            {
                worldEventTickerLabel.text = "No active world events affecting the exchange.";
                worldEventTickerLabel.gameObject.SetActive(true);
            }
        }
    }

    private void SpawnTrendRow(MarketSpeciesState state, bool isRiser)
    {
        if (trendRowPrefab == null || trendsListParent == null) return;

        var def = MonsterCatalog.GetById(state.speciesId);
        if (def == null) return;

        var go = Instantiate(trendRowPrefab, trendsListParent);
        go.SetActive(true);

        // Populate via row component or inline
        var row = go.GetComponent<ExchangeMarketRowUI>();
        if (row != null)
        {
            row.Populate(def, state, CountOwned(def.id));
        }
        else
        {
            var label = go.GetComponentInChildren<TextMeshProUGUI>();
            if (label != null)
            {
                int delta = state.currentValue - state.previousValue;
                string arrow = isRiser ? "▲" : "▼";
                label.text = $"{arrow} {def.displayName}: {state.currentValue} Credits ({(delta >= 0 ? "+" : "")}{delta})";
            }
        }
    }

    // ─────────── Helpers ───────────

    private int CountOwned(string speciesId)
    {
        var data = SaveManager.Data;
        if (data?.owned == null) return 0;
        int count = 0;
        for (int i = 0; i < data.owned.Count; i++)
        {
            if (data.owned[i] != null && data.owned[i].monsterId == speciesId)
                count++;
        }
        return count;
    }

    private void ClearChildren(Transform parent)
    {
        for (int i = parent.childCount - 1; i >= 0; i--)
            Destroy(parent.GetChild(i).gameObject);
    }

    private void Close()
    {
        if (UIManager.I != null) UIManager.I.Hide(PanelId.Exchange);
    }

    private void OnValuesChanged()
    {
        ShowSection(_currentSection);
    }
}
