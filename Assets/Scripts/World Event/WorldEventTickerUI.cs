using System.Collections;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// WorldEventTickerUI (Fade Mode)
///
/// Logic:
/// - If the World Events feature is unlocked:
///     - World Event Bar GameObject is active
///     - The message text fades in -> stays -> fades out -> waits 10 seconds -> repeats
/// - If the feature is locked:
///     - World Event Bar GameObject is inactive
///
/// Notes:
/// - Uses unscaled time (works regardless of Time.timeScale).
/// - Requires a CanvasGroup on the bar root (auto-added if missing).
/// - Assumes WorldEventManager provides an Items list with .message strings.
/// </summary>
public sealed class WorldEventTickerUI : MonoBehaviour
{
    [Header("Wiring")]
    [Tooltip("The root GameObject of the bar (background + text). This will be toggled active/inactive.")]
    [SerializeField] private GameObject worldEventBar;

    [Tooltip("TMP text that displays the message.")]
    [SerializeField] private TextMeshProUGUI messageText;

    [Tooltip("Optional icon image reference. Ticker is text-only, so this will be disabled when present.")]
    [SerializeField] private Image tickerIcon;

    [Tooltip("Optional hold detector used to show world event details on long-press.")]
    [SerializeField] private HoldTapDetector holdTapDetector;

    [Header("Timing")]
    [SerializeField, Min(0f)] private float fadeInSeconds = 0.35f;
    [SerializeField, Min(0f)] private float holdSeconds = 4.0f;
    [SerializeField, Min(0f)] private float fadeOutSeconds = 0.35f;

    [Tooltip("Wait after fade-out before repeating.")]
    [SerializeField, Min(0f)] private float waitSeconds = 10.0f;

    [Header("Colors")]
    [Tooltip("Color used for ticker messages that have a real gameplay effect.")]
    [SerializeField] private Color effectColor = new Color(0xDE / 255f, 0x99 / 255f, 0x53 / 255f, 1f);
    [Tooltip("Color used for flavor/no-effect ticker messages.")]
    [SerializeField] private Color defaultColor = Color.white;

    private CanvasGroup _barCanvasGroup;
    private Coroutine _loop;
    private int _messageIndex;
    private bool _featureChecked;
    private WorldEventManager.Item _currentItem;

    private void Awake()
    {
        if (!worldEventBar) worldEventBar = gameObject;
        DisableTickerIconIfPresent();
        EnsureCanvasGroup();
        EnsureHoldDetectorWiring();
        SetAlphaInstant(0f);
    }

    private void OnEnable()
    {
        DisableTickerIconIfPresent();
        EnsureCanvasGroup();
        EnsureHoldDetectorWiring();
        RefreshBarActive();

        if (FeatureUnlockManager.I != null)
            FeatureUnlockManager.I.OnFeatureUnlocked += HandleFeatureUnlocked;

        // Start/stop loop based on unlock state.
        if (IsFeatureUnlocked())
            StartLoopIfNeeded();
        else
            StopLoopAndHide();
    }

    private void OnDisable()
    {
        StopLoop();
        if (FeatureUnlockManager.I != null)
            FeatureUnlockManager.I.OnFeatureUnlocked -= HandleFeatureUnlocked;
    }

    private void HandleFeatureUnlocked(FeatureId feature)
    {
        if (!IsFeatureUnlocked()) return;
        if (worldEventBar && !worldEventBar.activeSelf)
            worldEventBar.SetActive(true);
        StartLoopIfNeeded();
    }

    private void Update()
    {
        // Only needed for the one-time case where FeatureUnlockManager is not ready
        // in OnEnable — once hooked, this early-outs immediately.
        if (_featureChecked) return;
        if (!IsFeatureUnlocked()) return;
        _featureChecked = true;
        if (worldEventBar && !worldEventBar.activeSelf)
            worldEventBar.SetActive(true);
        StartLoopIfNeeded();
    }

    // ─────────────────────────────────────────────────────────────
    // Core loop
    // ─────────────────────────────────────────────────────────────

    private void StartLoopIfNeeded()
    {
        if (_loop != null) return;
        _loop = StartCoroutine(FadeLoop());
    }

