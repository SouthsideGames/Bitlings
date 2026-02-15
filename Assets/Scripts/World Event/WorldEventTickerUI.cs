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

    private Coroutine _loop;

    private void Awake()
    {
        if (!barRoot) barRoot = gameObject;
        if (!messageRect && messageText) messageRect = messageText.rectTransform;
    }

    private void OnEnable()
    {
        if (WorldEventManager.I != null)
            WorldEventManager.I.Changed += HandleChanged;

        HandleChanged();
    }

    private void OnDisable()
    {
        if (WorldEventManager.I != null)
            WorldEventManager.I.Changed -= HandleChanged;

        StopLoop();
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
            bool onHome = UIManager.I != null && UIManager.I.IsOpen(PanelId.Home);
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

                var it = feed.Items[i];
                if (it == null || string.IsNullOrWhiteSpace(it.message)) continue;

                SetMessage(it.message);
                yield return ScrollOnce();
            }

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
