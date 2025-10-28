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
        List<string> levelUpSummaries = null
    )
    {
        if (titleLabel)  titleLabel.text  = r.victory ? "Victory!" : "Defeat";
        if (coinsLabel)  coinsLabel.text  = $"+{Mathf.Max(0, r.coinsGained)} Coins";
        if (xpLabel)     xpLabel.text     = $"+{Mathf.Max(0, xpGained)} XP";

        if (levelupsLabel)
        {
            if (monstersLeveledUp > 0)
            {
                if (levelUpSummaries != null && levelUpSummaries.Count > 0)
                {
                    // e.g., ["Gru 3→4", "Cindrax 5→6"]
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
                else
                    levelupsLabel.text = $"{monstersLeveledUp} leveled up";
            }
            else
                levelupsLabel.text = "No level ups";
        }

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
            else
            {
                captureLabel.text = "No capture";
            }
        }
    }

    public void Show()
    {
        LeanTween.alphaCanvas(cg, 1f, fadeIn).setEaseOutSine();
        if (root) LeanTween.scale(root, Vector3.one, popIn).setEaseOutBack();
    }

}