    private void StopLoopAndHide()
    {
        StopLoop();
        _currentItem = null;
        if (worldEventBar) worldEventBar.SetActive(false);
        SetAlphaInstant(0f);
    }

    private void StopLoop()
    {
        if (_loop != null)
        {
            StopCoroutine(_loop);
            _loop = null;
        }
    }

    private IEnumerator FadeLoop()
    {
        // Ensure bar is visible (active), but start transparent.
        if (worldEventBar && !worldEventBar.activeSelf)
            worldEventBar.SetActive(true);

        SetAlphaInstant(0f);

        while (true)
        {
            // Safety: if feature becomes locked while running, shut down.
            if (!IsFeatureUnlocked())
            {
                StopLoopAndHide();
                yield break;
            }

            // Pull next message (or keep hidden if none).
            var item = GetNextItemSafe();
            if (item == null || string.IsNullOrWhiteSpace(item.message))
            {
                _currentItem = null;
                SetAlphaInstant(0f);
                yield return null;
                continue;
            }

            _currentItem = item;

            if (messageText)
            {
                messageText.text = item.message;
                messageText.color = item.hasEffect ? effectColor : defaultColor;
                Canvas.ForceUpdateCanvases();
            }

            // Fade in
            yield return FadeTo(1f, fadeInSeconds);

            // Hold
            yield return WaitUnscaled(holdSeconds);

            // Fade out
            yield return FadeTo(0f, fadeOutSeconds);

            // Wait 10 seconds (or configured)
            yield return WaitUnscaled(waitSeconds);
        }
    }

    // ─────────────────────────────────────────────────────────────
    // Message selection
    // ─────────────────────────────────────────────────────────────

    private WorldEventManager.Item GetNextItemSafe()
    {
        var mgr = WorldEventManager.I;
        if (mgr == null || mgr.Items == null || mgr.Items.Count == 0)
            return null;

        // Cycle through items until we find a valid message, at most Count tries.
        int tries = 0;
        while (tries < mgr.Items.Count)
        {
            if (_messageIndex >= mgr.Items.Count) _messageIndex = 0;

            var it = mgr.Items[_messageIndex];
            _messageIndex++;

            if (it != null && !string.IsNullOrWhiteSpace(it.message))
                return it;

            tries++;
        }

        return null;
    }

    // ─────────────────────────────────────────────────────────────
    // Unlock gating
    // ─────────────────────────────────────────────────────────────

    private bool IsFeatureUnlocked()
    {
        // WorldEventSystem is your feature gate. If it's missing, treat as locked for safety.
        if (WorldEventSystem.I == null) return false;
        return WorldEventSystem.I.IsFeatureActive();
    }

    private void RefreshBarActive()
    {
        bool unlocked = IsFeatureUnlocked();
        if (worldEventBar) worldEventBar.SetActive(unlocked);
        if (!unlocked) SetAlphaInstant(0f);
    }

    // ─────────────────────────────────────────────────────────────
    // CanvasGroup helpers
    // ─────────────────────────────────────────────────────────────

    private void EnsureCanvasGroup()
    {
        if (!worldEventBar) worldEventBar = gameObject;

        if (worldEventBar)
        {
            _barCanvasGroup = worldEventBar.GetComponent<CanvasGroup>();
            if (_barCanvasGroup == null)
                _barCanvasGroup = worldEventBar.AddComponent<CanvasGroup>();
        }
        else
        {
            _barCanvasGroup = GetComponent<CanvasGroup>();
            if (_barCanvasGroup == null)
                _barCanvasGroup = gameObject.AddComponent<CanvasGroup>();
        }
    }

    private void SetAlphaInstant(float a)
    {
        if (_barCanvasGroup) _barCanvasGroup.alpha = Mathf.Clamp01(a);
    }

