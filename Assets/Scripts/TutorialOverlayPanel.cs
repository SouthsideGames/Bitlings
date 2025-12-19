using System;
using System.Collections.Generic;
using System.IO;
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

    [Header("Behavior")]
    [SerializeField] private bool allowEarlyClose = false;
    [SerializeField] private bool completeOnlyOnLastSlide = true;

    [Header("Skip")]
    [SerializeField] private Button skipButton;

    [Serializable]
    public struct TutorialPage
    {
        public Sprite icon;
        [TextArea(2, 5)] public string text;
    }

    private int _index;
    private bool _openedThisSession;

    private void Awake()
    {
        if (overlayRoot) overlayRoot.SetActive(false);
        if (dimmerImage) dimmerImage.raycastTarget = true;

        if (nextButton) nextButton.onClick.AddListener(OnNextClicked);
        if (closeButton) closeButton.onClick.AddListener(OnCloseClicked);

        if (skipButton)
            skipButton.onClick.AddListener(OnSkipClicked);
    }

    private void OnEnable()
    {
        if (!autoOpenOnEnable) return;
        StartCoroutine(OpenNextFrame());
    }

    private System.Collections.IEnumerator OpenNextFrame()
    {
        yield return null; // wait one frame so UI + routing settles
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
        if (TutorialJsonStore.IsComplete(tutorialKey)) return;
        if (pages == null || pages.Count == 0) return;
        if (_openedThisSession) return;

        _openedThisSession = true;
        _index = 0;

        ShowOverlay(true);
        RenderPage();
        ApplyButtons();
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
                TutorialJsonStore.SetComplete(tutorialKey, true);

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
        if (!overlayRoot) return;

        if (show)
        {
            overlayRoot.SetActive(true);
        }
        else
        {
            overlayRoot.SetActive(false);
        }
    }


    // ─────────────────────────────────────────────────────────────────────────────
    // JSON persistence (no PlayerPrefs)
    // ─────────────────────────────────────────────────────────────────────────────
    private static class TutorialJsonStore
    {
        [Serializable]
        private sealed class TutorialFlagsData
        {
            public List<string> completed = new List<string>();
        }

        private static TutorialFlagsData _data;
        private static HashSet<string> _set;
        private static bool _loaded;

        private static string PathFile =>
            System.IO.Path.Combine(Application.persistentDataPath, "tutorial_flags.json");

        public static bool IsComplete(string key)
        {
            EnsureLoaded();
            return _set.Contains(key);
        }

        public static void SetComplete(string key, bool done)
        {
            if (string.IsNullOrWhiteSpace(key)) return;

            EnsureLoaded();

            bool changed;
            if (done) changed = _set.Add(key);
            else changed = _set.Remove(key);

            if (!changed) return;

            _data.completed = new List<string>(_set);
            Save();
        }

        private static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;

            _data = new TutorialFlagsData();
            _set = new HashSet<string>(StringComparer.Ordinal);

            try
            {
                if (File.Exists(PathFile))
                {
                    var json = File.ReadAllText(PathFile);
                    if (!string.IsNullOrWhiteSpace(json))
                        _data = JsonUtility.FromJson<TutorialFlagsData>(json) ?? new TutorialFlagsData();
                }
            }
            catch { _data = new TutorialFlagsData(); }

            if (_data.completed != null)
            {
                for (int i = 0; i < _data.completed.Count; i++)
                {
                    var k = _data.completed[i];
                    if (!string.IsNullOrWhiteSpace(k)) _set.Add(k);
                }
            }
        }

        private static void Save()
        {
            try
            {
                var json = JsonUtility.ToJson(_data, true);
                File.WriteAllText(PathFile, json);
            }
            catch { }
        }
    }

    private void OnSkipClicked()
    {
        if (string.IsNullOrWhiteSpace(tutorialKey)) return;

        // Always mark as complete when skipping
        TutorialJsonStore.SetComplete(tutorialKey, true);

        // Consume any pending open request so it doesn't re-open
        _pendingOpen.Remove(tutorialKey);

        _openedThisSession = true;

        ShowOverlay(false);
    }


    public bool MatchesKey(string key)
    {
        return string.Equals(tutorialKey, key, StringComparison.Ordinal);
    }

}
