using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// TV-style scrolling ticker.
///
/// Key behavior:
/// - Stages message just outside the viewport on the RIGHT.
/// - Waits pauseBeforeScrollSeconds (visible pause).
/// - Scrolls left until the message RIGHT edge clears the viewport LEFT edge.
/// - Waits pauseAfterClearSeconds (optional blank pause).
///
/// IMPORTANT:
/// - Uses viewport-local edge checks (robust under CanvasScaler, ScreenSpace-Camera, WorldSpace).
/// - Avoid putting the message under LayoutGroups/ContentSizeFitter that fight manual positioning.
/// </summary>
public sealed class WorldEventTickerUI : MonoBehaviour
{
    [Header("Wiring (Optional)")]
    [Tooltip("Optional: root GameObject of the ticker bar to show/hide. If null, uses this GameObject.")]
    [SerializeField] private GameObject barRoot;

    [Tooltip("Optional: TMP text. If null, auto-finds the first TextMeshProUGUI under barRoot.")]
    [SerializeField] private TextMeshProUGUI messageText;

    [Header("Behavior")]
    [SerializeField] private bool onlyShowOnHome = false;

    [Tooltip("Units per second (UI units).")]
    [SerializeField] private float scrollSpeed = 220f;

    [Tooltip("Wait time BEFORE the message starts moving (message staged just off the right edge).")]
    [SerializeField] private float pauseBeforeScrollSeconds = 0.75f;

    [Tooltip("Wait time AFTER the message fully clears the viewport before restarting (blank time).")]
    [SerializeField] private float pauseAfterClearSeconds = 2.0f;

    // Auto-resolved runtime refs (keeps inspector light)
    private RectTransform _viewportRect;
    private RectTransform _messageRect;

    private Coroutine _loop;
    private bool _subscribed;

    // Optional fallback for projects where UIManager.IsOpen(PanelId.Home) isn't authoritative.
    private GameObject _cachedHomePanel;

    private void Awake()
    {
        if (!barRoot) barRoot = gameObject;
        ResolveRefs();
    }

    private void OnEnable()
    {
        ResolveRefs();
        TryHookFeed();
        HandleChanged();
    }

    private void Update()
    {
        if (!_subscribed)
            TryHookFeed();

        if (_viewportRect == null || _messageRect == null || messageText == null)
            ResolveRefs();
    }

    private void OnDisable()
    {
        UnhookFeed();
        StopLoop();
    }

    /// <summary>
    /// Auto-wires viewport + message refs to reduce inspector setup.
    /// </summary>
    private void ResolveRefs()
    {
        if (!barRoot) barRoot = gameObject;

        // Resolve message text
        if (!messageText)
        {
            if (barRoot)
                messageText = barRoot.GetComponentInChildren<TextMeshProUGUI>(true);

            if (!messageText)
                messageText = GetComponentInChildren<TextMeshProUGUI>(true);
        }

        _messageRect = messageText ? messageText.rectTransform : null;

        // Resolve viewport: prefer a RectMask2D/Mask ancestor (clipped viewport), else barRoot rect.
        _viewportRect = null;

        if (messageText)
        {
            var rectMask = messageText.GetComponentInParent<RectMask2D>(true);
            if (rectMask) _viewportRect = rectMask.rectTransform;

            if (_viewportRect == null)
            {
                var mask = messageText.GetComponentInParent<Mask>(true);
                if (mask) _viewportRect = mask.rectTransform as RectTransform;
            }
        }

        if (_viewportRect == null && barRoot)
            _viewportRect = barRoot.GetComponent<RectTransform>();

        if (_viewportRect == null)
            _viewportRect = GetComponent<RectTransform>();
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
        // Feature gate: if World Events feature is locked, hide the bar.
        if (WorldEventSystem.I != null && !WorldEventSystem.I.IsFeatureActive())
        {
            if (barRoot) barRoot.SetActive(false);
            return;
        }

        bool hasFeed = WorldEventManager.I != null &&
                       WorldEventManager.I.Items != null &&
                       WorldEventManager.I.Items.Count > 0;

        if (onlyShowOnHome)
        {
            bool onHome = false;

            if (UIManager.I != null)
                onHome = UIManager.I.IsOpen(PanelId.Home);

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

            for (int i = 0; i < feed.Items.Count; i++)
            {
                if (!barRoot || !barRoot.activeInHierarchy) break;

                if (i >= feed.Items.Count) break;

                var it = feed.Items[i];
                if (it == null || string.IsNullOrWhiteSpace(it.message))
                    continue;

                SetMessage(it.message);

                yield return ScrollUntilCleared();

                RefreshVisibility();
                if (!barRoot || !barRoot.activeInHierarchy) break;
            }

            yield return null;
        }
    }

