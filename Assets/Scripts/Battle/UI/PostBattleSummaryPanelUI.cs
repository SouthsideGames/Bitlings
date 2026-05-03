using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;
using System.Collections.Generic;

public class PostBattleSummaryPanelUI : MonoBehaviour
{
    [Header("Refs")]
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

    [Header("Promotion Rank XP")]
    [Tooltip("Optional: root container for the rank XP section. If null, labels/sliders are still updated if assigned.")]
    [SerializeField] private GameObject promotionSectionRoot;

    [Tooltip("Optional: e.g., 'Rank 7'")]
    [SerializeField] private TextMeshProUGUI promotionRankLabel;

    [Tooltip("Optional: e.g., 'XP: 120 / 175 (to Rank 8)'")]
    [SerializeField] private TextMeshProUGUI promotionProgressLabel;

    [Tooltip("Optional: e.g., '+18 XP this battle'")]
    [SerializeField] private TextMeshProUGUI promotionDeltaLabel;

    [Tooltip("Optional: slider showing progress within current rank.")]
    [SerializeField] private Slider promotionProgressSlider;

    [Header("Controls")]
    [SerializeField] private Button continueButton;

    public Action OnClosed;

    [Header("Optional")]
    [SerializeField] private BattleManager battleManager;

    const string GREEN = "#3CDE74";

    [Header("Micro-Juice")]
    [SerializeField] private float revealStartDelay = 0.05f;
    [SerializeField] private float revealStepDelay = 0.08f;
    [SerializeField] private float countUpDuration = 0.35f;

    private bool _hasClosed;
    private BattleResult? _lastResult;


    private static readonly Dictionary<Rarity, Color> RARITY_COLORS = new()
    {
        { Rarity.Common,    new Color32(176,176,176,255) },
        { Rarity.Uncommon,  new Color32( 76,175, 80,255) },
        { Rarity.Rare,      new Color32( 33,150,243,255) },
        { Rarity.Epic,      new Color32(156, 39,176,255) },
        { Rarity.Legendary, new Color32(255,152,  0,255) },
        { Rarity.Mythic,    new Color32(255,235, 59,255) },
    };



    void Awake()
    {
        if (!root) root = transform as RectTransform;

        _hasClosed = false;
        gameObject.SetActive(false);

        if (continueButton)
            continueButton.onClick.AddListener(OnContinueClicked);
    }

    private void OnDestroy()
    {
        if (continueButton)
            continueButton.onClick.RemoveListener(OnContinueClicked);
    }

    private void OnDisable()
    {
        ResetTweenState();
    }

    private void OnApplicationPause(bool paused)
    {
        if (paused)
            ResetTweenState();
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        if (!hasFocus)
            ResetTweenState();
    }

    private void OnContinueClicked()
    {
        RiftPanelUI.I?.ForceBlinderAlphaToOne();

        // If defeated, go to Home after continuing.
        if (_lastResult.HasValue && !_lastResult.Value.victory && !_lastResult.Value.escaped)
        {
            UIManager.I?.Show(PanelId.Home);
            // Also close the Rift panel so the player isn't left in the rift flow
            // with a fully KO'd team.
            UIManager.I?.Hide(PanelId.Rift);
        }

        Close();
    }

    public void Show()
    {
        _hasClosed = false;

        if (!gameObject.activeSelf)
            gameObject.SetActive(true);

        if (root)
        {
            LeanTween.cancel(root);
            root.localScale = Vector3.one;
        }
    }

    public void Close()
    {
        if (_hasClosed) return;
        _hasClosed = true;

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

        OnClosed?.Invoke();
        UIManager.I?.Hide(PanelId.PostBattleSummary);
    }

    private bool IsAutoBattleMode() => RiftManager.I && RiftManager.I.IsAutoMode;

    private void ResetTweenState()
    {
        if (root)
        {
            LeanTween.cancel(root);
            root.localScale = Vector3.one;
        }

        ResetLabelTween(titleLabel);
        ResetLabelTween(creditsLabel);
        ResetLabelTween(growthCoresLabel);
        ResetLabelTween(captureLabel);

        ResetLabelTween(enemyNameLabel);
        ResetLabelTween(wildLevelLabel);
        ResetLabelTween(rarityLabel);
        ResetLabelTween(typeLabel);

        ResetLabelTween(turnsTakenLabel);
        ResetLabelTween(damageDealtLabel);
        ResetLabelTween(damageTakenLabel);
        ResetLabelTween(critsLabel);
        ResetLabelTween(firstHitLabel);
        ResetLabelTween(timeLabel);

        ResetLabelTween(promotionRankLabel);
        ResetLabelTween(promotionProgressLabel);
        ResetLabelTween(promotionDeltaLabel);
    }