    private IEnumerator FadeTo(float targetAlpha, float seconds)
    {
        if (_barCanvasGroup == null)
        {
            yield break;
        }

        targetAlpha = Mathf.Clamp01(targetAlpha);

        if (seconds <= 0f)
        {
            _barCanvasGroup.alpha = targetAlpha;
            yield break;
        }

        float startAlpha = _barCanvasGroup.alpha;
        float t = 0f;

        while (t < seconds)
        {
            // If feature locks mid-fade, stop immediately.
            if (!IsFeatureUnlocked())
            {
                StopLoopAndHide();
                yield break;
            }

            t += Time.unscaledDeltaTime;
            float u = Mathf.Clamp01(t / seconds);
            _barCanvasGroup.alpha = Mathf.Lerp(startAlpha, targetAlpha, u);
            yield return null;
        }

        _barCanvasGroup.alpha = targetAlpha;
    }

    private static IEnumerator WaitUnscaled(float seconds)
    {
        if (seconds <= 0f) yield break;

        float t = 0f;
        while (t < seconds)
        {
            t += Time.unscaledDeltaTime;
            yield return null;
        }
    }

    private void DisableTickerIconIfPresent()
    {
        if (!tickerIcon) return;
        tickerIcon.enabled = false;
        tickerIcon.gameObject.SetActive(false);
    }

    private void EnsureHoldDetectorWiring()
    {
        if (holdTapDetector == null)
        {
            if (worldEventBar != null)
                holdTapDetector = worldEventBar.GetComponent<HoldTapDetector>();

            if (holdTapDetector == null)
                holdTapDetector = GetComponent<HoldTapDetector>();
        }

        if (holdTapDetector != null)
            holdTapDetector.SetCallbacks(onTap: HandleTickerTap, onHold: ShowCurrentEventTooltip);
    }

    private void HandleTickerTap()
    {
        if (TooltipUI.I == null) return;
        TooltipUI.I.Hide();
    }

    private void ShowCurrentEventTooltip()
    {
        if (TooltipUI.I == null) return;

        string tooltipText = BuildCurrentEventTooltipText();
        if (string.IsNullOrWhiteSpace(tooltipText)) return;

        TooltipUI.I.Show(tooltipText);
    }

    private string BuildCurrentEventTooltipText()
    {
        if (!IsFeatureUnlocked()) return null;

        var evt = FindEventForCurrentItem();
        if (evt == null)
        {
            if (WorldEventManager.I != null && WorldEventManager.I.TryGetWeeklyEventView(out var weeklyView))
            {
                var fallback = new StringBuilder();
                fallback.Append("<b>").Append(weeklyView.displayName).Append("</b>");
                if (!string.IsNullOrWhiteSpace(weeklyView.description))
                    fallback.Append("\n").Append(weeklyView.description.Trim());
                if (!string.IsNullOrWhiteSpace(weeklyView.countdownText))
                    fallback.Append("\n\nEnds in: ").Append(weeklyView.countdownText);
                return fallback.ToString();
            }

            return "No active world event details available.";
        }

        var sb = new StringBuilder();
        string title = string.IsNullOrWhiteSpace(evt.displayName) ? evt.id : evt.displayName;
        sb.Append("<b>").Append(title).Append("</b>");

        if (!string.IsNullOrWhiteSpace(evt.description))
            sb.Append("\n").Append(evt.description.Trim());

        string effectSummary = BuildEffectSummary(evt);
        if (!string.IsNullOrWhiteSpace(effectSummary))
            sb.Append("\n\n").Append(effectSummary);

        if (WorldEventManager.I != null && WorldEventManager.I.ActiveWeeklyEvent == evt)
        {
            string countdown = WorldEventManager.I.GetWeekCountdownText();
            if (!string.IsNullOrWhiteSpace(countdown))
                sb.Append("\n\nEnds in: ").Append(countdown);
        }

        return sb.ToString();
    }

