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
    [SerializeField] private TextMeshProUGUI nameLabel;
    [SerializeField] private TextMeshProUGUI valueLabel;

    private static readonly Color ColorUp   = new Color(0.2f, 0.85f, 0.3f);  // green
    private static readonly Color ColorDown = new Color(0.95f, 0.25f, 0.25f); // red
    private static readonly Color ColorFlat = Color.white;

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
        if (nameLabel != null) nameLabel.text = def.displayName;

        int current  = state?.currentValue  ?? def.baseMarketValue;
        int previous = state?.previousValue ?? def.baseMarketValue;
        CurrentValue = current;
        PreviousValue = previous;
        Trend = state?.trend ?? TrendDirection.Stable;

        if (valueLabel != null)
        {
            int delta = current - previous;
            string arrow;
            Color color;

            if (delta > 0)      { arrow = "▲"; color = ColorUp;   }
            else if (delta < 0) { arrow = "▼"; color = ColorDown; }
            else                { arrow = "";  color = ColorFlat; }

            valueLabel.text = delta != 0
                ? $"{current} {arrow}{Mathf.Abs(delta)}"
                : $"{current}";
            valueLabel.color = color;
        }
    }
}
