using UnityEngine;
using UnityEngine.UI;
using TMPro;

// ─────────────────────────────────────────────────────────────
// ExchangeMarketRowUI — a single row in the Exchange Market or
// Portfolio list, displaying species info and market data.
// ─────────────────────────────────────────────────────────────

public class ExchangeMarketRowUI : MonoBehaviour
{
    [SerializeField] private Image speciesIcon;
    [SerializeField] private TextMeshProUGUI nameLabel;
    [SerializeField] private TextMeshProUGUI valueLabel;
    [SerializeField] private TextMeshProUGUI trendLabel;
    [SerializeField] private TextMeshProUGUI demandLabel;
    [SerializeField] private TextMeshProUGUI ownedLabel;
    [SerializeField] private TextMeshProUGUI levelLabel;        // portfolio only
    [SerializeField] private TextMeshProUGUI brokerPayoutLabel; // portfolio only

    public void Populate(MonsterDataSO def, MarketSpeciesState state, int ownedCount, int level = -1, int brokerPayout = -1)
    {
        if (def == null) return;

        if (speciesIcon != null) speciesIcon.sprite = def.icon;
        if (nameLabel != null) nameLabel.text = def.displayName;

        int val = state?.currentValue ?? def.baseMarketValue;
        if (valueLabel != null)
        {
            bool isPortfolioRow = level >= 0 || brokerPayout >= 0;
            valueLabel.text = isPortfolioRow ? $"Value: {val}" : $"{val}";
        }

        // Trend arrow
        if (trendLabel != null)
        {
            if (state != null)
            {
                switch (state.trend)
                {
                    case TrendDirection.Rising:  trendLabel.text = "▲"; trendLabel.color = new Color(0.2f, 0.8f, 0.3f); break;
                    case TrendDirection.Falling: trendLabel.text = "▼"; trendLabel.color = new Color(0.9f, 0.3f, 0.3f); break;
                    default:                     trendLabel.text = "→"; trendLabel.color = Color.gray; break;
                }
            }
            else
            {
                trendLabel.text = "→";
                trendLabel.color = Color.gray;
            }
        }

        // Demand label
        if (demandLabel != null)
        {
            if (state != null)
            {
                switch (state.demandLevel)
                {
                    case DemandLevel.Low:    demandLabel.text = "Low";    demandLabel.color = Color.gray; break;
                    case DemandLevel.Medium: demandLabel.text = "Steady"; demandLabel.color = Color.white; break;
                    case DemandLevel.High:   demandLabel.text = "High";   demandLabel.color = new Color(1f, 0.7f, 0.2f); break;
                    case DemandLevel.Surge:  demandLabel.text = "Surge!"; demandLabel.color = new Color(1f, 0.3f, 0.3f); break;
                }
            }
            else
            {
                demandLabel.text = "Steady";
                demandLabel.color = Color.white;
            }
        }

        if (ownedLabel != null) ownedLabel.text = ownedCount > 0 ? "Owned" : "Not Owned";

        // Portfolio-specific fields
        if (levelLabel != null)
        {
            levelLabel.gameObject.SetActive(level >= 0);
            if (level >= 0) levelLabel.text = $"Lv {level}";
        }
        if (brokerPayoutLabel != null)
        {
            brokerPayoutLabel.gameObject.SetActive(brokerPayout >= 0);
            if (brokerPayout >= 0) brokerPayoutLabel.text = $"Broker: +{brokerPayout}";
        }
    }
}
