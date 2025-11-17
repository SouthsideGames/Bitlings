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
    [SerializeField] private TextMeshProUGUI growthCoresLabel;
    [SerializeField] private TextMeshProUGUI levelupsLabel;
    [SerializeField] private TextMeshProUGUI captureLabel;
    [SerializeField] private BattleManager battleManager;

    [Header("Anim")]
    [SerializeField] private float fadeIn = 0.18f;
    [SerializeField] private float popIn = 0.22f;

    public Action OnClosed;

    [Header("Optional Detail Block")]
    [SerializeField] private TextMeshProUGUI growthCoresDetailsLabel;

    const string GREEN = "#3CDE74";

    void Awake()
    {
        if (!cg) cg = GetComponent<CanvasGroup>();
        if (!cg) cg = gameObject.AddComponent<CanvasGroup>();
        if (!root) root = transform as RectTransform;

        cg.alpha = 0f;
        cg.blocksRaycasts = false;
        cg.interactable = false;
        if (root) root.localScale = Vector3.one * 0.96f;
    }

    public void Set(
        BattleResult result,
        int growthCoresGained = 0,          
        int monstersLeveledUp = 0,
        bool captured = false,
        string capturedMonsterId = null,
        int capturedLevel = 0,
        List<string> levelUpSummaries = null,
        int coinsBase = 0,
        int coinsTitleBonus = 0,
        int growthCoresBase = 0,            
        int growthCoresTitleBonus = 0,      
        List<string> growthCoresDetailLines = null
    )
    {
        // ───────────── Title ─────────────
        if (titleLabel)
            titleLabel.text = result.victory ? "Victory!" : "Defeat";

        // ───────────── Coins ─────────────
        int coinsTotal = Mathf.Max(0, coinsBase + coinsTitleBonus);
        if (coinsLabel)
            coinsLabel.text = BuildRewardLine("Coins", coinsBase, coinsTitleBonus, coinsTotal);

        // ───────────── Growth Cores ─────────────
        int coresTotal = Mathf.Max(0, growthCoresBase + growthCoresTitleBonus);
        if (growthCoresLabel)
        {
            if (growthCoresBase <= 0 && growthCoresTitleBonus <= 0 && growthCoresGained > 0)
            {
                growthCoresLabel.text = $"Growth Cores: {growthCoresGained}";
            }
            else
            {
                growthCoresLabel.text = BuildRewardLine(
                    "Growth Cores",
                    growthCoresBase,
                    growthCoresTitleBonus,
                    coresTotal
                );
            }
        }

        // ───────────── Level Ups ─────────────
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
                else
                {
                    levelupsLabel.text = $"{monstersLeveledUp} leveled up";
                }
            }
            else
            {
                levelupsLabel.text = "No level ups";
            }
        }

        // ───────────── Capture ─────────────
        if (captureLabel)
        {
            if (captured)
            {
                string name = !string.IsNullOrEmpty(capturedMonsterId)
                    ? capturedMonsterId
                    : (result.wildDef ? result.wildDef.id : "Unknown");

                int lvl = capturedLevel > 0
                    ? capturedLevel
                    : Mathf.Max(1, result.wildLevel);

                captureLabel.text = $"Captured: {name} (Lv {lvl})";
            }
            else
            {
                captureLabel.text = "No capture";
            }
        }

        // ───────────── Growth Core Breakdown (per-monster lines) ─────────────
        if (growthCoresDetailsLabel)
        {
            if (growthCoresDetailLines != null && growthCoresDetailLines.Count > 0)
            {
                growthCoresDetailsLabel.gameObject.SetActive(true);
                growthCoresDetailsLabel.text = string.Join("\n", growthCoresDetailLines);
            }
            else
            {
                growthCoresDetailsLabel.gameObject.SetActive(false);
            }
        }
    }

    public void Show()
    {
        cg.blocksRaycasts = true;
        cg.interactable = true;
        LeanTween.alphaCanvas(cg, 1f, fadeIn).setEaseOutSine();
        if (root) LeanTween.scale(root, Vector3.one, popIn).setEaseOutBack();
    }

    public void Close()
    {
        bool isAuto = IsAutoBattleMode();

        if (isAuto)
        {
            BattleLogger.SetEnabled(false);
            BattleLogger.ClearAll(false);
        }
        else
        {
            BattleLogger.SetEnabled(true);
            BattleLogger.ClearAll(false);
        }

        cg.blocksRaycasts = false;
        cg.interactable = false;
        LeanTween.alphaCanvas(cg, 0f, 0.12f).setEaseInSine()
            .setOnComplete(() => OnClosed?.Invoke());
    }

    private bool IsAutoBattleMode() => EncounterManager.I && EncounterManager.I.IsAutoMode;

    private string BuildRewardLine(string label, int baseValue, int titleBonus, int total)
    {
        if (titleBonus <= 0 || baseValue <= 0)
        {
            int display = Mathf.Max(baseValue, total);
            return $"{label}: {Mathf.Max(0, display)}";
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
