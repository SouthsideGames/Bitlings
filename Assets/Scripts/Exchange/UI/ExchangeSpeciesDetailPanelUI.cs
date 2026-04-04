using UnityEngine;
using UnityEngine.UI;
using TMPro;

// ─────────────────────────────────────────────────────────────
// ExchangeSpeciesDetailPanelUI — overlay shown when tapping a
// species in the Exchange Market grid. Displays market info and
// Bull/Bear token controls when unlocked.
// ─────────────────────────────────────────────────────────────

public class ExchangeSpeciesDetailPanelUI : MonoBehaviour
{
    public static ExchangeSpeciesDetailPanelUI I;

    /// <summary>Set by caller before opening via UIManager.</summary>
    public static MonsterDataSO PendingSpecies;

    [Header("Species Info")]
    [SerializeField] private Image speciesIcon;
    [SerializeField] private TextMeshProUGUI nameLabel;
    [SerializeField] private TextMeshProUGUI typeRarityLabel;
    [SerializeField] private TextMeshProUGUI marketValueLabel;
    [SerializeField] private TextMeshProUGUI demandLabel;
    [SerializeField] private TextMeshProUGUI trendLabel;
    [SerializeField] private TextMeshProUGUI brokerPayoutLabel;

    [Header("Forecast (requires unlock)")]
    [SerializeField] private GameObject forecastSection;
    [SerializeField] private TextMeshProUGUI forecastLabel;
    [SerializeField] private Button setAlertButton;
    [SerializeField] private TextMeshProUGUI setAlertButtonLabel;

    [Header("Monopoly Bonus")]
    [SerializeField] private GameObject monopolySection;
    [SerializeField] private TextMeshProUGUI monopolyLabel;

    [Header("Token Controls (requires unlock)")]
    [SerializeField] private GameObject tokenSection;
    [SerializeField] private Button bullTokenButton;
    [SerializeField] private TextMeshProUGUI bullTokenCountLabel;
    [SerializeField] private Button bearTokenButton;
    [SerializeField] private TextMeshProUGUI bearTokenCountLabel;

    [Header("Close")]
    [SerializeField] private Button closeButton;

    private MonsterDataSO _def;

    void Awake() { I = this; }

    void OnEnable()
    {
        if (closeButton) closeButton.onClick.AddListener(Close);
        if (setAlertButton) setAlertButton.onClick.AddListener(OnToggleSurgeAlert);
        GameEvents.OnResourcesChanged += RefreshTokenCounts;
        GameEvents.ExchangeValuesChanged += RefreshMarketData;

        Populate();
    }

    void OnDisable()
    {
        if (closeButton) closeButton.onClick.RemoveListener(Close);
        if (setAlertButton) setAlertButton.onClick.RemoveListener(OnToggleSurgeAlert);
        if (bullTokenButton) bullTokenButton.onClick.RemoveAllListeners();
        if (bearTokenButton) bearTokenButton.onClick.RemoveAllListeners();

        GameEvents.OnResourcesChanged -= RefreshTokenCounts;
        GameEvents.ExchangeValuesChanged -= RefreshMarketData;

        _def = null;
    }

    // ─────────── Population ───────────

    private void Populate()
    {
        _def = PendingSpecies;
        if (_def == null) { Close(); return; }

        if (speciesIcon) speciesIcon.sprite = _def.icon;
        if (nameLabel) nameLabel.text = _def.displayName;
        if (typeRarityLabel) typeRarityLabel.text = $"{_def.type}  •  {_def.rarity}";

        RefreshMarketData();
        RefreshForecast();
        RefreshAlertButton();
        RefreshMonopoly();
        RefreshTokenSection();
    }

    private void RefreshMarketData()
    {
        if (_def == null) return;

        var state = ExchangeManager.I?.GetState(_def.id);
        int current = state?.currentValue ?? _def.baseMarketValue;
        int previous = state?.previousValue ?? _def.baseMarketValue;
        int delta = current - previous;

        // Market value with delta
        if (marketValueLabel)
        {
            if (delta > 0)
                marketValueLabel.text = $"Value: {current} <color=#33D94D>▲{delta}</color>";
            else if (delta < 0)
                marketValueLabel.text = $"Value: {current} <color=#F24040>▼{Mathf.Abs(delta)}</color>";
            else
                marketValueLabel.text = $"Value: {current}";
        }

        // Demand
        if (demandLabel)
        {
            var demand = state?.demandLevel ?? DemandLevel.Medium;
            demandLabel.text = $"Demand: {demand}";
        }

        // Trend
        if (trendLabel)
        {
            var trend = state?.trend ?? TrendDirection.Stable;
            string trendStr = trend switch
            {
                TrendDirection.Rising  => "<color=#33D94D>Rising ▲</color>",
                TrendDirection.Falling => "<color=#F24040>Falling ▼</color>",
                _                      => "Stable"
            };
            trendLabel.text = $"Trend: {trendStr}";
        }

        // Broker payout
        if (brokerPayoutLabel && ExchangeManager.I != null)
        {
            int payout = ExchangeManager.I.GetBrokerPayout(_def.id);
            brokerPayoutLabel.text = $"Broker Payout: {payout} Credits";
        }
    }

