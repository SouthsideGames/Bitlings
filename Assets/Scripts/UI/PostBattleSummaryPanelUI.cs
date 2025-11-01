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

    /// <summary>
    /// Sets all summary values.
    /// coinsBase/coinsTitleBonus and xpBase/xpTitleBonus are the breakdowns we render.
    /// xpDetailLines are per-monster progress lines (optional).
    /// </summary>
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

        // Coins: “+<total>” with green “(+bonus)”
        int totalCoins = Mathf.Max(0, coinsBase + coinsTitleBonus);
        if (coinsLabel)
        {
            if (coinsTitleBonus > 0)
                coinsLabel.text = $"+{totalCoins} Coins <color={GREEN}>(+{coinsTitleBonus})</color>";
            else
                coinsLabel.text = $"+{totalCoins} Coins";
        }

        // XP: “+<total> XP” with green bonus
        int totalXP = Mathf.Max(0, xpBase + xpTitleBonus);
        if (xpLabel)
        {
            if (xpTitleBonus > 0)
                xpLabel.text = $"+{totalXP} XP <color={GREEN}>(+{xpTitleBonus})</color>";
            else
                xpLabel.text = $"+{totalXP} XP";
        }

        // Level-up banner (unchanged behavior, but included for completeness)
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

        // Capture text (unchanged)
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
}
