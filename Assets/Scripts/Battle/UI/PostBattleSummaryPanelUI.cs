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

    [Header("Level Ups")]
    [Tooltip("Optional: shows the provided level-up summary lines.")]
    [SerializeField] private TextMeshProUGUI levelUpsLabel;

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

    [Header("Key Moments (Optional)")]
    [Tooltip("Optional: shows the last ~20 key battle events (crits, KOs, swaps).")]
    [SerializeField] private TextMeshProUGUI keyMomentsLabel;

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

    private void OnContinueClicked()
    {
        EncounterPanelUI.I?.ForceBlinderAlphaToOne();

        // CLEANUP REQUEST:
        // If defeated, go to Home after continuing.
        if (_lastResult.HasValue && !_lastResult.Value.victory && !_lastResult.Value.escaped)
        {
            UIManager.I?.Show(PanelId.Home);
            // Also close the Encounter panel so the player isn't left in the encounter flow
            // with a fully KO'd team.
            UIManager.I?.Hide(PanelId.Encounter);
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

    private bool IsAutoBattleMode() => EncounterManager.I && EncounterManager.I.IsAutoMode;

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
    // UPDATED: capturedShiny added as 7th argument
    // ─────────────────────────────────────────────
    public void Set(
        BattleResult result,
        int growthCoresGained = 0,
        int monstersLeveledUp = 0,
        bool captured = false,
        string capturedMonsterId = null,
        int capturedLevel = 0,
        bool capturedShiny = false,                  // NEW
        List<string> levelUpSummaries = null,
        int creditsBase = 0,
        int creditsTitleBonus = 0,
        int growthCoresBase = 0,
        int growthCoresTitleBonus = 0,
        List<string> growthCoresDetailLines = null,
        bool wildWasShiny = false
    )
    {
        _lastResult = result;

        bool effectiveShiny = capturedShiny || wildWasShiny;

        if (titleLabel)
            titleLabel.text = result.victory ? "Victory!" : "Defeat";

        // ─────────────────────────────────────────────
        // Rewards: hide empty lines + micro-juice (count-up + stagger)
        // ─────────────────────────────────────────────
        float delay = Mathf.Max(0f, revealStartDelay);

        int creditsTotal = Mathf.Max(0, creditsBase + creditsTitleBonus);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        Debug.Log($"[PostBattleSummaryPanelUI] Credits: base={creditsBase}, bonus={creditsTitleBonus}, total={creditsTotal}");
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

        // Capture line (UPDATED: wraps shiny name in *)
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

                string name = MonsterNameFormatter.Format(baseName, effectiveShiny);

                int lvl = capturedLevel > 0 ? capturedLevel : Mathf.Max(1, result.wildLevel);
                captureLabel.text = $"Captured: {name} (Lv {lvl})";

                delay += revealStepDelay;
            }
        }

        // Level ups (delta-only): render the provided summary lines if present.
        if (levelUpsLabel)
        {
            if (levelUpSummaries == null || levelUpSummaries.Count == 0)
            {
                HideRow(levelUpsLabel);
            }
            else
            {
                ShowRowDelayed(levelUpsLabel, delay);

                var sb = new System.Text.StringBuilder(256);
                sb.AppendLine("Level Ups:");
                for (int i = 0; i < levelUpSummaries.Count; i++)
                {
                    if (string.IsNullOrWhiteSpace(levelUpSummaries[i])) continue;
                    sb.AppendLine($"• {levelUpSummaries[i]}");
                }
                levelUpsLabel.text = sb.ToString();

                // Tiny pop to make it feel like a meaningful change.
                LeanTween.delayedCall(levelUpsLabel.gameObject, delay, () =>
                {
                    if (!levelUpsLabel) return;
                    var rt = levelUpsLabel.rectTransform;
                    rt.localScale = Vector3.one;
                    LeanTween.scale(rt, Vector3.one * 1.04f, 0.12f).setLoopPingPong(1).setEase(LeanTweenType.easeOutBack);
                });

                delay += revealStepDelay;
            }
        }

        MonsterDataSO wildDef = result.wildDef;

        if (enemyPortraitImage)
        {
            Sprite portrait = GetBestPortraitSprite(wildDef, effectiveShiny);
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
            enemyNameLabel.text = wildDef ? wildDef.displayName : "Unknown Foe";

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
                typeLabel.color = TYPE_COLORS.TryGetValue(type, out var col) ? col : Color.white;
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

        // Key moments (debug + UX)
        if (keyMomentsLabel)
        {
            var km = BattleLogger.GetKeyMomentsSnapshot(20);
            if (km != null && km.Count > 0)
            {
                // bullet-ish formatting without rich text dependency
                var sb = new System.Text.StringBuilder(512);
                sb.AppendLine("Key Moments:");
                for (int i = 0; i < km.Count; i++)
                    sb.AppendLine($"• {km[i]}");
                keyMomentsLabel.text = sb.ToString();
            }
            else
            {
                keyMomentsLabel.text = "Key Moments:\n• (none)";
            }
        }
    }

    private Sprite GetBestPortraitSprite(MonsterDataSO def, bool shiny)
    {
        if (!def) return null;

        if (shiny && def.shinyIcon != null)
            return def.shinyIcon;

        return def.icon;
    }

}
