using System.Collections;
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

    private void Awake()
    {
        if (!worldEventBar) worldEventBar = gameObject;
        DisableTickerIconIfPresent();
        EnsureCanvasGroup();
        SetAlphaInstant(0f);
    }

    private void OnEnable()
    {
        DisableTickerIconIfPresent();
        EnsureCanvasGroup();
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
                SetAlphaInstant(0f);
                yield return null;
                continue;
            }

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
}
