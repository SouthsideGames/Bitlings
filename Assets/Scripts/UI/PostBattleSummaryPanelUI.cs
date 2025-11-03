using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;
using System.Collections.Generic;
using System.Text;

public class PostBattleSummaryPanelUI : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private CanvasGroup cg;
    [SerializeField] private RectTransform root;
    [SerializeField] private TextMeshProUGUI titleLabel;
    [SerializeField] private TextMeshProUGUI coinsLabel;
    [SerializeField] private TextMeshProUGUI xpLabel;
    [SerializeField] private TextMeshProUGUI levelupsLabel;
    [SerializeField] private TextMeshProUGUI captureLabel;

    [Header("Anim")]
    [SerializeField] private float fadeIn = 0.18f;
    [SerializeField] private float popIn = 0.22f;

    public Action OnClosed;

    // Rich XP breakdown list (optional): one line per monster
    [Header("Optional Detail Block")]
    [SerializeField] private TextMeshProUGUI xpDetailsLabel;

    const string GREEN = "#3CDE74";

    void Awake()
    {
        if (!cg) cg = GetComponent<CanvasGroup>();
        if (!cg) cg = gameObject.AddComponent<CanvasGroup>();
        if (!root) root = transform as RectTransform;

        cg.alpha = 0f;
        if (root) root.localScale = Vector3.one * 0.96f;
    }

    public void Set(
        BattleResult r,
        int xpGained = 0,
        int monstersLeveledUp = 0,
        bool captured = false,
        string capturedMonsterId = null,
        int capturedLevel = 0,
        List<string> levelUpSummaries = null,
        int coinsBase = 0,
        int coinsTitleBonus = 0,
        int xpBase = 0,
        int xpTitleBonus = 0,
        List<string> xpDetailLines = null
    )
    {
        if (titleLabel) titleLabel.text = r.victory ? "Victory!" : "Defeat";

        // ─────────────── Coins line ───────────────
        int coinsTotal = Mathf.Max(0, coinsBase + coinsTitleBonus);
        if (coinsLabel)
        {
            coinsLabel.text = BuildRewardLine("Coins", coinsBase, coinsTitleBonus, coinsTotal);
        }

        // ─────────────── XP line ───────────────
        int xpTotal = Mathf.Max(0, xpBase + xpTitleBonus);
        if (xpLabel)
        {
            xpLabel.text = BuildRewardLine("XP", xpBase, xpTitleBonus, xpTotal);
        }

        // Level-up banner
        if (levelupsLabel)
        {
            if (monstersLeveledUp > 0)
            {
                if (levelUpSummaries != null && levelUpSummaries.Count > 0)
                {
                    var sb = new StringBuilder();
                    sb.Append($"{monstersLeveledUp} leveled up (");
                    for (int i = 0; i < levelUpSummaries.Count; i++)
                    {
                        if (i > 0) sb.Append(", ");
                        sb.Append(levelUpSummaries[i]);
                    }
                    sb.Append(')');
                    levelupsLabel.text = sb.ToString();
                }
                else levelupsLabel.text = $"{monstersLeveledUp} leveled up";
            }
            else levelupsLabel.text = "No level ups";
        }

        // Capture text
        if (captureLabel)
        {
            if (captured)
            {
                string name = !string.IsNullOrEmpty(capturedMonsterId)
                              ? capturedMonsterId
                              : (r.wildDef ? r.wildDef.id : "Unknown");
                int lvl = capturedLevel > 0 ? capturedLevel : Mathf.Max(1, r.wildLevel);
                captureLabel.text = $"Captured: {name} (Lv {lvl})";
            }
            else captureLabel.text = "No capture";
        }

        // Optional detailed XP list: one line per monster
        if (xpDetailsLabel)
        {
            if (xpDetailLines != null && xpDetailLines.Count > 0)
            {
                // Example line format you can feed in:
                // "Cindrax Lv5 (12/180) → +36 <color=#3CDE74>(+6)</color> → Lv6 (4/225)"
                xpDetailsLabel.gameObject.SetActive(true);
                xpDetailsLabel.text = string.Join("\n", xpDetailLines);
            }
            else
            {
                xpDetailsLabel.gameObject.SetActive(false);
            }
        }
    }

    public void Show()
    {
        LeanTween.alphaCanvas(cg, 1f, fadeIn).setEaseOutSine();
        if (root) LeanTween.scale(root, Vector3.one, popIn).setEaseOutBack();
    }

    // ─────────────────────────────────────────────────────────────────────────────

    private string BuildRewardLine(string label, int baseValue, int titleBonus, int total)
    {
        // If no title bonus affected the reward, show: "Coins: 10"
        if (titleBonus <= 0 || baseValue <= 0)
        {
            return $"{label}: {Mathf.Max(0, baseValue)}";
        }

        float multiplier = total > 0 && baseValue > 0 ? (float)total / baseValue : 1f;
        string multText = FormatMultiplier(multiplier);

        return $"{label}: {baseValue} ({multText}) = <color={GREEN}>{total}</color>";
    }

    private string FormatMultiplier(float m)
    {
        float rounded = Mathf.Round(m);
        if (Mathf.Abs(rounded - m) < 0.001f)
            return $"x{rounded:0}";

        return $"x{m:0.##}";
    }
}
