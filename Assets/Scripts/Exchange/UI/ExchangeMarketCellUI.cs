using UnityEngine;
using UnityEngine.UI;
using TMPro;

// ─────────────────────────────────────────────────────────────
// ExchangeMarketCellUI — compact grid cell for the Market tab.
// Shows species icon, name, and value with color coding:
//   Green = value up, Red = value down, White = unchanged.
// ─────────────────────────────────────────────────────────────

public class ExchangeMarketCellUI : MonoBehaviour
{
    [SerializeField] private Image speciesIcon;
    [SerializeField] private TextMeshProUGUI nameValueLabel;
    [SerializeField] private TextMeshProUGUI forecastLabel;
    [SerializeField] private GameObject monopolyBonusIcon;

    private static readonly Color ColorUp   = new Color(0.2f, 0.85f, 0.3f);  // green
    private static readonly Color ColorDown = new Color(0.95f, 0.25f, 0.25f); // red
    private static readonly Color ColorFlat = Color.white;
    private static readonly Color ColorForecastSurge = new Color(1f, 0.85f, 0.2f);   // gold
    private static readonly Color ColorForecastHigh  = new Color(0.5f, 0.85f, 1f);   // light blue
    private static readonly Color ColorForecastLow   = new Color(0.7f, 0.7f, 0.7f);  // grey

    public string SpeciesId { get; private set; }
    public int CurrentValue { get; private set; }
    public int PreviousValue { get; private set; }
    public Rarity SpeciesRarity { get; private set; }
    public MonsterType SpeciesType { get; private set; }
    public TrendDirection Trend { get; private set; }

    public void Populate(MonsterDataSO def, MarketSpeciesState state)
    {
        if (def == null) return;

        SpeciesId = def.id;
        SpeciesRarity = def.rarity;
        SpeciesType = def.type;

        if (speciesIcon != null) speciesIcon.sprite = def.icon;

        int current  = state?.currentValue  ?? def.baseMarketValue;
        int previous = state?.previousValue ?? def.baseMarketValue;
        CurrentValue = current;
        PreviousValue = previous;
        Trend = state?.trend ?? TrendDirection.Stable;

        if (nameValueLabel != null)
        {
            int delta = current - previous;
            string arrow;
            string colorHex;

            if (delta > 0)      { arrow = " ▲"; colorHex = "#33D94D"; }
            else if (delta < 0) { arrow = " ▼"; colorHex = "#F24040"; }
            else                { arrow = "";    colorHex = null; }

            string valueStr = delta != 0
                ? $"<color={colorHex}>{current} {arrow}{Mathf.Abs(delta)}</color>"
                : $"{current}";

            nameValueLabel.text = $"{def.displayName} | {valueStr}";
        }

        // Market Forecast: show tomorrow's predicted demand if unlocked
        if (forecastLabel != null)
        {
            bool showForecast = FeatureUnlockManager.I != null &&
                                FeatureUnlockManager.I.IsUnlocked(FeatureId.Exchange_MarketForecast) &&
                                ExchangeManager.I != null;
            if (showForecast)
            {
                var forecast = ExchangeManager.I.GetForecastDemand(def.id);
                forecastLabel.gameObject.SetActive(true);
                forecastLabel.text = forecast switch
                {
                    DemandLevel.Surge  => "▶ SURGE",
                    DemandLevel.High   => "▶ High",
                    DemandLevel.Low    => "▶ Low",
                    _                  => "▶ Med"
                };
                forecastLabel.color = forecast switch
                {
                    DemandLevel.Surge  => ColorForecastSurge,
                    DemandLevel.High   => ColorForecastHigh,
                    DemandLevel.Low    => ColorForecastLow,
                    _                  => ColorFlat
                };
            }
            else
            {
                forecastLabel.gameObject.SetActive(false);
            }
        }

        // Monopoly Bonus icon: active when upgrade is unlocked and player owns all species of this type
        if (monopolyBonusIcon != null)
        {
            bool showMonopoly = FeatureUnlockManager.I != null &&
                                FeatureUnlockManager.I.IsUnlocked(FeatureId.Exchange_MonopolyBonus) &&
                                ExchangeManager.I != null &&
                                ExchangeManager.I.HasMonopoly(def.type);
            monopolyBonusIcon.SetActive(showMonopoly);
        }
    }
}
