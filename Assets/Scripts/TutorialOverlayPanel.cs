using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class TutorialOverlayPanel : MonoBehaviour
{
    [Header("Tutorial Identity")]
    [SerializeField] private string tutorialKey = "tut_home_v1";
    [SerializeField] private bool autoOpenOnEnable = true;

    [Header("Pages")]
    [SerializeField] private List<TutorialPage> pages = new List<TutorialPage>();

    [Header("UI Refs")]
    [SerializeField] private GameObject overlayRoot;
    [SerializeField] private Image dimmerImage;
    [SerializeField] private Image iconImage;
    [SerializeField] private TMP_Text bodyText;
    [SerializeField] private TMP_Text progressText;
    [SerializeField] private Button nextButton;
    [SerializeField] private TMP_Text nextButtonLabel;
    [SerializeField] private Button closeButton;

    [Header("Skip")]
    [SerializeField] private Button skipButton;

    [Header("Behavior")]
    [SerializeField] private bool allowEarlyClose = false;
    [SerializeField] private bool completeOnlyOnLastSlide = true;

    [Serializable]
    public struct TutorialPage
    {
        public Sprite icon;
        [TextArea(2, 5)] public string text;
    }

    private int _index;
    private bool _openedThisSession;

    // ─────────────────────────────────────────────────────────────
    // Runtime-only request queue (NOT persisted; SaveManager owns saving)
    // ─────────────────────────────────────────────────────────────
    private static readonly HashSet<string> _pendingOpen =
        new HashSet<string>(StringComparer.Ordinal);

    /// <summary>
    /// Request that a tutorial opens when/where it exists.
    /// Safe to call even before panels/UI are enabled or instantiated.
    /// (No saving happens here.)
    /// </summary>
    public static void RequestOpen(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return;

        _pendingOpen.Add(key);

        // If an instance is already in scene (even inactive), open it now.
        try
        {
            var all = UnityEngine.Object.FindObjectsOfType<TutorialOverlayPanel>(true);
            for (int i = 0; i < all.Length; i++)
            {
                var t = all[i];
                if (!t) continue;

                if (t.MatchesKey(key))
                {
                    t.TryOpen();
                    break;
                }
            }
        }
        catch { }
    }

    /// <summary>
    /// Clears runtime-only requests (useful after ClearAll / hard reset).
    /// </summary>
    public static void ClearPendingRequests()
    {
        _pendingOpen.Clear();
    }

    public bool MatchesKey(string key) =>
        string.Equals(tutorialKey, key, StringComparison.Ordinal);

    private void Awake()
    {
        if (overlayRoot) overlayRoot.SetActive(false);
        if (dimmerImage) dimmerImage.raycastTarget = true;

        if (nextButton)  nextButton.onClick.AddListener(OnNextClicked);
        if (closeButton) closeButton.onClick.AddListener(OnCloseClicked);
        if (skipButton)  skipButton.onClick.AddListener(OnSkipClicked);
    }

    private void OnEnable()
    {
        bool requested = !string.IsNullOrWhiteSpace(tutorialKey) && _pendingOpen.Contains(tutorialKey);
        if (!autoOpenOnEnable && !requested) return;

        StartCoroutine(OpenNextFrame());
    }

    private IEnumerator OpenNextFrame()
    {
        yield return null; // let UI routing/instantiation settle
        TryOpen();
    }

    private void OnDisable()
    {
        if (overlayRoot) overlayRoot.SetActive(false);
        _openedThisSession = false;
    }

    public void TryOpen()
    {
        if (string.IsNullOrWhiteSpace(tutorialKey)) return;

        // SaveManager is the ONLY persistence owner
        if (SaveManager.IsTutorialComplete(tutorialKey)) return;

        if (pages == null || pages.Count == 0) return;
        if (_openedThisSession) return;

        _openedThisSession = true;
        _index = 0;

        ShowOverlay(true);
        RenderPage();
        ApplyButtons();

        // Consume any pending open request once we successfully open.
        _pendingOpen.Remove(tutorialKey);
    }

    private void OnNextClicked()
    {
        if (pages == null || pages.Count == 0)
        {
            ShowOverlay(false);
            return;
        }

        bool isLast = _index >= pages.Count - 1;
        if (isLast)
        {
            if (completeOnlyOnLastSlide)
                SaveManager.SetTutorialComplete(tutorialKey, true);

            ShowOverlay(false);
            return;
        }

        _index = Mathf.Clamp(_index + 1, 0, pages.Count - 1);
        RenderPage();
        ApplyButtons();
    }

    private void OnCloseClicked()
    {
        if (!allowEarlyClose) return;
        ShowOverlay(false);
    }

    private void OnSkipClicked()
    {
        if (string.IsNullOrWhiteSpace(tutorialKey)) return;

        // Skip = complete immediately (SaveManager owns saving)
        SaveManager.SetTutorialComplete(tutorialKey, true);

        // Ensure it won't reopen due to a pending request
        _pendingOpen.Remove(tutorialKey);

        _openedThisSession = true;
        ShowOverlay(false);
    }

    private void RenderPage()
    {
        var p = pages[_index];

        if (iconImage)
        {
            iconImage.sprite = p.icon;
            iconImage.enabled = (p.icon != null);
        }

        if (bodyText) bodyText.text = p.text ?? string.Empty;
        if (progressText) progressText.text = $"{_index + 1}/{pages.Count}";
    }

    private void ApplyButtons()
    {
        bool isLast = _index >= pages.Count - 1;

        if (nextButtonLabel)
            nextButtonLabel.text = isLast ? "Done" : "Next";

        if (closeButton)
            closeButton.gameObject.SetActive(allowEarlyClose);

        if (skipButton)
            skipButton.gameObject.SetActive(!isLast);
    }

    private void ShowOverlay(bool show)
    {
        if (overlayRoot) overlayRoot.SetActive(show);
    }
}
