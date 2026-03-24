using TMPro;
using UnityEngine;

public sealed class StatBreakdownRowUI : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI label;
    [SerializeField] private TextMeshProUGUI value;

    private static readonly Color BuffColor    = new Color(0.21f, 0.84f, 0.45f);  // #36D674
    private static readonly Color DebuffColor  = new Color(1f, 0.48f, 0.33f);     // #FF7A53
    private static readonly Color ConsumedColor = new Color(0.55f, 0.55f, 0.55f); // dimmed gray
    private static readonly Color HeaderColor  = Color.white;

    public void SetHeader(string text)
    {
        if (label) { label.text = text; label.color = HeaderColor; label.fontStyle = FontStyles.Bold; }
        if (value) { value.text = ""; }
    }

    public void SetStatRow(string source, BattleStatKind stat, int delta)
    {
        string statName = stat switch
        {
            BattleStatKind.HP  => "HP",
            BattleStatKind.ATK => "ATK",
            BattleStatKind.DEF => "DEF",
            BattleStatKind.SPD => "SPD",
            _ => stat.ToString()
        };

        bool positive = delta > 0;
        Color c = positive ? BuffColor : DebuffColor;
        string sign = positive ? "+" : "";

        if (label) { label.text = $"  {source}"; label.color = c; label.fontStyle = FontStyles.Normal; }
        if (value) { value.text = $"{sign}{delta} {statName}"; value.color = c; }
    }

    public void SetEffectRow(string effectLabel, bool isConsumed)
    {
        Color c = isConsumed ? ConsumedColor : BuffColor;
        if (label) { label.text = $"  {effectLabel}"; label.color = c; label.fontStyle = FontStyles.Normal; }
        if (value) { value.text = ""; }
    }
}