    private static void ResetLabelTween(TextMeshProUGUI label)
    {
        if (!label) return;

        LeanTween.cancel(label.gameObject);
        label.rectTransform.localScale = Vector3.one;
    }

    private void CancelRowTweens(GameObject row)
    {
        if (!row) return;
        LeanTween.cancel(row);
        var rt = row.transform as RectTransform;
        if (rt) rt.localScale = Vector3.one;
    }

    private void HideRow(TextMeshProUGUI label)
    {
        if (!label) return;
        CancelRowTweens(label.gameObject);
        label.gameObject.SetActive(false);
    }

    private void ShowRowDelayed(TextMeshProUGUI label, float delaySeconds)
    {
        if (!label) return;
        CancelRowTweens(label.gameObject);
        label.gameObject.SetActive(false);

        LeanTween.delayedCall(label.gameObject, Mathf.Max(0f, delaySeconds), () =>
        {
            if (!label) return;
            label.gameObject.SetActive(true);
            // subtle appear pop
            var rt = label.rectTransform;
            rt.localScale = Vector3.one;
            LeanTween.scale(rt, Vector3.one * 1.03f, 0.10f).setLoopPingPong(1).setEase(LeanTweenType.easeOutBack);
        });
    }

    private void AnimateCountUp(TextMeshProUGUI label, int from, int to, float duration, Func<int, string> textBuilder)
    {
        if (!label) return;

        from = Mathf.Max(0, from);
        to = Mathf.Max(0, to);
        duration = Mathf.Max(0.01f, duration);

        // Ensure active before we animate.
        label.gameObject.SetActive(true);

        LeanTween.cancel(label.gameObject);
        LeanTween.value(label.gameObject, from, to, duration)
            .setEase(LeanTweenType.easeOutQuad)
            .setOnUpdate((float v) =>
            {
                if (!label) return;
                label.text = textBuilder(Mathf.RoundToInt(v));
            })
            .setOnComplete(() =>
            {
                if (!label) return;
                label.text = textBuilder(to);
            });
    }

    private void PunchIfBonus(TextMeshProUGUI label)
    {
        if (!label) return;
        var rt = label.rectTransform;
        LeanTween.cancel(rt.gameObject);
        rt.localScale = Vector3.one;
        LeanTween.scale(rt, Vector3.one * 1.08f, 0.12f).setLoopPingPong(1).setEase(LeanTweenType.easeOutBack);
    }

