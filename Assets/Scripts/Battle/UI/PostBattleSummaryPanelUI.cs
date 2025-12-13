using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;
using System.Collections.Generic;

public class PostBattleSummaryPanelUI : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private CanvasGroup cg;
    [SerializeField] private RectTransform root;

    [Header("Title & Layout")]
    [SerializeField] private TextMeshProUGUI titleLabel;

    [Header("Rewards")]
    [SerializeField] private TextMeshProUGUI creditsLabel;
    [SerializeField] private TextMeshProUGUI growthCoresLabel;
    [SerializeField] private TextMeshProUGUI captureLabel;

    [Header("Monster Info")]
    [SerializeField] private Image enemyPortraitImage;
    [SerializeField] private TextMeshProUGUI enemyNameLabel;
    [SerializeField] private TextMeshProUGUI wildLevelLabel;
    [SerializeField] private TextMeshProUGUI rarityLabel;
    [SerializeField] private TextMeshProUGUI typeLabel;

    [Header("Battle")]
    [SerializeField] private TextMeshProUGUI turnsTakenLabel;
    [SerializeField] private TextMeshProUGUI damageDealtLabel;
    [SerializeField] private TextMeshProUGUI damageTakenLabel;
    [SerializeField] private TextMeshProUGUI critsLabel;
    [SerializeField] private TextMeshProUGUI firstHitLabel;
    [SerializeField] private TextMeshProUGUI timeLabel;

    [Header("Controls")]
    [SerializeField] private Button continueButton;

    [Header("Anim")]
    [SerializeField] private float fadeIn = 0.18f;
    [SerializeField] private float popIn = 0.22f;

    public Action OnClosed;

    [Header("Optional")]
    [SerializeField] private BattleManager battleManager;

    const string GREEN = "#3CDE74";

    private static readonly Dictionary<Rarity, Color> RARITY_COLORS = new()
    {
        { Rarity.Common,    new Color32(176,176,176,255) },
        { Rarity.Uncommon,  new Color32( 76,175, 80,255) },
        { Rarity.Rare,      new Color32( 33,150,243,255) },
        { Rarity.Epic,      new Color32(156, 39,176,255) },
        { Rarity.Legendary, new Color32(255,152,  0,255) },
        { Rarity.Mythic,    new Color32(255,235, 59,255) },
    };

    private static readonly Dictionary<MonsterType, Color> TYPE_COLORS = new()
    {
        { MonsterType.Fire,     new Color32(230, 74,  25,255) },
        { MonsterType.Water,    new Color32( 30,136, 229,255) },
        { MonsterType.Grass,    new Color32( 56,142,  60,255) },
        { MonsterType.Electric, new Color32(255,193,   7,255) },
        { MonsterType.Ice,      new Color32( 79,195, 247,255) },
        { MonsterType.Clash,    new Color32(121, 85,  72,255) },
        { MonsterType.Corrupt,  new Color32(156, 39, 176,255) },
        { MonsterType.Ground,   new Color32(141,110,  99,255) },
        { MonsterType.Sky,      new Color32( 63, 81, 181,255) },
        { MonsterType.Oracle,   new Color32(  0,150, 136,255) },
        { MonsterType.Bug,      new Color32(104,159,  56,255) },
        { MonsterType.Rock,     new Color32(120,144, 156,255) },
        { MonsterType.Specter,  new Color32(103, 58, 183,255) },
        { MonsterType.Wyrm,     new Color32(255,112,  67,255) },
        { MonsterType.Umbral,   new Color32( 97, 97,  97,255) },
        { MonsterType.Alloy,    new Color32(158,158, 158,255) },
    };

    void Awake()
    {
        if (!cg) cg = GetComponent<CanvasGroup>();
        if (!cg) cg = gameObject.AddComponent<CanvasGroup>();
        if (!root) root = transform as RectTransform;

        cg.alpha = 0f;
        cg.blocksRaycasts = false;
        cg.interactable = false;

        if (root)
            root.localScale = Vector3.one * 0.96f;

        if (continueButton)
            continueButton.onClick.AddListener(OnContinueClicked);
    }

    private void OnDestroy()
    {
        if (continueButton)
            continueButton.onClick.RemoveListener(OnContinueClicked);
    }

    private void OnContinueClicked()
    {
        // 1) Fade out this panel (and trigger your manager's OnClosed chain)
        Close();

        // 2) Ensure the panel root is actually hidden/disabled via UIManager
        //    (This uses your UIManager close animation and disables the root on completion.)
        UIManager.I?.Hide(PanelId.PostBattleSummary);
    }

    public void Set(
        BattleResult result,
        int growthCoresGained = 0,
        int monstersLeveledUp = 0,              // kept for compatibility (unused)
        bool captured = false,
        string capturedMonsterId = null,
        int capturedLevel = 0,
        List<string> levelUpSummaries = null,   // kept for compatibility (unused)
        int creditsBase = 0,
        int creditsTitleBonus = 0,
        int growthCoresBase = 0,
        int growthCoresTitleBonus = 0,
        List<string> growthCoresDetailLines = null // kept for compatibility (unused)
    )
    {
        // ───────────── Title ─────────────
        if (titleLabel)
            titleLabel.text = result.victory ? "Victory!" : "Defeat";

        // ───────────── credits ─────────────
        int creditsTotal = Mathf.Max(0, creditsBase + creditsTitleBonus);
        if (creditsLabel)
            creditsLabel.text = BuildRewardLine("credits", creditsBase, creditsTitleBonus, creditsTotal);

        // ───────────── Growth Cores ─────────────
        int coresTotal = Mathf.Max(0, growthCoresBase + growthCoresTitleBonus);
        if (growthCoresLabel)
        {
            if (growthCoresBase <= 0 && growthCoresTitleBonus <= 0 && growthCoresGained > 0)
            {
                // Legacy fallback: only total given
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

        // ───────────── Capture ─────────────
        if (captureLabel)
        {
            if (captured)
            {
                string name = !string.IsNullOrEmpty(capturedMonsterId)
                    ? capturedMonsterId
                    : (result.wildDef ? result.wildDef.id : "Unknown");

                int lvl = capturedLevel > 0 ? capturedLevel : Mathf.Max(1, result.wildLevel);
                captureLabel.text = $"Captured: {name} (Lv {lvl})";
            }
            else
            {
                captureLabel.text = "No capture";
            }
        }

        // ───────────── Center – Portrait & Basic Info ─────────────
        MonsterDataSO wildDef = result.wildDef;

        if (enemyPortraitImage)
        {
            if (wildDef && wildDef.icon)
            {
                enemyPortraitImage.enabled = true;
                enemyPortraitImage.sprite = wildDef.icon;
            }
            else
            {
                enemyPortraitImage.enabled = false;
            }
        }

        if (enemyNameLabel)
        {
            if (wildDef)
                enemyNameLabel.text = wildDef.displayName;
            else
                enemyNameLabel.text = "Unknown Foe";
        }

        if (wildLevelLabel)
        {
            int lvl = Mathf.Max(1, result.wildLevel);
            wildLevelLabel.text = $"Lv {lvl}";
        }

        // Rarity text + color
        if (rarityLabel)
        {
            if (wildDef)
            {
                Rarity rarity = wildDef.rarity;
                rarityLabel.text = rarity.ToString();

                if (RARITY_COLORS.TryGetValue(rarity, out var col))
                    rarityLabel.color = col;
                else
                    rarityLabel.color = Color.white;
            }
            else
            {
                rarityLabel.text = string.Empty;
            }
        }

        // Type text + color
        if (typeLabel)
        {
            if (wildDef)
            {
                MonsterType type = wildDef.type; // adjust field name if needed
                typeLabel.text = type.ToString();

                if (TYPE_COLORS.TryGetValue(type, out var col))
                    typeLabel.color = col;
                else
                    typeLabel.color = Color.white;
            }
            else
            {
                typeLabel.text = string.Empty;
            }
        }

        // ───────────── Right – Breakdown (individual TMP fields) ─────────────
        if (turnsTakenLabel)
            turnsTakenLabel.text = $"Turns Taken: {Mathf.Max(1, result.turnsSurvived)}";

        if (damageDealtLabel)
            damageDealtLabel.text = $"Damage Dealt: {Mathf.Max(0, result.damageDealt)}";

        if (damageTakenLabel)
            damageTakenLabel.text = $"Damage Taken: {Mathf.Max(0, result.damageTaken)}";

        if (critsLabel)
            critsLabel.text = $"Crits: {Mathf.Max(0, result.critCount)}";

        if (firstHitLabel)
            firstHitLabel.text = $"First Hit: {(result.gotFirstHit ? "Yes" : "No")}";

        if (timeLabel)
            timeLabel.text = $"Time: {FormatTime(result.secondsSurvived)}";
    }

    public void Show()
    {
        cg.blocksRaycasts = true;
        cg.interactable = true;

        LeanTween.alphaCanvas(cg, 1f, fadeIn).setEaseOutSine();
        if (root)
            LeanTween.scale(root, Vector3.one, popIn).setEaseOutBack();
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
        string multText = multiplier.ToString("0.##");

        return $"{label}: {baseValue} (x{multText}) = <color={GREEN}>{total}</color>";
    }

    private string FormatTime(float seconds)
    {
        int s = Mathf.Max(0, Mathf.RoundToInt(seconds));
        int m = s / 60;
        int r = s % 60;

        return m > 0 ? $"{m}:{r:00}" : $"{r}s";
    }
}
