using System;
using System.Collections;
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

    [Header("Market Tab — Grid")]
    [SerializeField] private Transform marketGridParent;
    [SerializeField] private GameObject marketCellPrefab;
    [SerializeField] private Button sortByValueButton;
    [SerializeField] private Button sortByTrendButton;
    [SerializeField] private Button sortByRarityButton;

    [Header("Portfolio Tab")]
    [SerializeField] private Transform portfolioListParent;
    [SerializeField] private GameObject portfolioRowPrefab;
    [SerializeField] private TextMeshProUGUI totalRosterValueLabel;
    [SerializeField] private TextMeshProUGUI lifetimeBrokeredLabel;
    [SerializeField] private GameObject dividendYieldRoot;
    [SerializeField] private TextMeshProUGUI dividendYieldLabel;

    [Header("Requests Tab")]
    [SerializeField] private Transform requestsListParent;
    [SerializeField] private GameObject requestRowPrefab;
    [SerializeField] private GameObject noRequestsLabel;

    [Header("Trends Tab")]
    [SerializeField] private Transform trendsListParent;
    [SerializeField] private GameObject trendRowPrefab;
    [SerializeField] private TextMeshProUGUI worldEventTickerLabel;
    [SerializeField] private float tickerFadeDuration = 0.5f;
    [SerializeField] private float tickerDisplayDuration = 3f;

    [Header("Close")]
    [SerializeField] private Button closeButton;

    [Header("Fulfillment Confirmation")]
    [SerializeField] private GameObject confirmOverlayRoot;
    [SerializeField] private TextMeshProUGUI confirmMessageLabel;
    [SerializeField] private Button confirmYesButton;
    [SerializeField] private Button confirmNoButton;

    [Header("Token Display")]
    [SerializeField] private GameObject tokenDisplayRoot;
    [SerializeField] private TextMeshProUGUI bullTokenLabel;
    [SerializeField] private TextMeshProUGUI bearTokenLabel;

    private ExchangeSection _currentSection = ExchangeSection.Market;

    private enum SortMode { Value, Trend, Rarity }
    private SortMode _sortMode = SortMode.Value;

    // Ticker fade-cycle state
    private List<string> _tickerLines = new List<string>();
    private int _tickerIndex;
    private Coroutine _tickerCoroutine;

    // Confirmation state
    private ActiveRequest _pendingRequest;
    private OwnedMonsterData _pendingOwned;
    private CanvasGroup _mainContentCg;

    // Tutorial keys — must match TutorialOverlayPanel.tutorialKey on prefab
    private const string TutMarket    = "tut_exchange_market_v1";
    private const string TutPortfolio = "tut_exchange_portfolio_v1";
    private const string TutRequests  = "tut_exchange_requests_v1";
    private const string TutTrends    = "tut_exchange_trends_v1";

    void Awake()
    {
        I = this;
        SetConfirmVisible(false);
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

        if (confirmYesButton) { confirmYesButton.onClick.RemoveAllListeners(); confirmYesButton.onClick.AddListener(OnConfirmYes); }
        if (confirmNoButton)  { confirmNoButton.onClick.RemoveAllListeners();  confirmNoButton.onClick.AddListener(OnConfirmNo);  }
        SetConfirmVisible(false);

        GameEvents.ExchangeValuesChanged += OnValuesChanged;
        GameEvents.ExchangeMarketReset += OnValuesChanged;
        GameEvents.OnResourcesChanged += RefreshTokenDisplay;

        if (FeatureUnlockManager.I != null)
            FeatureUnlockManager.I.OnFeatureUnlocked += HandleFeatureUnlocked;

        RefreshTokenDisplay();
        RefreshDividendDisplay();
        ShowSection(_currentSection);
    }

    void OnDisable()
    {
        StopTickerCycle();

        if (marketTabButton)    marketTabButton.onClick.RemoveAllListeners();
        if (portfolioTabButton) portfolioTabButton.onClick.RemoveAllListeners();
        if (requestsTabButton)  requestsTabButton.onClick.RemoveAllListeners();
        if (trendsTabButton)    trendsTabButton.onClick.RemoveAllListeners();
        if (sortByValueButton)  sortByValueButton.onClick.RemoveAllListeners();
        if (sortByTrendButton)  sortByTrendButton.onClick.RemoveAllListeners();
        if (sortByRarityButton) sortByRarityButton.onClick.RemoveAllListeners();
        if (closeButton) closeButton.onClick.RemoveAllListeners();
        if (confirmYesButton) confirmYesButton.onClick.RemoveAllListeners();
        if (confirmNoButton)  confirmNoButton.onClick.RemoveAllListeners();

        GameEvents.ExchangeValuesChanged -= OnValuesChanged;
        GameEvents.ExchangeMarketReset -= OnValuesChanged;
        GameEvents.OnResourcesChanged -= RefreshTokenDisplay;

        if (FeatureUnlockManager.I != null)
            FeatureUnlockManager.I.OnFeatureUnlocked -= HandleFeatureUnlocked;
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

        RefreshTicker();

        // First-open tutorials per tab
        switch (section)
        {
            case ExchangeSection.Market:    TutorialOverlayPanel.RequestOpen(TutMarket);    break;
            case ExchangeSection.Portfolio: TutorialOverlayPanel.RequestOpen(TutPortfolio); break;
            case ExchangeSection.Requests:  TutorialOverlayPanel.RequestOpen(TutRequests);  break;
            case ExchangeSection.Trends:    TutorialOverlayPanel.RequestOpen(TutTrends);    break;
        }
    }

    // ─────────── Market Tab (Grid) ───────────

    private void RefreshMarket()
    {
        if (marketGridParent == null || marketCellPrefab == null) return;

        // Show ALL species in the catalog (base library + unlocked packs)
        var allMonsters = MonsterCatalog.All;
        if (allMonsters == null) return;

        var entries = new List<(MonsterDataSO def, MarketSpeciesState state)>();
        for (int i = 0; i < allMonsters.Count; i++)
        {
            var def = allMonsters[i];
            if (def == null || def.rarity == Rarity.Boss || def.baseMarketValue <= 0) continue;

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

        // Build grid cells (pooled)
        for (int i = 0; i < entries.Count; i++)
        {
            var (def, state) = entries[i];
            var go = GetOrCreateChild(marketGridParent, marketCellPrefab, i);

            var cell = go.GetComponent<ExchangeMarketCellUI>();
            if (cell != null)
            {
                cell.Populate(def, state);
                var capturedDef = def;
                cell.SetDetailCallback(() => OpenSpeciesDetail(capturedDef));
            }
        }

        DeactivateUnusedChildren(marketGridParent, entries.Count);
    }

    private void OpenSpeciesDetail(MonsterDataSO def)
    {
        if (def == null) return;
        ExchangeSpeciesDetailPanelUI.PendingSpecies = def;
        if (UIManager.I != null)
            UIManager.I.Show(PanelId.ExchangeSpeciesDetail);
    }

    // ─────────── Portfolio Tab ───────────

    private void RefreshPortfolio()
    {
        if (portfolioListParent == null) return;

        var data = SaveManager.Data;

        // Build unique species list from owned
        var seen = new HashSet<string>(StringComparer.Ordinal);
        int totalValue = 0;
        int rowIndex = 0;

        if (data?.owned != null)
        {
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
                    var go = GetOrCreateChild(portfolioListParent, portfolioRowPrefab, rowIndex);
                    rowIndex++;

                    var row = go.GetComponent<ExchangeTrendRowUI>();
                    if (row != null)
                    {
                        var state = ExchangeManager.I?.GetState(def.id);
                        row.Populate(def, state, CountOwned(def.id), o.level, payout);
                    }
                }
            }
        }

        DeactivateUnusedChildren(portfolioListParent, rowIndex);

        if (totalRosterValueLabel != null) totalRosterValueLabel.text = $"Total Roster Value: {totalValue} Credits";

        int lifetimeBrokered = ExchangeManager.I?.SaveData?.totalCreditsBrokered ?? 0;
        if (lifetimeBrokeredLabel != null) lifetimeBrokeredLabel.text = $"Lifetime Brokered: {lifetimeBrokered} Credits";

        RefreshDividendDisplay();
    }

    // ─────────── Requests Tab ───────────

    private void RefreshRequests()
    {
        if (requestsListParent == null) return;

        if (ExchangeRequestManager.I == null)
        {
            DeactivateUnusedChildren(requestsListParent, 0);
            if (noRequestsLabel != null) noRequestsLabel.gameObject.SetActive(true);
            return;
        }

        var active = ExchangeRequestManager.I.ActiveRequests;
        if (active == null || active.Count == 0)
        {
            DeactivateUnusedChildren(requestsListParent, 0);
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

        int rowIndex = 0;

        for (int i = 0; i < active.Count; i++)
        {
            var req = active[i];
            if (req == null) continue;
            if (req.fulfilled) continue;

            if (requestRowPrefab != null)
            {
                var go = GetOrCreateChild(requestsListParent, requestRowPrefab, rowIndex);
                rowIndex++;

                var row = go.GetComponent<ExchangeRequestRowUI>();
                if (row != null) row.Populate(req);
            }
        }

        DeactivateUnusedChildren(requestsListParent, rowIndex);

        if (noRequestsLabel != null)
        {
            bool hasVisibleRequests = rowIndex > 0;
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

        if (ExchangeManager.I == null)
        {
            DeactivateUnusedChildren(trendsListParent, 0);
            return;
        }

        var allStates = ExchangeManager.I.AllStates;
        if (allStates == null)
        {
            DeactivateUnusedChildren(trendsListParent, 0);
            return;
        }

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
        int rowIndex = 0;

        // Risers
        for (int i = 0; i < Mathf.Min(showCount, risers.Count); i++)
        {
            SpawnTrendRow(risers[i], true, rowIndex);
            rowIndex++;
        }

        // Fallers
        for (int i = 0; i < Mathf.Min(showCount, fallers.Count); i++)
        {
            SpawnTrendRow(fallers[i], false, rowIndex);
            rowIndex++;
        }

        DeactivateUnusedChildren(trendsListParent, rowIndex);
    }

    // ─────────── Ticker (fade cycle) ───────────

    private void RefreshTicker()
    {
        if (worldEventTickerLabel == null) return;

        var newLines = new List<string>();

        var allStates = ExchangeManager.I?.AllStates;
        if (allStates != null && allStates.Count > 0)
        {
            foreach (var kv in allStates)
            {
                var s = kv.Value;
                if (s == null) continue;
                var def = MonsterCatalog.GetById(s.speciesId);
                if (def == null) continue;

                int delta = s.currentValue - s.previousValue;
                if (delta == 0) continue;

                string color = delta > 0 ? "#00CC00" : "#FF3333";
                string arrow = delta > 0 ? "▲" : "▼";
                newLines.Add($"{def.displayName} <color={color}>{arrow} {Mathf.Abs(delta)}</color>    {s.currentValue} Credits");
            }
        }

        if (newLines.Count == 0)
            newLines.Add("Markets are steady \u2014 no movement.");

        bool wasRunning = _tickerCoroutine != null;

        _tickerLines = newLines;

        if (_tickerIndex >= _tickerLines.Count)
            _tickerIndex = 0;

        worldEventTickerLabel.gameObject.SetActive(true);

        if (!wasRunning)
        {
            _tickerCoroutine = StartCoroutine(TickerFadeCycle());
        }
    }

    private IEnumerator TickerFadeCycle()
    {
        while (true)
        {
            // Set text and fade in
            worldEventTickerLabel.text = _tickerLines[_tickerIndex];
            yield return FadeTicker(0f, 1f, tickerFadeDuration);

            // Hold visible
            yield return new WaitForSeconds(tickerDisplayDuration);

            // Fade out
            yield return FadeTicker(1f, 0f, tickerFadeDuration);

            // Advance to next line (loop)
            _tickerIndex = (_tickerIndex + 1) % _tickerLines.Count;
        }
    }

    private IEnumerator FadeTicker(float from, float to, float duration)
    {
        float elapsed = 0f;
        Color c = worldEventTickerLabel.color;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            c.a = Mathf.Lerp(from, to, t);
            worldEventTickerLabel.color = c;
            yield return null;
        }
        c.a = to;
        worldEventTickerLabel.color = c;
    }

    private void StopTickerCycle()
    {
        if (_tickerCoroutine != null)
        {
            StopCoroutine(_tickerCoroutine);
            _tickerCoroutine = null;
        }
    }

    private void SpawnTrendRow(MarketSpeciesState state, bool isRiser, int index)
    {
        if (trendRowPrefab == null || trendsListParent == null) return;

        var def = MonsterCatalog.GetById(state.speciesId);
        if (def == null) return;

        var go = GetOrCreateChild(trendsListParent, trendRowPrefab, index);

        // Populate via row component or inline
        var row = go.GetComponent<ExchangeTrendRowUI>();
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

    /// <summary>
    /// Return a pooled child or instantiate a new one under <paramref name="parent"/>.
    /// Call <see cref="DeactivateUnusedChildren"/> after populating all rows.
    /// </summary>
    private GameObject GetOrCreateChild(Transform parent, GameObject prefab, int index)
    {
        if (index < parent.childCount)
        {
            var existing = parent.GetChild(index).gameObject;
            existing.SetActive(true);
            return existing;
        }
        var go = Instantiate(prefab, parent);
        go.SetActive(true);
        return go;
    }

    private void DeactivateUnusedChildren(Transform parent, int usedCount)
    {
        for (int i = usedCount; i < parent.childCount; i++)
            parent.GetChild(i).gameObject.SetActive(false);
    }

    private void Close()
    {
        SetConfirmVisible(false);
        if (UIManager.I != null) UIManager.I.Hide(PanelId.Exchange);
    }

    // ─────────── Fulfillment Confirmation ───────────

    /// <summary>
    /// Called by ExchangeRequestRowUI to show the confirmation overlay
    /// before permanently consuming the monster.
    /// </summary>
    public void ShowFulfillConfirmation(ActiveRequest request, OwnedMonsterData owned)
    {
        if (request == null || owned == null) return;

        // Reject if the confirmation overlay is already visible to prevent
        // overwriting the pending request/owned with a different pair.
        if (confirmOverlayRoot != null && confirmOverlayRoot.activeSelf) return;

        _pendingRequest = request;
        _pendingOwned = owned;

        // Build warning message
        var def = MonsterCatalog.GetById(owned.monsterId);
        string monsterName = def != null ? def.displayName : owned.monsterId;
        string bonus = request.bonusResourceAmount > 0
            ? $" + {request.bonusResourceAmount} {request.bonusResourceType}"
            : "";

        if (confirmMessageLabel != null)
            confirmMessageLabel.text =
                $"Are you sure you want to give away <b>{monsterName}</b>?\n\n" +
                $"This Bitling will be permanently removed from your roster, team, and any active jobs.\n\n" +
                $"Reward: <b>+{request.creditReward} Credits{bonus}</b>";

        SetConfirmVisible(true);
    }

    private void OnConfirmYes()
    {
        if (_pendingRequest == null || _pendingOwned == null || ExchangeRequestManager.I == null)
        {
            SetConfirmVisible(false);
            return;
        }

        if (confirmYesButton != null) confirmYesButton.interactable = false;

        string speciesId = _pendingOwned.monsterId;
        int reward = ExchangeRequestManager.I.TryFulfillRequestByConsumingOwned(
            _pendingRequest.requestId, _pendingOwned);

        SetConfirmVisible(false);

        if (reward > 0)
        {
            PendingDuplicateCapture.Clear();
            var def = MonsterCatalog.GetById(speciesId);
            string name = def != null ? def.displayName : speciesId;
            GameEvents.RaiseToast($"{name} placed! +{reward} Credits");
            ShowRequests();
        }
    }

    private void OnConfirmNo()
    {
        _pendingRequest = null;
        _pendingOwned = null;
        SetConfirmVisible(false);
    }

    private void SetConfirmVisible(bool on)
    {
        if (confirmOverlayRoot != null)
            confirmOverlayRoot.SetActive(on);

        // Block interaction with the rest of the panel (tabs, close, etc.)
        // while the confirmation is visible, so the player can't navigate away.
        if (_mainContentCg == null)
        {
            // Cache a CanvasGroup on the first content-bearing child or self.
            // We use the panel's own root but exclude the overlay itself.
            var panelRoot = gameObject;
            _mainContentCg = panelRoot.GetComponent<CanvasGroup>();
            if (_mainContentCg == null)
                _mainContentCg = panelRoot.AddComponent<CanvasGroup>();
        }

        // When overlay is on, disable interaction on the whole Exchange panel;
        // the overlay sits on top and has its own interactable state.
        _mainContentCg.interactable = !on;

        // Re-enable the overlay's own interactability so its buttons work.
        if (on && confirmOverlayRoot != null)
        {
            var overlayCg = confirmOverlayRoot.GetComponent<CanvasGroup>();
            if (overlayCg == null) overlayCg = confirmOverlayRoot.AddComponent<CanvasGroup>();
            overlayCg.interactable = true;
            overlayCg.blocksRaycasts = true;
            overlayCg.ignoreParentGroups = true;
        }

        if (!on)
        {
            _pendingRequest = null;
            _pendingOwned = null;
        }
    }

    private void OnValuesChanged()
    {
        ShowSection(_currentSection);
    }

    // ─────────── Token Display ───────────

    private void RefreshTokenDisplay()
    {
        if (tokenDisplayRoot == null) return;

        bool unlocked = FeatureUnlockManager.I != null &&
                        FeatureUnlockManager.I.IsUnlocked(FeatureId.Exchange_BearBullTokens);
        tokenDisplayRoot.SetActive(unlocked);

        if (!unlocked) return;

        if (bullTokenLabel != null)
            bullTokenLabel.text = ResourceBank.Get(ResourceType.BullToken).ToString();
        if (bearTokenLabel != null)
            bearTokenLabel.text = ResourceBank.Get(ResourceType.BearToken).ToString();
    }

    private void HandleFeatureUnlocked(FeatureId id)
    {
        if (id == FeatureId.Exchange_BearBullTokens)
            RefreshTokenDisplay();

        if (id == FeatureId.Exchange_DividendYield)
            RefreshDividendDisplay();
    }

    private void RefreshDividendDisplay()
    {
        if (dividendYieldRoot == null && dividendYieldLabel == null) return;

        bool unlocked = FeatureUnlockManager.I != null &&
                        FeatureUnlockManager.I.IsUnlocked(FeatureId.Exchange_DividendYield);

        if (dividendYieldRoot != null)
            dividendYieldRoot.SetActive(unlocked);

        if (!unlocked || dividendYieldLabel == null)
            return;

        int dividend = ExchangeManager.I != null ? ExchangeManager.I.GetCurrentDividendAmount() : 0;
        dividendYieldLabel.text = $"Dividend Yield (Collected Monday): +{dividend} Credits";
    }
}