    private string BuildRewardLine(string label, int baseValue, int titleBonus, int total)
    {
        if (titleBonus <= 0 || baseValue <= 0)
        {
            int display = Mathf.Max(baseValue, total);
            return $"{label}: {Mathf.Max(0, display)}";
        }

        // For currency (credits/coins) show total with an explicit +Bonus.
        // Example: "Credits: 40 (+12 Bonus)".
        if (string.Equals(label, "coins", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(label, "credits", StringComparison.OrdinalIgnoreCase))
        {
            return $"{label}: {Mathf.Max(0, total)} (<color={GREEN}>+{Mathf.Max(0, titleBonus)} Bonus</color>)";
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

    // ─────────────────────────────────────────────
    // UPDATED: capturedPremium added as 7th argument
    // ─────────────────────────────────────────────
    public void Set(
        BattleResult result,
        int growthCoresGained = 0,
        int monstersLeveledUp = 0,
        bool captured = false,
        string capturedMonsterId = null,
        int capturedLevel = 0,
        bool capturedPremium = false,                  // NEW
        List<string> levelUpSummaries = null,
        int creditsBase = 0,
        int creditsTitleBonus = 0,
        int growthCoresBase = 0,
        int growthCoresTitleBonus = 0,
        List<string> growthCoresDetailLines = null,
        bool wildWasPremium = false
    )
    {
        _lastResult = result;

        bool effectivePremium = capturedPremium || wildWasPremium;

        if (titleLabel)
            titleLabel.text = result.victory ? "VICTORY!" : "DEFEAT";

        // ─────────────────────────────────────────────
        // Rewards: hide empty lines + micro-juice (count-up + stagger)
        // ─────────────────────────────────────────────
        float delay = Mathf.Max(0f, revealStartDelay);

        int creditsTotal = Mathf.Max(0, creditsBase + creditsTitleBonus);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        DevLog.Log($"[PostBattleSummaryPanelUI] Credits: base={creditsBase}, bonus={creditsTitleBonus}, total={creditsTotal}");
#endif

        if (creditsLabel)
        {
            if (creditsTotal <= 0)
            {
                HideRow(creditsLabel);
            }
            else
            {
                ShowRowDelayed(creditsLabel, delay);

                int bonus = Mathf.Max(0, creditsTitleBonus);
                Func<int, string> builder = (val) =>
                    bonus > 0
                        ? $"Credits: {val} (<color={GREEN}>+{bonus} Bonus</color>)"
                        : $"Credits: {val}";

                // Start at 0 for punchier feel.
                LeanTween.delayedCall(creditsLabel.gameObject, delay, () =>
                {
                    AnimateCountUp(creditsLabel, 0, creditsTotal, countUpDuration, builder);
                    if (bonus > 0) PunchIfBonus(creditsLabel);
                });

                delay += revealStepDelay;
            }
        }

        int coresTotal = Mathf.Max(0, growthCoresBase + growthCoresTitleBonus);
        int coresDisplay = (growthCoresBase <= 0 && growthCoresTitleBonus <= 0 && growthCoresGained > 0)
            ? Mathf.Max(0, growthCoresGained)
            : coresTotal;

        if (growthCoresLabel)
        {
            if (coresDisplay <= 0)
            {
                HideRow(growthCoresLabel);
            }
            else
            {
                ShowRowDelayed(growthCoresLabel, delay);

                int bonus = Mathf.Max(0, growthCoresTitleBonus);
                Func<int, string> builder = (val) =>
                    bonus > 0
                        ? $"Growth Cores: {val} (<color={GREEN}>+{bonus} Bonus</color>)"
                        : $"Growth Cores: {val}";

                LeanTween.delayedCall(growthCoresLabel.gameObject, delay, () =>
                {
                    AnimateCountUp(growthCoresLabel, 0, coresDisplay, countUpDuration, builder);
                    if (bonus > 0) PunchIfBonus(growthCoresLabel);
                });

                delay += revealStepDelay;
            }
        }

        // Capture line (UPDATED: wraps premium name in *)
        if (captureLabel)
        {
            if (!captured)
            {
                // QoL: hide the line entirely if no capture.
                HideRow(captureLabel);
            }
            else
            {
                ShowRowDelayed(captureLabel, delay);

                // Use display name, but still fall back safely if needed.
                string baseName = (result.wildDef != null)
                    ? MonsterNameFormatter.GetDisplayName(result.wildDef)
                    : (!string.IsNullOrEmpty(capturedMonsterId) ? capturedMonsterId : "Unknown");

                string name = MonsterNameFormatter.Format(baseName, effectivePremium);

                int lvl = capturedLevel > 0 ? capturedLevel : Mathf.Max(1, result.wildLevel);
                captureLabel.text = $"Captured: {name} (Lv {lvl})";

                delay += revealStepDelay;
            }
        }


        MonsterDataSO wildDef = result.wildDef;

        if (enemyPortraitImage)
        {
            Sprite portrait = GetBestPortraitSprite(wildDef, effectivePremium);
            if (portrait != null)
            {
                enemyPortraitImage.enabled = true;
                enemyPortraitImage.sprite = portrait;
            }
            else
            {
                enemyPortraitImage.enabled = false;
                enemyPortraitImage.sprite = null;
            }
        }

        if (enemyNameLabel)
        {
            if (wildDef)
            {
                string baseName = MonsterNameFormatter.GetDisplayName(wildDef);
                enemyNameLabel.text = MonsterNameFormatter.Format(baseName, effectivePremium);
            }
            else
            {
                enemyNameLabel.text = "Unknown Foe";
            }
        }

        if (wildLevelLabel)
            wildLevelLabel.text = $"Lv {Mathf.Max(1, result.wildLevel)}";

        if (rarityLabel)
        {
            if (wildDef)
            {
                Rarity rarity = wildDef.rarity;
                rarityLabel.text = rarity.ToString();
                rarityLabel.color = RARITY_COLORS.TryGetValue(rarity, out var col) ? col : Color.white;
            }
            else
            {
                rarityLabel.text = string.Empty;
            }
        }

        if (typeLabel)
        {
            if (wildDef)
            {
                MonsterType type = wildDef.type;
                typeLabel.text = type.ToString();
                typeLabel.color = TypeColorLibrary.Get(type);
            }
            else
            {
                typeLabel.text = string.Empty;
            }
        }

        if (turnsTakenLabel) turnsTakenLabel.text = $"Turns Taken: {Mathf.Max(1, result.turnsSurvived)}";
        if (damageDealtLabel) damageDealtLabel.text = $"Damage Dealt: {Mathf.Max(0, result.damageDealt)}";
        if (damageTakenLabel) damageTakenLabel.text = $"Damage Taken: {Mathf.Max(0, result.damageTaken)}";
        if (critsLabel) critsLabel.text = $"Crits: {Mathf.Max(0, result.critCount)}";
        if (firstHitLabel) firstHitLabel.text = $"First Hit: {(result.gotFirstHit ? "Yes" : "No")}";
        if (timeLabel) timeLabel.text = $"Time: {FormatTime(result.secondsSurvived)}";

        // ─────────────────────────────────────────────
        // Promotion XP (Phase 5)
        // Shows current rank + progress to next rank + XP gained from this battle.
        // NOTE: PromotionManager should have already applied XP via GameEvents.BattleFinished.
        // ─────────────────────────────────────────────
        if (promotionSectionRoot != null)
            promotionSectionRoot.SetActive(SaveManager.Data != null);

        if (SaveManager.Data != null)
        {
            int maxRank = PromotionManager.I != null ? PromotionManager.I.GetMaxRank() : 20;
            int rank = Mathf.Clamp(SaveManager.Data.promotionRank, 1, Mathf.Max(1, maxRank));
            int totalXp = Mathf.Max(0, SaveManager.Data.promotionXP);
            int delta = 0;

            if (PromotionManager.I != null)
                delta = Mathf.Max(0, PromotionManager.I.ComputeXpGain(result));

            bool atMaxRank = rank >= maxRank;
            if (atMaxRank)
                delta = 0;

            if (promotionRankLabel)
                promotionRankLabel.text = $"Rank {rank}";

            if (PromotionManager.I != null)
            {
                int curFloor = PromotionManager.I.GetTotalXpToReach(rank);
                int nextFloor = atMaxRank ? curFloor : PromotionManager.I.GetTotalXpToReach(rank + 1);

                int inRank = Mathf.Max(0, totalXp - curFloor);
                int toNext = Mathf.Max(0, nextFloor - curFloor);

                if (promotionProgressLabel)
                {
                    if (toNext <= 0)
                        promotionProgressLabel.text = "Max Rank";
                    else
                        promotionProgressLabel.text = $"XP: {inRank} / {toNext} (to Rank {rank + 1})";
                }

                if (promotionProgressSlider)
                {
                    promotionProgressSlider.minValue = 0f;
                    if (atMaxRank)
                    {
                        promotionProgressSlider.maxValue = 1f;
                        promotionProgressSlider.value = 1f;
                    }
                    else
                    {
                        promotionProgressSlider.maxValue = Mathf.Max(1f, toNext);
                        promotionProgressSlider.value = Mathf.Clamp(inRank, 0, Mathf.Max(1, toNext));
                    }
                }
            }
            else
            {
                // Fallback if PromotionManager isn't in the scene yet.
                if (promotionProgressLabel)
                    promotionProgressLabel.text = $"XP: {totalXp}";
                if (promotionProgressSlider)
                {
                    promotionProgressSlider.minValue = 0f;
                    promotionProgressSlider.maxValue = 1f;
                    promotionProgressSlider.value = 1f;
                }
            }

            if (promotionDeltaLabel)
                promotionDeltaLabel.text = delta > 0 ? $"<color={GREEN}>+{delta} XP</color>" : string.Empty;
        }
        else
        {
            if (promotionRankLabel) promotionRankLabel.text = string.Empty;
            if (promotionProgressLabel) promotionProgressLabel.text = string.Empty;
            if (promotionDeltaLabel) promotionDeltaLabel.text = string.Empty;
            if (promotionProgressSlider)
            {
                promotionProgressSlider.minValue = 0f;
                promotionProgressSlider.maxValue = 1f;
                promotionProgressSlider.value = 0f;
            }
        }

       
    }

    private Sprite GetBestPortraitSprite(MonsterDataSO def, bool premium)
    {
        if (!def) return null;

        if (premium && def.premiumIcon != null)
            return def.premiumIcon;

        return def.icon;
    }

}