    private WorldEventSO FindEventForCurrentItem()
    {
        var eventSystem = WorldEventSystem.I;
        if (eventSystem == null || eventSystem.ActiveEvents == null || eventSystem.ActiveEvents.Count == 0)
            return null;

        string currentMessage = _currentItem?.message;
        if (!string.IsNullOrWhiteSpace(currentMessage))
        {
            string needle = currentMessage.Trim();
            for (int i = 0; i < eventSystem.ActiveEvents.Count; i++)
            {
                var evt = eventSystem.ActiveEvents[i];
                if (!evt) continue;

                string tickerMessage = !string.IsNullOrWhiteSpace(evt.tickerMessage)
                    ? evt.tickerMessage.Trim()
                    : (!string.IsNullOrWhiteSpace(evt.displayName) ? evt.displayName.Trim() : (evt.id ?? string.Empty).Trim());

                if (string.Equals(tickerMessage, needle, System.StringComparison.Ordinal))
                    return evt;
            }
        }

        return eventSystem.ActiveEvents[0];
    }

    private static string BuildEffectSummary(WorldEventSO evt)
    {
        var sb = new StringBuilder();
        bool any = false;

        // ── structured effects list ─────────────────────────────────────────────
        if (evt.effects != null && evt.effects.Count > 0)
        {
            // ResourceGainMultiplier — aggregate into a single line when all share the same value.
            AppendResourceGainLines(sb, ref any, evt.effects);

            // BoostedMonsterType / TypeDamageMultiplier from effects list — collect first type found.
            MonsterType boostedFromEffects = MonsterType.None;
            float typeDmgFromEffects = 1f;

            for (int i = 0; i < evt.effects.Count; i++)
            {
                var fx = evt.effects[i];
                switch (fx.kind)
                {
                    case WorldEventEffectKind.DisableJobSite:
                        if (fx.job != JobType.None)
                            AppendPlainLine(sb, ref any, "- " + FormatJobName(fx.job) + " job site disabled");
                        break;

                    case WorldEventEffectKind.JobRateMultiplier:
                        if (fx.job != JobType.None)
                            AppendMultiplierLine(sb, ref any, FormatJobName(fx.job) + " output", fx.value);
                        break;

                    case WorldEventEffectKind.JobStorageCapMultiplier:
                        if (fx.job != JobType.None)
                            AppendMultiplierLine(sb, ref any, FormatJobName(fx.job) + " storage cap", fx.value);
                        break;

                    case WorldEventEffectKind.JobCollectDisabled:
                        if (fx.job != JobType.None && (fx.flag || fx.value > 0f))
                            AppendPlainLine(sb, ref any, "- " + FormatJobName(fx.job) + " collection disabled");
                        break;

                    case WorldEventEffectKind.JobFatigueRateMultiplier:
                        if (fx.job != JobType.None)
                            AppendMultiplierLine(sb, ref any, FormatJobName(fx.job) + " fatigue rate", fx.value);
                        break;

                    case WorldEventEffectKind.DisableRifts:
                        AppendPlainLine(sb, ref any, "- Rift Operations disabled");
                        break;

                    case WorldEventEffectKind.RiftEnergyCostMultiplier:
                        AppendMultiplierLine(sb, ref any, "Rift Energy cost", fx.value);
                        break;

                    case WorldEventEffectKind.WildPremiumChanceMultiplier:
                        AppendMultiplierLine(sb, ref any, "Premium encounter chance", fx.value);
                        break;

                    case WorldEventEffectKind.BossCadenceMultiplier:
                        AppendMultiplierLine(sb, ref any, "Boss encounter cadence", fx.value);
                        break;

                    case WorldEventEffectKind.ShopPriceMultiplier:
                        AppendMultiplierLine(sb, ref any, "Shop prices", fx.value);
                        break;

                    case WorldEventEffectKind.ExchangeDemandMultiplier:
                        AppendMultiplierLine(sb, ref any, "Exchange demand", fx.value);
                        break;

                    case WorldEventEffectKind.ExchangeValueMultiplier:
                        AppendMultiplierLine(sb, ref any, "Exchange value", fx.value);
                        break;

                    case WorldEventEffectKind.IdleRewardMultiplier:
                        AppendMultiplierLine(sb, ref any, "Idle rewards", fx.value);
                        break;

                    case WorldEventEffectKind.BattleRewardMultiplier:
                        AppendMultiplierLine(sb, ref any, "Battle rewards", fx.value);
                        break;

                    case WorldEventEffectKind.BoostedMonsterType:
                        if (boostedFromEffects == MonsterType.None)
                            boostedFromEffects = fx.monsterType;
                        break;

                    case WorldEventEffectKind.TypeDamageMultiplier:
                        if (!Mathf.Approximately(fx.value, 1f))
                            typeDmgFromEffects = fx.value;
                        break;

                    // ResourceGainMultiplier handled above by AppendResourceGainLines.
                }
            }

            if (boostedFromEffects != MonsterType.None)
            {
                if (any) sb.Append("\n");
                sb.Append("- ").Append(boostedFromEffects).Append(" damage ").Append(FormatMultiplierChange(typeDmgFromEffects));
                any = true;
            }
        }

        // ── flat modifier fields ────────────────────────────────────────────────
        // Used by fallback (BuiltInFallbackEvents) and CSV flat columns.
        AppendMultiplierLine(sb, ref any, "Idle rewards", evt.idleRewardMultiplier);
        AppendMultiplierLine(sb, ref any, "Battle rewards", evt.battleRewardMultiplier);
        AppendMultiplierLine(sb, ref any, "Exchange value", evt.exchangeValueMultiplier);

        if (evt.boostedMonsterType != MonsterType.None)
        {
            if (any) sb.Append("\n");
            sb.Append("- ").Append(evt.boostedMonsterType).Append(" damage ").Append(FormatMultiplierChange(evt.typeDamageMultiplier));
            any = true;
        }

        if (!any)
            return "No gameplay modifiers. Flavor event only.";

        return sb.ToString();
    }

