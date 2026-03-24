using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class StatBreakdownPanelUI : MonoBehaviour
{
    public static StatBreakdownPanelUI I { get; private set; }

    [Header("UI References")]
    [SerializeField] private TextMeshProUGUI headerLabel;
    [SerializeField] private TextMeshProUGUI detailLabel;
    [SerializeField] private Button closeButton;

    [Header("Stat Buttons (also TMP labels)")]
    [SerializeField] private Button atkButton;
    [SerializeField] private TextMeshProUGUI atkLabel;
    [SerializeField] private Button defButton;
    [SerializeField] private TextMeshProUGUI defLabel;
    [SerializeField] private Button spdButton;
    [SerializeField] private TextMeshProUGUI spdLabel;
    [SerializeField] private Button hpButton;
    [SerializeField] private TextMeshProUGUI hpLabel;

    [Header("Combat Effects")]
    [SerializeField] private TextMeshProUGUI combatEffectsLabel;

    const string GREEN = "#36D674";
    const string RED   = "#FF7A53";
    const string GRAY  = "#888888";
    const string YELLOW = "#FFD966";

    // Cached detail strings per stat so button presses can swap the detail text.
    private string _atkDetail;
    private string _defDetail;
    private string _spdDetail;
    private string _hpDetail;

    void Awake()
    {
        I = this;
        WireButtons();
    }

    void OnDestroy()
    {
        if (I == this) I = null;
    }

    public void Show(
        string monsterName,
        BattleStatBlock baseStats,
        BattleStatBlock finalStats,
        List<BattleStatsSystem.StatBreakdownLine> statLines,
        List<CombatEffectLineBuilder.CombatEffectLine> effectLines,
        string jobName)
    {
        // Lazy-init if Awake hasn't run (panel started inactive).
        if (I == null)
        {
            I = this;
            WireButtons();
        }

        if (headerLabel)
            headerLabel.text = monsterName;

        // ── Update the 4 stat buttons ──
        SetStatButton(atkButton, atkLabel, statLines, BattleStatKind.ATK, "Attack",  baseStats.atk,   finalStats.atk,   out _atkDetail);
        SetStatButton(defButton, defLabel, statLines, BattleStatKind.DEF, "Defense", baseStats.def,   finalStats.def,   out _defDetail);
        SetStatButton(spdButton, spdLabel, statLines, BattleStatKind.SPD, "Speed",   baseStats.spd,   finalStats.spd,   out _spdDetail);
        SetStatButton(hpButton,  hpLabel,  statLines, BattleStatKind.HP,  "HP",      baseStats.maxHP, finalStats.maxHP, out _hpDetail);

        // ── Combat Effects text ──
        if (combatEffectsLabel)
        {
            bool hasEffects = effectLines != null && effectLines.Count > 0;
            if (hasEffects)
            {
                var sb = new StringBuilder();
                string header = string.IsNullOrEmpty(jobName)
                    ? $"<b><color={YELLOW}>Combat Effects</color></b>"
                    : $"<b><color={YELLOW}>Combat Effects ({jobName})</color></b>";
                sb.AppendLine(header);

                for (int i = 0; i < effectLines.Count; i++)
                {
                    var eff = effectLines[i];
                    string color = eff.isConsumed ? GRAY : GREEN;
                    sb.AppendLine($"  <color={color}>{eff.label}</color>");
                }
                combatEffectsLabel.text = sb.ToString();
                combatEffectsLabel.gameObject.SetActive(true);
            }
            else
            {
                combatEffectsLabel.gameObject.SetActive(false);
            }
        }

        // Clear detail area — player taps a stat button to see details.
        if (detailLabel) detailLabel.text = "";

        gameObject.SetActive(true);
        var cg = GetComponent<CanvasGroup>();
        if (cg) { cg.alpha = 1f; cg.blocksRaycasts = true; cg.interactable = true; }
    }

    public void Hide()
    {
        gameObject.SetActive(false);
    }

    // ── Internals ──

    private void WireButtons()
    {
        if (closeButton) closeButton.onClick.AddListener(Hide);
        if (atkButton) atkButton.onClick.AddListener(() => ShowDetail(_atkDetail));
        if (defButton) defButton.onClick.AddListener(() => ShowDetail(_defDetail));
        if (spdButton) spdButton.onClick.AddListener(() => ShowDetail(_spdDetail));
        if (hpButton)  hpButton.onClick.AddListener(() => ShowDetail(_hpDetail));
    }

    private void ShowDetail(string detail)
    {
        if (detailLabel) detailLabel.text = detail ?? "";
    }

    private void SetStatButton(
        Button btn,
        TextMeshProUGUI label,
        List<BattleStatsSystem.StatBreakdownLine> allLines,
        BattleStatKind stat,
        string statName,
        int baseVal,
        int finalVal,
        out string detail)
    {
        int delta = finalVal - baseVal;
        bool hasDelta = delta != 0;

        // ── Button label: "Attack:  120  +35  = 155" ──
        if (label)
        {
            if (hasDelta)
            {
                string deltaColor = delta > 0 ? GREEN : RED;
                string sign = delta > 0 ? "+" : "";
                label.text = $"{statName}:  {baseVal}  <color={deltaColor}>{sign}{delta}</color>  = {finalVal}";
            }
            else
            {
                label.text = $"{statName}:  {baseVal}";
            }
        }

        // ── Build per-source detail string ──
        if (!hasDelta || allLines == null)
        {
            detail = $"<color={GRAY}>No modifiers affecting {statName}.</color>";
            if (btn) btn.interactable = hasDelta;
            return;
        }

        var sb = new StringBuilder();
        for (int i = 0; i < allLines.Count; i++)
        {
            if (allLines[i].stat != stat) continue;
            int d = allLines[i].delta;
            string color = d > 0 ? GREEN : RED;
            string dSign = d > 0 ? "+" : "";

            if (sb.Length > 0) sb.Append(" and ");
            sb.Append($"<color={color}>{allLines[i].source} provided {dSign}{d} {statName}</color>");
        }
        sb.Append(".");
        detail = sb.ToString();

        if (btn) btn.interactable = true;
    }
}