    // ─────────── Forecast ───────────

    private void RefreshForecast()
    {
        if (forecastSection == null) return;

        bool unlocked = FeatureUnlockManager.I != null &&
                        FeatureUnlockManager.I.IsUnlocked(FeatureId.Exchange_MarketForecast) &&
                        ExchangeManager.I != null;
        forecastSection.SetActive(unlocked);

        if (!unlocked || forecastLabel == null || _def == null) return;

        var forecast = ExchangeManager.I.GetForecastDemand(_def.id);
        forecastLabel.text = $"Tomorrow's Forecast: {forecast}";
    }

    private void RefreshAlertButton()
    {
        if (setAlertButton == null) return;

        bool unlocked = FeatureUnlockManager.I != null &&
                        FeatureUnlockManager.I.IsUnlocked(FeatureId.Exchange_SurgeAlert) &&
                        _def != null &&
                        ExchangeManager.I != null;

        setAlertButton.gameObject.SetActive(unlocked);
        if (!unlocked) return;

        bool enabled = ExchangeManager.I.IsSurgeAlertEnabledForSpecies(_def.id);
        if (setAlertButtonLabel != null)
            setAlertButtonLabel.text = enabled ? "Alert: ON" : "Set Alert";
    }

    // ─────────── Monopoly ───────────

    private void RefreshMonopoly()
    {
        if (monopolySection == null) return;

        bool unlocked = FeatureUnlockManager.I != null &&
                        FeatureUnlockManager.I.IsUnlocked(FeatureId.Exchange_MonopolyBonus) &&
                        ExchangeManager.I != null &&
                        _def != null &&
                        ExchangeManager.I.HasMonopoly(_def.type);
        monopolySection.SetActive(unlocked);

        if (unlocked && monopolyLabel != null)
            monopolyLabel.text = $"Monopoly Bonus active! All {_def.type} species get +25% value.";
    }

    // ─────────── Token Section ───────────

    private void RefreshTokenSection()
    {
        if (tokenSection == null) return;

        bool unlocked = FeatureUnlockManager.I != null &&
                        FeatureUnlockManager.I.IsUnlocked(FeatureId.Exchange_BearBullTokens);
        tokenSection.SetActive(unlocked);

        if (!unlocked) return;

        // Wire buttons
        if (bullTokenButton)
        {
            bullTokenButton.onClick.RemoveAllListeners();
            bullTokenButton.onClick.AddListener(OnBullToken);
        }
        if (bearTokenButton)
        {
            bearTokenButton.onClick.RemoveAllListeners();
            bearTokenButton.onClick.AddListener(OnBearToken);
        }

        RefreshTokenCounts();
    }

    private void RefreshTokenCounts()
    {
        int bullCount = ResourceBank.Get(ResourceType.BullToken);
        int bearCount = ResourceBank.Get(ResourceType.BearToken);
        bool canUseBullToday = ExchangeManager.I != null && _def != null && ExchangeManager.I.CanUseBullTokenOnSpecies(_def.id);
        bool canUseBearToday = ExchangeManager.I != null && _def != null && ExchangeManager.I.CanUseBearTokenOnSpecies(_def.id);

        if (bullTokenCountLabel)
            bullTokenCountLabel.text = bullCount.ToString();
        if (bearTokenCountLabel)
            bearTokenCountLabel.text = bearCount.ToString();

        // Disable buttons if no tokens are available or today's token has already been used.
        if (bullTokenButton)
            bullTokenButton.interactable = bullCount > 0 && canUseBullToday;
        if (bearTokenButton)
            bearTokenButton.interactable = bearCount > 0 && canUseBearToday;
    }

    // ─────────── Actions ───────────

    private void OnBullToken()
    {
        if (_def == null || ExchangeManager.I == null) return;
        if (bullTokenButton != null) bullTokenButton.interactable = false;
        ExchangeManager.I.UseBullToken(_def.id);
        RefreshTokenCounts();
        AudioManager.I?.PlayClick();
    }

    private void OnBearToken()
    {
        if (_def == null || ExchangeManager.I == null) return;
        if (bearTokenButton != null) bearTokenButton.interactable = false;
        ExchangeManager.I.UseBearToken(_def.id);
        RefreshTokenCounts();
        AudioManager.I?.PlayClick();
    }

    private void OnToggleSurgeAlert()
    {
        if (_def == null || ExchangeManager.I == null) return;

        bool enabled = ExchangeManager.I.IsSurgeAlertEnabledForSpecies(_def.id);
        ExchangeManager.I.SetSurgeAlertForSpecies(_def.id, !enabled);
        RefreshAlertButton();
        AudioManager.I?.PlayClick();
    }

    private void Close()
    {
        PendingSpecies = null;
        if (UIManager.I != null)
        {
            UIManager.I.Hide(PanelId.ExchangeSpeciesDetail);
            UIManager.I.Show(PanelId.Exchange);
        }
    }
}