    private IEnumerator ScrollUntilCleared()
    {
        ResolveRefs();
        if (_viewportRect == null || _messageRect == null) yield break;

        // Ensure text sizing/layout is up to date before positioning.
        Canvas.ForceUpdateCanvases();

        // Stage just outside viewport right edge.
        PositionMessageJustOutsideViewportRight_Local(_viewportRect, _messageRect);

        // Visible pause at start position.
        if (pauseBeforeScrollSeconds > 0f)
            yield return new WaitForSecondsRealtime(pauseBeforeScrollSeconds);

        // Scroll left until message right edge clears viewport left edge (in viewport-local space).
        while (!IsMessageRightPastViewportLeft_Local(_viewportRect, _messageRect))
        {
            if (!barRoot || !barRoot.activeInHierarchy) yield break;

            // Move in viewport-local X direction (robust in ScreenSpace-Camera/WorldSpace)
            float dx = scrollSpeed * Time.unscaledDeltaTime;
            _messageRect.position -= _viewportRect.right * dx;

            yield return null;
        }

        // Optional blank pause after clear.
        if (pauseAfterClearSeconds > 0f)
            yield return new WaitForSecondsRealtime(pauseAfterClearSeconds);
    }

    private void SetMessage(string msg)
    {
        ResolveRefs();
        if (!messageText) return;

        messageText.text = msg;

        // Force layout so message rect/corners reflect the new string before scrolling.
        Canvas.ForceUpdateCanvases();
    }

    private void StopLoop()
    {
        if (_loop != null)
        {
            StopCoroutine(_loop);
            _loop = null;
        }
    }

    // --------------------------------------------------------------------
    // Robust edge math in viewport-local space
    // --------------------------------------------------------------------

    /// <summary>
    /// Place message just outside viewport right edge:
    /// message left edge == viewport right edge (in viewport-local space).
    /// </summary>
    private static void PositionMessageJustOutsideViewportRight_Local(RectTransform viewport, RectTransform message)
    {
        if (!viewport || !message) return;

        Vector3[] v = new Vector3[4];
        Vector3[] m = new Vector3[4];

        viewport.GetWorldCorners(v);
        message.GetWorldCorners(m);

        // Convert world corners to viewport-local coordinates
        float viewportRight = viewport.InverseTransformPoint(v[2]).x; // TR
        float messageLeft = viewport.InverseTransformPoint(m[0]).x;   // BL

        float dxLocal = viewportRight - messageLeft;

        // Move message along viewport's local +X direction in world space
        message.position += viewport.right * dxLocal;
    }

    /// <summary>
    /// True when message right edge <= viewport left edge (fully cleared),
    /// comparing in viewport-local coordinates.
    /// </summary>
    private static bool IsMessageRightPastViewportLeft_Local(RectTransform viewport, RectTransform message)
    {
        if (!viewport || !message) return true;

        Vector3[] v = new Vector3[4];
        Vector3[] m = new Vector3[4];

        viewport.GetWorldCorners(v);
        message.GetWorldCorners(m);

        float viewportLeft = viewport.InverseTransformPoint(v[0]).x;  // BL
        float messageRight = viewport.InverseTransformPoint(m[2]).x;  // TR

        return messageRight <= viewportLeft;
    }
}
