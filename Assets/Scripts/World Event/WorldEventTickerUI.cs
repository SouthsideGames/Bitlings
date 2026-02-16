using System;
using System.Collections;
using TMPro;
using UnityEngine;

/// <summary>
/// TV-style scrolling ticker.
///
/// IMPORTANT: This component should live on an always-active GameObject.
/// It toggles barRoot active/inactive based on whether there is content.
/// </summary>
public sealed class WorldEventTickerUI : MonoBehaviour
{
    [Header("Wiring")]
    [SerializeField] private GameObject barRoot;
    [SerializeField] private RectTransform viewportRect;
    [SerializeField] private TextMeshProUGUI messageText;
    [SerializeField] private RectTransform messageRect;

    [Header("Behavior")]
    [SerializeField] private bool onlyShowOnHome = false;

    [Tooltip("Units per second")]
    [SerializeField] private float scrollSpeed = 220f;

    [SerializeField] private float edgePadding = 40f;
    [SerializeField] private float pauseAtStartSeconds = 0.25f;
    [SerializeField] private float pauseAtEndSeconds = 0.15f;

    [Header("Loop Timing")]
    [SerializeField] private float loopDelaySeconds = 3f;
    [SerializeField] private float betweenMessageDelay = 1.5f;

    private Coroutine _loop;

    private bool _subscribed;

    // Cached fallback home root (for projects where UIManager.IsOpen(PanelId.Home) isn't authoritative).
    private GameObject _cachedHomePanel;

    private void Awake()
    {
        if (!barRoot) barRoot = gameObject;
        if (!messageRect && messageText) messageRect = messageText.rectTransform;
    }

    private void OnEnable()
    {
        TryHookFeed();
        HandleChanged();
    }

    private void Update()
    {
        // Execution-order safety:
        // If this component enables before WorldEventManager exists, we won't get events.
        // Keep trying to hook until it appears.
        if (!_subscribed)
            TryHookFeed();
    }

    private void OnDisable()
    {
        UnhookFeed();
        StopLoop();
    }

    private void TryHookFeed()
    {
        if (_subscribed) return;

        var feed = WorldEventManager.I;
        if (feed == null) return;

        feed.Changed += HandleChanged;
        _subscribed = true;
    }

    private void UnhookFeed()
    {
        if (!_subscribed) return;
        if (WorldEventManager.I != null)
            WorldEventManager.I.Changed -= HandleChanged;
        _subscribed = false;
    }

    private void HandleChanged()
    {
        RefreshVisibility();

        if (!barRoot || !barRoot.activeSelf)
        {
            StopLoop();
            return;
        }

        if (_loop == null)
            _loop = StartCoroutine(Loop());
    }

    private void RefreshVisibility()
    {
        // Feature gate: if the World Events feature is locked, hide the bar.
        if (WorldEventSystem.I != null && !WorldEventSystem.I.IsFeatureActive())
        {
            if (barRoot) barRoot.SetActive(false);
            return;
        }

        bool hasFeed = WorldEventManager.I != null && WorldEventManager.I.Items != null && WorldEventManager.I.Items.Count > 0;

        if (onlyShowOnHome)
        {
            // Safe check: UIManager may not exist in all scenes.
            bool onHome = false;

            if (UIManager.I != null)
            {
                // Depending on your panel stack rules, Home may remain active while sub-panels open.
                // We'll accept UIManager's state when it reports true.
                onHome = UIManager.I.IsOpen(PanelId.Home);
            }

            // Fallback: if UIManager isn't authoritative, detect by actual Home panel GameObject state.
            if (!onHome)
            {
                if (!_cachedHomePanel)
                    _cachedHomePanel = GameObject.Find("Panel_Home");

                if (_cachedHomePanel)
                    onHome = _cachedHomePanel.activeInHierarchy;
            }

            hasFeed = hasFeed && onHome;
        }

        if (barRoot)
            barRoot.SetActive(hasFeed);
    }

    private IEnumerator Loop()
    {
        while (true)
        {
            var feed = WorldEventManager.I;
            if (feed == null || feed.Items == null || feed.Items.Count == 0)
            {
                RefreshVisibility();
                yield return null;
                continue;
            }

            // Snapshot count each pass; items might change mid-loop.
            int count = feed.Items.Count;

            for (int i = 0; i < count; i++)
            {
                if (!barRoot || !barRoot.activeInHierarchy) break;

                // Feed might have changed size; clamp.
                if (i >= feed.Items.Count) break;

                var it = feed.Items[i];
                if (it == null || string.IsNullOrWhiteSpace(it.message))
                    continue;

                SetMessage(it.message);

                // Scroll it fully across.
                yield return ScrollOnce();

                // After each message, pause a bit to avoid instant snap/restart feel.
                // If there's only one message, use the longer loop delay.
                float delay = (feed.Items.Count > 1) ? betweenMessageDelay : loopDelaySeconds;
                if (delay > 0f)
                    yield return new WaitForSecondsRealtime(delay);

                // If the feed changed while waiting, re-evaluate visibility immediately.
                RefreshVisibility();
                if (!barRoot || !barRoot.activeInHierarchy) break;
            }

            // If there are multiple messages, add a small loop delay before the list repeats.
            if (feed.Items.Count > 1 && loopDelaySeconds > 0f)
                yield return new WaitForSecondsRealtime(loopDelaySeconds);

            yield return null;
        }
    }

    private IEnumerator ScrollOnce()
    {
        if (!viewportRect || !messageRect) yield break;

        // Layout update
        Canvas.ForceUpdateCanvases();

        float viewW = viewportRect.rect.width;
        float msgW = messageRect.rect.width;

        float startX = viewW + edgePadding;
        float endX = -msgW - edgePadding;

        SetMessageX(startX);

        if (pauseAtStartSeconds > 0f)
            yield return new WaitForSecondsRealtime(pauseAtStartSeconds);

        float x = startX;
        while (x > endX)
        {
            if (!barRoot || !barRoot.activeInHierarchy) yield break;

            x -= scrollSpeed * Time.unscaledDeltaTime;
            SetMessageX(x);
            yield return null;
        }

        if (pauseAtEndSeconds > 0f)
            yield return new WaitForSecondsRealtime(pauseAtEndSeconds);
    }

    private void SetMessage(string msg)
    {
        if (!messageText) return;
        messageText.text = msg;
        Canvas.ForceUpdateCanvases();
    }

    private void SetMessageX(float x)
    {
        var p = messageRect.anchoredPosition;
        p.x = x;
        messageRect.anchoredPosition = p;
    }

    private void StopLoop()
    {
        if (_loop != null)
        {
            StopCoroutine(_loop);
            _loop = null;
        }
    }
}