    /// <summary>
    /// Aggregates all ResourceGainMultiplier entries. Shows a single grouped line when all entries
    /// share the same multiplier value; otherwise shows one line per resource type.
    /// </summary>
    private static void AppendResourceGainLines(StringBuilder sb, ref bool any, List<WorldEventEffect> effects)
    {
        // Collect resource gain entries.
        float groupValue = 0f;
        bool first = true;
        bool allSame = true;
        int count = 0;
        ResourceType singleResource = ResourceType.None;

        for (int i = 0; i < effects.Count; i++)
        {
            var fx = effects[i];
            if (fx.kind != WorldEventEffectKind.ResourceGainMultiplier) continue;

            count++;
            singleResource = fx.resource;

            if (first)
            {
                groupValue = fx.value;
                first = false;
            }
            else if (!Mathf.Approximately(fx.value, groupValue))
            {
                allSame = false;
            }
        }

        if (count == 0) return;

        if (count == 1)
        {
            string label = singleResource != ResourceType.None
                ? singleResource.ToString().Replace("_", " ") + " gains"
                : "Resource gains";
            AppendMultiplierLine(sb, ref any, label, groupValue);
            return;
        }

        if (allSame)
        {
            // Multiple resources all with the same multiplier — show a single grouped line.
            AppendMultiplierLine(sb, ref any, "Resource gains", groupValue);
            return;
        }

        // Different multipliers — show per-resource.
        for (int i = 0; i < effects.Count; i++)
        {
            var fx = effects[i];
            if (fx.kind != WorldEventEffectKind.ResourceGainMultiplier) continue;
            string label = fx.resource != ResourceType.None
                ? fx.resource.ToString().Replace("_", " ") + " gains"
                : "Resource gains";
            AppendMultiplierLine(sb, ref any, label, fx.value);
        }
    }

    private static void AppendPlainLine(StringBuilder sb, ref bool any, string text)
    {
        if (any) sb.Append("\n");
        sb.Append(text);
        any = true;
    }

    private static void AppendMultiplierLine(StringBuilder sb, ref bool any, string label, float multiplier)
    {
        if (Mathf.Approximately(multiplier, 1f)) return;

        if (any) sb.Append("\n");
        sb.Append("- ").Append(label).Append(" ").Append(FormatMultiplierChange(multiplier));
        any = true;
    }

    private static string FormatMultiplierChange(float multiplier)
    {
        float deltaPercent = (multiplier - 1f) * 100f;
        string sign = deltaPercent >= 0f ? "+" : string.Empty;
        return $"{sign}{deltaPercent:0.#}%";
    }

    private static string FormatJobName(JobType job)
    {
        return job.ToString().Replace("_", " ");
    }
}
